namespace Bcfier.Revit.Host
{
    /// <summary>
    /// Источник имени автора BCF (Revit username или fallback).
    /// </summary>
    public interface IBcfierAuthorProvider
    {
        string GetCurrentAuthor();
    }
}
