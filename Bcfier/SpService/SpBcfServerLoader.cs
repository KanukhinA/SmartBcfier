using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bcfier.Bcf;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data;

namespace Bcfier.SpService
{
  /// <summary>Загрузка BCF-отчёта через BCF-API (list) + export zip для полного VisInfo.</summary>
  public static class SpBcfServerLoader
  {
    public static async Task<BcfFile> OpenFromServerAsync(SpBcfServiceClient client, string projectId)
    {
      if (client == null)
        throw new ArgumentNullException(nameof(client));
      if (string.IsNullOrWhiteSpace(projectId))
        throw new ArgumentException("project_id required");

      BcfFile bcf = null;
      if (Guid.TryParse(projectId, out Guid fileId))
      {
        try
        {
          byte[] zipBytes = await client.ExportBcfAsync(fileId).ConfigureAwait(false);
          string tempZip = Path.Combine(Path.GetTempPath(), "BCFier", "server-" + fileId.ToString("N") + ".bcf");
          Directory.CreateDirectory(Path.GetDirectoryName(tempZip) ?? Path.GetTempPath());
          File.WriteAllBytes(tempZip, zipBytes);
          try
          {
            bcf = BcfReader.Open(tempZip);
          }
          finally
          {
            try { File.Delete(tempZip); } catch { /* ignore */ }
          }
        }
        catch
        {
          bcf = null;
        }
      }

      if (bcf == null)
        bcf = await BuildFromApiAsync(client, projectId).ConfigureAwait(false);

      bcf.IsFromServer = true;
      if (Guid.TryParse(projectId, out Guid id))
        bcf.ServerBcfFileId = id;
      bcf.ServerBcfVersion = "3.0";
      bcf.HasBeenSaved = true;
      bcf.PendingServerTopicDeletes.Clear();
      bcf.PendingServerCommentDeletes.Clear();
      bcf.ServerEventsWatermark = DateTimeOffset.UtcNow;

      // пометить issues как синхронизированные (dirty=false)
      foreach (Markup issue in bcf.Issues ?? Enumerable.Empty<Markup>())
      {
        issue.IsServerDirty = false;
        if (issue.Comment != null)
        {
          foreach (Comment c in issue.Comment)
          {
            if (c != null)
              c.IsServerDirty = false;
          }
        }
      }

      return bcf;
    }

    private static async Task<BcfFile> BuildFromApiAsync(SpBcfServiceClient client, string projectId)
    {
      var bcf = new BcfFile
      {
        Filename = projectId,
        Issues = new ObservableCollection<Markup>()
      };

      IReadOnlyList<SpBcfServiceClient.BcfApiTopic> topics = await client.GetTopicsAsync(projectId).ConfigureAwait(false);
      foreach (var topicDto in topics)
      {
        if (topicDto == null || string.IsNullOrWhiteSpace(topicDto.Guid))
          continue;

        var markup = new Markup
        {
          Topic = new Topic
          {
            Guid = topicDto.Guid,
            Title = topicDto.Title,
            TopicType = topicDto.TopicType,
            TopicStatus = topicDto.TopicStatus,
            Priority = topicDto.Priority,
            Stage = topicDto.Stage,
            Description = topicDto.Description,
            AssignedTo = topicDto.AssignedTo,
            CreationAuthor = topicDto.CreationAuthor,
            CreationDate = topicDto.CreationDate.LocalDateTime,
            Labels = topicDto.Labels?.ToArray(),
            ReferenceLink = topicDto.ReferenceLinks?.ToArray()
          },
          Comment = new ObservableCollection<Comment>(),
          Viewpoints = new ObservableCollection<ViewPoint>()
        };

        if (topicDto.DueDate != null)
        {
          markup.Topic.DueDate = topicDto.DueDate.Value.LocalDateTime;
          markup.Topic.DueDateSpecified = true;
        }

        if (topicDto.Index != null)
        {
          markup.Topic.Index = topicDto.Index.Value;
          markup.Topic.IndexSpecified = true;
        }

        string topicDir = Path.Combine(bcf.TempPath, topicDto.Guid);
        Directory.CreateDirectory(topicDir);

        IReadOnlyList<SpBcfServiceClient.BcfApiViewpoint> viewpoints =
          await client.GetViewpointsAsync(projectId, topicDto.Guid).ConfigureAwait(false);
        foreach (var vpDto in viewpoints ?? Array.Empty<SpBcfServiceClient.BcfApiViewpoint>())
        {
          if (vpDto == null || string.IsNullOrWhiteSpace(vpDto.Guid))
            continue;
          var vp = new ViewPoint(false)
          {
            Guid = vpDto.Guid,
            Index = vpDto.Index ?? 0,
            IndexSpecified = vpDto.Index != null
          };
          byte[] snap = await client.DownloadViewpointSnapshotAsync(projectId, topicDto.Guid, vpDto.Guid)
            .ConfigureAwait(false);
          if (snap != null && snap.Length > 0)
          {
            string snapPath = Path.Combine(topicDir, vp.Snapshot);
            File.WriteAllBytes(snapPath, snap);
            vp.SnapshotPath = snapPath;
          }
          markup.Viewpoints.Add(vp);
        }

        IReadOnlyList<SpBcfServiceClient.BcfApiComment> comments =
          await client.GetCommentsAsync(projectId, topicDto.Guid).ConfigureAwait(false);
        foreach (var cDto in comments ?? Array.Empty<SpBcfServiceClient.BcfApiComment>())
        {
          if (cDto == null)
            continue;
          var comment = new Comment
          {
            Guid = cDto.Guid,
            Comment1 = cDto.Comment,
            Author = cDto.Author,
            Date = cDto.Date.LocalDateTime
          };
          if (!string.IsNullOrWhiteSpace(cDto.ViewpointGuid))
          {
            comment.Viewpoint = new CommentViewpoint { Guid = cDto.ViewpointGuid };
          }
          markup.Comment.Add(comment);
        }

        BcfIssueHelper.SyncLabelsFromTopic(markup.Topic);
        bcf.Issues.Add(markup);
      }

      return bcf;
    }
  }
}
