using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Bcfier.Localization;
using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bcfier.Data.Utils;

namespace Bcfier.Revit.Entry
{

  [Transaction(TransactionMode.Manual)]
  public class AppMain : IExternalApplication
  {
    private string _path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

    #region Revit IExternalApplciation Implementation

    /// <summary>
    /// Startup
    /// </summary>
    /// <param name="application"></param>
    /// <returns></returns>
    public Result OnStartup(UIControlledApplication application)
    {
      try
      {
        Loc.ApplyCultureFromSettings();
        // Панель и кнопка на ленте — локализованные подписи
        RibbonPanel panel = application.CreateRibbonPanel(Loc.RibbonPanel);

        PushButton pushButton = panel.AddItem(new PushButtonData("BCFier",
                                                                     Loc.RibbonButton,
                                                                     Path.Combine(_path, "Bcfier.Revit.dll"),
                                                                     "Bcfier.Revit.Entry.CmdMain")) as PushButton;

        if (pushButton != null)
        {
          pushButton.Image = LoadPngImgSource("Bcfier.Assets.SP_BCFier16.png", Path.Combine(_path, "Bcfier.dll"));
          pushButton.LargeImage = LoadPngImgSource("Bcfier.Assets.SP_BCFier32.png", Path.Combine(_path, "Bcfier.dll"));
          pushButton.ToolTip = Loc.RibbonTooltip;
        }
      }
      catch (Exception ex1)
      {
        ExceptionUi.Show(ex1);
        return Result.Failed;
      }

      return Result.Succeeded;
    }

    /// <summary>
    /// Shut Down
    /// </summary>
    /// <param name="application"></param>
    /// <returns></returns>
    public Result OnShutdown(UIControlledApplication application)
    {
      return Result.Succeeded;
    }

    #endregion

    #region Private Members

    /// <summary>
    /// Get System Architecture
    /// </summary>
    /// <returns></returns>
    static string ProgramFilesx86()
    {
      if (8 == IntPtr.Size || (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432"))))
        return Environment.GetEnvironmentVariable("ProgramFiles(x86)");

      return Environment.GetEnvironmentVariable("ProgramFiles");
    }


    /// <summary>
    /// Load an Image Source from File
    /// </summary>
    /// <param name="sourceName"></param>
    /// <param name="path"></param>
    /// <returns></returns>
    private ImageSource LoadPngImgSource(string sourceName, string path)
    {

      try
      {
        // Assembly & Stream
        var assembly = Assembly.LoadFrom(Path.Combine(path));
        var icon = assembly.GetManifestResourceStream(sourceName);

        // Decoder
        PngBitmapDecoder m_decoder = new PngBitmapDecoder(icon, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);

        // Source
        ImageSource m_source = m_decoder.Frames[0];
        return (m_source);

      }
      catch { }

      // Fail
      return null;

    }

    #endregion

  }
}