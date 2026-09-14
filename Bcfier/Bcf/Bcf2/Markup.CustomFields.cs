using System.Collections.ObjectModel;
using System.Xml.Serialization;
using Bcfier.CustomFields;

namespace Bcfier.Bcf.Bcf2
{
  public partial class Markup
  {
    private ObservableCollection<CustomFieldValue> _customFields;

    /// <summary>Пользовательские поля Topic (Documents XML, не в markup.bcf).</summary>
    [XmlIgnore]
    public ObservableCollection<CustomFieldValue> CustomFields
    {
      get
      {
        if (_customFields == null)
          _customFields = new ObservableCollection<CustomFieldValue>();
        return _customFields;
      }
      set
      {
        _customFields = value ?? new ObservableCollection<CustomFieldValue>();
        NotifyPropertyChanged(nameof(CustomFields));
      }
    }
  }
}
