namespace TranslationBackend.Utils;

/// <summary>
/// Every timestamp the backend writes goes through this helper, so it lands in Pakistan Standard Time (UTC+5),
/// the convention of the other Avanceon apps (the SupplyChain backend uses the same helper).
/// </summary>
public static class DateTimeHelper
{
    private static readonly TimeZoneInfo PakistanTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Pakistan Standard Time");

    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, PakistanTimeZone);

    /// <summary>Converts a UTC time to Pakistan time; an Unspecified kind is treated as UTC.</summary>
    public static DateTime ToPakistanTime(DateTime utcDateTime)
    {
        if (utcDateTime.Kind == DateTimeKind.Unspecified)
            utcDateTime = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime.ToUniversalTime(), PakistanTimeZone);
    }
}
