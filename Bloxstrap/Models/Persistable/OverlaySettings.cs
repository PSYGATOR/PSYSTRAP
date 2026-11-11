namespace Bloxstrap.Models.Persistable
{
    /// <summary>
    /// PSYstrap in-game overlay ("mod menu"). Stored inside Settings.json under "Overlay".
    /// </summary>
    public class OverlaySettings
    {
        public bool Enabled { get; set; } = true;

        public OverlayMenuKey MenuKey { get; set; } = OverlayMenuKey.Insert;

        public OverlayCorner Corner { get; set; } = OverlayCorner.BottomLeft;

        public bool ShowFramerate { get; set; } = true;

        public bool ShowClicksPerSecond { get; set; } = true;

        public bool ShowMemory { get; set; } = false;

        public bool ShowSessionTime { get; set; } = true;

        public bool ShowClock { get; set; } = false;

        // macros: F6 records, F7 plays the selected one
        public string SelectedMacro { get; set; } = "";

        /// <summary>How many times F7 plays the macro; 0 = until stopped.</summary>
        public int MacroRuns { get; set; } = 1;

        public double MacroSpeed { get; set; } = 1.0;

        /// <summary>Smart macro F8 starts/stops.</summary>
        public string SelectedSmartMacro { get; set; } = "";
    }
}
