using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using Autodesk.Revit.UI;
using Bcfier.Data.Utils;
using Bcfier.Localization;
using Bcfier.Revit.Entry;
using Bcfier.UserControls;

namespace Bcfier.Revit.Host
{
    /// <summary>
    /// Единая точка запуска BCF-панели (standalone add-in и внешние хосты).
    /// </summary>
    public static class BcfierRevitHost
    {
        private static RevitWindow _openWindow;
        private static IBcfierAuthorProvider _authorProvider;
        private static bool _wpfBootstrapped;
        private static bool _assemblyResolveHooked;

        /// <summary>
        /// Сторонние Revit-плагины (например, SpreadsheetEdit) несут свою копию ClosedXML /
        /// DocumentFormat.OpenXml. Если Revit грузит обе копии через LoadFrom из разных папок,
        /// типы этих сборок перестают быть взаимозаменяемыми (несовпадение generic-ограничений,
        /// напр. EnumValue&lt;CalculateModeValues&gt;) — Excel-импорт падает с невнятной ошибкой.
        /// Явно закрепляем за собой уже загруженную (или свою) копию, чтобы наш код всегда
        /// работал с одним и тем же экземпляром сборки.
        /// </summary>
        private static readonly string[] PrivateAssemblyNames =
        {
            "ClosedXML",
            "ClosedXML.Parser",
            "DocumentFormat.OpenXml",
            "DocumentFormat.OpenXml.Framework",
            "ExcelNumberFormat",
            "SixLabors.Fonts",
            "RBush",
            "Microsoft.Bcl.HashCode",
        };

        private static void EnsureAssemblyResolve()
        {
            if (_assemblyResolveHooked)
                return;
            _assemblyResolveHooked = true;
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                var requested = new AssemblyName(args.Name);
                if (Array.IndexOf(PrivateAssemblyNames, requested.Name) < 0)
                    return null;

                // Уже что-то загружено под этим именем (нами же, или конфликтующим плагином) —
                // переиспользуем его, чтобы не плодить третий экземпляр той же сборки.
                foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (string.Equals(loaded.GetName().Name, requested.Name, StringComparison.OrdinalIgnoreCase))
                        return loaded;
                }

                string dir = Path.GetDirectoryName(typeof(BcfierRevitHost).Assembly.Location);
                string path = Path.Combine(dir ?? string.Empty, requested.Name + ".dll");
                return File.Exists(path) ? Assembly.LoadFrom(path) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Текущий провайдер имени автора (Revit username).
        /// </summary>
        public static IBcfierAuthorProvider AuthorProvider => _authorProvider;

        /// <summary>
        /// Открывает modeless-окно BCFier. Повторный вызов активирует существующее окно.
        /// </summary>
        public static Window OpenPanel(UIApplication uiApp, IntPtr ownerHandle)
        {
            if (uiApp == null)
                throw new ArgumentNullException(nameof(uiApp));

            try
            {
                EnsureAssemblyResolve();
                // WPF в Revit: GetEntryAssembly() == null → относительные ResourceDictionary падают
                EnsureWpfForRevit();
                _authorProvider = new RevitAuthorProvider(uiApp);
                Bcfier.Data.BcfAuthorContext.GetCurrentAuthor = () => _authorProvider.GetCurrentAuthor();
                Bcfier.Data.BcfHostCapabilities.ConfigureRevit();
                BindTaskDialogErrorHandler();
                RevitComponentListHost.Register(uiApp);

                if (_openWindow != null)
                {
                    // Повторный вызов команды — только активируем существующее окно
                    if (_openWindow.WindowState == WindowState.Minimized)
                        _openWindow.WindowState = WindowState.Normal;

                    _openWindow.Activate();
                    return _openWindow;
                }

                var handler = new ExtEvntOpenView();
                var extEvent = ExternalEvent.Create(handler);
                _openWindow = new RevitWindow(uiApp, extEvent, handler);

                if (ownerHandle != IntPtr.Zero)
                {
                    new WindowInteropHelper(_openWindow) { Owner = ownerHandle };
                }

                _openWindow.Closed += (_, __) => _openWindow = null;
                _openWindow.Show();
                return _openWindow;
            }
            catch (Exception ex)
            {
                _openWindow = null;
                ShowRevitError(Loc.ProductName, FormatException(ex));
                return null;
            }
        }

        /// <summary>
        /// Подключает TaskDialog к Bcfier. Прямой сеттер ErrorDialog нельзя вызывать:
        /// если Revit уже загрузил старый Bcfier.dll, будет MissingMethodException.
        /// </summary>
        private static void BindTaskDialogErrorHandler()
        {
            try
            {
                PropertyInfo property = typeof(Bcfier.Data.BcfHostCapabilities).GetProperty("ErrorDialog");
                if (property == null || !property.CanWrite)
                    return;

                Action<string, string> handler = ShowRevitError;
                property.SetValue(null, handler);
            }
            catch
            {
                // Старый Bcfier.dll без ErrorDialog: ошибки пойдут в MessageBox
            }
        }

        /// <summary>
        /// Показывает ошибку через Revit TaskDialog, без обращения к новым членам Bcfier.dll.
        /// </summary>
        public static void ShowRevitError(string title, string message)
        {
            try
            {
                RevitExceptionUi.Show(message ?? string.Empty, title);
            }
            catch
            {
                try
                {
                    ExceptionUi.Show(message, title);
                }
                catch
                {
                    // Диалог не должен ронять команду Revit
                }
            }
        }

        private static string FormatException(Exception ex)
        {
            if (ex == null)
                return string.Empty;

            var parts = new System.Collections.Generic.List<string>();
            for (Exception current = ex; current != null; current = current.InnerException)
            {
                parts.Add(current.GetType().FullName + ": " + current.Message);
            }

            return string.Join(Environment.NewLine + " ---> ", parts)
                   + Environment.NewLine + Environment.NewLine
                   + ex;
        }

        /// <summary>
        /// Создаёт Application.Current и задаёт ResourceAssembly = Bcfier (темы/иконки).
        /// </summary>
        private static void EnsureWpfForRevit()
        {
            if (_wpfBootstrapped)
                return;

            if (Application.Current == null)
            {
                // Modeless WPF в хосте Revit без своего App.xaml
                new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
            }

            Assembly bcfierAsm = typeof(BcfierPanel).Assembly;
            try
            {
                if (Application.ResourceAssembly != bcfierAsm)
                    Application.ResourceAssembly = bcfierAsm;
            }
            catch
            {
                // Уже задан другой ResourceAssembly — pack URI с именем сборки всё равно сработают
            }

            Loc.ApplyCultureFromSettings();
            EnsureApplicationThemeResources();
            _wpfBootstrapped = true;
        }

        /// <summary>
        /// Подключает SpTheme на уровне Application для DynamicResource в modeless-окне Revit.
        /// </summary>
        private static void EnsureApplicationThemeResources()
        {
            try
            {
                if (Application.Current == null)
                    return;

                const string themeUri = "pack://application:,,,/Bcfier;component/Themes/SpTheme.xaml";
                bool hasTheme = Application.Current.Resources.MergedDictionaries
                    .Any(d => string.Equals(d.Source?.OriginalString, themeUri, StringComparison.OrdinalIgnoreCase));
                if (hasTheme)
                    return;

                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(themeUri, UriKind.Absolute)
                });
            }
            catch
            {
                // тема не критична для открытия окна
            }
        }
    }
}
