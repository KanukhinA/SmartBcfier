using System;
using Bcfier.Data.Utils;

namespace Bcfier.Data
{
    /// <summary>
    /// Поставщик имени автора для ядра Bcfier (устанавливается из Revit host).
    /// </summary>
    public static class BcfAuthorContext
    {
        public static Func<string> GetCurrentAuthor { get; set; }

        public static string ResolveAuthor()
        {
            string author = GetCurrentAuthor?.Invoke();
            if (!string.IsNullOrWhiteSpace(author))
                return author.Trim();

            // Host не задан (тесты/дизайн) — Windows username
            return Bcfier.Data.Utils.Utils.GetUsername();
        }
    }
}
