using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml.Linq;
using Bcfier.Bcf;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data.Utils;
using Bcfier.Windows;
using Bcfier.Data;
using Bcfier.Localization;
using Bcfier.SpService;
using Bcfier.ReportTable;
using Version = System.Version;
using BcfComponent = Bcfier.Bcf.Bcf2.Component;

namespace Bcfier.UserControls
{
  /// <summary>
  /// Main panel UI and logic that need to be used by all modules
  /// </summary>
  public partial class BcfierPanel : UserControl
  {
    //my data context
    private readonly BcfContainer _bcf = new BcfContainer();
    private int _loadingOverlayCounter;
    private readonly DispatcherTimer _loadingMessageTimer;
    private string _pendingLoadingMessage;
    private string _lastAppliedLoadingMessage;
    private DispatcherTimer _autoSyncTimer;
    private bool _autoSyncEnabled;
    private bool _isAutoSyncing;
    private bool _isTableMode;



    public BcfierPanel()
    {
      // Культура должна применяться до разбора XAML со {x:Static loc:...}
      Loc.ApplyCultureFromSettings();
      InitializeComponent();
      DataContext = _bcf;
      _loadingMessageTimer = CreateLoadingMessageTimer();
      ComponentListHost.ReportProgress = UpdateLoadingOverlayMessage;
      BcfReader.ReportProgress = UpdateLoadingOverlayMessage;
      _bcf.UpdateDropdowns();
      //top menu buttons and events
      NewBcfBtn.Click += delegate
      {
        _bcf.NewFile();
        OnAddIssue(null, null);
        ScheduleRefreshComponentLinks();
        UpdateServerSyncUi();
      };
      OpenBcfBtn.Click += delegate
      {
        _ = OpenBcfFilesFromDialogAsync();
      };
      OpenFromDbBtn.Click += delegate
      {
        _ = OpenBcfFromDatabaseAsync();
      };
      //OpenProjectBtn.Click += OnOpenWebProject;
      SaveBcfBtn.Click += delegate { _bcf.SaveFile(SelectedBcf()); };
      SendToDbBtn.Click += delegate
      {
        _ = SendSelectedToDatabaseAsync(interactive: true);
      };
      MergeBcfBtn.Click += delegate
      {
        _bcf.MergeFiles(SelectedBcf());
        _bcf.UpdateDropdowns();
        ScheduleRefreshComponentLinks();
      };
      SettingsBtn.Click += delegate
      {
        var s = new Settings();
        s.ShowDialog();
        //update bcfs with new statuses and types
        if (s.DialogResult.HasValue && s.DialogResult.Value)
        {
          _bcf.UpdateDropdowns();
          foreach (BcfFile bcf in _bcf.BcfFiles)
            bcf?.RefreshReportMetadata();
          ApplyAutoSyncIntervalFromSettings();
          UpdateServerSyncUi();
        }

      };
      HelpBtn.Click += HelpBtnOnClick;
      TableModeBtn.Click += delegate { SetTableMode(!_isTableMode); };
      AutoSyncCheckBox.Checked += delegate
      {
        _autoSyncEnabled = true;
        UpdateServerSyncUi();
        EnsureAutoSyncTimer();
      };
      AutoSyncCheckBox.Unchecked += delegate
      {
        _autoSyncEnabled = false;
        UpdateServerSyncUi();
        UpdateAutoSyncTimerState();
      };
      BcfTabControl.SelectionChanged += delegate
      {
        UpdateServerSyncUi();
        UpdateAutoSyncTimerState();
        if (_isTableMode)
          TablePanel.RefreshRows();
      };
      _bcf.BcfFiles.CollectionChanged += delegate
      {
        UpdateServerSyncUi();
        UpdateAutoSyncTimerState();
        if (_isTableMode)
          TablePanel.RefreshRows();
        UpdateEmptyHintVisibility();
      };
      //set version
      LabelVersion.Content = Loc.ProductName + " " +
                         System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
      ApplyAutoSyncIntervalFromSettings();
      UpdateServerSyncUi();
      TablePanel.AttachContainer(_bcf);
      SetTableMode(ReportTableSettings.IsTableMode(), persist: false);
    }

    /// <summary>Переключает классический и табличный режимы отображения.</summary>
    private void SetTableMode(bool tableMode, bool persist = true)
    {
      _isTableMode = tableMode;
      if (persist)
        ReportTableSettings.SetDisplayMode(tableMode);

      BcfTabControl.Visibility = tableMode ? Visibility.Collapsed : Visibility.Visible;
      TablePanel.Visibility = tableMode ? Visibility.Visible : Visibility.Collapsed;

      TableModeBtn.Content = tableMode ? Loc.ClassicMode : Loc.TableMode;
      TableModeBtn.ToolTip = tableMode ? Loc.ClassicModeTip : Loc.TableModeTip;

      if (tableMode)
        TablePanel.RefreshRows();

      UpdateEmptyHintVisibility();
    }

    private void UpdateEmptyHintVisibility()
    {
      if (EmptyDropHint == null)
        return;

      bool hasFiles = _bcf.BcfFiles != null && _bcf.BcfFiles.Count > 0;
      // В табличном режиме пустой hint скрываем — его показывает сам TablePanel
      EmptyDropHint.Visibility = (!hasFiles && !_isTableMode) ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool CheckSaveBcf(BcfFile bcf)
    {
      try
      {
        if (BcfTabControl.SelectedIndex != -1 && bcf != null && !bcf.HasBeenSaved && bcf.Issues.Any())
        {

          MessageBoxResult answer = MessageBox.Show(
            Loc.Format("SaveReportMessage", bcf.Filename),
            Loc.Get("SaveReportTitle"),
          MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
          if (answer == MessageBoxResult.Yes)
          {
            _bcf.SaveFile(bcf);
            return false;
          }
          if (answer == MessageBoxResult.Cancel)
          {
            return false;
          }
        }
      }
      catch (System.Exception ex1)
      {
        ExceptionUi.Show(ex1);
      }
      return true;
    }




    #region commands
    private void OnDeleteIssues(object sender, ExecutedRoutedEventArgs e)
    {
      try
      {
        if (SelectedBcf() == null)
          return;

        List<Markup> issues;
        if (e.Parameter is Markup single)
          issues = new List<Markup> { single };
        else if (e.Parameter is IList selItems)
          issues = selItems.Cast<Markup>().ToList();
        else
          issues = new List<Markup>();

        if (!issues.Any())
        {
          MessageBox.Show(Loc.Get("NoIssueSelected"), Loc.Error, MessageBoxButton.OK, MessageBoxImage.Error);
          return;
        }
        MessageBoxResult answer = MessageBox.Show(
            issues.Count == 1
              ? Loc.Format("DeleteIssuesMessage", issues.Count, "\n - " + string.Join("\n - ", issues.Select(x => x.Topic.Title)))
              : Loc.Format("DeleteIssuesMessagePlural", issues.Count, "\n - " + string.Join("\n - ", issues.Select(x => x.Topic.Title))),
            issues.Count == 1 ? Loc.Get("DeleteIssuesTitle") : Loc.Get("DeleteIssuesTitlePlural"),
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.No)
          return;

        SelectedBcf().RemoveIssues(issues);

      }
      catch (System.Exception ex1)
      {
        ExceptionUi.Show(ex1);
      }
    }

    private void OnAddComment(object sender, ExecutedRoutedEventArgs e)
    {
      try
      {

        if (SelectedBcf() == null)
          return;
        var values = (object[])e.Parameter;
        var view = values[0] as ViewPoint;
        var issue = values[1] as Markup;
        var textBox = values[2] as System.Windows.Controls.TextBox;
        var content = textBox != null ? textBox.Text : values[2]?.ToString();
        if (issue == null)
        {
          MessageBox.Show(Loc.Get("NoIssueSelected"), Loc.Error, MessageBoxButton.OK, MessageBoxImage.Error);
          return;
        }

        if (string.IsNullOrWhiteSpace(content))
          return;

        Comment c = new Comment();
        c.Guid = Guid.NewGuid().ToString();
        c.Comment1 = content.Trim();
        c.Date = DateTime.Now;
        c.Author = BcfAuthorContext.ResolveAuthor();

        c.Viewpoint = new CommentViewpoint();
        c.Viewpoint.Guid = (view != null) ? view.Guid : "";

        issue.Comment.Add(c);

        SelectedBcf().HasBeenSaved = false;

        if (textBox != null)
          textBox.Text = string.Empty;

      }
      catch (System.Exception ex1)
      {
        ExceptionUi.Show(ex1);
      }
    }

    private void OnEditComment(object sender, ExecutedRoutedEventArgs e)
    {
      try
      {
        if (SelectedBcf() == null)
          return;
        var values = e.Parameter as object[];
        if (values == null || values.Length < 2)
          return;

        var comment = values[0] as Comment;
        var textBox = values[1] as System.Windows.Controls.TextBox;
        var content = textBox != null
          ? textBox.Text
          : (values.Length > 1 ? values[1]?.ToString() : null);

        if (comment == null || !comment.CanModify)
          return;
        if (string.IsNullOrWhiteSpace(content))
          return;

        comment.ApplyEdit(content.Trim(), BcfAuthorContext.ResolveAuthor());
        SelectedBcf().HasBeenSaved = false;
      }
      catch (System.Exception ex1)
      {
        ExceptionUi.Show(ex1);
      }
    }

    private void OnDeleteComment(object sender, ExecutedRoutedEventArgs e)
    {
      try
      {
        if (SelectedBcf() == null)
          return;
        var values = (object[])e.Parameter;
        var comment = values[0] as Comment;
        if (comment == null)
        {
          MessageBox.Show(Loc.Get("NoCommentSelected"), Loc.Error, MessageBoxButton.OK, MessageBoxImage.Error);
          return;
        }
        if (!comment.CanModify)
          return;

        comment.MarkDeleted(BcfAuthorContext.ResolveAuthor());
        SelectedBcf().HasBeenSaved = false;
      }
      catch (System.Exception ex1)
      {
        ExceptionUi.Show(ex1);
      }
    }

    private void OnDeleteView(object sender, ExecutedRoutedEventArgs e)
    {
      try
      {

        if (SelectedBcf() == null)
          return;
        var values = (object[])e.Parameter;
        var view = values[0] as ViewPoint;
        var issue = (Markup)values[1];
        if (issue == null)
        {
          MessageBox.Show(Loc.Get("NoIssueSelected"), Loc.Error, MessageBoxButton.OK, MessageBoxImage.Error);
          return;
        }
        if (view == null)
        {
          MessageBox.Show(Loc.Get("NoViewSelected"), Loc.Error, MessageBoxButton.OK, MessageBoxImage.Error);
          return;
        }
        var delComm = true;

        MessageBoxResult answer = MessageBox.Show(Loc.Get("DeleteViewpointCommentsMessage"),
           Loc.Get("DeleteViewpointCommentsTitle"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        if (answer == MessageBoxResult.Cancel)
          return;
        if (answer == MessageBoxResult.No)
          delComm = false;

        SelectedBcf().RemoveView(view, issue, delComm);
      }
      catch (System.Exception ex1)
      {
        ExceptionUi.Show(ex1);
      }
    }
    private void OnAddIssue(object sender, ExecutedRoutedEventArgs e)
    {
      try
      {

        if (SelectedBcf() == null)
          return;
        var issue = new Markup(DateTime.UtcNow);
        BcfIssueHelper.InitializeNewIssue(issue, BcfAuthorContext.ResolveAuthor());
        SelectedBcf().Issues.Add(issue);
        SelectedBcf().SelectedIssue = issue;
        _bcf.UpdateDropdowns();

      }
      catch (System.Exception ex1)
      {
        ExceptionUi.Show(ex1);
      }
    }
    private void HasIssueSelected(object sender, CanExecuteRoutedEventArgs e)
    {
      var bcf = SelectedBcf();
      if (bcf == null)
      {
        e.CanExecute = false;
        return;
      }

      // Крестик на карточке передаёт Markup — команда доступна без выбора в списке
      if (e.Parameter is Markup)
      {
        e.CanExecute = true;
        return;
      }

      if (e.Parameter is IList list && list.Count > 0)
      {
        e.CanExecute = true;
        return;
      }

      e.CanExecute = bcf.SelectedIssue != null;
    }
    private void OnOpenSnapshot(object sender, ExecutedRoutedEventArgs e)
    {
      try
      {
        var view = e.Parameter as ViewPoint;
        if (view == null || !File.Exists(view.SnapshotPath))
        {
          MessageBox.Show(Loc.Get("SnapshotMissing"), Loc.Error, MessageBoxButton.OK, MessageBoxImage.Error);
          return;
        }
        if (!UserSettings.GetBool("useDefPhoto", true))
        {
          var dialog = new SnapWin(view.SnapshotPath);
          dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
          dialog.Show();
        }
        else
          Process.Start(view.SnapshotPath);
      }
      catch (System.Exception ex1)
      {
        ExceptionUi.Show(ex1);
      }
    }
    private void OnOpenComponents(object sender, ExecutedRoutedEventArgs e)
    {
      try
      {
        var view = e.Parameter as ViewPoint;
        if (view == null)
        {
          MessageBox.Show(Loc.Get("ViewpointNull"), Loc.Error, MessageBoxButton.OK, MessageBoxImage.Error);
          return;
        }
        Window window = ComponentListHost.CreateWindow != null
          ? ComponentListHost.CreateWindow(view.VisInfo.Components, true)
          : new Windows.ComponentsList(view.VisInfo.Components, true);

        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.ShowDialog();
      }
      catch (System.Exception ex1)
      {
        ExceptionUi.Show(ex1);
      }
    }

    private void OnSelectComponent(object sender, ExecutedRoutedEventArgs e)
    {
      try
      {
        if (ComponentListHost.SelectInModel == null || e.Parameter == null)
          return;

        int elementId;
        if (e.Parameter is BcfComponent component)
        {
          if (!BcfViewpointComponents.TryGetSelectableElementId(component, out elementId))
            return;
        }
        else if (e.Parameter is int intId)
        {
          elementId = intId;
        }
        else if (!int.TryParse(e.Parameter.ToString(), out elementId))
        {
          return;
        }

        bool append = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        ComponentListHost.SelectInModel(new List<int> { elementId }, append);
      }
      catch (Exception ex)
      {
        ExceptionUi.Show(ex);
      }
    }

    /// <summary>
    /// Выделяет в модели все сопоставленные элементы viewpoint.
    /// </summary>
    private void OnSelectViewpointComponents(object sender, ExecutedRoutedEventArgs e)
    {
      try
      {
        if (ComponentListHost.SelectInModel == null)
          return;

        var viewpoint = e.Parameter as ViewPoint;
        List<int> ids = CollectLinkedElementIds(viewpoint?.VisInfo?.Components?.DisplayComponents);
        if (ids.Count == 0)
          return;

        ComponentListHost.SelectInModel(ids, false);
      }
      catch (Exception ex)
      {
        ExceptionUi.Show(ex);
      }
    }

    /// <summary>
    /// Собирает Revit ElementId компонентов: найденный LinkedElementId или числовой AuthoringToolId.
    /// </summary>
    private static List<int> CollectLinkedElementIds(IEnumerable<BcfComponent> components)
    {
      var ids = new List<int>();
      if (components == null)
        return ids;

      foreach (BcfComponent component in components)
      {
        if (!BcfViewpointComponents.TryGetSelectableElementId(component, out int elementId))
          continue;

        if (!ids.Contains(elementId))
          ids.Add(elementId);
      }

      return ids;
    }

    /// <summary>
    /// Планирует обновление ссылок компонентов: сначала отрисовка BCF, затем фоновый resolve в Revit.
    /// </summary>
    public void ScheduleRefreshComponentLinks()
    {
      UpdateLoadingOverlayMessage("Подготовка списка компонентов BCF...");

      // Сбор компонентов на UI-потоке (ObservableCollection нельзя читать из фонового потока).
      IList<BcfComponent> components;
      try
      {
        components = CollectAllBcfComponents();
      }
      catch (Exception ex)
      {
        Debug.WriteLine("ScheduleRefreshComponentLinks CollectAllBcfComponents: " + ex);
        HideLoadingOverlay();
        return;
      }

      if (components == null || components.Count == 0)
      {
        HideLoadingOverlay();
        return;
      }

      ShowLoadingOverlay("Выполняется анализ элементов модели...");
      UpdateLoadingOverlayMessage("Передача компонентов в Revit...");

      if (ComponentListHost.RunWithRevitContext != null)
      {
        // ExternalEvent Revit: кэш строится на API-потоке, UI обновляется в колбэке.
        ComponentListHost.RunWithRevitContext(() =>
        {
          try
          {
            UpdateLoadingOverlayMessage("Обновление связей в списке замечаний...");
            RefreshComponentLinks();
            UpdateLoadingOverlayMessage("Загрузка завершена.");
          }
          finally
          {
            HideLoadingOverlay();
          }
        }, components);
      }
      else
      {
        try
        {
          RefreshComponentLinks();
        }
        finally
        {
          HideLoadingOverlay();
        }
      }
    }

    /// <summary>
    /// Собирает все компоненты из открытых BCF для пакетного сопоставления в Revit.
    /// </summary>
    private List<BcfComponent> CollectAllBcfComponents()
    {
      var components = new List<BcfComponent>();
      var componentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      if (_bcf?.BcfFiles == null)
        return components;

      foreach (BcfFile bcf in _bcf.BcfFiles)
      {
        bcf?.RefreshReportMetadata();
        foreach (Markup issue in bcf?.Issues ?? Enumerable.Empty<Markup>())
        {
          BcfViewpointComponents.ApplyAuthoringToolIdsFromIssueText(issue);
          foreach (ViewPoint viewpoint in issue?.Viewpoints ?? Enumerable.Empty<ViewPoint>())
          {
            AddComponents(componentKeys, components, BcfViewpointComponents.EnumerateViewpointComponents(viewpoint));
          }
        }
      }

      return components;
    }

    /// <summary>
    /// Добавляет уникальные компоненты в список для пакетного resolve.
    /// </summary>
    private static void AddComponents(
      HashSet<string> componentKeys,
      List<BcfComponent> target,
      IEnumerable<BcfComponent> source)
    {
      if (target == null || source == null)
        return;

      foreach (BcfComponent component in source)
      {
        if (component == null)
          continue;

        string key = BuildComponentResolveKey(component);
        if (componentKeys.Add(key))
          target.Add(component);
      }
    }

    /// <summary>
    /// При подгрузке BCF помечает компоненты, которые реально существуют в активной модели.
    /// </summary>
    public void RefreshComponentLinks()
    {
      if (_bcf?.BcfFiles == null)
        return;

      bool revitHost = ComponentListHost.RunWithRevitContext != null;
      if (!revitHost && ComponentListHost.ResolveElementId == null)
        return;

      try
      {
        // Локальный пакетный кэш на одну загрузку: одинаковые компоненты в разных viewpoints
        // не должны запускать одинаковый resolve повторно.
        var batchCache = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);

        foreach (var bcf in _bcf.BcfFiles)
        {
          foreach (var issue in bcf?.Issues ?? Enumerable.Empty<Markup>())
          {
            foreach (var viewpoint in issue?.Viewpoints ?? Enumerable.Empty<ViewPoint>())
            {
              UpdateComponentLinks(BcfViewpointComponents.EnumerateViewpointComponents(viewpoint), batchCache, revitHost);
            }
          }
        }

        RefreshIssueFilters();
      }
      catch (Exception ex)
      {
        Debug.WriteLine("RefreshComponentLinks: " + ex);
      }
    }

    /// <summary>
    /// Выставляет признак ссылки и найденный id для списка компонентов.
    /// </summary>
    private static void UpdateComponentLinks(
      IEnumerable<Bcfier.Bcf.Bcf2.Component> components,
      IDictionary<string, int?> batchCache,
      bool revitHost)
    {
      foreach (Bcfier.Bcf.Bcf2.Component component in components ?? Enumerable.Empty<Bcfier.Bcf.Bcf2.Component>())
      {
        if (component == null)
          continue;

        string key = BuildComponentResolveKey(component);
        if (!batchCache.TryGetValue(key, out int? resolved))
        {
          if (revitHost)
          {
            // В Revit UI-поток не вызывает API: только уже прогретый кэш после ExternalEvent.
            // found=true означает «ключ был в кэше» (в т.ч. явный miss с elementId=null).
            var cached = ComponentListHost.TryGetCachedElementId?.Invoke(component);
            if (cached != null && cached.Value.found)
              resolved = cached.Value.elementId;
            else
              resolved = null;
          }
          else
          {
            resolved = ComponentListHost.ResolveElementId?.Invoke(component);
          }

          batchCache[key] = resolved;
        }

        bool hasLink = resolved.HasValue;
        int linkedId = resolved ?? 0;
        if (component.HasModelLink == hasLink && component.LinkedElementId == linkedId)
          continue;

        // Сначала Id, потом флаг: DisplayLabel должен сразу показать числовой ElementId, а не IfcGuid.
        component.LinkedElementId = linkedId;
        component.HasModelLink = hasLink;
      }
    }

    /// <summary>
    /// Формирует ключ для пакетного кэша сопоставления компонента.
    /// </summary>
    private static string BuildComponentResolveKey(Bcfier.Bcf.Bcf2.Component component)
    {
      return BcfViewpointComponents.BuildComponentKey(component);
    }

    /// <summary>
    /// Обновляет фильтры замечаний после пакетного resolve компонентов в активном документе.
    /// </summary>
    private void RefreshIssueFilters()
    {
      foreach (BcfFile bcf in _bcf?.BcfFiles ?? Enumerable.Empty<BcfFile>())
      {
        bcf?.ApplyIssueFilter();
      }
    }

    /// <summary>
    /// Создаёт таймер коалесцирования статусов загрузки, чтобы не перегружать Dispatcher.
    /// </summary>
    private DispatcherTimer CreateLoadingMessageTimer()
    {
      var timer = new DispatcherTimer(DispatcherPriority.Background)
      {
        Interval = TimeSpan.FromMilliseconds(150)
      };
      timer.Tick += (_, __) => FlushPendingLoadingMessage();
      return timer;
    }

    /// <summary>
    /// Применяет последнее накопленное сообщение загрузки на UI-потоке.
    /// </summary>
    private void FlushPendingLoadingMessage()
    {
      try
      {
        string message = Interlocked.Exchange(ref _pendingLoadingMessage, null);
        if (string.IsNullOrWhiteSpace(message) || string.Equals(message, _lastAppliedLoadingMessage, StringComparison.Ordinal))
          return;

        _lastAppliedLoadingMessage = message;
        LoadingText.Text = message;
      }
      catch
      {
        // Ошибка текста статуса не должна блокировать основную загрузку BCF
      }
    }

    /// <summary>
    /// Показывает overlay-индикатор на время анализа BCF-компонентов.
    /// </summary>
    private void ShowLoadingOverlay(string message)
    {
      try
      {
        _loadingOverlayCounter++;
        if (!_loadingMessageTimer.IsEnabled)
          _loadingMessageTimer.Start();
        UpdateLoadingOverlayMessage(message);

        LoadingOverlay.Visibility = Visibility.Visible;
      }
      catch
      {
        // Ошибка индикатора не должна блокировать основную загрузку BCF
      }
    }

    /// <summary>
    /// Скрывает overlay-индикатор после завершения анализа компонентов.
    /// </summary>
    private void HideLoadingOverlay()
    {
      try
      {
        _loadingOverlayCounter = Math.Max(0, _loadingOverlayCounter - 1);
        if (_loadingOverlayCounter > 0)
          return;

        FlushPendingLoadingMessage();
        _loadingMessageTimer.Stop();
        LoadingOverlay.Visibility = Visibility.Collapsed;
      }
      catch
      {
        // Ошибка индикатора не должна блокировать основную загрузку BCF
      }
    }

    /// <summary>
    /// Обновляет текст окна загрузки из любого потока.
    /// </summary>
    private void UpdateLoadingOverlayMessage(string message)
    {
      try
      {
        if (string.IsNullOrWhiteSpace(message))
          return;

        Interlocked.Exchange(ref _pendingLoadingMessage, message);
        if (Dispatcher.CheckAccess())
          FlushPendingLoadingMessage();
      }
      catch
      {
        // Ошибка текста статуса не должна блокировать основную загрузку BCF
      }
    }

    private void OnCloseBcf(object sender, ExecutedRoutedEventArgs e)
    {
      try
      {
        var guid = Guid.Parse(e.Parameter.ToString());
        var bcfs = _bcf.BcfFiles.Where(x => x.Id.Equals(guid));
        if (!bcfs.Any())
          return;
        var bcf = bcfs.First();

        if (CheckSaveBcf(bcf))
          _bcf.CloseFile(bcf);
      }
      catch (System.Exception ex1)
      {
        ExceptionUi.Show(ex1);
      }
    }

    private void CommandBinding_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
      var target = e.Source as Control;

      if (target != null)
      {
        e.CanExecute = true;
      }
      else
      {
        e.CanExecute = false;
      }
    }
    #endregion
    #region events 

    private void OnOpenWebProject(object sender, RoutedEventArgs routedEventArgs)
    {

    }

    /// <summary>
    /// Открывает диалог выбора BCF и выносит разбор архива с UI-потока.
    /// </summary>
    private async Task OpenBcfFilesFromDialogAsync()
    {
      try
      {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
          Filter = Loc.Get("OpenBcfFilter"),
          DefaultExt = ".bcf",
          Multiselect = true,
          RestoreDirectory = true,
          CheckFileExists = true,
          CheckPathExists = true
        };

        if (dialog.ShowDialog() != true)
          return;

        await OpenBcfFilesAsync(dialog.FileNames);
      }
      catch (Exception ex)
      {
        BcfHostCapabilities.ShowError(Loc.Error, ex.InnerException?.Message ?? ex.Message);
      }
    }

    /// <summary>
    /// Читает BCF-файлы в фоне и добавляет их в UI только после завершения разбора.
    /// </summary>
    private async Task OpenBcfFilesAsync(IEnumerable<string> paths)
    {
      try
      {
        List<string> validPaths = paths?
          .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
          .Distinct(StringComparer.OrdinalIgnoreCase)
          .ToList();

        if (validPaths == null || validPaths.Count == 0)
          return;

        ShowLoadingOverlay("Загрузка BCF...");
        UpdateLoadingOverlayMessage("Чтение BCF-архива...");

        var uiSw = System.Diagnostics.Stopwatch.StartNew();
        Debug.WriteLine("[BCFier-UI] before Task.Run: " + uiSw.ElapsedMilliseconds + "ms");

        var loadResult = await Task.Run(() =>
        {
          var result = new List<BcfFile>();
          var errors = new List<string>();
          foreach (string path in validPaths)
          {
            try
            {
              BcfFile file = BcfReader.Open(path);
              if (file != null)
                result.Add(file);
            }
            catch (Exception ex)
            {
              errors.Add(Path.GetFileName(path) + ": " + (ex.InnerException?.Message ?? ex.Message));
            }
          }

          return new { Files = result, Errors = errors };
        });

        List<BcfFile> loadedFiles = loadResult.Files;

        Debug.WriteLine("[BCFier-UI] Task.Run done: " + uiSw.ElapsedMilliseconds + "ms");
        UpdateLoadingOverlayMessage("Добавление замечаний в интерфейс...");
        await Task.Yield();

        foreach (BcfFile loadedFile in loadedFiles)
        {
          try
          {
            _bcf.AddOpenedFile(loadedFile);
            if (loadedFile.HasCustomFields)
            {
              var preview = new CustomFieldsPreviewWindow(loadedFile)
              {
                Owner = Window.GetWindow(this)
              };
              preview.ShowDialog();
              loadedFile.CustomFieldsVisible = preview.ShowCustomFields;
            }

            if (loadedFile.ReadWarnings != null && loadedFile.ReadWarnings.Count > 0)
            {
              loadResult.Errors.Add(
                loadedFile.Filename + ":\n" + string.Join("\n", loadedFile.ReadWarnings));
            }
          }
          catch (Exception ex)
          {
            loadResult.Errors.Add(
              (loadedFile.Filename ?? "BCF") + ": " + (ex.InnerException?.Message ?? ex.Message));
          }
        }

        Debug.WriteLine("[BCFier-UI] AddOpenedFile done: " + uiSw.ElapsedMilliseconds + "ms");
        UpdateLoadingOverlayMessage("Подготовка списков статусов...");
        await Task.Yield();

        _bcf.UpdateDropdowns();
        Debug.WriteLine("[BCFier-UI] UpdateDropdowns done: " + uiSw.ElapsedMilliseconds + "ms");

        // Закрываем оверлей разбора BCF: дальнейший resolve управляет своим индикатором.
        HideLoadingOverlay();

        if (loadResult.Errors.Count > 0)
        {
          BcfHostCapabilities.ShowError(
            Loc.Error,
            string.Join(Environment.NewLine + Environment.NewLine, loadResult.Errors));
        }

        // Связи с моделью и фильтр обновятся в колбэке после ExternalEvent Revit.
        ScheduleRefreshComponentLinks();
        Debug.WriteLine("[BCFier-UI] ScheduleRefreshComponentLinks queued: " + uiSw.ElapsedMilliseconds + "ms");
      }
      catch (Exception ex)
      {
        HideLoadingOverlay();
        BcfHostCapabilities.ShowError(Loc.Error, ex.InnerException?.Message ?? ex.Message);
      }
    }

    public void BcfFileClicked(string path)
    {
      _ = OpenBcfFilesAsync(new[] { path });
    }
    //prompt to save bcf
    //delete temp folders
    public bool onClosing(CancelEventArgs e)
    {
      foreach (var bcf in _bcf.BcfFiles)
      {
        //does not need to be saved, or user has saved it
        if (CheckSaveBcf(bcf))
        {
          //delete temp folder
          Utils.DeleteDirectory(bcf.TempPath);
        }
        else
          return true;
      }


      return false;
    }
    private void HelpBtnOnClick(object sender, RoutedEventArgs routedEventArgs)
    {
      const string url = "http://bcfier.com/";
      try
      {
        Process.Start(url);
      }
      catch (Win32Exception)
      {
        Process.Start("IExplore.exe", url);
      }
    }

    #endregion
    #region web
    // Автообновления отключены в форке SP-BCFier
    private void CheckUpdates() { }

    #endregion
    #region drag&drop
    private void Window_DragEnter(object sender, DragEventArgs e)
    {
      whitespace.Visibility = Visibility.Visible;
    }
    private void Window_DragLeave(object sender, DragEventArgs e)
    {
      whitespace.Visibility = Visibility.Hidden;
    }
    private void Window_Drop(object sender, DragEventArgs e)
    {
      try
      {
        whitespace.Visibility = Visibility.Hidden;
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
          var files = (string[])e.Data.GetData(DataFormats.FileDrop);
          _ = OpenBcfFilesAsync(files);
        }
      }
      catch (System.Exception ex1)
      {
        ExceptionUi.Show(ex1);
      }
    }
    private void Window_DragOver(object sender, DragEventArgs e)
    {
      try
      {
        var dropEnabled = true;

        if (e.Data.GetDataPresent(DataFormats.FileDrop, true))
        {
          var filenames = e.Data.GetData(DataFormats.FileDrop, true) as string[];
                  if (filenames.Any(x =>
                  {
                    var ext = Path.GetExtension(x).ToUpperInvariant();
                    return ext != ".BCFZIP" && ext != ".BCF";
                  }))
            dropEnabled = false;
        }
        else
          dropEnabled = false;

        if (!dropEnabled)
        {
          e.Effects = DragDropEffects.None;
          e.Handled = true;
        }
      }
      catch (System.Exception ex1)
      {
        ExceptionUi.Show(ex1);
      }
    }
    #endregion
    #region shortcuts
    public BcfFile SelectedBcf()
    {
      if (BcfTabControl.SelectedIndex == -1 || _bcf.BcfFiles.Count <= BcfTabControl.SelectedIndex)
        return null;
      return _bcf.BcfFiles.ElementAt(BcfTabControl.SelectedIndex);
    }
    #endregion

    #region SP-Service / BCF-API sync

    private async Task OpenBcfFromDatabaseAsync()
    {
      try
      {
        SpBcfServiceSettings settings = SpBcfServiceSettingsStore.Load();
        if (!settings.HasConnection)
        {
          MessageBox.Show(Loc.Get("SpServiceNotConfigured"), Loc.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
          return;
        }

        Guid? projectId = await PickAndRememberProjectAsync(settings).ConfigureAwait(true);
        if (projectId == null)
          return;

        ShowLoadingOverlay(Loc.Get("OpenFromDb"));
        using var client = new SpBcfServiceClient(settings.BaseUrl);
        await client.LoginAsync(settings.Login, settings.Password).ConfigureAwait(true);
        IReadOnlyList<SpBcfServiceClient.ProjectBcfFileItem> files =
          await client.ListProjectBcfFilesAsync(projectId.Value).ConfigureAwait(true);

        if (files == null || files.Count == 0)
        {
          HideLoadingOverlay();
          MessageBox.Show(Loc.Get("SpServiceNoBcfFiles"), Loc.Warning, MessageBoxButton.OK, MessageBoxImage.Information);
          return;
        }

        string currentModel = ResolveActiveModelName(null);
        var items = files
          .Select(f =>
          {
            bool match = !string.IsNullOrWhiteSpace(currentModel)
              && string.Equals(f.ModelName, currentModel, StringComparison.OrdinalIgnoreCase);
            return new SpBcfServiceClient.BcfFileItem
            {
              Id = f.Id,
              Name = f.Name,
              ModelName = f.ModelName,
              MatchesCurrentModel = match,
              BcfVersion = f.BcfVersion,
              UpdatedAt = f.UpdatedAt
            };
          })
          .OrderByDescending(i => i.MatchesCurrentModel)
          .ThenBy(i => i.ModelName, StringComparer.OrdinalIgnoreCase)
          .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
          .ToList();

        HideLoadingOverlay();
        var pick = new BcfServerPickWindow(items) { Owner = Window.GetWindow(this) };
        if (pick.ShowDialog() != true || pick.SelectedFile == null)
          return;

        ShowLoadingOverlay(Loc.Get("OpenFromDb"));
        BcfFile opened = await SpBcfServerLoader
          .OpenFromServerAsync(client, pick.SelectedFile.Id.ToString("D"))
          .ConfigureAwait(true);
        _bcf.AddOpenedFile(opened);
        _autoSyncEnabled = true;
        if (AutoSyncCheckBox != null)
          AutoSyncCheckBox.IsChecked = true;
        UpdateServerSyncUi();
        EnsureAutoSyncTimer();
        ScheduleRefreshComponentLinks();
      }
      catch (Exception ex)
      {
        MessageBox.Show(Loc.Format("SpServiceOpenError", ex.Message), Loc.Error, MessageBoxButton.OK, MessageBoxImage.Error);
      }
      finally
      {
        HideLoadingOverlay();
      }
    }

    private async Task SendSelectedToDatabaseAsync(bool interactive)
    {
      try
      {
        BcfFile bcf = SelectedBcf();
        if (bcf == null)
          return;

        SpBcfServiceSettings settings = SpBcfServiceSettingsStore.Load();
        if (!settings.HasConnection)
        {
          if (interactive)
            MessageBox.Show(Loc.Get("SpServiceNotConfigured"), Loc.Error, MessageBoxButton.OK, MessageBoxImage.Warning);
          return;
        }

        if (bcf.IsFromServer && bcf.ServerBcfFileId != null)
        {
          ShowLoadingOverlay(Loc.Get("SendToDb"));
          await SpBcfSyncService.SyncAsync(bcf, interactive).ConfigureAwait(true);
        }
        else
        {
          Guid? projectId = await PickAndRememberProjectAsync(
            settings,
            requireReportName: IsUnsetReportName(bcf.Filename),
            initialReportName: bcf.Filename,
            onReportName: name => bcf.Filename = name).ConfigureAwait(true);
          if (projectId == null)
            return;

          ShowLoadingOverlay(Loc.Get("SendToDb"));
          await PublishNewBcfToDatabaseAsync(bcf, settings, projectId.Value).ConfigureAwait(true);
        }

        if (interactive)
          MessageBox.Show(Loc.Get("SpServiceSyncOk"), Loc.ProductName, MessageBoxButton.OK, MessageBoxImage.Information);
        UpdateServerSyncUi();
      }
      catch (Exception ex)
      {
        if (interactive)
          MessageBox.Show(Loc.Format("SpServiceSyncError", ex.Message), Loc.Error, MessageBoxButton.OK, MessageBoxImage.Error);
      }
      finally
      {
        HideLoadingOverlay();
      }
    }

    /// <summary>Показывает выбор проекта и запоминает его как последний использованный.</summary>
    private async Task<Guid?> PickAndRememberProjectAsync(
      SpBcfServiceSettings settings,
      bool requireReportName = false,
      string initialReportName = null,
      Action<string> onReportName = null)
    {
      ShowLoadingOverlay(Loc.Get("SpServicePickProjectTitle"));
      IReadOnlyList<SpBcfServiceClient.ProjectItem> projects;
      try
      {
        using var client = new SpBcfServiceClient(settings.BaseUrl);
        await client.LoginAsync(settings.Login, settings.Password).ConfigureAwait(true);
        projects = await client.GetProjectsAsync().ConfigureAwait(true);
      }
      finally
      {
        HideLoadingOverlay();
      }

      if (projects == null || projects.Count == 0)
      {
        MessageBox.Show(Loc.Get("SpServiceNoProjects"), Loc.Warning, MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
      }

      Guid? preselect = Guid.TryParse(settings.ProjectId, out Guid saved) ? saved : (Guid?)null;
      string seedName = requireReportName && !IsUnsetReportName(initialReportName)
        ? initialReportName
        : string.Empty;
      var pick = new SpProjectPickWindow(projects, preselect, requireReportName, seedName)
      {
        Owner = Window.GetWindow(this)
      };
      if (pick.ShowDialog() != true || pick.SelectedProject == null)
        return null;

      if (requireReportName && !string.IsNullOrWhiteSpace(pick.SelectedReportName))
        onReportName?.Invoke(pick.SelectedReportName.Trim());

      settings.ProjectId = pick.SelectedProject.Id.ToString("D");
      SpBcfServiceSettingsStore.Save(settings);
      return pick.SelectedProject.Id;
    }

    /// <summary>Имя не задано: пустое или стандартное «Новый BCF-отчёт».</summary>
    private static bool IsUnsetReportName(string name)
    {
      if (string.IsNullOrWhiteSpace(name))
        return true;

      string trimmed = name.Trim();
      if (string.Equals(trimmed, Loc.Get("NewBcfReport"), StringComparison.OrdinalIgnoreCase))
        return true;

      // На случай смены языка после создания файла
      return string.Equals(trimmed, "New BCF Report", StringComparison.OrdinalIgnoreCase)
             || string.Equals(trimmed, "Новый BCF-отчёт", StringComparison.OrdinalIgnoreCase);
    }

    private async Task PublishNewBcfToDatabaseAsync(BcfFile bcf, SpBcfServiceSettings settings, Guid projectId)
    {
      string modelName = ResolveActiveModelName(bcf);
      if (string.IsNullOrWhiteSpace(modelName))
        modelName = "Model";

      string pathName = string.Empty;
      try
      {
        if (ComponentListHost.GetActiveDocumentInfo != null)
          pathName = ComponentListHost.GetActiveDocumentInfo().PathName ?? string.Empty;
      }
      catch
      {
        pathName = string.Empty;
      }

      string pathHash = null;
      if (!string.IsNullOrWhiteSpace(pathName))
      {
        using var sha = System.Security.Cryptography.SHA256.Create();
        byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(pathName));
        pathHash = BitConverter.ToString(hash).Replace("-", string.Empty);
      }

      string bcfName = string.IsNullOrWhiteSpace(bcf.Filename)
        ? "BCF"
        : Path.GetFileNameWithoutExtension(bcf.Filename);

      string tempPath = null;
      try
      {
        if (bcf.Issues != null && bcf.Issues.Count > 0)
        {
          tempPath = Path.Combine(
            Path.GetTempPath(),
            "BCFier",
            "publish-" + Guid.NewGuid().ToString("N") + ".bcf");
          Directory.CreateDirectory(Path.GetDirectoryName(tempPath) ?? Path.GetTempPath());
          if (!BcfWriter.Save(bcf, tempPath))
            throw new InvalidOperationException(Loc.Get("SpServicePublishSaveFailed"));
        }

        using var client = new SpBcfServiceClient(settings.BaseUrl);
        await client.LoginAsync(settings.Login, settings.Password).ConfigureAwait(true);
        SpBcfServiceClient.ProjectBcfFileItem created = await client.PublishBcfAsync(
          projectId,
          modelName,
          bcfName,
          modelName,
          pathHash,
          null,
          tempPath).ConfigureAwait(true);

        bcf.IsFromServer = true;
        bcf.ServerBcfFileId = created.Id;
        bcf.ServerRevision = created.Revision;
        bcf.ServerBcfVersion = created.BcfVersion ?? "3.0";
        bcf.HasBeenSaved = true;
        bcf.Filename = created.Name ?? bcf.Filename;
        _autoSyncEnabled = true;
        if (AutoSyncCheckBox != null)
          AutoSyncCheckBox.IsChecked = true;
        EnsureAutoSyncTimer();
      }
      finally
      {
        if (!string.IsNullOrWhiteSpace(tempPath))
        {
          try { File.Delete(tempPath); } catch { /* ignore */ }
        }
      }
    }

    private static string ResolveActiveModelName(BcfFile bcf)
    {
      try
      {
        if (ComponentListHost.GetActiveDocumentInfo != null)
        {
          (string title, _) = ComponentListHost.GetActiveDocumentInfo();
          if (!string.IsNullOrWhiteSpace(title))
            return title.Trim();
        }
      }
      catch
      {
        // ignore
      }

      if (bcf != null && !string.IsNullOrWhiteSpace(bcf.Filename))
        return Path.GetFileNameWithoutExtension(bcf.Filename);

      return string.Empty;
    }

    private void UpdateServerSyncUi()
    {
      try
      {
        BcfFile bcf = SelectedBcf();
        SpBcfServiceSettings settings = SpBcfServiceSettingsStore.Load();
        bool fromServer = bcf != null && bcf.IsFromServer;
        bool canSend = bcf != null && settings.HasConnection;
        if (AutoSyncCheckBox != null)
          AutoSyncCheckBox.Visibility = fromServer ? Visibility.Visible : Visibility.Collapsed;
        if (SendToDbBtn != null)
        {
          // Для серверного файла — только без автосинка; для локального — при настроенном подключении.
          bool showSend = canSend && (!fromServer || !_autoSyncEnabled);
          SendToDbBtn.Visibility = showSend ? Visibility.Visible : Visibility.Collapsed;
        }
      }
      catch
      {
        // ignore
      }
    }

    private void ApplyAutoSyncIntervalFromSettings()
    {
      SpBcfServiceSettings settings = SpBcfServiceSettingsStore.Load();
      EnsureAutoSyncTimer();
      if (_autoSyncTimer != null)
        _autoSyncTimer.Interval = TimeSpan.FromSeconds(settings.SyncIntervalSeconds);
    }

    private void EnsureAutoSyncTimer()
    {
      if (_autoSyncTimer != null)
      {
        UpdateAutoSyncTimerState();
        return;
      }

      SpBcfServiceSettings settings = SpBcfServiceSettingsStore.Load();
      _autoSyncTimer = new DispatcherTimer
      {
        Interval = TimeSpan.FromSeconds(settings.SyncIntervalSeconds)
      };
      _autoSyncTimer.Tick += async (s, e) =>
      {
        if (_isAutoSyncing)
          return;
        BcfFile bcf = SelectedBcf();
        if (bcf == null || !bcf.IsFromServer || !_autoSyncEnabled)
          return;
        _isAutoSyncing = true;
        try
        {
          await SpBcfSyncService.SyncAsync(bcf, interactive: false).ConfigureAwait(true);
        }
        catch
        {
          // silent in auto mode
        }
        finally
        {
          _isAutoSyncing = false;
        }
      };
      UpdateAutoSyncTimerState();
    }

    private void UpdateAutoSyncTimerState()
    {
      if (_autoSyncTimer == null)
        return;
      BcfFile bcf = SelectedBcf();
      bool run = bcf != null && bcf.IsFromServer && _autoSyncEnabled;
      if (run && !_autoSyncTimer.IsEnabled)
        _autoSyncTimer.Start();
      else if (!run && _autoSyncTimer.IsEnabled)
        _autoSyncTimer.Stop();
    }

    #endregion
  }
}
