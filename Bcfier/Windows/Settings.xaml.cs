using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Bcfier.CustomFields;
using Bcfier.Data;
using Bcfier.Data.Utils;
using Bcfier.Localization;
using Bcfier.SpService;
using Bcfier.Themes;

namespace Bcfier.Windows
{
    /// <summary>
    /// Окно настроек: автор, списки topic, язык UI, версия BCF и подключение к SP-Service.
    /// </summary>
    public partial class Settings : Window
    {
        private readonly List<Control> _controlsToSave = new List<Control>();
        private readonly Dictionary<string, TextPlaceholder> _topicListControls =
            new Dictionary<string, TextPlaceholder>();

        /// <summary>Черновики списков по языку до нажатия Save.</summary>
        private readonly Dictionary<string, Dictionary<string, string>> _topicDrafts =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        private readonly ObservableCollection<TopicStatusEntry> _statusRows =
            new ObservableCollection<TopicStatusEntry>();

        private readonly ObservableCollection<CustomFieldDefinition> _customFieldDefs =
            new ObservableCollection<CustomFieldDefinition>();

        private string _editingLanguage;
        private bool _suppressLanguageChange;
        private bool _suppressProjectChange;
        private bool _savedDuringSession;

        public Settings()
        {
            Loc.ApplyCultureFromSettings();
            InitializeComponent();
            Title = Loc.SettingsTitle;
            if (HeaderTitle != null)
                HeaderTitle.Text = Loc.SettingsTitle;

            _controlsToSave = new List<Control>
            {
                BCFusername, editSnap, useDefPhoto, alwaysNewView
            };

            // Статусы редактируются в таблице, остальные списки — в TextPlaceholder
            _topicListControls["Types"] = Types;
            _topicListControls["Priorities"] = Priorities;
            _topicListControls["Labels"] = Labels;
            _topicListControls["Assignees"] = Assignees;

            StatusesGrid.ItemsSource = _statusRows;

            foreach (CustomFieldDefinition def in CustomFieldDefinitionsStore.Load())
                _customFieldDefs.Add(def);
            CustomFieldsDefsGrid.ItemsSource = _customFieldDefs;
            if (CustomFieldsDefsGrid.Columns.Count > 1
                && CustomFieldsDefsGrid.Columns[1] is DataGridComboBoxColumn typeCol)
            {
              typeCol.ItemsSource = new[]
              {
                new { Value = CustomFieldType.Text, Display = Loc.CustomFieldTypeText },
                new { Value = CustomFieldType.Date, Display = Loc.CustomFieldTypeDate }
              };
              typeCol.DisplayMemberPath = "Display";
              typeCol.SelectedValuePath = "Value";
              typeCol.SelectedValueBinding = new System.Windows.Data.Binding(nameof(CustomFieldDefinition.FieldType))
              {
                UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
              };
            }

            foreach (var control in _controlsToSave)
                UserSettings.LoadControlSettings(control);

            _editingLanguage = UserSettings.CurrentLanguage;
            _suppressLanguageChange = true;
            LanguageCombo.SelectedIndex = _editingLanguage == "en" ? 1 : 0;
            _suppressLanguageChange = false;

            SelectBcfWriteVersion(UserSettings.Get(Bcfier.Bcf.BcfVersionReader.WriteVersionSettingKey));
            LoadTopicListsForLanguage(_editingLanguage);
            LoadServerSettings();
        }

        private void LoadServerSettings()
        {
            SpBcfServiceSettings stored = SpBcfServiceSettingsStore.Load();
            SpServiceBaseUrlBox.Text = stored.BaseUrl ?? string.Empty;
            SpServiceLoginBox.Text = stored.Login ?? string.Empty;
            SpServicePasswordBox.Password = stored.Password ?? string.Empty;
            SpServiceSyncIntervalBox.Text = stored.SyncIntervalSeconds.ToString(CultureInfo.InvariantCulture);

            SpServiceProjectCombo.Items.Clear();

            if (Guid.TryParse(stored.ProjectId, out Guid projectId))
            {
                SpServiceProjectCombo.Items.Add(new SpBcfServiceClient.ProjectItem
                {
                    Id = projectId,
                    Name = Loc.Get("SpServiceSavedProject")
                });
                _suppressProjectChange = true;
                SpServiceProjectCombo.SelectedIndex = 0;
                _suppressProjectChange = false;
            }
        }

        private void Settings_OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                SpWindowChrome.Apply(this);
                SpWindowChrome.EnsureHittableBackground(this);
                ApplyChromeClip();
            }
            catch
            {
                // chrome не критичен
            }
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            ApplyChromeClip();
        }

        private void ApplyChromeClip()
        {
            try
            {
                double radius = SpWindowChrome.GetWindowClipRadius(this);
                SpWindowChrome.ClipToRoundedRect(ChromeRoot, radius);
            }
            catch
            {
                // ignore
            }
        }

        private void ChromeRoot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyChromeClip();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (e.ClickCount == 2)
                {
                    WindowState = WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;
                    return;
                }

                if (e.LeftButton == MouseButtonState.Pressed)
                    SpWindowChrome.DragMove(this);
            }
            catch
            {
                // DragMove может бросить, если кнопка уже отпущена
            }
        }

        private void HeaderClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressLanguageChange || LanguageCombo == null)
                return;

            string newLang = LanguageCombo.SelectedIndex == 1 ? "en" : "ru";
            if (string.Equals(newLang, _editingLanguage, StringComparison.OrdinalIgnoreCase))
                return;

            CaptureTopicListsToDraft(_editingLanguage);
            _editingLanguage = newLang;
            LoadTopicListsForLanguage(_editingLanguage);
        }

        private void CaptureTopicListsToDraft(string language)
        {
            var draft = GetOrCreateDraft(language);
            foreach (var pair in _topicListControls)
                draft[pair.Key] = pair.Value.Text ?? string.Empty;

            // Статусы — JSON с цветами
            draft["Stauses"] = TopicStatusListCodec.Serialize(_statusRows);
        }

        private void LoadTopicListsForLanguage(string language)
        {
            var draft = GetOrCreateDraft(language);
            foreach (var pair in _topicListControls)
            {
                if (!draft.TryGetValue(pair.Key, out string text))
                {
                    text = UserSettings.GetLanguageBound(pair.Key, language);
                    draft[pair.Key] = text;
                }

                pair.Value.Text = text ?? string.Empty;
            }

            if (!draft.TryGetValue("Stauses", out string statusRaw))
            {
                statusRaw = UserSettings.GetLanguageBound("Stauses", language);
                draft["Stauses"] = statusRaw;
            }

            LoadStatusRows(statusRaw);
        }

        private void LoadStatusRows(string raw)
        {
            _statusRows.Clear();
            foreach (var entry in TopicStatusListCodec.Parse(raw))
                _statusRows.Add(entry);

            if (_statusRows.Count == 0)
            {
                foreach (var entry in TopicStatusListCodec.GetDefaults(_editingLanguage))
                    _statusRows.Add(entry);
            }
        }

        private Dictionary<string, string> GetOrCreateDraft(string language)
        {
            language = UserSettings.NormalizeLanguage(language);
            if (!_topicDrafts.TryGetValue(language, out var draft))
            {
                draft = new Dictionary<string, string>(StringComparer.Ordinal);
                _topicDrafts[language] = draft;
            }

            return draft;
        }

        private void AddStatus_Click(object sender, RoutedEventArgs e)
        {
            var entry = new TopicStatusEntry(
                Loc.Get("NewStatusName"),
                TopicStatusListCodec.ColorForIndex(_statusRows.Count));
            _statusRows.Add(entry);
            StatusesGrid.SelectedItem = entry;
            StatusesGrid.ScrollIntoView(entry);
        }

        private void RemoveStatus_Click(object sender, RoutedEventArgs e)
        {
            if (StatusesGrid.SelectedItem is TopicStatusEntry selected)
                _statusRows.Remove(selected);
            else if (_statusRows.Count > 0)
                _statusRows.RemoveAt(_statusRows.Count - 1);
        }

        /// <summary>Выбор цвета статуса через системную палитру Windows.</summary>
        private void StatusColor_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement element) || !(element.DataContext is TopicStatusEntry entry))
                return;

            try
            {
                using (var dialog = new System.Windows.Forms.ColorDialog())
                {
                    dialog.FullOpen = true;
                    dialog.AnyColor = true;
                    dialog.SolidColorOnly = false;
                    dialog.AllowFullOpen = true;

                    System.Windows.Media.Color current;
                    try
                    {
                        current = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                            TopicStatusListCodec.NormalizeColor(entry.Color));
                    }
                    catch
                    {
                        current = System.Windows.Media.Colors.Gray;
                    }

                    dialog.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);

                    // Кастомные цвета из палитры статусов — быстрый доступ
                    dialog.CustomColors = TopicStatusListCodec.Palette
                        .Select(HexToOleColor)
                        .Take(16)
                        .ToArray();

                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return;

                    entry.Color = string.Format(
                        "#{0:X2}{1:X2}{2:X2}",
                        dialog.Color.R,
                        dialog.Color.G,
                        dialog.Color.B);
                }
            }
            catch (Exception ex)
            {
                ExceptionUi.Show(ex);
            }
        }

        /// <summary>Преобразует #RRGGBB в OLE COLOR для CustomColors в ColorDialog.</summary>
        private static int HexToOleColor(string hex)
        {
            try
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                    TopicStatusListCodec.NormalizeColor(hex));
                // OLE: 0x00BBGGRR
                return c.R | (c.G << 8) | (c.B << 16);
            }
            catch
            {
                return 0;
            }
        }

        private void SelectBcfWriteVersion(string version)
        {
            string normalized = Bcfier.Bcf.BcfVersionReader.NormalizeWriteVersion(version);
            foreach (var item in BcfWriteVersionCombo.Items)
            {
                if (item is ComboBoxItem comboItem
                    && string.Equals(comboItem.Tag as string, normalized, StringComparison.Ordinal))
                {
                    BcfWriteVersionCombo.SelectedItem = comboItem;
                    return;
                }
            }

            BcfWriteVersionCombo.SelectedIndex = 0;
        }

        private string GetSelectedBcfWriteVersion()
        {
            if (BcfWriteVersionCombo.SelectedItem is ComboBoxItem item
                && item.Tag is string version
                && !string.IsNullOrWhiteSpace(version))
            {
                return Bcfier.Bcf.BcfVersionReader.NormalizeWriteVersion(version);
            }

            return Bcfier.Bcf.BcfVersionReader.DefaultWriteVersion;
        }

        private async void SpServiceTestConnection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SpServiceStatusText.Text = Loc.Get("SpServiceTesting");
                string baseUrl = SpBcfServiceSettings.NormalizeBaseUrl(SpServiceBaseUrlBox.Text);
                SpServiceBaseUrlBox.Text = baseUrl;

                using var client = new SpBcfServiceClient(baseUrl);
                string displayName = await client.LoginAsync(SpServiceLoginBox.Text, SpServicePasswordBox.Password)
                    .ConfigureAwait(true);

                IReadOnlyList<SpBcfServiceClient.BcfApiProject> bcfProjects =
                    await client.GetBcfApiProjectsAsync().ConfigureAwait(true);

                Guid selectedId = Guid.Empty;
                if (SpServiceProjectCombo.SelectedItem is SpBcfServiceClient.ProjectItem cur)
                    selectedId = cur.Id;
                else if (Guid.TryParse(UserSettings.Get(SpBcfServiceSettings.ProjectIdKey), out Guid saved))
                    selectedId = saved;

                _suppressProjectChange = true;
                SpServiceProjectCombo.Items.Clear();
                foreach (var p in bcfProjects)
                {
                    if (p.ParsedId == null)
                        continue;
                    SpServiceProjectCombo.Items.Add(new SpBcfServiceClient.ProjectItem
                    {
                        Id = p.ParsedId.Value,
                        Name = p.Name ?? p.ProjectId
                    });
                }
                SpServiceProjectCombo.SelectedItem = SpServiceProjectCombo.Items
                    .OfType<SpBcfServiceClient.ProjectItem>()
                    .FirstOrDefault(p => p.Id == selectedId)
                    ?? SpServiceProjectCombo.Items.OfType<SpBcfServiceClient.ProjectItem>().FirstOrDefault();
                _suppressProjectChange = false;

                SpServiceStatusText.Text = Loc.Format("SpServiceTestOk", displayName, bcfProjects.Count);
            }
            catch (Exception ex)
            {
                SpServiceStatusText.Text = Loc.Format("SpServiceTestError", ex.Message);
            }
        }

        private void SpServiceProjectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressProjectChange)
                return;
        }

        private void SaveBtnClick(object sender, RoutedEventArgs e)
        {
            // Завершить редактирование ячейки DataGrid перед сохранением
            StatusesGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            StatusesGrid.CommitEdit(DataGridEditingUnit.Row, true);

            foreach (var control in _controlsToSave)
                UserSettings.SaveControlSettings(control);

            CaptureTopicListsToDraft(_editingLanguage);

            foreach (var langDraft in _topicDrafts)
            {
                foreach (var pair in langDraft.Value)
                    UserSettings.SetLanguageBound(pair.Key, pair.Value, langDraft.Key);
            }

            UserSettings.Set("Language", _editingLanguage);
            UserSettings.Set(Bcfier.Bcf.BcfVersionReader.WriteVersionSettingKey, GetSelectedBcfWriteVersion());
            Loc.ApplyCultureFromSettings();
            SaveServerSettings();

            CustomFieldsDefsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            CustomFieldsDefsGrid.CommitEdit(DataGridEditingUnit.Row, true);

            string duplicateName = _customFieldDefs
                .Select(d => d.Name?.Trim() ?? string.Empty)
                .Where(n => n.Length > 0)
                .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .FirstOrDefault();
            if (duplicateName != null)
            {
                MessageBox.Show(
                    Loc.Format("CustomFieldDuplicateName", duplicateName),
                    Loc.Warning,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            CustomFieldDefinitionsStore.Save(_customFieldDefs);

            DialogResult = true;
        }

        private void AddCustomFieldDef_Click(object sender, RoutedEventArgs e)
        {
            var def = new CustomFieldDefinition
            {
                Name = Loc.CustomFieldName + " " + (_customFieldDefs.Count + 1),
                FieldType = CustomFieldType.Text
            };
            _customFieldDefs.Add(def);
            CustomFieldsDefsGrid.SelectedItem = def;
            CustomFieldsDefsGrid.ScrollIntoView(def);
        }

        private void RemoveCustomFieldDef_Click(object sender, RoutedEventArgs e)
        {
            if (CustomFieldsDefsGrid.SelectedItem is CustomFieldDefinition def)
                _customFieldDefs.Remove(def);
        }

        private void SaveServerSettings()
        {
            int interval = SpBcfServiceSettings.ParseSyncInterval(SpServiceSyncIntervalBox.Text);
            SpBcfServiceSettings previous = SpBcfServiceSettingsStore.Load();
            var settings = new SpBcfServiceSettings
            {
                BaseUrl = SpServiceBaseUrlBox.Text,
                Login = SpServiceLoginBox.Text,
                Password = SpServicePasswordBox.Password,
                ProjectId = (SpServiceProjectCombo.SelectedItem as SpBcfServiceClient.ProjectItem)?.Id.ToString("D")
                            ?? string.Empty,
                // Модель не выбирается вручную: при экспорте назначается по имени модели ФХ.
                ModelId = previous.ModelId ?? string.Empty,
                SyncIntervalSeconds = interval
            };
            SpBcfServiceSettingsStore.Save(settings);
        }

        private void CancelBtnClick(object sender, RoutedEventArgs e) => DialogResult = false;

        private void editphoto_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = Loc.Get("OpenExeFilter"),
                DefaultExt = ".exe"
            };

            if (dialog.ShowDialog() == true)
                editSnap.Text = dialog.FileName;
        }
    }
}
