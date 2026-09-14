using System;
using Bcfier.Localization;

namespace Bcfier.Data.Utils
{
  /// <summary>
  /// Convert a DateTime or string to a relative date
  /// </summary>
  public static class RelativeDate
  {
    public static string ToRelative(string dateString)
    {
      return ToRelative(Convert.ToDateTime(dateString));
    }

    public static string ToRelative(DateTime theDate)
    {
      // CreationDate создаётся в UTC; сравниваем в той же шкале времени.
      DateTime now = theDate.Kind == DateTimeKind.Utc ? DateTime.UtcNow : DateTime.Now;
      TimeSpan elapsed = now - theDate;
      if (elapsed < TimeSpan.Zero)
        elapsed = TimeSpan.Zero;

      if (elapsed.TotalSeconds < 10)
        return Loc.Get("RelativeJustNow");
      if (elapsed.TotalSeconds < 60)
        return Loc.Plural("RelativeSeconds", Math.Max(1, elapsed.Seconds));
      if (elapsed.TotalMinutes < 2)
        return Loc.Get("RelativeMinuteAgo");
      if (elapsed.TotalMinutes < 60)
        return Loc.Plural("RelativeMinutes", elapsed.Minutes);
      if (elapsed.TotalHours < 2)
        return Loc.Get("RelativeHourAgo");
      if (elapsed.TotalHours < 24)
        return Loc.Plural("RelativeHours", elapsed.Hours);
      if (elapsed.TotalDays < 2)
        return Loc.Get("RelativeYesterday");
      if (elapsed.TotalDays < 365)
        return Loc.Plural("RelativeDays", elapsed.Days);

      return Loc.Plural("RelativeYears", Math.Max(1, elapsed.Days / 365));
    }
  }
}
