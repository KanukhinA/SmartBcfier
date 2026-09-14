using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Bcfier.Localization;

namespace Bcfier.Revit.Standalone
{
    /// <summary>
    /// Регистрация вкладки BCFier на ленте Revit (standalone-сборка для подрядчиков).
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class AppMain : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                Loc.ApplyCultureFromSettings();
                string path = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                RibbonPanel panel = application.CreateRibbonPanel(Loc.RibbonPanel);

                var pushButton = panel.AddItem(new PushButtonData(
                    "BCFier",
                    Loc.RibbonButton,
                    System.IO.Path.Combine(path, "BCFier.Revit.Addin.dll"),
                    "Bcfier.Revit.Standalone.CmdMain")) as PushButton;

                if (pushButton != null)
                {
                    // Иконки SP_BCFier из Assets Standalone-сборки
                    pushButton.Image = LoadPng("Bcfier.Revit.Standalone.Assets.SP_BCFier16.png");
                    pushButton.LargeImage = LoadPng("Bcfier.Revit.Standalone.Assets.SP_BCFier32.png");
                    pushButton.ToolTip = Loc.RibbonTooltip;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), Loc.RibbonPanel);
                return Result.Failed;
            }

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

        private static ImageSource LoadPng(string resourceName)
        {
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                        return null;

                    var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
                    return decoder.Frames[0];
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
