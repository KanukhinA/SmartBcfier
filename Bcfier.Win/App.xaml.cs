using System;
using System.Windows;
using Bcfier.Data;
using Bcfier.Data.Utils;
using Bcfier.Localization;

namespace Bcfier.Win
{
    /// <summary>
    /// Точка входа Windows Viewer.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Применяет культуру и имя автора до показа главного окна.
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                Loc.ApplyCultureFromSettings();
                BcfAuthorContext.GetCurrentAuthor = () => Utils.GetUsername();
                BcfHostCapabilities.ConfigureStandaloneWin();
            }
            catch (Exception ex)
            {
                ExceptionUi.Show(ex);
            }

            base.OnStartup(e);
        }
    }
}
