namespace Bloxstrap.Enums
{
    // PSYstrap: stored in Settings.json as an int - only append new values.
    public enum SkyboxPreset
    {
        [EnumName(StaticName = "Roblox default")]
        Default,

        [EnumName(StaticName = "Custom images")]
        Custom,

        [EnumName(StaticName = "PSY Green")]
        PsyGreen,

        [EnumName(StaticName = "Midnight")]
        Midnight,

        [EnumName(StaticName = "Sunset")]
        Sunset,

        [EnumName(StaticName = "Cotton Candy")]
        CottonCandy,

        [EnumName(StaticName = "Void (pure black)")]
        Void
    }
}
