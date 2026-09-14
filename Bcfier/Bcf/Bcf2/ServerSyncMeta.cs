using System;
using System.Xml.Serialization;

namespace Bcfier.Bcf.Bcf2
{
  public partial class Markup
  {
    private Guid? _serverTopicId;
    private long _serverRowVersion;
    private bool _isServerDirty;

    [XmlIgnore]
    public Guid? ServerTopicId
    {
      get => _serverTopicId;
      set
      {
        _serverTopicId = value;
        NotifyPropertyChanged(nameof(ServerTopicId));
      }
    }

    [XmlIgnore]
    public long ServerRowVersion
    {
      get => _serverRowVersion;
      set
      {
        _serverRowVersion = value;
        NotifyPropertyChanged(nameof(ServerRowVersion));
      }
    }

    [XmlIgnore]
    public bool IsServerDirty
    {
      get => _isServerDirty;
      set
      {
        _isServerDirty = value;
        NotifyPropertyChanged(nameof(IsServerDirty));
      }
    }
  }

  public partial class Comment
  {
    private Guid? _serverCommentId;
    private long _serverRowVersion;
    private bool _isServerDirty;

    [XmlIgnore]
    public Guid? ServerCommentId
    {
      get => _serverCommentId;
      set
      {
        _serverCommentId = value;
        OnPropertyChanged(nameof(ServerCommentId));
      }
    }

    [XmlIgnore]
    public long ServerRowVersion
    {
      get => _serverRowVersion;
      set
      {
        _serverRowVersion = value;
        OnPropertyChanged(nameof(ServerRowVersion));
      }
    }

    [XmlIgnore]
    public bool IsServerDirty
    {
      get => _isServerDirty;
      set
      {
        _isServerDirty = value;
        OnPropertyChanged(nameof(IsServerDirty));
      }
    }
  }

  public partial class ViewPoint
  {
    private Guid? _serverViewpointId;
    private long _serverRowVersion;
    private bool _isServerDirty;

    [XmlIgnore]
    public Guid? ServerViewpointId
    {
      get => _serverViewpointId;
      set
      {
        _serverViewpointId = value;
        NotifyPropertyChanged(nameof(ServerViewpointId));
      }
    }

    [XmlIgnore]
    public long ServerRowVersion
    {
      get => _serverRowVersion;
      set
      {
        _serverRowVersion = value;
        NotifyPropertyChanged(nameof(ServerRowVersion));
      }
    }

    [XmlIgnore]
    public bool IsServerDirty
    {
      get => _isServerDirty;
      set
      {
        _isServerDirty = value;
        NotifyPropertyChanged(nameof(IsServerDirty));
      }
    }
  }
}
