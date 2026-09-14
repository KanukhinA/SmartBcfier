using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using Bcfier.Bcf.Bcf2;

namespace Bcfier.Bcf
{
  /// <summary>Серверные идентификаторы и dirty-флаги для sync с SP-Service.</summary>
  public partial class BcfFile
  {
    private bool _isFromServer;
    private Guid? _serverBcfFileId;
    private long _serverRevision;
    private string _serverBcfVersion;

    [XmlIgnore]
    public bool IsFromServer
    {
      get => _isFromServer;
      set
      {
        _isFromServer = value;
        NotifyPropertyChanged(nameof(IsFromServer));
      }
    }

    [XmlIgnore]
    public Guid? ServerBcfFileId
    {
      get => _serverBcfFileId;
      set
      {
        _serverBcfFileId = value;
        NotifyPropertyChanged(nameof(ServerBcfFileId));
      }
    }

    [XmlIgnore]
    public long ServerRevision
    {
      get => _serverRevision;
      set
      {
        _serverRevision = value;
        NotifyPropertyChanged(nameof(ServerRevision));
      }
    }

    [XmlIgnore]
    public string ServerBcfVersion
    {
      get => _serverBcfVersion;
      set
      {
        _serverBcfVersion = value;
        NotifyPropertyChanged(nameof(ServerBcfVersion));
      }
    }

    /// <summary>Watermark для pull events (BCF-API).</summary>
    [XmlIgnore]
    public DateTimeOffset ServerEventsWatermark { get; set; }

    /// <summary>Topic BCF guid, ожидающие удаления при следующей отправке.</summary>
    [XmlIgnore]
    public List<string> PendingServerTopicDeletes { get; } = new List<string>();

    /// <summary>Comment BCF guids: (topicGuid, commentGuid).</summary>
    [XmlIgnore]
    public List<(string TopicGuid, string CommentGuid)> PendingServerCommentDeletes { get; } =
      new List<(string TopicGuid, string CommentGuid)>();

    /// <summary>Помечает замечание как удалённое на сервере при следующей синхронизации.</summary>
    public void QueueServerTopicDelete(Markup issue)
    {
      string guid = issue?.Topic?.Guid;
      if (string.IsNullOrWhiteSpace(guid))
        return;
      if (!PendingServerTopicDeletes.Contains(guid))
        PendingServerTopicDeletes.Add(guid);
    }

    /// <summary>Помечает комментарий как удалённый на сервере при следующей синхронизации.</summary>
    public void QueueServerCommentDelete(Markup issue, Comment comment)
    {
      string topicGuid = issue?.Topic?.Guid;
      string commentGuid = comment?.Guid;
      if (string.IsNullOrWhiteSpace(topicGuid) || string.IsNullOrWhiteSpace(commentGuid))
        return;
      var pair = (topicGuid, commentGuid);
      if (!PendingServerCommentDeletes.Contains(pair))
        PendingServerCommentDeletes.Add(pair);
    }
  }
}
