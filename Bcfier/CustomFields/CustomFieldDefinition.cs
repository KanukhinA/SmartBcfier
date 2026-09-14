using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Bcfier.CustomFields
{
  /// <summary>Определение поля в каталоге настроек.</summary>
  public sealed class CustomFieldDefinition : INotifyPropertyChanged
  {
    private string _name = string.Empty;
    private CustomFieldType _fieldType = CustomFieldType.Text;

    public event PropertyChangedEventHandler PropertyChanged;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
      get => _name;
      set
      {
        string next = value ?? string.Empty;
        if (string.Equals(_name, next, StringComparison.Ordinal))
          return;
        _name = next;
        OnPropertyChanged();
      }
    }

    public CustomFieldType FieldType
    {
      get => _fieldType;
      set
      {
        if (_fieldType == value)
          return;
        _fieldType = value;
        OnPropertyChanged();
      }
    }

    private void OnPropertyChanged([CallerMemberName] string name = null)
    {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
  }
}
