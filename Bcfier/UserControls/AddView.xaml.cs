using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Bcfier.Bcf;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data.Utils;
using Bcfier.Localization;

namespace Bcfier.UserControls
{
  /// <summary>
  /// Control used to add views
  /// I wanted to extand a window insted than embedding a User Control but if was giving problems
  /// </summary>
  public partial class AddView : UserControl
  {
    internal string TempFolder;
    internal Markup Issue;

    /// <summary>Существующий viewpoint при редактировании; null — создание нового.</summary>
    internal ViewPoint EditingViewpoint { get; private set; }

    /// <summary>Снимок заменён/отредактирован в диалоге.</summary>
    private bool _snapshotDirty;

    public AddView()
    {
      Loc.ApplyCultureFromSettings();
      InitializeComponent();
    }

    public AddView(Markup issue, string bcfTempFolder)
    {
      try
      {
        Loc.ApplyCultureFromSettings();
        InitializeComponent();
        Issue = issue;
        TempFolder = bcfTempFolder;
      }
      catch (System.Exception ex1)
      {
        ExceptionUi.Show(ex1);
      }
    }

    /// <summary>
    /// Переводит контроль в режим правки существующего viewpoint.
    /// </summary>
    public void BeginEdit(ViewPoint viewpoint)
    {
      EditingViewpoint = viewpoint;
      _snapshotDirty = false;
      if (!string.IsNullOrWhiteSpace(viewpoint?.SnapshotPath) && File.Exists(viewpoint.SnapshotPath))
      {
        AddViewpoint(viewpoint.SnapshotPath);
        _snapshotDirty = false;
      }
    }

    #region events
    private void Button_RemoveImage(object sender, RoutedEventArgs e)
    {
      SnapshotImg.Source = null;
      _snapshotDirty = true;
    }

    //LOAD EXTERNAL IMAGE
    private void Button_LoadImage(object sender, RoutedEventArgs e)
    {

      var dialog = new Microsoft.Win32.OpenFileDialog
      {
        Filter = Loc.Get("OpenImageFilter"),
        DefaultExt = ".png",
        CheckFileExists = true,
        CheckPathExists = true,
        RestoreDirectory = true
      };
      var result = dialog.ShowDialog(); // Show the dialog.

      if (result != true) // Test result.
        return;

      AddViewpoint(dialog.FileName);
    }

    internal void AddViewpoint(string imagePath)
    {
      SnapshotImg.Source = ImagingUtils.ImageSourceFromPath(imagePath);
      _snapshotDirty = true;
    }

    /// <summary>
    /// Отменяет диалог (закрывает родительское окно с DialogResult=false).
    /// </summary>
    public void Cancel()
    {
      try
      {
        CloseHost(false);
      }
      catch (Exception ex)
      {
        ExceptionUi.Show(ex);
      }
    }

    /// <summary>
    /// Подтверждает добавление или обновление viewpoint и закрывает диалог.
    /// </summary>
    public void Confirm()
    {
      try
      {
        if (Issue == null)
          return;

        if (!Directory.Exists(Path.Combine(TempFolder, Issue.Topic.Guid)))
          Directory.CreateDirectory(Path.Combine(TempFolder, Issue.Topic.Guid));

        ViewPoint view = EditingViewpoint;
        bool isEdit = view != null;
        if (!isEdit)
          view = new ViewPoint(!Issue.Viewpoints.Any());

        if (!string.IsNullOrWhiteSpace(CommentBox.Text))
        {
          var c = new Comment
          {
            Comment1 = CommentBox.Text.Trim(),
            Author = Utils.GetUsername(),
            Date = DateTime.Now,
            Viewpoint = new CommentViewpoint { Guid = view.Guid }
          };
          Issue.Comment.Add(c);
        }

        string path = isEdit && !string.IsNullOrWhiteSpace(view.SnapshotPath)
          ? view.SnapshotPath
          : Path.Combine(TempFolder, Issue.Topic.Guid, view.Snapshot);

        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
          Directory.CreateDirectory(directory);

        if (!isEdit || _snapshotDirty)
        {
          ImagingUtils.SaveImageSource(SnapshotImg.Source, path);
          view.SnapshotPath = path;
        }

        if (!isEdit)
          Issue.Viewpoints.Add(view);

        CloseHost(true);
      }
      catch (Exception ex)
      {
        ExceptionUi.Show(ex);
      }
    }

    /// <summary>true, если режим правки существующего вида.</summary>
    public bool IsEditing => EditingViewpoint != null;

    /// <summary>true, если снимок изменился в диалоге.</summary>
    public bool SnapshotWasChanged => !IsEditing || _snapshotDirty;

    /// <summary>true, если viewpoint сохранён, даже если DialogResult недоступен.</summary>
    public bool Confirmed { get; private set; }

    /// <summary>
    /// Закрывает окно: DialogResult для ShowDialog, иначе Close.
    /// После Hide()+Show() в Revit окно уже не диалог, DialogResult бросает.
    /// </summary>
    private void CloseHost(bool result)
    {
      Confirmed = result;
      Window win = Window.GetWindow(this);
      if (win == null)
        return;

      try
      {
        win.DialogResult = result;
      }
      catch (InvalidOperationException)
      {
        try { win.Close(); }
        catch { /* окно уже закрыто */ }
      }
    }

    private void EditSnapshot_Click(object sender, RoutedEventArgs e)
    {
      string tempImg = null;
      try
      {
        if (SnapshotImg.Source == null)
        {
          MessageBox.Show(Loc.Get("InvalidSnapshot"), Loc.Error, MessageBoxButton.OK, MessageBoxImage.Error);
          return;
        }

        string editSnap = "mspaint";
        string customeditor = UserSettings.Get("editSnap");
        if (!string.IsNullOrEmpty(customeditor) && File.Exists(customeditor))
          editSnap = customeditor;

        string tempDir = Path.Combine(Path.GetTempPath(), "BCFier");
        Directory.CreateDirectory(tempDir);
        tempImg = Path.Combine(tempDir, "annotate-" + Guid.NewGuid().ToString("N") + ".png");
        ImagingUtils.SaveImageSource(SnapshotImg.Source, tempImg);

        if (!File.Exists(tempImg))
        {
          MessageBox.Show(Loc.Get("InvalidSnapshot"), Loc.Error, MessageBoxButton.OK, MessageBoxImage.Error);
          return;
        }

        var paint = new Process();
        var paintInfo = new ProcessStartInfo(editSnap, "\"" + tempImg + "\"")
        {
          // Shell нужен для современного Paint: процесс-заглушка иначе сразу завершается.
          UseShellExecute = true
        };
        paint.StartInfo = paintInfo;
        paint.Start();
        try { paint.WaitForExit(); }
        catch { /* у части редакторов WaitForExit недоступен */ }

        // Win11 Paint часто отпускает процесс раньше файла — ждём разблокировки.
        ImagingUtils.WaitForExternalEditor(tempImg);

        // Сбрасываем Source, иначе WPF может оставить старое превью.
        SnapshotImg.Source = null;
        AddViewpoint(tempImg);
      }
      catch (System.Exception ex1)
      {
        ExceptionUi.Show(ex1);
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
          // Файл может ещё держаться редактором.
        }
      }
    }
    private bool IsProcessOpen(string name)
    {
      foreach (Process clsProcess in Process.GetProcesses())
      {

        if (clsProcess.ProcessName.Contains(name))
        {
          return true;
        }
      }
      //otherwise we return a false
      return false;
    }
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
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
          return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (!files.Any() || !File.Exists(files.First()))
          return;
        AddViewpoint(files.First());
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
        var extensions = new List<string> { ".jpg", ".jpeg", ".gif", ".bmp", ".png", ".tif" };
        var dropEnabled = true;

        if (e.Data.GetDataPresent(DataFormats.FileDrop, true))
        {
          var filenames = e.Data.GetData(DataFormats.FileDrop, true) as string[];
          if (filenames.Count() != 1 || !extensions.Contains(Path.GetExtension(filenames.First()).ToLowerInvariant()))
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

  }
}
