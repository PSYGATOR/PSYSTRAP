namespace Bloxstrap.Models.Macros
{
    public enum MacroStepKind
    {
        KeyDown,
        KeyUp,
        MouseMove,
        MouseDown,
        MouseUp,
        Wheel
    }

    public enum MacroMouseButton
    {
        Left,
        Right,
        Middle
    }

    /// <summary>
    /// One recorded input. Mouse positions are fractions (0-1) of the Roblox window's client area,
    /// so a macro keeps working if the window is moved or resized.
    /// </summary>
    public class MacroStep
    {
        /// <summary>Milliseconds after the previous step.</summary>
        public int Delay { get; set; }

        public MacroStepKind Kind { get; set; }

        /// <summary>Virtual-key code for key steps.</summary>
        public int Key { get; set; }

        /// <summary>Hardware scan code for key steps (games read these).</summary>
        public int ScanCode { get; set; }

        public bool Extended { get; set; }

        public MacroMouseButton Button { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public int WheelDelta { get; set; }
    }

    /// <summary>PSYstrap macro, saved as JSON in the Macros folder.</summary>
    public class Macro
    {
        public string Name { get; set; } = "";

        public DateTime Created { get; set; } = DateTime.Now;

        public List<MacroStep> Steps { get; set; } = new();

        public TimeSpan Duration => TimeSpan.FromMilliseconds(Steps.Sum(step => (long)step.Delay));
    }
}
