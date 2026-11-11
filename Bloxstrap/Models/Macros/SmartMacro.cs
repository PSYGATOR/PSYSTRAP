namespace Bloxstrap.Models.Macros
{
    public enum DetectorKind
    {
        /// <summary>A point on screen is (close to) a colour.</summary>
        Pixel,

        /// <summary>A small picture cut from a screenshot appears near where it was.</summary>
        Image,

        /// <summary>An area of the screen contains some text (read with Windows OCR).</summary>
        Text
    }

    /// <summary>
    /// Something a smart macro watches for. Positions/sizes are fractions (0-1) of the Roblox window's client area,
    /// so detectors keep working when the window is resized.
    /// </summary>
    public class Detector
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

        public string Name { get; set; } = "";

        public DetectorKind Kind { get; set; }

        /// <summary>Pixel: the point. Image: the template's top-left corner.</summary>
        public double X { get; set; }

        public double Y { get; set; }

        /// <summary>Text only: what the area must contain (case, spaces and punctuation ignored).</summary>
        public string ExpectedText { get; set; } = "";

        /// <summary>Image and Text: area size as a fraction of the client area.</summary>
        public double Width { get; set; }

        public double Height { get; set; }

        /// <summary>Pixel only: expected colour.</summary>
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }

        /// <summary>Pixel only: how far each colour channel may be off (0-255).</summary>
        public int Tolerance { get; set; } = 30;

        /// <summary>Image only: how similar the picture must be (0-1).</summary>
        public double Threshold { get; set; } = 0.9;

        /// <summary>Image only: how far (fraction of the window) to look around the original spot.</summary>
        public double SearchMargin { get; set; } = 0.08;

        /// <summary>Image only: template file name inside the smart macro's folder.</summary>
        public string TemplateFile { get; set; } = "";
    }

    public enum RuleAction
    {
        [EnumName(StaticName = "Play macro")]
        PlayMacro,

        [EnumName(StaticName = "Press key")]
        PressKey,

        [EnumName(StaticName = "Click it")]
        ClickDetector,

        [EnumName(StaticName = "Wait (seconds)")]
        Wait,

        [EnumName(StaticName = "Rejoin server")]
        Rejoin,

        [EnumName(StaticName = "Stop smart macro")]
        Stop
    }

    /// <summary>"When [detector] is [seen / not seen] for [time] → [action]".</summary>
    public class SmartRule
    {
        public string DetectorId { get; set; } = "";

        public bool WhenSeen { get; set; } = true;

        /// <summary>How long the condition must hold before the rule fires.</summary>
        public double ForSeconds { get; set; } = 0;

        public RuleAction Action { get; set; }

        /// <summary>Macro name for PlayMacro, key name for PressKey (e.g. "E", "Space"), seconds for Wait.</summary>
        public string Parameter { get; set; } = "";

        /// <summary>Minimum time between two firings of this rule.</summary>
        public double CooldownSeconds { get; set; } = 2;
    }

    /// <summary>PSYstrap smart macro: detectors + rules checked a few times a second. Saved in Macros\Smart\{name}\.</summary>
    public class SmartMacro
    {
        public string Name { get; set; } = "";

        public int IntervalMs { get; set; } = 400;

        public List<Detector> Detectors { get; set; } = new();

        public List<SmartRule> Rules { get; set; } = new();
    }
}
