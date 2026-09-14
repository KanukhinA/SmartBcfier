using System;
using System.Configuration;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Bcfier.Localization;
using Bcfier.Themes;

namespace Bcfier.Data.Utils
{
  public static class UserSettings
  {
    /// <summary>
    /// Retrives the user setting with the specified key, if nothing is found returns an empty string
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public static string Get(string key)
    {
      try
      {
        Configuration config = GetConfig();

        if (config == null)
          return string.Empty;


        KeyValueConfigurationElement element = config.AppSettings.Settings[key];
        if (element != null)
        {
          string value = element.Value;
          if (!string.IsNullOrEmpty(value))
            return value;
        }
        else
        {
          config.AppSettings.Settings.Add(key, "");
          config.Save(ConfigurationSaveMode.Modified);
        }
      }
      catch
      {
      }
      return string.Empty;
    }

    /// <summary>Нормализация кода языка настроек: en или ru.</summary>
    public static string NormalizeLanguage(string language)
    {
      if (string.IsNullOrWhiteSpace(language))
        return "ru";
      return language.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "ru";
    }

    public static string CurrentLanguage => NormalizeLanguage(Get("Language"));

    /// <summary>
    /// Список topic для языка: сохранённое значение, иначе миграция со старого ключа, иначе дефолт.
    /// </summary>
    public static string GetLanguageBound(string key, string language = null)
    {
      string lang = NormalizeLanguage(language ?? Get("Language"));
      string boundKey = key + "." + lang;

      if (TryGet(boundKey, out string bound) && !string.IsNullOrWhiteSpace(bound))
        return bound;

      // Миграция: старый общий ключ без суффикса — на язык текущих настроек
      if (lang == CurrentLanguage
          && TryGet(key, out string legacy)
          && !string.IsNullOrWhiteSpace(legacy))
      {
        Set(boundKey, legacy);
        return legacy;
      }

      return GetDefaultTopicList(key, lang);
    }

    public static void SetLanguageBound(string key, string value, string language = null)
    {
      string lang = NormalizeLanguage(language ?? Get("Language"));
      Set(key + "." + lang, value ?? string.Empty);
    }

    /// <summary>Значения по умолчанию для списков topic на указанном языке.</summary>
    public static string GetDefaultTopicList(string key, string language)
    {
      string lang = NormalizeLanguage(language);
      // Ответственные по умолчанию пустые — в UI только placeholder
      if (string.Equals(key, "Assignees", StringComparison.Ordinal))
        return string.Empty;

      CultureInfo culture = lang == "en" ? new CultureInfo("en") : new CultureInfo("ru-RU");
      switch (key)
      {
        case "Stauses":
          return Loc.Get("PlaceholderStatuses", culture);
        case "Types":
          return Loc.Get("PlaceholderTypes", culture);
        case "Priorities":
          return Loc.Get("PlaceholderPriorities", culture);
        case "Labels":
          return Loc.Get("PlaceholderLabels", culture);
        default:
          return string.Empty;
      }
    }

    private static bool TryGet(string key, out string value)
    {
      value = null;
      try
      {
        Configuration config = GetConfig();
        if (config == null)
          return false;

        KeyValueConfigurationElement element = config.AppSettings.Settings[key];
        if (element == null)
          return false;

        value = element.Value;
        return true;
      }
      catch
      {
        return false;
      }
    }
    /// <summary>
    /// Sets the user setting with the specified key and value, if it doesn't exists it is created
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    public static void Set(string key, string value)
    {
      try
      {
        Configuration config = GetConfig();
        if (config == null)
          return;

        KeyValueConfigurationElement element = config.AppSettings.Settings[key];
        if (element != null)
          element.Value = value;
        else
          config.AppSettings.Settings.Add(key, value);

        config.Save(ConfigurationSaveMode.Modified);
        ConfigurationManager.RefreshSection("appSettings");

      }
      catch (System.Exception ex1)
      {
        ExceptionUi.Show(ex1);
      }
    }
    /// <summary>
    /// Retrives the user setting with the specified key and converts it to bool
    /// </summary>
    /// <param name="key"></param>
    /// <param name="defValue">If the key is not found or invalid return this</param>
    /// <returns></returns>
    public static bool GetBool(string key, bool defValue = false)
    {
      bool value = defValue;
      try
      {
        //if it doesn't exist, use the optional default value
        if(!Boolean.TryParse(Get(key), out value))
        value = defValue;
      }
      catch (System.Exception ex1)
      {
        ExceptionUi.Show(ex1);
      }
      return value;
    }

    /// <summary>
    /// The configuration file used to store our settings
    /// Saved in a location accessible by all modules
    /// </summary>
    /// <returns></returns>
    private static Configuration GetConfig()
    {
      string _settings =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BCFier",
          "settings.config");
      var configMap = new ExeConfigurationFileMap {ExeConfigFilename = _settings};
      var config = ConfigurationManager.OpenMappedExeConfiguration(configMap, ConfigurationUserLevel.None);

      if (config == null)
        MessageBox.Show("Error loading the Configuration file.", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
      return config;
    }

     /// <summary>
    /// Tries to set user controls to what they were last time the user used the app
    /// </summary>
    public static void LoadControlSettings(Control control)
    {
      try
      {
        if (control.GetType() == typeof(TextBox))
        {
          var textbox = control as TextBox;
          if (textbox == null)
            return;
          var value = Get(textbox.Name);
          if (!string.IsNullOrEmpty(value))
            textbox.Text = value;
        }
        else if (control.GetType() == typeof(TextPlaceholder))
        {
          var textbox = control as TextPlaceholder;
          if (textbox == null)
            return;
          var value = Get(textbox.Name);
          if (!string.IsNullOrEmpty(value))
            textbox.Text = value;
        }
        else if (control.GetType() == typeof(ComboBox))
        {
          var combobox = control as ComboBox;
          if (combobox == null)
            return;
          var value = Get(combobox.Name);
          if (!string.IsNullOrEmpty(value))
          {
            int elemIndex = 0;
            int.TryParse(value, out elemIndex);
            if (combobox.Items.Count > elemIndex)
              combobox.SelectedIndex = elemIndex;
          }     
        }
        else if (control.GetType() == typeof(CheckBox))
        {
          var checkbox = control as CheckBox;
          if (checkbox == null)
            return;
          bool value;
          if (Boolean.TryParse(Get(checkbox.Name), out value))
            checkbox.IsChecked = value;
        }
        else if (control.GetType() == typeof(TabControl))
        {
          var tabcontrol = control as TabControl;
          if (tabcontrol == null)
            return;
          int value;
          if (int.TryParse(Get(tabcontrol.Name), out value))
            tabcontrol.SelectedIndex = value;
        }
      }
      catch (Exception ex)
      {
        Console.Write(ex.Message);
      }
    }
    /// <summary>
    /// Tries to set user controls to what they were last time the user used the app
    /// </summary>
    public static void SaveControlSettings(Control control)
    {
      try
      {
        if (control.GetType() == typeof(TextBox))
        {
          var textbox = control as TextBox;
          if (textbox == null)
            return;
          Set(textbox.Name, textbox.Text);
        }
        else if (control.GetType() == typeof(TextPlaceholder))
        {
          var textbox = control as TextPlaceholder;
          if (textbox == null)
            return;
          Set(textbox.Name, textbox.Text);
        }
        else if (control.GetType() == typeof(ComboBox))
        {
          var combobox = control as ComboBox;
          if (combobox == null)
            return;
          var elemIndex = combobox.SelectedIndex;
          if (elemIndex != -1)
            Set(combobox.Name, elemIndex.ToString());
        }
        else if (control.GetType() == typeof(CheckBox))
        {
          var checkbox = control as CheckBox;
          if (checkbox == null || !checkbox.IsChecked.HasValue)
            return;
          Set(checkbox.Name, checkbox.IsChecked.Value.ToString());
        }
        else if (control.GetType() == typeof(TabControl))
        {
          var tabcontrol = control as TabControl;
          if (tabcontrol == null)
            return;
          Set(tabcontrol.Name, tabcontrol.SelectedIndex.ToString());
        }
      }
      catch (Exception ex)
      {
        Console.Write(ex.Message);
      }
    }
  }
}
