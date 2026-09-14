using System;
using System.Windows;
using System.Windows.Input;
using Bcfier.Bcf.Bcf2;
using Bcfier.Localization;
using Bcfier.Themes;
using Bcfier.Data.Utils;

namespace Bcfier.Windows
{
    /// <summary>
    /// Sp-диалог добавления viewpoint из файла изображения (Windows Viewer и общие сценарии без Revit).
    /// </summary>
    public partial class AddViewWindow : Window
    {
        /// <summary>
        /// Создаёт диалог для указанного замечания и временной папки BCF.
        /// </summary>
        public AddViewWindow(Markup issue, string bcfTempFolder)
            : this(issue, bcfTempFolder, null)
        {
        }

        /// <summary>
        /// Диалог добавления или правки viewpoint.
        /// </summary>
        public AddViewWindow(Markup issue, string bcfTempFolder, ViewPoint editingViewpoint)
        {
            Loc.ApplyCultureFromSettings();
            InitializeComponent();

            try
            {
                AddViewControl.Issue = issue;
                AddViewControl.TempFolder = bcfTempFolder;
                if (editingViewpoint != null)
                {
                    Title = Loc.EditView;
                    if (HeaderTitle != null)
                        HeaderTitle.Text = Loc.EditView;
                    AddViewControl.BeginEdit(editingViewpoint);
                }
            }
            catch (Exception ex)
            {
                ExceptionUi.Show(ex);
            }
        }

        /// <summary>
        /// Подключает frameless chrome после загрузки.
        /// </summary>
        private void AddViewWindow_OnLoaded(object sender, RoutedEventArgs e)
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
        /// Закрывает окно без сохранения.
        /// </summary>
        private void HeaderClose_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AddViewControl?.Cancel();
            }
            catch
            {
                try { DialogResult = false; }
                catch
                {
                    try { Close(); } catch { /* ignore */ }
                }
            }
        }

        /// <summary>
        /// Подтверждает добавление вида.
        /// </summary>
        private void FooterAdd_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AddViewControl?.Confirm();
            }
            catch (Exception ex)
            {
                ExceptionUi.Show(ex);
            }
        }

        /// <summary>
        /// Отменяет диалог.
        /// </summary>
        private void FooterCancel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AddViewControl?.Cancel();
            }
            catch
            {
                try { DialogResult = false; }
                catch
                {
                    try { Close(); } catch { /* ignore */ }
                }
            }
        }
    }
}
