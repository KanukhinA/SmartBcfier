using System.Collections.Generic;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Bcfier.Revit.Data;

namespace Bcfier.Revit.Entry
{
    /// <summary>
    /// Выделение элементов в модели по Id из viewpoint.
    /// </summary>
    public class ExtEvntSelectElements : IExternalEventHandler
    {
        public List<int> ElementIds { get; set; } = new List<int>();
        /// <summary>true — добавить Id к текущему выделению Revit, false — заменить его.</summary>
        public bool Append { get; set; }

        public void Execute(UIApplication app)
        {
            try
            {
                UIDocument uiDoc = app.ActiveUIDocument;
                if (uiDoc == null || ElementIds == null || ElementIds.Count == 0)
                    return;

                Document doc = uiDoc.Document;
                var ids = new List<ElementId>();
                foreach (int id in ElementIds)
                {
                    try
                    {
                        if (!RevitIdHelper.TryFromLong(id, out ElementId elementId))
                            continue;

                        if (doc.GetElement(elementId) == null)
                            continue;

                        ids.Add(elementId);
                    }
                    catch
                    {
                        // Один неверный Id не должен ронять выделение остальных
                    }
                }

                if (ids.Count == 0)
                    return;

                if (Append)
                {
                    var merged = new List<ElementId>(uiDoc.Selection.GetElementIds());
                    foreach (ElementId elementId in ids)
                    {
                        if (!merged.Contains(elementId))
                            merged.Add(elementId);
                    }

                    uiDoc.Selection.SetElementIds(merged);
                }
                else
                {
                    uiDoc.Selection.SetElementIds(ids);
                }
            }
            catch
            {
                // Ошибка выделения не должна ронять Revit
            }
        }

        public string GetName() => Bcfier.Localization.Loc.ProductName + " Select Elements";
    }
}
