using System;
using System.Windows;
using Bcfier.Localization;

namespace Bcfier.Data
{
    /// <summary>
    /// Возможности хоста (Revit add-in vs Windows Viewer).
    /// Задаются при старте приложения; по умолчанию всё выключено (безопасный режим без модели).
    /// </summary>
    public static class BcfHostCapabilities
    {
        /// <summary>
        /// Хост-диалог ошибки. В Revit подключается TaskDialog, иначе MessageBox.
        /// </summary>
        public static Action<string, string> ErrorDialog { get; set; }

        /// <summary>Кнопки «Открыть вид» / изоляция в 3D-модели.</summary>
        public static bool SupportsOpenViewInModel { get; private set; }

        /// <summary>Фильтр «только замечания активного документа».</summary>
        public static bool SupportsActiveDocumentFilter { get; private set; }

        /// <summary>Вкладка настроек Revit.</summary>
        public static bool SupportsRevitSettings { get; private set; }

        /// <summary>Выделение элементов в модели из списка компонентов.</summary>
        public static bool SupportsSelectInModel { get; private set; }

        /// <summary>
        /// Режим автономного Windows Viewer: без привязки к BIM-модели.
        /// </summary>
        public static void ConfigureStandaloneWin()
        {
            SupportsOpenViewInModel = false;
            SupportsActiveDocumentFilter = false;
            SupportsRevitSettings = false;
            SupportsSelectInModel = false;
            ErrorDialog = null;
        }

        /// <summary>
        /// Режим Revit add-in: полный набор команд модели.
        /// </summary>
        public static void ConfigureRevit()
        {
            SupportsOpenViewInModel = true;
            SupportsActiveDocumentFilter = true;
            SupportsRevitSettings = true;
            SupportsSelectInModel = true;
        }

        /// <summary>
        /// Показывает ошибку через диалог хоста (TaskDialog в Revit) или MessageBox.
        /// </summary>
        public static void ShowError(string title, string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message))
                    return;

                string dialogTitle = string.IsNullOrWhiteSpace(title) ? Loc.Error : title;
                if (ErrorDialog != null)
                {
                    ErrorDialog(dialogTitle, message);
                    return;
                }

                MessageBox.Show(message, dialogTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch
            {
                try
                {
                    MessageBox.Show(message, title ?? Loc.Error, MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch
                {
                    // Диалог не должен ронять вызывающий код
                }
            }
        }
    }
}
