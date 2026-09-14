using System.ComponentModel;
using System.Xml.Serialization;

namespace Bcfier.Bcf.Bcf2
{
  /// <summary>
  /// Данные о наличии ссылки компонента BCF на элемент модели.
  /// </summary>
  public partial class Component : INotifyPropertyChanged
  {
    private bool _hasModelLink;
    private int _linkedElementId;

    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>
    /// Признак того, что компонент сопоставлен с элементом в текущей модели.
    /// </summary>
    [XmlIgnore]
    public bool HasModelLink
    {
      get { return _hasModelLink; }
      set
      {
        if (_hasModelLink == value)
          return;
        _hasModelLink = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasModelLink)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayLabel)));
      }
    }

    /// <summary>
    /// Найденный Revit ElementId для быстрого выделения по клику.
    /// </summary>
    [XmlIgnore]
    public int LinkedElementId
    {
      get { return _linkedElementId; }
      set
      {
        if (_linkedElementId == value)
          return;
        _linkedElementId = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LinkedElementId)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayLabel)));
      }
    }

    /// <summary>
    /// Возвращает удобный идентификатор для UI: найденный Revit Id, иначе AuthoringToolId, иначе IfcGuid.
    /// </summary>
    [XmlIgnore]
    public string DisplayLabel
    {
      get
      {
        // Если элемент сопоставлен с моделью — всегда показываем истинный ElementId, не IfcGuid.
        if (_linkedElementId > 0)
          return _linkedElementId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (!string.IsNullOrWhiteSpace(AuthoringToolId))
          return AuthoringToolId;

        if (!string.IsNullOrWhiteSpace(IfcGuid))
          return IfcGuid;

        return string.Empty;
      }
    }
  }
}
