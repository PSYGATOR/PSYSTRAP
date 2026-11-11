namespace Bloxstrap.Enums
{
    // PSYstrap: the key that opens the in-game menu. Values are Windows virtual-key codes.
    public enum OverlayMenuKey
    {
        [EnumName(StaticName = "Insert")]
        Insert = 0x2D,

        [EnumName(StaticName = "Right Shift")]
        RightShift = 0xA1,

        [EnumName(StaticName = "Home")]
        Home = 0x24,

        // F6/F7/F8 are the macro keys
        [EnumName(StaticName = "F10")]
        F10 = 0x79
    }
}
