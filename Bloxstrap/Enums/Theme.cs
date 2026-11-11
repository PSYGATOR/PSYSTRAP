namespace Bloxstrap.Enums
{
    public enum Theme
    {
        [EnumName(FromTranslation = "Common.SystemDefault")]
        Default,
        Light,
        Dark,

        // PSYstrap: dark theme in the logo's colours (green body, red/gold crown).
        // Appended last so existing Settings.json values (stored as ints) keep their meaning.
        [EnumName(StaticName = "PSY")]
        PSY
    }
}
