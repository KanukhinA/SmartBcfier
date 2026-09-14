using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Input;

using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Bcfier.Bcf;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data;
using Bcfier.Revit.Data;
using Bcfier.Revit.Entry;
using System.ComponentModel;
using System.Threading.Tasks;
using Component = Bcfier.Bcf.Bcf2.Component;
using Point = Bcfier.Bcf.Bcf2.Point;
using Bcfier.Data.Utils;
using Bcfier.Localization;
using Bcfier.Themes;

namespace Bcfier.Revit
{
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class RevitWindow : Window
  {
    private ExternalEvent ExtEvent;
    private ExtEvntOpenView Handler;
    private UIApplication uiapp;

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="_uiapp"></param>
    /// <param name="exEvent"></param>
    /// <param name="handler"></param>
    public RevitWindow(UIApplication _uiapp, ExternalEvent exEvent, ExtEvntOpenView handler)
    {
      // Культура до InitializeComponent — иначе {x:Static loc:...} берёт дефолт потока
      Loc.ApplyCultureFromSettings();
      InitializeComponent();
      TryBindOpenViewIsolatedCommand();

      Dispatcher.UnhandledException += OnDispatcherUnhandledException;

      try
      {
        ExtEvent = exEvent;
        Handler = handler;
        uiapp = _uiapp;
      }
      catch (Exception ex1)
      {
        RevitExceptionUi.Show(ex1);
      }
    }

    /// <summary>
    /// Перехватывает ошибки WPF в modeless-окне, чтобы они не закрывали Revit.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
      try
      {
        e.Handled = true;
        RevitExceptionUi.Show(e.Exception, Loc.Error);
      }
      catch
      {
        e.Handled = true;
      }
    }

    #region commands
    /// <summary>
    /// Подключает OpenViewIsolated без прямой ссылки на поле:
    /// при уже загруженном старом Bcfier.dll MissingFieldException не роняет окно.
    /// </summary>
    private void TryBindOpenViewIsolatedCommand()
    {
      try
      {
        FieldInfo field = typeof(Commands).GetField(
            "OpenViewIsolated",
            BindingFlags.Public | BindingFlags.Static);
        if (field?.GetValue(null) is ICommand command)
          CommandBindings.Add(new CommandBinding(command, OnOpenViewIsolated));
      }
      catch
      {
        // Старый Bcfier.dll — кнопка изоляции просто не будет обработана хостом
      }
    }

    /// <summary>
    /// Raises the External Event to accomplish a transaction in a modeless window
    /// http://help.autodesk.com/view/RVT/2014/ENU/?guid=GUID-0A0D656E-5C44-49E8-A891-6C29F88E35C0
    /// http://matteocominetti.com/starting-a-transaction-from-an-external-application-running-outside-of-api-context-is-not-allowed/
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OnOpenView(object sender, ExecutedRoutedEventArgs e)
    {
      OpenViewInternal(e, false);
    }

    /// <summary>
    /// Открывает viewpoint с временной изоляцией элементов по правилам BCF.
    /// </summary>
    private void OnOpenViewIsolated(object sender, ExecutedRoutedEventArgs e)
    {
      OpenViewInternal(e, true);
    }

    /// <summary>
    /// Открывает viewpoint в 3D и при необходимости включает временную изоляцию.
    /// </summary>
    private void OpenViewInternal(ExecutedRoutedEventArgs e, bool applyIsolation)
    {
      try
      {
        if (Bcfier.SelectedBcf() == null)
          return;
        var view = e.Parameter as ViewPoint;
        if (view == null)
          return;

        UIDocument uidoc = uiapp?.ActiveUIDocument;
        if (uidoc?.ActiveView == null)
          return;

        if (uidoc.ActiveView.ViewType == ViewType.Schedule)
        {
          MessageBox.Show(Loc.Get("ScheduleSnapshotWarning"),
              Loc.Warning, MessageBoxButton.OK, MessageBoxImage.Warning);
          return;
        }

        if (view.VisInfo == null)
        {
          RevitExceptionUi.Show(Loc.Get("ViewpointNull"), Loc.Error);
          return;
        }

        Handler.v = view.VisInfo;
        Handler.ApplyVisibilityIsolation = applyIsolation;
        ExtEvent.Raise();
      }
      catch (System.Exception ex1)
      {
        RevitExceptionUi.Show(ex1, "Error opening a View!");
      }
    }
    /// <summary>
    /// Same as in the windows app, but here we generate a VisInfo that is attached to the view
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OnAddView(object sender, ExecutedRoutedEventArgs e)
    {
      try
      {

        if (Bcfier.SelectedBcf() == null)
          return;
        var issue = e.Parameter as Markup;
        if (issue == null)
        {
          MessageBox.Show(Loc.Get("NoIssueSelected"), Loc.Error, MessageBoxButton.OK, MessageBoxImage.Error);
          return;
        }

        var dialog = new AddViewRevit(issue, Bcfier.SelectedBcf().TempPath, uiapp.ActiveUIDocument);
        dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.ShowDialog();
        if ((dialog.DialogResult.HasValue && dialog.DialogResult.Value)
            || dialog.AddViewConfirmed)
        {
          if (dialog.GetSelectedComponents().Length > 1000)
          {
            MessageBox.Show(
              Loc.TooManyComponents,
              Loc.RibbonPanel,
              MessageBoxButton.OK,
              MessageBoxImage.Warning);
          }

          //generate and set the VisInfo
          issue.Viewpoints.Last().VisInfo = RevitView.GenerateViewpoint(
            uiapp.ActiveUIDocument,
            dialog.SelectedElementIds);

          //get filename
          UIDocument uidoc = uiapp.ActiveUIDocument;

          if (uidoc.Document.Title != null)
            issue.Header[0].Filename = uidoc.Document.Title;
          else
            issue.Header[0].Filename = Loc.Get("UnknownFilename");

          Bcfier.SelectedBcf().HasBeenSaved = false;
          Bcfier.ScheduleRefreshComponentLinks();
        }

      }
      catch (System.Exception ex1)
      {
        RevitExceptionUi.Show(ex1, "Error adding a View!");
      }
    }

    /// <summary>
    /// Редактирует существующий viewpoint: то же меню, что «Добавить вид».
    /// </summary>
    private void OnEditView(object sender, ExecutedRoutedEventArgs e)
    {
      try
      {
        if (Bcfier.SelectedBcf() == null)
          return;

        var values = e.Parameter as object[];
        ViewPoint view = values != null && values.Length > 0 ? values[0] as ViewPoint : e.Parameter as ViewPoint;
        Markup issue = values != null && values.Length > 1 ? values[1] as Markup : null;

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

        var dialog = new AddViewRevit(issue, Bcfier.SelectedBcf().TempPath, uiapp.ActiveUIDocument, view)
        {
          WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        dialog.ShowDialog();
        if (!((dialog.DialogResult.HasValue && dialog.DialogResult.Value) || dialog.AddViewConfirmed))
          return;

        if (dialog.GetSelectedComponents().Length > 1000)
        {
          MessageBox.Show(
            Loc.TooManyComponents,
            Loc.RibbonPanel,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        }

        bool snapshotChanged = dialog.AddViewControl != null && dialog.AddViewControl.SnapshotWasChanged;
        bool elementsChanged = dialog.ElementsWereChanged;

        view.VisInfo = RevitView.GenerateViewpoint(
          uiapp.ActiveUIDocument,
          dialog.SelectedElementIds);

        UIDocument uidoc = uiapp.ActiveUIDocument;
        if (issue.Header != null && issue.Header.Count > 0)
        {
          issue.Header[0].Filename = uidoc?.Document?.Title != null
            ? uidoc.Document.Title
            : Loc.Get("UnknownFilename");
        }

        ViewpointEditAudit.AppendChangeComment(issue, view, snapshotChanged, elementsChanged);

        // Обновляем привязку снимка (тот же путь файла после перезаписи).
        string snapPath = view.SnapshotPath;
        view.SnapshotPath = null;
        view.SnapshotPath = snapPath;

        Bcfier.SelectedBcf().HasBeenSaved = false;
        Bcfier.ScheduleRefreshComponentLinks();
      }
      catch (System.Exception ex1)
      {
        RevitExceptionUi.Show(ex1, Loc.EditView);
      }
    }
    #endregion

    #region private methods

    /// <summary>
    /// passing event to the user control
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Window_Closing(object sender, CancelEventArgs e)
    {
      e.Cancel = Bcfier.onClosing(e);
    }
    #endregion

    //stats
    /// <summary>Подключает frameless chrome SP после загрузки окна.</summary>
    private void RevitWindow_OnLoaded(object sender, RoutedEventArgs e)
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

    /// <summary>В развёрнутом окне радиус 0, иначе 10 как SpRadius.Window.</summary>
    private void Window_StateChanged(object sender, EventArgs e)
    {
      ApplyChromeClip();
    }

    /// <summary>Подклипляем внутренний chrome как в SP.</summary>
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
        Close();
      }
      catch (Exception ex)
      {
        RevitExceptionUi.Show(ex);
      }
    }

        private void Bcfier_Loaded(object sender, RoutedEventArgs e)
        {

        }
    }
}