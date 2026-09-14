using System;
using System.Reflection;
using System.Windows;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Bcfier.Revit.Host;

namespace Bcfier.Revit.Standalone
{
    /// <summary>
    /// Команда ленты standalone add-in: открывает BCF-панель.
    /// </summary>
    [Obfuscation(Exclude = true, ApplyToMembers = false)]
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class CmdMain : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                BcfierRevitHost.OpenPanel(commandData.Application, commandData.Application.MainWindowHandle);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                BcfierRevitHost.ShowRevitError("BCFier", ex.ToString());
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
