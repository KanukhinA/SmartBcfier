using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using Bcfier.Bcf;
using Bcfier.Bcf.Bcf2;
using Bcfier.Localization;
using Bcfier.Revit.Data;
using Bcfier.Revit.Entry;
using Bcfier.Revit.Host;
using Bcfier.Themes;
using Bcfier.UserControls;
using BcfComponent = Bcfier.Bcf.Bcf2.Component;
using Bcfier.Data.Utils;

namespace Bcfier.Revit
{
  /// <summary>
  /// Диалог добавления viewpoint с выбором элементов.
  /// </summary>
  public partial class AddViewRevit : Window
  {
    private readonly UIDocument _uidoc;
    private readonly ElementSelectorViewModel _selectorViewModel = new ElementSelectorViewModel();
    private readonly ExtEvntPickModelElements _pickHandler;
    private readonly ExternalEvent _pickEvent;
    private bool _pickInProgress;
    private readonly HashSet<string> _initialElementIds =
      new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Id элементов до открытия диалога (для автокомментария).</summary>
    public IReadOnlyCollection<string> InitialElementIds => _initialElementIds;

    public AddViewRevit(Markup issue, string bcfTempFolder, UIDocument uidoc)
      : this(issue, bcfTempFolder, uidoc, null)
    {
    }

    /// <summary>
    /// Диалог добавления или правки viewpoint.
    /// </summary>
    public AddViewRevit(Markup issue, string bcfTempFolder, UIDocument uidoc, ViewPoint editingViewpoint)
    {
      try
      {
        _uidoc = uidoc;
        // ExternalEvent создаётся при OpenPanel (контекст API), не из modeless WPF
        RevitComponentListHost.TryGetPickModelElementsEvent(out _pickHandler, out _pickEvent);

        InitializeComponent();

        AddViewControl.Issue = issue;
        AddViewControl.TempFolder = bcfTempFolder;
        AddViewControl.TextBlockInfo.Text = Bcfier.Localization.Loc.ViewHas3dInfo;

        bool isEdit = editingViewpoint != null;
        if (isEdit)
        {
          Title = Loc.EditView;
          if (HeaderTitle != null)
            HeaderTitle.Text = Loc.EditView;
          AddViewControl.BeginEdit(editingViewpoint);
          foreach (string id in ViewpointEditAudit.CollectElementIds(editingViewpoint))
            _initialElementIds.Add(id);
        }

        var selectedRevitIds = uidoc.Selection.GetElementIds();
        var selectedIdStrings = new HashSet<string>(
          isEdit
            ? _initialElementIds
            : selectedRevitIds.Select(id => id.GetValue().ToString()),
          StringComparer.OrdinalIgnoreCase);

        var candidates = RevitView.BuildCandidateComponents(
          uidoc,
          out Dictionary<string, string> familyNames,
          out Dictionary<string, string> typeNames,
          out bool exceedsLimit);

        if (exceedsLimit)
        {
          // Слишком много элементов в виде: показываем только текущий Selection / связанные.
          string versionName = uidoc.Document.Application.VersionName;
          var selectionComponents = new List<BcfComponent>();
          var selFamilyNames = new Dictionary<string, string>();
          var selTypeNames = new Dictionary<string, string>();

          IEnumerable<ElementId> seedIds = isEdit
            ? selectedIdStrings
                .Select(id => int.TryParse(id, out int n) ? RevitIdHelper.FromInt(n) : ElementId.InvalidElementId)
                .Where(id => id != ElementId.InvalidElementId)
            : selectedRevitIds;

          foreach (ElementId id in seedIds)
          {
            Element element = uidoc.Document.GetElement(id);
            if (element == null) continue;
            selectionComponents.Add(RevitView.ToComponent(uidoc.Document, id, versionName));
            string key = id.GetValue().ToString();
            RevitView.GetElementDisplayNames(element, out string fn, out string tn);
            selFamilyNames[key] = fn;
            selTypeNames[key] = tn;
          }
          _selectorViewModel.LoadFromSelection(
            selectionComponents.ToArray(), selectionComponents.ToArray(),
            selFamilyNames, selTypeNames);
        }
        else
        {
          // В режиме правки дополняем кандидатов уже связанными элементами, если их нет в виде.
          var candidateList = candidates.ToList();
          if (isEdit)
          {
            string versionName = uidoc.Document.Application.VersionName;
            foreach (string idText in selectedIdStrings)
            {
              if (candidateList.Any(c => string.Equals(c.AuthoringToolId, idText, StringComparison.OrdinalIgnoreCase)))
                continue;
              if (!int.TryParse(idText, out int intId))
                continue;
              ElementId elementId = RevitIdHelper.FromInt(intId);
              Element element = uidoc.Document.GetElement(elementId);
              if (element == null)
                continue;
              BcfComponent component = RevitView.ToComponent(uidoc.Document, elementId, versionName);
              candidateList.Add(component);
              RevitView.GetElementDisplayNames(element, out string fn, out string tn);
              familyNames[idText] = fn;
              typeNames[idText] = tn;
            }
          }

          var currentSelection = candidateList
            .Where(c => selectedIdStrings.Contains(c.AuthoringToolId))
            .ToArray();

          _selectorViewModel.LoadFromSelection(currentSelection, candidateList.ToArray(), familyNames, typeNames);
        }

        ElementSelector.Bind(_selectorViewModel);

        if (!isEdit)
          GetRevitSnapshot();
      }
      catch (System.Exception ex1)
      {
        RevitExceptionUi.Show(ex1);
      }
    }

    /// <summary>true, если элементы изменились относительно исходного viewpoint.</summary>
    public bool ElementsWereChanged =>
      ViewpointEditAudit.ElementsChanged(
        _initialElementIds,
        ViewpointEditAudit.CollectElementIds(GetSelectedComponents()));

    /// <summary>Подключает frameless chrome SP после загрузки окна.</summary>
    private void AddViewRevit_OnLoaded(object sender, RoutedEventArgs e)
    {
      try
      {
        SpWindowChrome.Apply(this);
        SpWindowChrome.EnsureHittableBackground(this);
        ApplyChromeClip();
      }
      catch (Exception ex)
      {
        RevitExceptionUi.Show(ex);
      }
    }

    /// <summary>В развёрнутом окне радиус 0, иначе как SpRadius.Window.</summary>
    private void Window_StateChanged(object sender, EventArgs e)
    {
      ApplyChromeClip();
    }

    /// <summary>Подклипляет внутренний chrome как в SP.</summary>
    private void ApplyChromeClip()
    {
      try
      {
        double radius = SpWindowChrome.GetWindowClipRadius(this);
        SpWindowChrome.ClipToRoundedRect(ChromeRoot, radius);
      }
      catch
      {
        // клип не критичен
      }
    }

    /// <summary>Клип при любом изменении размеров.</summary>
    private void ChromeRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
      ApplyChromeClip();
    }

    /// <summary>Перетаскивание окна за шапку.</summary>
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

    /// <summary>Закрывает окно кнопкой шапки SP.</summary>
    private void HeaderClose_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        DialogResult = false;
      }
      catch
      {
        try { Close(); } catch { /* ignore */ }
      }
    }

    /// <summary>Подтверждает добавление viewpoint из footer.</summary>
    private void FooterAdd_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        AddViewControl?.Confirm();
      }
      catch (Exception ex)
      {
        RevitExceptionUi.Show(ex);
      }
    }

    /// <summary>Отменяет диалог из footer.</summary>
    private void FooterCancel_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        AddViewControl?.Cancel();
      }
      catch (Exception ex)
      {
        RevitExceptionUi.Show(ex);
      }
    }

    /// <summary>
    /// Скрывает диалог и запускает выбор элементов через ExternalEvent Revit.
    /// </summary>
    private void SelectElements_Click(object sender, RoutedEventArgs e)
    {
      if (_pickInProgress || _pickEvent == null || _pickHandler == null)
        return;

      Window owner = Owner;
      try
      {
        _pickInProgress = true;
        owner?.Hide();
        Hide();

        _pickHandler.Prompt = Loc.SelectModelElementsPrompt;
        _pickHandler.Completed = picked =>
        {
          Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher;
          dispatcher.BeginInvoke(new Action(() =>
          {
            try
            {
              RestoreWindows(owner);
              if (picked != null && picked.Count > 0)
                ApplyPickedElements(picked);
            }
            catch (Exception ex)
            {
              try { RestoreWindows(owner); } catch { /* ignore */ }
              RevitExceptionUi.Show(ex);
            }
            finally
            {
              _pickInProgress = false;
            }
          }), DispatcherPriority.Normal);
        };

        ExternalEventRequest request = _pickEvent.Raise();
        if (request != ExternalEventRequest.Accepted
            && request != ExternalEventRequest.Pending)
        {
          RestoreWindows(owner);
          _pickInProgress = false;
          _pickHandler.Completed = null;
        }
      }
      catch (System.Exception ex1)
      {
        try
        {
          RestoreWindows(owner);
        }
        catch
        {
          // ignore
        }

        _pickInProgress = false;
        _pickHandler.Completed = null;
        RevitExceptionUi.Show(ex1);
      }
    }

    /// <summary>
    /// Возвращает диалог и родительское окно после выбора в модели.
    /// </summary>
    private void RestoreWindows(Window owner)
    {
      try
      {
        if (!IsVisible)
          Visibility = System.Windows.Visibility.Visible;
        if (owner != null && !owner.IsVisible)
          owner.Visibility = System.Windows.Visibility.Visible;
        Activate();
      }
      catch
      {
        // Фокус/Show могут бросать при закрытом окне
      }
    }

    /// <summary>
    /// Добавляет выбранные в модели элементы в список viewpoint.
    /// </summary>
    private void ApplyPickedElements(IList<Reference> picked)
    {
      Document doc = _uidoc.Document;
      string versionName = doc.Application.VersionName;
      var elements = picked
        .Select(reference => doc.GetElement(reference))
        .Where(element => element != null)
        .ToList();

      if (elements.Count == 0)
        return;

      if (_selectorViewModel.IsManualIdMode)
      {
        _selectorViewModel.AppendManualIds(elements.Select(element => element.Id.GetValue().ToString()));
        ElementSelector.RefreshAfterModelPick();
        return;
      }

      var items = new List<ComponentSelectionItem>();
      foreach (Element element in elements)
      {
        BcfComponent component = RevitView.ToComponent(doc, element.Id, versionName);
        RevitView.GetElementDisplayNames(element, out string familyName, out string typeName);
        items.Add(ComponentSelectionItem.FromComponent(component, true, familyName, typeName));
      }

      _selectorViewModel.AddOrSelect(items);
      ElementSelector.RefreshAfterModelPick();
    }

    /// <summary>
    /// Выбранные элементы для viewpoint.
    /// </summary>
    public ICollection<ElementId> SelectedElementIds =>
      RevitView.ParseSelectedIds(_uidoc.Document, GetSelectedComponents());

    public BcfComponent[] GetSelectedComponents() => ElementSelector.GetSelectedComponents();

    /// <summary>Viewpoint сохранён, даже если DialogResult недоступен после Hide/Show.</summary>
    public bool AddViewConfirmed => AddViewControl != null && AddViewControl.Confirmed;

    private void GetRevitSnapshot()
    {
      string tempImg = null;
      try
      {
        string tempDir = Path.Combine(Path.GetTempPath(), "BCFier");
        Directory.CreateDirectory(tempDir);
        // FilePath без расширения: Revit сам добавляет .png по ImageFileType.
        string tempBase = Path.Combine(tempDir, "snapshot-" + Guid.NewGuid().ToString("N"));
        tempImg = tempBase + ".png";

        var options = new ImageExportOptions
        {
          FilePath = tempBase,
          HLRandWFViewsFileType = ImageFileType.PNG,
          ShadowViewsFileType = ImageFileType.PNG,
          ExportRange = ExportRange.VisibleRegionOfCurrentView,
          ZoomType = ZoomFitType.FitToPage,
          ImageResolution = ImageResolution.DPI_72,
          PixelSize = Bcfier.Data.Utils.ImagingUtils.BcfMaxPixelSize
        };
        _uidoc.Document.ExportImage(options);

        if (!File.Exists(tempImg))
        {
          // На части версий Revit путь может отличаться — берём свежий файл с этим префиксом.
          tempImg = Directory.GetFiles(tempDir, Path.GetFileName(tempBase) + ".*")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        }

        if (string.IsNullOrEmpty(tempImg) || !File.Exists(tempImg))
          throw new FileNotFoundException("Revit не создал файл снимка.");

        AddViewControl.AddViewpoint(tempImg);
      }
      catch (System.Exception ex1)
      {
        RevitExceptionUi.Show(ex1);
      }
      finally
      {
        try
        {
          if (!string.IsNullOrEmpty(tempImg) && File.Exists(tempImg))
            File.Delete(tempImg);
        }
        catch
        {
          // временный файл мог уже удалиться
        }
      }
    }
  }
}
