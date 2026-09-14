using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Bcfier.Data.Utils;

namespace Bcfier.SpService
{
  /// <summary>Настройки подключения BCFier к SP-Service.</summary>
  public sealed class SpBcfServiceSettings
  {
    public const int DefaultSyncIntervalSeconds = 5;
    public const int MinSyncIntervalSeconds = 2;
    public const int MaxSyncIntervalSeconds = 3600;

    public const string BaseUrlKey = "SpServiceBaseUrl";
    public const string LoginKey = "SpServiceLogin";
    public const string PasswordKey = "SpServicePassword";
    public const string ProjectIdKey = "SpServiceProjectId";
    public const string ModelIdKey = "SpServiceModelId";
    public const string SyncIntervalKey = "SpServiceSyncIntervalSeconds";

    public string BaseUrl { get; set; } = string.Empty;
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public int SyncIntervalSeconds { get; set; } = DefaultSyncIntervalSeconds;

    public bool HasConnection =>
      !string.IsNullOrWhiteSpace(BaseUrl)
      && !string.IsNullOrWhiteSpace(Login)
      && !string.IsNullOrWhiteSpace(Password);

    public bool HasProject =>
      HasConnection
      && Guid.TryParse(ProjectId, out _);

    /// <summary>Устарело: модель назначается при экспорте по имени модели ФХ, не из настроек UI.</summary>
    public bool HasModel => HasProject && Guid.TryParse(ModelId, out _);

    /// <summary>Добавляет http://, если схема не указана.</summary>
    public static string NormalizeBaseUrl(string baseUrl)
    {
      string trimmed = (baseUrl ?? string.Empty).Trim();
      if (string.IsNullOrEmpty(trimmed))
        return string.Empty;

      if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
          || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        return trimmed;

      return "http://" + trimmed;
    }

    /// <summary>Нормализует интервал синхронизации в допустимый диапазон.</summary>
    public static int NormalizeSyncInterval(int seconds)
    {
      if (seconds < MinSyncIntervalSeconds)
        return DefaultSyncIntervalSeconds;
      if (seconds > MaxSyncIntervalSeconds)
        return MaxSyncIntervalSeconds;
      return seconds;
    }

    public static int ParseSyncInterval(string value)
    {
      if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds))
        return DefaultSyncIntervalSeconds;
      return NormalizeSyncInterval(seconds);
    }
  }

  /// <summary>Чтение и запись настроек SP-Service. Пароль хранится через DPAPI.</summary>
  public static class SpBcfServiceSettingsStore
  {
    public static SpBcfServiceSettings Load()
    {
      var settings = new SpBcfServiceSettings();
      try
      {
        settings.BaseUrl = SpBcfServiceSettings.NormalizeBaseUrl(UserSettings.Get(SpBcfServiceSettings.BaseUrlKey));
        settings.Login = UserSettings.Get(SpBcfServiceSettings.LoginKey) ?? string.Empty;
        settings.ProjectId = UserSettings.Get(SpBcfServiceSettings.ProjectIdKey) ?? string.Empty;
        settings.ModelId = UserSettings.Get(SpBcfServiceSettings.ModelIdKey) ?? string.Empty;
        settings.Password = Unprotect(UserSettings.Get(SpBcfServiceSettings.PasswordKey));
        settings.SyncIntervalSeconds = SpBcfServiceSettings.ParseSyncInterval(
          UserSettings.Get(SpBcfServiceSettings.SyncIntervalKey));
      }
      catch
      {
        settings.Password = string.Empty;
      }

      return settings;
    }

    public static void Save(SpBcfServiceSettings settings)
    {
      if (settings == null)
        return;

      UserSettings.Set(SpBcfServiceSettings.BaseUrlKey, SpBcfServiceSettings.NormalizeBaseUrl(settings.BaseUrl));
      UserSettings.Set(SpBcfServiceSettings.LoginKey, (settings.Login ?? string.Empty).Trim());
      UserSettings.Set(SpBcfServiceSettings.PasswordKey, Protect(settings.Password ?? string.Empty));
      UserSettings.Set(SpBcfServiceSettings.ProjectIdKey, (settings.ProjectId ?? string.Empty).Trim());
      UserSettings.Set(SpBcfServiceSettings.ModelIdKey, (settings.ModelId ?? string.Empty).Trim());
      UserSettings.Set(
        SpBcfServiceSettings.SyncIntervalKey,
        SpBcfServiceSettings.NormalizeSyncInterval(settings.SyncIntervalSeconds)
          .ToString(CultureInfo.InvariantCulture));
    }

    private static string Protect(string plain)
    {
      if (string.IsNullOrEmpty(plain))
        return string.Empty;

      byte[] bytes = Encoding.UTF8.GetBytes(plain);
      byte[] protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
      return Convert.ToBase64String(protectedBytes);
    }

    private static string Unprotect(string cipher)
    {
      if (string.IsNullOrWhiteSpace(cipher))
        return string.Empty;

      try
      {
        byte[] protectedBytes = Convert.FromBase64String(cipher);
        byte[] bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
      }
      catch
      {
        return string.Empty;
      }
    }
  }
}
