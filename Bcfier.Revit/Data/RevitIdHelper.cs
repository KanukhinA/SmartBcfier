using Autodesk.Revit.DB;

namespace Bcfier.Revit.Data
{
    /// <summary>
    /// Совместимость ElementId между Revit 2025+ (long) и более ранними API (int).
    /// </summary>
    internal static class RevitIdHelper
    {
        public static int GetValue(this ElementId id)
        {
#if REVIT_LONG_ELEMENT_ID
            return (int)id.Value;
#else
            return id.IntegerValue;
#endif
        }

        public static ElementId FromInt(int value)
        {
#if REVIT_LONG_ELEMENT_ID
            return new ElementId((long)value);
#else
            return new ElementId(value);
#endif
        }

        public static bool TryFromLong(long value, out ElementId elementId)
        {
            // Большинство id укладывается в int — этот путь работает на всех версиях API
            if (value >= int.MinValue && value <= int.MaxValue)
            {
#if REVIT_LONG_ELEMENT_ID
                elementId = new ElementId(value);
#else
                elementId = new ElementId((int)value);
#endif
                return true;
            }

#if REVIT_LONG_ELEMENT_ID
            elementId = new ElementId(value);
            return true;
#else
            elementId = ElementId.InvalidElementId;
            return false;
#endif
        }
    }
}
