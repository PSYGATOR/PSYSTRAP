using Bloxstrap.Models.Macros;

namespace Bloxstrap.Integrations.Macros
{
    /// <summary>
    /// The decision part of a smart macro for one client: given a frame, which rule (if any) should fire now.
    /// Keeps per-rule timing (how long a condition has held, cooldowns). Used by the single-client runner (F8) and by
    /// turn-taking across clients, which keeps one brain per client.
    /// </summary>
    public sealed class SmartMacroBrain
    {
        private readonly DetectorEvaluator _evaluator;
        private readonly Dictionary<SmartRule, DateTime> _conditionSince = new();
        private readonly Dictionary<SmartRule, DateTime> _lastFired = new();

        public SmartMacro Macro { get; }

        public SmartMacroBrain(SmartMacro macro)
        {
            Macro = macro;
            _evaluator = new DetectorEvaluator(SmartMacroStore.FolderFor(macro.Name));
        }

        /// <summary>Forget how long conditions have held (after a pause, conditions must hold again).</summary>
        public void ResetTiming() => _conditionSince.Clear();

        /// <summary>Checks every detector and returns the first rule ready to fire, with its detector's result.</summary>
        public (SmartRule Rule, DetectorResult Result, string Description)? Decide(ScreenFrame frame)
        {
            var results = new Dictionary<string, DetectorResult>();

            foreach (var detector in Macro.Detectors)
                results[detector.Id] = _evaluator.Evaluate(detector, frame);

            var now = DateTime.Now;
            (SmartRule, DetectorResult, string)? decision = null;

            foreach (var rule in Macro.Rules)
            {
                if (!results.TryGetValue(rule.DetectorId, out var result))
                    continue;

                bool holds = rule.WhenSeen ? result.Seen : !result.Seen;

                if (!holds)
                {
                    _conditionSince.Remove(rule);
                    continue;
                }

                if (!_conditionSince.ContainsKey(rule))
                    _conditionSince[rule] = now;

                bool heldLongEnough = (now - _conditionSince[rule]).TotalSeconds >= rule.ForSeconds;
                bool cooledDown = !_lastFired.TryGetValue(rule, out var fired) || (now - fired).TotalSeconds >= rule.CooldownSeconds;

                if (decision is null && heldLongEnough && cooledDown)
                {
                    string name = Macro.Detectors.FirstOrDefault(d => d.Id == rule.DetectorId)?.Name ?? "?";
                    decision = (rule, result, $"{name} {(rule.WhenSeen ? "seen" : "gone")} → {rule.Action.ToEnumName()}");
                }
            }

            return decision;
        }

        public void MarkFired(SmartRule rule)
        {
            _lastFired[rule] = DateTime.Now;
            _conditionSince.Remove(rule);
        }

        /// <summary>
        /// Runs a rule's action through a player bound to the client. Returns false when the smart macro should stop
        /// (Stop action, or a rejoin that closes the client). status explains problems like an unknown macro name.
        /// </summary>
        public static bool Execute(SmartRule rule, DetectorResult result, MacroPlayer player, Action rejoin, CancellationToken token, out string? status)
        {
            status = null;

            switch (rule.Action)
            {
                case RuleAction.PlayMacro:
                    var macro = MacroStore.Load(rule.Parameter);

                    if (macro is null)
                        status = $"macro \"{rule.Parameter}\" not found";
                    else
                        player.PlayAndWait(macro, App.Settings.Prop.Overlay.MacroSpeed, token);
                    break;

                case RuleAction.PressKey:
                    int? key = SmartMacroRunner.ParseKey(rule.Parameter);

                    if (key is null)
                        status = $"unknown key \"{rule.Parameter}\"";
                    else
                        player.Tap(key.Value);
                    break;

                case RuleAction.ClickDetector:
                    player.Click(result.CenterX, result.CenterY);
                    break;

                case RuleAction.Wait:
                    if (double.TryParse(rule.Parameter, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
                        token.WaitHandle.WaitOne(TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 3600)));
                    break;

                case RuleAction.Rejoin:
                    if (App.Settings.Prop.Gameplay.Webhook.OnSmartMacro)
                        Webhook.Send("Smart macro is rejoining", "A smart macro rule asked to rejoin the server.", Webhook.Gold);
                    rejoin();
                    return false;

                case RuleAction.Stop:
                    if (App.Settings.Prop.Gameplay.Webhook.OnSmartMacro)
                        Webhook.Send("Smart macro stopped", "A smart macro rule stopped the smart macro.", Webhook.Grey);
                    return false;

                case RuleAction.Notify:
                    // always sent: the rule itself is the "tell me" switch
                    Webhook.Send("Smart macro", String.IsNullOrWhiteSpace(rule.Parameter) ? "A smart macro rule fired." : rule.Parameter, Webhook.Gold);
                    break;
            }

            return true;
        }
    }
}
