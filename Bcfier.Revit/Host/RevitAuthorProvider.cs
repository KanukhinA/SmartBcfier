using Autodesk.Revit.UI;
using Bcfier.Data.Utils;

namespace Bcfier.Revit.Host
{
    /// <summary>
    /// Имя постановщика из Revit Application.Username.
    /// </summary>
    public sealed class RevitAuthorProvider : IBcfierAuthorProvider
    {
        private readonly UIApplication _uiApp;

        public RevitAuthorProvider(UIApplication uiApp)
        {
            _uiApp = uiApp;
        }

        public string GetCurrentAuthor()
        {
            string revitUser = _uiApp?.Application?.Username;
            if (!string.IsNullOrWhiteSpace(revitUser))
                return revitUser.Trim();

            // Username в Revit пуст — Windows login
            return Utils.GetUsername();
        }
    }
}
