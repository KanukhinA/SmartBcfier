using Bcfier.Data.Utils;
using Bcfier.Localization;

namespace Bcfier.Data
{
  /// <summary>
  /// Чтение и запись выбранной системы координат BCF из пользовательских настроек.
  /// </summary>
  public static class BcfCoordinateSettings
  {
    public const string SettingKey = "BcfCoordinateMode";
    public const string DefaultMode = "RevitZUp";

    /// <summary>
    /// Нормализует ключ пресета осей BCF.
    /// </summary>
    public static string Normalize(string mode)
    {
      if (string.IsNullOrWhiteSpace(mode))
        return DefaultMode;

      switch (mode.Trim())
      {
        case "SourceYUp":
        case "SourceXUp":
        case "RevitZUp":
          return mode.Trim();
        default:
          return DefaultMode;
      }
    }

    /// <summary>
    /// Возвращает сохранённый ключ пресета осей.
    /// </summary>
    public static string GetMode()
    {
      return Normalize(UserSettings.Get(SettingKey));
    }

    /// <summary>
    /// Сохраняет выбранный пресет осей.
    /// </summary>
    public static void SetMode(string mode)
    {
      UserSettings.Set(SettingKey, Normalize(mode));
    }

    /// <summary>
    /// Возвращает локализованное имя текущего пресета системы координат.
    /// </summary>
    public static string GetDisplayName(string mode = null)
    {
      string raw = Normalize(string.IsNullOrWhiteSpace(mode) ? GetMode() : mode);

      switch (raw)
      {
        case "SourceYUp":
          return Loc.BcfCoordinateModeSourceYUp;
        case "SourceXUp":
          return Loc.BcfCoordinateModeSourceXUp;
        default:
          return Loc.BcfCoordinateModeRevitZUp;
      }
    }
  }
}
