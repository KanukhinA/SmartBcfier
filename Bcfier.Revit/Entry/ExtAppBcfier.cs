using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using System;
using System.Windows;
using Bcfier.Revit.Host;

namespace Bcfier.Revit.Entry
{

  /// <summary>
  /// Obfuscation Ignore for External Interface
  /// </summary>
  [Obfuscation(Exclude = true, ApplyToMembers = false)]
  [Transaction(TransactionMode.Manual)]
  public class ExtAppBcfier : IExternalApplication
  {

    // class instance  
    public static ExtAppBcfier This = null;
    // ModelessForm instance  
    public RevitWindow RvtWindow;

    #region Revit IExternalApplication Implementation

    /// <summary>
    /// Startup
    /// </summary>
    /// <param name="application"></param>
    /// <returns></returns>
    public Result OnStartup(UIControlledApplication application)
    {
      RvtWindow = null;   // no dialog needed yet; the command will bring it  
      This = this;  // static access to this application instance  

      return Result.Succeeded;
    }

    /// <summary>
    /// Shut Down
    /// </summary>
    /// <param name="application"></param>
    /// <returns></returns>
    public Result OnShutdown(UIControlledApplication application)
    {
      if (RvtWindow != null && RvtWindow.IsVisible)
      {
        RvtWindow.Close();
      }

      return Result.Succeeded;
    }

    #endregion

    #region public methods
    /// <summary>
    /// The external command invokes this on the end-user's request 
    /// </summary>
    /// <param name="uiapp"></param>
    public void ShowForm(UIApplication uiapp)
    {
      try
      {
        // Тот же bootstrap WPF + окно, что у Standalone
        var win = BcfierRevitHost.OpenPanel(uiapp, IntPtr.Zero) as RevitWindow;
        if (win != null)
          RvtWindow = win;
      }
      catch (Exception ex)
      {
        BcfierRevitHost.ShowRevitError("BCFier", ex.ToString());
      }
    }

    /// <summary>
    /// Set Focus
    /// </summary>
    public void Focus()
    {
      try
      {
        if (RvtWindow == null) return;
        RvtWindow.Activate();
        RvtWindow.WindowState = WindowState.Normal;
      }
      catch (Exception ex)
      {
        MessageBox.Show(ex.ToString());
      }

    }
    #endregion
  }

}