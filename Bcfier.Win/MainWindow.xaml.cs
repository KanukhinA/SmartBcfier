using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Bcfier.Bcf;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data;
using Bcfier.Localization;
using Bcfier.Themes;
using Bcfier.Windows;
using Bcfier.Data.Utils;

namespace Bcfier.Win
{
    /// <summary>
    /// Главное окно автономного Windows Viewer.
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Инициализирует окно и при наличии аргумента открывает BCF-файл.
        /// </summary>
        public MainWindow()
        {
            Loc.ApplyCultureFromSettings();
            BcfHostCapabilities.ConfigureStandaloneWin();
            InitializeComponent();

            try
            {
                string[] args = Environment.GetCommandLineArgs();
                if (args.Length > 1 && File.Exists(args[1]))
                {
                    string path = args[1];
                    Bcfier.Dispatcher.BeginInvoke(
                        DispatcherPriority.Background,
                        new Action(() => Bcfier.BcfFileClicked(path)));
                }
            }
            catch (Exception ex)
            {
                ExceptionUi.Show(ex);
            }
        }

        /// <summary>
        /// Передаёт закрытие в панель (проверка несохранённых отчётов).
        /// </summary>
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            try
            {
                e.Cancel = Bcfier.onClosing(e);
            }
            catch (Exception ex)
            {
                ExceptionUi.Show(ex);
            }
        }

        /// <summary>
        /// Добавляет viewpoint из изображения (без привязки к модели).
        /// </summary>
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

                var dialog = new AddViewWindow(issue, Bcfier.SelectedBcf().TempPath)
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                dialog.ShowDialog();
                if (dialog.DialogResult == true
                    || (dialog.AddViewControl != null && dialog.AddViewControl.Confirmed))
                    Bcfier.SelectedBcf().HasBeenSaved = false;
            }
            catch (Exception ex)
            {
                ExceptionUi.Show(ex);
            }
        }

        /// <summary>
        /// Редактирует существующий viewpoint (снимок) в Windows Viewer.
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

                var dialog = new AddViewWindow(issue, Bcfier.SelectedBcf().TempPath, view)
                {
                    Owner = this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                dialog.ShowDialog();
                if (!(dialog.DialogResult == true
                      || (dialog.AddViewControl != null && dialog.AddViewControl.Confirmed)))
                    return;

                bool snapshotChanged = dialog.AddViewControl != null && dialog.AddViewControl.SnapshotWasChanged;
                ViewpointEditAudit.AppendChangeComment(issue, view, snapshotChanged, elementsChanged: false);

                string snapPath = view.SnapshotPath;
                view.SnapshotPath = null;
                view.SnapshotPath = snapPath;

                Bcfier.SelectedBcf().HasBeenSaved = false;
            }
            catch (Exception ex)
            {
                ExceptionUi.Show(ex);
            }
        }

        /// <summary>
        /// Подключает Sp chrome после загрузки.
        /// </summary>
        private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                ComponentListHost.CreateWindow = (components, editMode) =>
                    new ComponentsList(components, editMode);

                SpWindowChrome.Apply(this);
                SpWindowChrome.EnsureHittableBackground(this);
                ApplyChromeClip();
            }
            catch (Exception ex)
            {
                ExceptionUi.Show(ex);
            }
        }

        /// <summary>
        /// Обновляет клип при смене состояния окна.
        /// </summary>
        private void Window_StateChanged(object sender, EventArgs e)
        {
            ApplyChromeClip();
        }

        /// <summary>
        /// Клип внутреннего chrome.
        /// </summary>
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

        /// <summary>
        /// Клип при изменении размера.
        /// </summary>
        private void ChromeRoot_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyChromeClip();
        }

        /// <summary>
        /// Перетаскивание окна за шапку.
        /// </summary>
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

        /// <summary>
        /// Закрывает окно кнопкой шапки.
        /// </summary>
        private void HeaderClose_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Close();
            }
            catch (Exception ex)
            {
                ExceptionUi.Show(ex);
            }
        }
    }
}
