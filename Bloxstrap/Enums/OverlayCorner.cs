namespace Bloxstrap.Enums
{
    // PSYstrap: stored as an int in Settings.json - only append new values.
    public enum OverlayCorner
    {
        [EnumName(StaticName = "Top left")]
        TopLeft,

        [EnumName(StaticName = "Top right")]
        TopRight,

        [EnumName(StaticName = "Bottom left")]
        BottomLeft,

        [EnumName(StaticName = "Bottom right")]
        BottomRight
    }
}
