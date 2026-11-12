namespace Bloxstrap.Models.Persistable
{
    /// <summary>PSYstrap multi-instance options, stored in Settings.json under "MultiInstanceOptions".</summary>
    public class MultiInstanceSettings
    {
        /// <summary>Clients you aren't using run at below-normal CPU priority, so the one you're playing stays smooth.</summary>
        public bool LowerBackgroundPriority { get; set; } = true;

        /// <summary>
        /// Clients you aren't using run in Windows' efficiency mode (idle priority + power throttling), like the leaf
        /// in Task Manager. Takes priority over LowerBackgroundPriority.
        /// </summary>
        public bool EfficiencyModeBackground { get; set; } = true;

        /// <summary>
        /// Every minute, ask Windows to page out memory of clients you aren't using. Frees RAM on low-memory PCs,
        /// at the cost of a short hitch when you switch back to that client.
        /// </summary>
        public bool TrimBackgroundMemory { get; set; } = false;

        /// <summary>Only trim a window once it uses more than this many MB. 0 = trim every minute.</summary>
        public int TrimAboveMB { get; set; } = 0;

        /// <summary>Give each background window its own CPU cores, so they don't all fight over the same ones.</summary>
        public bool SpreadCpuCores { get; set; } = false;

        /// <summary>Turn-taking: which smart macro each instance slot runs (slot → smart macro name).</summary>
        public Dictionary<int, string> RotationMacros { get; set; } = new();

        /// <summary>Turn-taking: minimum time spent on each client per turn.</summary>
        public int RotationDwellMs { get; set; } = 700;

        /// <summary>Turn-taking: resume this many seconds after you last touched the mouse/keyboard.</summary>
        public int RotationResumeSeconds { get; set; } = 5;
    }
}
