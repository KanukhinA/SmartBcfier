using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Bcfier.ReportTable
{
  /// <summary>
  /// Оформление отчёта при экспорте в протокол: название документа и состав шапки.
  /// Хранится в самом BCF, чтобы разметка протокола переезжала вместе с файлом.
  /// </summary>
  public sealed class ReportDocumentSettings : INotifyPropertyChanged
  {
    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private bool _showFieldBlocks = true;
    private bool _showRowNumbers = true;

    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>Первая строка шапки; пусто — берётся имя отчёта.</summary>
    public string Title
    {
      get => _title;
      set => SetField(ref _title, value);
    }

    /// <summary>Вторая строка шапки ("совещания по отделке объекта").</summary>
    public string Subtitle
    {
      get => _subtitle;
      set => SetField(ref _subtitle, value);
    }

    /// <summary>Выводить блоки пользовательских полей между шапкой и таблицей.</summary>
    public bool ShowFieldBlocks
    {
      get => _showFieldBlocks;
      set => SetField(ref _showFieldBlocks, value);
    }

    /// <summary>Выводить колонку "№ п/п" в таблице замечаний.</summary>
    public bool ShowRowNumbers
    {
      get => _showRowNumbers;
      set => SetField(ref _showRowNumbers, value);
    }

    public bool IsDefault =>
      string.IsNullOrWhiteSpace(_title)
      && string.IsNullOrWhiteSpace(_subtitle)
      && _showFieldBlocks
      && _showRowNumbers;

    /// <summary>Название документа с подстановкой имени отчёта, если своё не задано.</summary>
    public string ResolveTitle(string reportName)
    {
      return string.IsNullOrWhiteSpace(_title) ? reportName ?? string.Empty : _title;
    }

    public ReportDocumentSettings Clone()
    {
      return new ReportDocumentSettings
      {
        Title = _title,
        Subtitle = _subtitle,
        ShowFieldBlocks = _showFieldBlocks,
        ShowRowNumbers = _showRowNumbers
      };
    }

    public void CopyFrom(ReportDocumentSettings other)
    {
      if (other == null)
        return;

      Title = other.Title;
      Subtitle = other.Subtitle;
      ShowFieldBlocks = other.ShowFieldBlocks;
      ShowRowNumbers = other.ShowRowNumbers;
    }

    private void SetField(ref string field, string value, [CallerMemberName] string name = null)
    {
      string next = value ?? string.Empty;
      if (string.Equals(field, next, StringComparison.Ordinal))
        return;
      field = next;
      OnPropertyChanged(name);
    }

    private void SetField(ref bool field, bool value, [CallerMemberName] string name = null)
    {
      if (field == value)
        return;
      field = value;
      OnPropertyChanged(name);
    }

    private void OnPropertyChanged([CallerMemberName] string name = null)
    {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
  }
}
