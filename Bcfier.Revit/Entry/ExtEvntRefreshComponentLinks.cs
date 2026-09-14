using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using Bcfier.Bcf.Bcf2;
using Bcfier.Data;
using Bcfier.Revit.Host;

namespace Bcfier.Revit.Entry
{
    /// <summary>
    /// Прогревает кэш сопоставления элементов на потоке Revit API и обновляет UI.
    /// </summary>
    public class ExtEvntRefreshComponentLinks : IExternalEventHandler
    {
        /// <summary>
        /// Компоненты BCF, для которых нужно построить индекс (если заданы).
        /// </summary>
        public IList<Component> Components { get; set; }

        /// <summary>
        /// Колбэк обновления ссылок компонентов в UI после подготовки кэша Revit.
        /// </summary>
        public Action UiRefreshAction { get; set; }

        /// <summary>
        /// Текущее короткое сообщение о стадии обработки.
        /// </summary>
        public string ProgressMessage { get; set; }

        /// <summary>
        /// Публикует текущее сообщение о стадии в WPF UI.
        /// </summary>
        public void ReportProgressToUi()
        {
            try
            {
                string message = ProgressMessage;
                if (string.IsNullOrWhiteSpace(message))
                    return;

                var dispatcher = Application.Current?.Dispatcher;
                Action report = () => ComponentListHost.ReportProgress?.Invoke(message);
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(report, DispatcherPriority.Background);
                    return;
                }

                if (dispatcher != null)
                    dispatcher.BeginInvoke(report, DispatcherPriority.Background);
                else
                    report();
            }
            catch
            {
                // Ошибка статуса не должна ломать основную загрузку
            }
        }

        /// <summary>
        /// Готовит кэш модели и вызывает обновление UI на WPF-потоке с низким приоритетом.
        /// </summary>
        public void Execute(UIApplication app)
        {
            try
            {
                ProgressMessage = "Revit: подготовка поиска элементов...";
                ReportProgressToUi();
                RevitComponentListHost.WarmCaches(app, Components);
                ProgressMessage = "Revit: кэш элементов подготовлен.";
                ReportProgressToUi();
            }
            catch
            {
                // Ошибка кэша не должна блокировать открытие BCF
            }

            Action refresh = UiRefreshAction;
            Components = null;
            UiRefreshAction = null;
            if (refresh == null)
                return;

            try
            {
                ProgressMessage = "Revit: обновление списка замечаний...";
                ReportProgressToUi();
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.CheckAccess())
                {
                    dispatcher.BeginInvoke(refresh, DispatcherPriority.Background);
                    return;
                }

                if (dispatcher != null)
                    dispatcher.BeginInvoke(refresh, DispatcherPriority.Background);
                else
                    refresh();
            }
            catch
            {
                try { refresh(); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Имя обработчика для журнала Revit.
        /// </summary>
        public string GetName() => Bcfier.Localization.Loc.ProductName + " Refresh Component Links";
    }
}
