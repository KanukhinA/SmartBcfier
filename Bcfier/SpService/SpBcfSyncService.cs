using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Bcfier.Bcf;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data;
using Bcfier.Localization;
using Newtonsoft.Json.Linq;

namespace Bcfier.SpService
{
  /// <summary>Push/pull через BCF-API 3.0 (CRUD + topic events).</summary>
  public static class SpBcfSyncService
  {
    public static async Task SyncAsync(BcfFile bcf, bool interactive)
    {
      if (bcf == null || !bcf.IsFromServer || bcf.ServerBcfFileId == null)
        return;

      SpBcfServiceSettings settings = SpBcfServiceSettingsStore.Load();
      if (!settings.HasConnection)
        throw new InvalidOperationException(Loc.Get("SpServiceNotConfigured"));

      if (!bcf.HasBeenSaved)
        MarkFileDirty(bcf);

      string projectId = bcf.ServerBcfFileId.Value.ToString("D");
      using var client = new SpBcfServiceClient(settings.BaseUrl);
      await client.LoginAsync(settings.Login, settings.Password).ConfigureAwait(true);
      await PushAsync(client, bcf, projectId, interactive).ConfigureAwait(true);
      await PullAsync(client, bcf, projectId).ConfigureAwait(true);
      bcf.HasBeenSaved = true;
    }

    public static async Task PushAsync(SpBcfServiceClient client, BcfFile bcf, string projectId, bool interactive)
    {
      foreach ((string topicGuid, string commentGuid) in bcf.PendingServerCommentDeletes.ToList())
      {
        try { await client.DeleteCommentAsync(projectId, topicGuid, commentGuid).ConfigureAwait(true); }
        catch { /* already gone */ }
        bcf.PendingServerCommentDeletes.Remove((topicGuid, commentGuid));
      }

      foreach (string topicGuid in bcf.PendingServerTopicDeletes.ToList())
      {
        try { await client.DeleteTopicAsync(projectId, topicGuid).ConfigureAwait(true); }
        catch { /* already gone */ }
        bcf.PendingServerTopicDeletes.Remove(topicGuid);
      }

      foreach (Markup issue in (bcf.Issues ?? Enumerable.Empty<Markup>()).ToList())
      {
        if (issue?.Topic == null || string.IsNullOrWhiteSpace(issue.Topic.Guid))
          continue;

        BcfIssueHelper.SyncLabelsToTopic(issue.Topic);
        string topicGuid = issue.Topic.Guid;

        if (issue.IsServerDirty || !bcf.HasBeenSaved)
        {
          var body = new
          {
            guid = topicGuid,
            title = issue.Topic.Title ?? string.Empty,
            topic_type = issue.Topic.TopicType,
            topic_status = issue.Topic.TopicStatus,
            priority = issue.Topic.Priority,
            stage = issue.Topic.Stage,
            description = issue.Topic.Description,
            assigned_to = issue.Topic.AssignedTo,
            due_date = issue.Topic.DueDateSpecified ? (DateTimeOffset?)new DateTimeOffset(issue.Topic.DueDate) : null,
            labels = issue.Topic.Labels?.ToList() ?? new List<string>(),
            reference_links = issue.Topic.ReferenceLink?.ToList() ?? new List<string>(),
            index = issue.Topic.IndexSpecified ? (int?)issue.Topic.Index : null
          };

          try
          {
            // try update first; on missing topic create
            await client.UpdateTopicAsync(projectId, topicGuid, body).ConfigureAwait(true);
            issue.IsServerDirty = false;
          }
          catch (SpBcfConflictException)
          {
            throw;
          }
          catch (Exception)
          {
            try
            {
              await client.CreateTopicAsync(projectId, body).ConfigureAwait(true);
              issue.IsServerDirty = false;
            }
            catch (Exception ex)
            {
              if (interactive)
              {
                MessageBox.Show(Loc.Format("SpServiceSyncError", ex.Message), Loc.Error,
                  MessageBoxButton.OK, MessageBoxImage.Error);
              }
              throw;
            }
          }
        }

        if (issue.Comment != null)
        {
          foreach (Comment comment in issue.Comment.ToList())
          {
            if (comment == null || comment.IsDeleted || string.IsNullOrWhiteSpace(comment.Guid))
              continue;
            if (!comment.IsServerDirty && bcf.HasBeenSaved)
              continue;

            var cBody = new
            {
              guid = comment.Guid,
              comment = comment.Comment1,
              viewpoint_guid = comment.Viewpoint?.Guid
            };

            try
            {
              await client.UpdateCommentAsync(projectId, topicGuid, comment.Guid, cBody).ConfigureAwait(true);
            }
            catch
            {
              await client.CreateCommentAsync(projectId, topicGuid, cBody).ConfigureAwait(true);
            }
            comment.IsServerDirty = false;
          }
        }

        if (issue.Viewpoints != null)
        {
          foreach (ViewPoint vp in issue.Viewpoints.ToList())
          {
            if (vp == null || string.IsNullOrWhiteSpace(vp.Guid) || !vp.IsServerDirty)
              continue;

            var jo = new JObject { ["guid"] = vp.Guid };
            if (vp.IndexSpecified)
              jo["index"] = vp.Index;
            jo["perspective_camera"] = JObject.FromObject(new
            {
              camera_view_point = new { x = 0, y = 0, z = 0 },
              camera_direction = new { x = 0, y = 0, z = -1 },
              camera_up_vector = new { x = 0, y = 1, z = 0 },
              field_of_view = 60
            });
            try
            {
              await client.CreateViewpointAsync(projectId, topicGuid, jo).ConfigureAwait(true);
              vp.IsServerDirty = false;
            }
            catch
            {
              // viewpoint may already exist
              vp.IsServerDirty = false;
            }
          }
        }
      }
    }

    public static async Task PullAsync(SpBcfServiceClient client, BcfFile bcf, string projectId)
    {
      DateTimeOffset since = bcf.ServerEventsWatermark;
      IReadOnlyList<SpBcfServiceClient.BcfApiTopicEvent> events =
        await client.GetTopicEventsAsync(projectId, since).ConfigureAwait(true);

      if (events == null || events.Count == 0)
      {
        bcf.ServerEventsWatermark = DateTimeOffset.UtcNow;
        return;
      }

      // При изменениях на сервере — полная перечитка project (надёжнее частичного merge)
      bool needsReload = events.Any(e =>
        e?.Actions != null && e.Actions.Any(a =>
          string.Equals(a.Type, "create", StringComparison.OrdinalIgnoreCase)
          || string.Equals(a.Type, "update", StringComparison.OrdinalIgnoreCase)
          || string.Equals(a.Type, "delete", StringComparison.OrdinalIgnoreCase)));

      if (needsReload && !bcf.Issues.Any(i => i.IsServerDirty))
      {
        BcfFile reloaded = await SpBcfServerLoader.OpenFromServerAsync(client, projectId).ConfigureAwait(true);
        string oldTemp = bcf.TempPath;
        bcf.TempPath = reloaded.TempPath;
        bcf.Issues.Clear();
        foreach (Markup issue in reloaded.Issues.ToList())
          bcf.Issues.Add(issue);
        bcf.ServerEventsWatermark = DateTimeOffset.UtcNow;
        try { Data.Utils.Utils.DeleteDirectory(oldTemp); } catch { /* ignore */ }
        bcf.RefreshReportMetadata();
      }
      else
      {
        bcf.ServerEventsWatermark = events.Max(e => e.Date);
      }
    }

    public static void MarkFileDirty(BcfFile bcf)
    {
      if (bcf == null || !bcf.IsFromServer)
        return;
      foreach (Markup issue in bcf.Issues ?? Enumerable.Empty<Markup>())
      {
        issue.IsServerDirty = true;
        if (issue.Comment == null)
          continue;
        foreach (Comment comment in issue.Comment)
        {
          if (comment != null && !comment.IsDeleted)
            comment.IsServerDirty = true;
        }
      }
    }
  }
}
