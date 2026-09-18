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
    /// Окно настроек: приложение, справочники замечаний, Revit, подключение к SP-Service.
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
        private readonly ObservableCollection<TopicStatusEntry> _typeRows =
            new ObservableCollection<TopicStatusEntry>();
        private readonly ObservableCollection<TopicStatusEntry> _priorityRows =
            new ObservableCollection<TopicStatusEntry>();
        private readonly ObservableCollection<TopicStatusEntry> _labelRows =
            new ObservableCollection<TopicStatusEntry>();

        private readonly ObservableCollection<CustomFieldDefinition> _customFieldDefs =
            new ObservableCollection<CustomFieldDefinition>();

        private string _editingLanguage;
        private bool _suppressLanguageChange;
        private bool _savedDuringSession;

        private bool _canModerateGoogleSheets;

        public Settings()
        {
            Loc.ApplyCultureFromSettings();
            InitializeComponent();
            Title = Loc.SettingsTitle;
            if (HeaderTitle != null)
                HeaderTitle.Text = Loc.SettingsTitle;
            if (CustomFieldsCatalogHintText != null)
                CustomFieldsCatalogHintText.Text = Loc.CustomFieldsCatalogHint;

            _controlsToSave = new List<Control>
            {
                BCFusername, editSnap, useDefPhoto, alwaysNewView
            };

            // Цветные списки — в DataGrid; ответственные по-прежнему CSV в TextPlaceholder
            _topicListControls["Assignees"] = Assignees;

            StatusesGrid.ItemsSource = _statusRows;
            TypesGrid.ItemsSource = _typeRows;
            PrioritiesGrid.ItemsSource = _priorityRows;
            LabelsGrid.ItemsSource = _labelRows;

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
            UpdateGoogleSheetsTabVisibility();
        }

        private void LoadServerSettings()
        {
            SpBcfServiceSettings stored = SpBcfServiceSettingsStore.Load();
            SpServiceBaseUrlBox.Text = stored.BaseUrl ?? string.Empty;
            SpServiceLoginBox.Text = stored.Login ?? string.Empty;
            SpServicePasswordBox.Password = stored.Password ?? string.Empty;
            SpServiceSyncIntervalBox.Text = stored.SyncIntervalSeconds.ToString(CultureInfo.InvariantCulture);
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

            draft["Stauses"] = TopicStatusListCodec.Serialize(_statusRows);
            draft["Types"] = TopicStatusListCodec.Serialize(_typeRows);
            draft["Priorities"] = TopicStatusListCodec.Serialize(_priorityRows);
            draft["Labels"] = TopicStatusListCodec.Serialize(_labelRows);
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

            LoadNamedColorRows("Stauses", language, _statusRows);
            LoadNamedColorRows("Types", language, _typeRows);
            LoadNamedColorRows("Priorities", language, _priorityRows);
            LoadNamedColorRows("Labels", language, _labelRows);
        }

        /// <summary>Загружает цветной список из черновика или настроек; пустой — дефолты языка.</summary>
        private void LoadNamedColorRows(
            string settingsKey,
            string language,
            ObservableCollection<TopicStatusEntry> target)
        {
            var draft = GetOrCreateDraft(language);
            if (!draft.TryGetValue(settingsKey, out string raw))
            {
                raw = UserSettings.GetLanguageBound(settingsKey, language);
                draft[settingsKey] = raw;
            }

            target.Clear();
            foreach (var entry in TopicStatusListCodec.Parse(raw))
                target.Add(entry);

            if (target.Count == 0)
            {
                foreach (var entry in TopicStatusListCodec.Parse(
                             UserSettings.GetDefaultTopicList(settingsKey, language)))
                    target.Add(entry);
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

        private void AddStatus_Click(object sender, RoutedEventArgs e) =>
            AddNamedColorRow(_statusRows, StatusesGrid, Loc.NewStatusName);

        private void RemoveStatus_Click(object sender, RoutedEventArgs e) =>
            RemoveNamedColorRow(_statusRows, StatusesGrid);

        private void AddType_Click(object sender, RoutedEventArgs e) =>
            AddNamedColorRow(_typeRows, TypesGrid, Loc.NewTypeName);

        private void RemoveType_Click(object sender, RoutedEventArgs e) =>
            RemoveNamedColorRow(_typeRows, TypesGrid);

        private void AddPriority_Click(object sender, RoutedEventArgs e) =>
            AddNamedColorRow(_priorityRows, PrioritiesGrid, Loc.NewPriorityName);

        private void RemovePriority_Click(object sender, RoutedEventArgs e) =>
            RemoveNamedColorRow(_priorityRows, PrioritiesGrid);

        private void AddLabel_Click(object sender, RoutedEventArgs e) =>
            AddNamedColorRow(_labelRows, LabelsGrid, Loc.NewLabelName);

        private void RemoveLabel_Click(object sender, RoutedEventArgs e) =>
            RemoveNamedColorRow(_labelRows, LabelsGrid);

        private static void AddNamedColorRow(
            ObservableCollection<TopicStatusEntry> rows,
            DataGrid grid,
            string defaultName)
        {
            var entry = new TopicStatusEntry(
                defaultName,
                TopicStatusListCodec.ColorForIndex(rows.Count));
            rows.Add(entry);
            grid.SelectedItem = entry;
            grid.ScrollIntoView(entry);
        }

        private static void RemoveNamedColorRow(
            ObservableCollection<TopicStatusEntry> rows,
            DataGrid grid)
        {
            if (grid.SelectedItem is TopicStatusEntry selected)
                rows.Remove(selected);
            else if (rows.Count > 0)
                rows.RemoveAt(rows.Count - 1);
        }

        /// <summary>Выбор цвета через системную палитру Windows (статусы/типы/приоритеты/метки).</summary>
        private void NamedColor_Click(object sender, RoutedEventArgs e)
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

                IReadOnlyList<SpBcfServiceClient.ProjectItem> projects =
                    await client.GetProjectsAsync().ConfigureAwait(true);

                SpServiceStatusText.Text = Loc.Format("SpServiceTestOk", displayName, projects.Count);
                await RefreshGoogleSheetsAccessAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                SpServiceStatusText.Text = Loc.Format("SpServiceTestError", ex.Message);
            }
        }

        private Guid? GetSelectedProjectId()
        {
            if (Guid.TryParse(UserSettings.Get(SpBcfServiceSettings.ProjectIdKey), out Guid saved))
                return saved;
            return null;
        }

        private void UpdateGoogleSheetsTabVisibility()
        {
            if (GoogleSheetsGroup != null)
                GoogleSheetsGroup.Visibility = _canModerateGoogleSheets ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task RefreshGoogleSheetsAccessAsync()
        {
            _canModerateGoogleSheets = false;
            UpdateGoogleSheetsTabVisibility();
            Guid? projectId = GetSelectedProjectId();
            if (projectId == null)
                return;

            try
            {
                string baseUrl = SpBcfServiceSettings.NormalizeBaseUrl(SpServiceBaseUrlBox.Text);
                using var client = new SpBcfServiceClient(baseUrl);
                await client.LoginAsync(SpServiceLoginBox.Text, SpServicePasswordBox.Password).ConfigureAwait(true);
                SpBcfServiceClient.ProjectMyRole role = await client.GetMyRoleAsync(projectId.Value).ConfigureAwait(true);
                _canModerateGoogleSheets = role != null && role.CanModerate;
                UpdateGoogleSheetsTabVisibility();
                if (_canModerateGoogleSheets)
                    await LoadGoogleSheetsSettingsAsync(client, projectId.Value).ConfigureAwait(true);
            }
            catch
            {
                _canModerateGoogleSheets = false;
                UpdateGoogleSheetsTabVisibility();
            }
        }

        private async Task LoadGoogleSheetsSettingsAsync(SpBcfServiceClient client, Guid projectId)
        {
            SpBcfServiceClient.GoogleSheetsSettings settings =
                await client.GetGoogleSheetsSettingsAsync(projectId).ConfigureAwait(true);
            GoogleSheetsEnabledBox.IsChecked = settings.Enabled;
            if (settings.Configured && !string.IsNullOrWhiteSpace(settings.ClientEmail))
                GoogleSheetsEmailText.Text = Loc.Format("GoogleSheetsConfiguredAs", settings.ClientEmail);
            else
                GoogleSheetsEmailText.Text = Loc.GoogleSheetsNotConfigured;
            GoogleSheetsStatusText.Text = string.Empty;
        }

        private async void GoogleSheetsLoad_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Guid? projectId = GetSelectedProjectId();
                if (projectId == null)
                {
                    GoogleSheetsStatusText.Text = Loc.GoogleSheetsNeedProject;
                    return;
                }

                string baseUrl = SpBcfServiceSettings.NormalizeBaseUrl(SpServiceBaseUrlBox.Text);
                using var client = new SpBcfServiceClient(baseUrl);
                await client.LoginAsync(SpServiceLoginBox.Text, SpServicePasswordBox.Password).ConfigureAwait(true);
                SpBcfServiceClient.ProjectMyRole role = await client.GetMyRoleAsync(projectId.Value).ConfigureAwait(true);
                _canModerateGoogleSheets = role != null && role.CanModerate;
                UpdateGoogleSheetsTabVisibility();
                if (!_canModerateGoogleSheets)
                {
                    GoogleSheetsStatusText.Text = Loc.GoogleSheetsExportNeedModerator;
                    return;
                }

                await LoadGoogleSheetsSettingsAsync(client, projectId.Value).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                GoogleSheetsStatusText.Text = Loc.Format("SpServiceTestError", ex.Message);
            }
        }

        private async void GoogleSheetsTest_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Guid? projectId = GetSelectedProjectId();
                if (projectId == null)
                {
                    GoogleSheetsStatusText.Text = Loc.GoogleSheetsNeedProject;
                    return;
                }

                string baseUrl = SpBcfServiceSettings.NormalizeBaseUrl(SpServiceBaseUrlBox.Text);
                using var client = new SpBcfServiceClient(baseUrl);
                await client.LoginAsync(SpServiceLoginBox.Text, SpServicePasswordBox.Password).ConfigureAwait(true);
                SpBcfServiceClient.GoogleSheetsTestResult result =
                    await client.TestGoogleSheetsAsync(projectId.Value).ConfigureAwait(true);
                GoogleSheetsStatusText.Text = result.Ok
                    ? Loc.Format("GoogleSheetsConfiguredAs", result.ClientEmail ?? "")
                    : Loc.Format("SpServiceTestError", result.Error ?? "failed");
            }
            catch (Exception ex)
            {
                GoogleSheetsStatusText.Text = Loc.Format("SpServiceTestError", ex.Message);
            }
        }

        private async void GoogleSheetsSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Guid? projectId = GetSelectedProjectId();
                if (projectId == null)
                {
                    GoogleSheetsStatusText.Text = Loc.GoogleSheetsNeedProject;
                    return;
                }

                string baseUrl = SpBcfServiceSettings.NormalizeBaseUrl(SpServiceBaseUrlBox.Text);
                using var client = new SpBcfServiceClient(baseUrl);
                await client.LoginAsync(SpServiceLoginBox.Text, SpServicePasswordBox.Password).ConfigureAwait(true);

                string json = string.IsNullOrWhiteSpace(GoogleSheetsJsonBox.Text)
                    ? null
                    : GoogleSheetsJsonBox.Text.Trim();
                SpBcfServiceClient.GoogleSheetsSettings saved = await client.PutGoogleSheetsSettingsAsync(
                    projectId.Value, json, GoogleSheetsEnabledBox.IsChecked == true).ConfigureAwait(true);

                GoogleSheetsJsonBox.Clear();
                if (saved.Configured && !string.IsNullOrWhiteSpace(saved.ClientEmail))
                    GoogleSheetsEmailText.Text = Loc.Format("GoogleSheetsConfiguredAs", saved.ClientEmail);
                else
                    GoogleSheetsEmailText.Text = Loc.GoogleSheetsNotConfigured;
                GoogleSheetsEnabledBox.IsChecked = saved.Enabled;
                GoogleSheetsStatusText.Text = Loc.Get("SpServiceSyncOk");
            }
            catch (Exception ex)
            {
                GoogleSheetsStatusText.Text = Loc.Format("SpServiceTestError", ex.Message);
            }
        }

        private void CommitNamedColorGrids()
        {
            foreach (DataGrid grid in new[] { StatusesGrid, TypesGrid, PrioritiesGrid, LabelsGrid })
            {
                if (grid == null)
                    continue;
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
            }
        }

        private void SaveBtnClick(object sender, RoutedEventArgs e)
        {
            // Завершить редактирование ячеек DataGrid перед сохранением
            CommitNamedColorGrids();

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
            SpBcfServiceSettings settings = SpBcfServiceSettingsStore.Load();
            settings.BaseUrl = SpServiceBaseUrlBox.Text;
            settings.Login = SpServiceLoginBox.Text;
            settings.Password = SpServicePasswordBox.Password;
            settings.SyncIntervalSeconds = SpBcfServiceSettings.ParseSyncInterval(SpServiceSyncIntervalBox.Text);
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
