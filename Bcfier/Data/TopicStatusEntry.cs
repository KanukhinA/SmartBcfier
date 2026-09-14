using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Newtonsoft.Json;

namespace Bcfier.Data
{
  /// <summary>
  /// Элемент списка статусов: отображаемое имя и цвет (#RRGGBB).
  /// </summary>
  public sealed class TopicStatusEntry : INotifyPropertyChanged
  {
    private string _name;
    private string _color;

    public TopicStatusEntry()
      : this(string.Empty, TopicStatusListCodec.FallbackColor)
    {
    }

    public TopicStatusEntry(string name, string color)
    {
      _name = name ?? string.Empty;
      _color = TopicStatusListCodec.NormalizeColor(color);
    }

    /// <summary>Название статуса (пишется в BCF TopicStatus).</summary>
    public string Name
    {
      get => _name;
      set
      {
        if (_name == value)
          return;
        _name = value ?? string.Empty;
        OnPropertyChanged();
      }
    }

    /// <summary>Цвет статуса в формате #RRGGBB.</summary>
    public string Color
    {
      get => _color;
      set
      {
        string normalized = TopicStatusListCodec.NormalizeColor(value);
        if (_color == normalized)
          return;
        _color = normalized;
        OnPropertyChanged();
        OnPropertyChanged(nameof(ColorBrush));
        OnPropertyChanged(nameof(ForegroundBrush));
      }
    }

    /// <summary>Кисть фона для UI.</summary>
    [JsonIgnore]
    public SolidColorBrush ColorBrush => TopicStatusListCodec.ToBrush(_color);

    /// <summary>Контрастный цвет текста на фоне статуса.</summary>
    [JsonIgnore]
    public SolidColorBrush ForegroundBrush => TopicStatusListCodec.ContrastingForeground(_color);

    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public TopicStatusEntry Clone() => new TopicStatusEntry(Name, Color);
  }

  /// <summary>
  /// Сериализация списка статусов: JSON с цветами или legacy CSV без цветов.
  /// </summary>
  public static class TopicStatusListCodec
  {
    public const string FallbackColor = "#6B7280";

    private static readonly string[] DefaultPalette =
    {
      "#22C55E", // green — Open
      "#EAB308", // amber — In Progress
      "#3B82F6", // blue — Resolved
      "#6B7280", // gray — Closed
      "#F97316", // orange
      "#A855F7", // purple
      "#EC4899", // pink
      "#14B8A6", // teal
      "#EF4444", // red
      "#0EA5E9"  // sky
    };

    /// <summary>Пресеты для выбора цвета в настройках.</summary>
    public static IReadOnlyList<string> Palette => DefaultPalette;

    /// <summary>Разбор сохранённой строки (JSON или CSV).</summary>
    public static List<TopicStatusEntry> Parse(string raw)
    {
      if (string.IsNullOrWhiteSpace(raw))
        return new List<TopicStatusEntry>();

      string trimmed = raw.Trim();
      if (trimmed.StartsWith("[", StringComparison.Ordinal))
      {
        try
        {
          var list = JsonConvert.DeserializeObject<List<TopicStatusEntry>>(trimmed);
          if (list != null)
          {
            return list
              .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Name))
              .Select(e => new TopicStatusEntry(e.Name.Trim(), e.Color))
              .ToList();
          }
        }
        catch
        {
          // fallback to CSV below
        }
      }

      // Legacy: "Open, In Progress, Resolved, Closed"
      var names = trimmed
        .Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
        .Select(o => o.Trim())
        .Where(o => o.Length > 0)
        .ToList();

      var result = new List<TopicStatusEntry>(names.Count);
      for (int i = 0; i < names.Count; i++)
        result.Add(new TopicStatusEntry(names[i], ColorForIndex(i)));
      return result;
    }

    /// <summary>Сериализация в JSON для settings.config.</summary>
    public static string Serialize(IEnumerable<TopicStatusEntry> items)
    {
      if (items == null)
        return "[]";

      var payload = items
        .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Name))
        .Select(e => new TopicStatusEntry(e.Name.Trim(), e.Color))
        .ToList();

      return JsonConvert.SerializeObject(payload);
    }

    /// <summary>Имена статусов через запятую (для BCF extensions и совместимости).</summary>
    public static string ToCsvNames(IEnumerable<TopicStatusEntry> items)
    {
      if (items == null)
        return string.Empty;
      return string.Join(", ", items
        .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Name))
        .Select(e => e.Name.Trim()));
    }

    public static string ColorForIndex(int index)
    {
      if (index < 0)
        return FallbackColor;
      return DefaultPalette[index % DefaultPalette.Length];
    }

    public static string NormalizeColor(string color)
    {
      if (string.IsNullOrWhiteSpace(color))
        return FallbackColor;

      string value = color.Trim();
      if (!value.StartsWith("#", StringComparison.Ordinal))
        value = "#" + value;

      if (value.Length == 4) // #RGB → #RRGGBB
      {
        value = "#" + value[1] + value[1] + value[2] + value[2] + value[3] + value[3];
      }

      if (value.Length == 9) // #AARRGGBB → #RRGGBB
        value = "#" + value.Substring(3);

      if (value.Length != 7)
        return FallbackColor;

      for (int i = 1; i < 7; i++)
      {
        char ch = value[i];
        bool hex = (ch >= '0' && ch <= '9')
                   || (ch >= 'a' && ch <= 'f')
                   || (ch >= 'A' && ch <= 'F');
        if (!hex)
          return FallbackColor;
      }

      return value.ToUpperInvariant();
    }

    public static SolidColorBrush ToBrush(string color)
    {
      try
      {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFrom(NormalizeColor(color));
        if (brush != null)
        {
          if (brush.CanFreeze)
            brush.Freeze();
          return brush;
        }
      }
      catch
      {
        // ignore
      }

      var fallback = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
      fallback.Freeze();
      return fallback;
    }

    public static SolidColorBrush ContrastingForeground(string color)
    {
      try
      {
        var c = (Color)ColorConverter.ConvertFromString(NormalizeColor(color));
        // Относительная яркость
        double luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        var brush = new SolidColorBrush(luminance > 0.55 ? Colors.Black : Colors.White);
        brush.Freeze();
        return brush;
      }
      catch
      {
        var brush = new SolidColorBrush(Colors.White);
        brush.Freeze();
        return brush;
      }
    }

    /// <summary>Дефолтные статусы с цветами для языка.</summary>
    public static List<TopicStatusEntry> GetDefaults(string language)
    {
      string csv = Utils.UserSettings.GetDefaultTopicList("Stauses", language);
      return Parse(csv);
    }
  }
}
