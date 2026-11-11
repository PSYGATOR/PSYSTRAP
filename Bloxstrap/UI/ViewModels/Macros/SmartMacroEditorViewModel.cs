using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

using CommunityToolkit.Mvvm.Input;

using Bloxstrap.Integrations.Macros;
using Bloxstrap.Models.Macros;

namespace Bloxstrap.UI.ViewModels.Macros
{
    public class DetectorRowViewModel : NotifyPropertyChangedViewModel
    {
        public Detector Detector { get; }

        public string Name
        {
            get => Detector.Name;
            set { Detector.Name = value; OnPropertyChanged(nameof(Name)); }
        }

        public string Summary => Detector.Kind switch
        {
            DetectorKind.Pixel => $"Pixel at {Detector.X:P0}, {Detector.Y:P0} is #{Detector.R:X2}{Detector.G:X2}{Detector.B:X2} (±{Detector.Tolerance})",
            DetectorKind.Image => $"Picture near {Detector.X:P0}, {Detector.Y:P0}, at least {Detector.Threshold:P0} alike",
            _ => $"Text in the area at {Detector.X:P0}, {Detector.Y:P0} contains:"
        };

        public string ExpectedText
        {
            get => Detector.ExpectedText;
            set { Detector.ExpectedText = value ?? ""; OnPropertyChanged(nameof(ExpectedText)); }
        }

        public Visibility TextVisibility => Detector.Kind == DetectorKind.Text ? Visibility.Visible : Visibility.Collapsed;

        public Visibility SensitivityVisibility => Detector.Kind == DetectorKind.Text ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>Pixel: allowed colour difference. Image: required similarity in percent.</summary>
        public double Sensitivity
        {
            get => Detector.Kind == DetectorKind.Pixel ? Detector.Tolerance : Math.Round(Detector.Threshold * 100);
            set
            {
                if (Detector.Kind == DetectorKind.Pixel)
                    Detector.Tolerance = (int)Math.Clamp(value, 0, 255);
                else
                    Detector.Threshold = Math.Clamp(value, 50, 100) / 100.0;

                OnPropertyChanged(nameof(Sensitivity));
                OnPropertyChanged(nameof(Summary));
            }
        }

        public string SensitivityLabel => Detector.Kind == DetectorKind.Pixel ? "Colour tolerance" : "Match %";

        private string _testResult = "";

        public string TestResult
        {
            get => _testResult;
            set { _testResult = value; OnPropertyChanged(nameof(TestResult)); }
        }

        public ICommand RemoveCommand { get; }

        public DetectorRowViewModel(Detector detector, Action<DetectorRowViewModel> remove)
        {
            Detector = detector;
            RemoveCommand = new RelayCommand(() => remove(this));
        }

        public override string ToString() => Name;
    }

    public class RuleRowViewModel : NotifyPropertyChangedViewModel
    {
        private readonly SmartMacroEditorViewModel _owner;

        public SmartRule Rule { get; }

        public ObservableCollection<DetectorRowViewModel> Detectors => _owner.Detectors;

        public DetectorRowViewModel? SelectedDetector
        {
            get => _owner.Detectors.FirstOrDefault(d => d.Detector.Id == Rule.DetectorId);
            set { Rule.DetectorId = value?.Detector.Id ?? ""; OnPropertyChanged(nameof(SelectedDetector)); }
        }

        public IReadOnlyList<KeyValuePair<bool, string>> ConditionChoices { get; } = new List<KeyValuePair<bool, string>>
        {
            new(true, "is seen"), new(false, "is gone")
        };

        public bool WhenSeen
        {
            get => Rule.WhenSeen;
            set { Rule.WhenSeen = value; OnPropertyChanged(nameof(WhenSeen)); }
        }

        public double ForSeconds
        {
            get => Rule.ForSeconds;
            set { Rule.ForSeconds = Math.Max(0, value); OnPropertyChanged(nameof(ForSeconds)); }
        }

        public IEnumerable<RuleAction> Actions { get; } = Enum.GetValues<RuleAction>();

        public RuleAction Action
        {
            get => Rule.Action;
            set { Rule.Action = value; OnPropertyChanged(nameof(Action)); OnPropertyChanged(nameof(ParameterHint)); OnPropertyChanged(nameof(ParameterVisibility)); }
        }

        public string Parameter
        {
            get => Rule.Parameter;
            set { Rule.Parameter = value ?? ""; OnPropertyChanged(nameof(Parameter)); }
        }

        public ObservableCollection<string> MacroNames => _owner.MacroNames;

        public string ParameterHint => Rule.Action switch
        {
            RuleAction.PlayMacro => "macro name",
            RuleAction.PressKey => "key, e.g. E or Space",
            RuleAction.Wait => "seconds",
            _ => ""
        };

        public Visibility ParameterVisibility => Rule.Action is RuleAction.PlayMacro or RuleAction.PressKey or RuleAction.Wait
            ? Visibility.Visible : Visibility.Collapsed;

        public double CooldownSeconds
        {
            get => Rule.CooldownSeconds;
            set { Rule.CooldownSeconds = Math.Max(0, value); OnPropertyChanged(nameof(CooldownSeconds)); }
        }

        public ICommand RemoveCommand { get; }
        public ICommand MoveUpCommand { get; }

        public RuleRowViewModel(SmartMacroEditorViewModel owner, SmartRule rule)
        {
            _owner = owner;
            Rule = rule;
            RemoveCommand = new RelayCommand(() => owner.Rules.Remove(this));
            MoveUpCommand = new RelayCommand(() =>
            {
                int index = owner.Rules.IndexOf(this);
                if (index > 0)
                    owner.Rules.Move(index, index - 1);
            });
        }

        public void RefreshDetector() => OnPropertyChanged(nameof(SelectedDetector));
    }

    /// <summary>PSYstrap smart macro editor: screenshot-driven detector creation plus a rule list.</summary>
    public class SmartMacroEditorViewModel : NotifyPropertyChangedViewModel
    {
        private readonly string? _originalName;
        private readonly Dictionary<string, ScreenFrame> _newTemplates = new();

        public ScreenFrame? Frame { get; private set; }

        public BitmapSource? Screenshot { get; private set; }

        public Visibility NoScreenshotVisibility => Screenshot is null ? Visibility.Visible : Visibility.Collapsed;

        public string Name { get; set; }

        public int IntervalMs { get; set; }

        public ObservableCollection<DetectorRowViewModel> Detectors { get; } = new();

        public ObservableCollection<RuleRowViewModel> Rules { get; } = new();

        public ObservableCollection<string> MacroNames { get; } = new(MacroStore.ListNames());

        /// <summary>What clicking/dragging on the screenshot adds.</summary>
        public DetectorKind Mode { get; private set; } = DetectorKind.Pixel;

        public bool PixelMode
        {
            get => Mode == DetectorKind.Pixel;
            set { if (value) SetMode(DetectorKind.Pixel); }
        }

        public bool ImageMode
        {
            get => Mode == DetectorKind.Image;
            set { if (value) SetMode(DetectorKind.Image); }
        }

        public bool TextMode
        {
            get => Mode == DetectorKind.Text;
            set { if (value) SetMode(DetectorKind.Text); }
        }

        private void SetMode(DetectorKind mode)
        {
            Mode = mode;
            OnPropertyChanged(nameof(PixelMode));
            OnPropertyChanged(nameof(ImageMode));
            OnPropertyChanged(nameof(TextMode));
        }

        /// <summary>Drag on the screenshot in text mode: reads the text there and watches for it.</summary>
        public void AddTextDetector(double fx1, double fy1, double fx2, double fy2)
        {
            if (Frame is null)
                return;

            var detector = new Detector
            {
                Kind = DetectorKind.Text,
                Name = $"Text {Detectors.Count(d => d.Detector.Kind == DetectorKind.Text) + 1}",
                X = Math.Min(fx1, fx2), Y = Math.Min(fy1, fy2),
                Width = Math.Abs(fx2 - fx1), Height = Math.Abs(fy2 - fy1)
            };

            if (detector.Width * Frame.Width < 6 || detector.Height * Frame.Height < 6)
            {
                Status = "That area is too small - drag a box around the whole piece of text.";
                return;
            }

            string? read;

            try
            {
                read = TextRecognizer.Read(DetectorEvaluator.CropArea(detector, Frame));
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("SmartMacroEditor::AddTextDetector", ex);
                read = null;
            }

            if (read is null)
            {
                Status = $"Text reading isn't available: {TextRecognizer.Error ?? "Windows OCR failed"}";
                return;
            }

            // one line, trimmed - the user can shorten it to just the important word
            detector.ExpectedText = String.Join(' ', read.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            if (detector.ExpectedText.Length > 60)
                detector.ExpectedText = detector.ExpectedText[..60];

            AddDetector(detector);
            Status = String.IsNullOrEmpty(detector.ExpectedText)
                ? $"Added {detector.Name}, but no text was read there - type what to look for below."
                : $"Added {detector.Name}: read \"{detector.ExpectedText}\". Shorten it to the important word(s) if you like.";
        }

        private string _status = "";

        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(nameof(Status)); }
        }

        /// <summary>Raised when markers on the screenshot need redrawing.</summary>
        public event EventHandler? DetectorsChanged;

        public ICommand AddRuleCommand => new RelayCommand(() =>
        {
            if (Detectors.Count == 0)
            {
                Status = "Add a detector first: take a screenshot, then click (pixel) or drag (picture) on it.";
                return;
            }

            Rules.Add(new RuleRowViewModel(this, new SmartRule { DetectorId = Detectors[0].Detector.Id }));
        });

        public ICommand TestCommand => new RelayCommand(Test);

        public ICommand LoadImageCommand => new RelayCommand(LoadImage);

        public SmartMacroEditorViewModel(string? existingName)
        {
            _originalName = existingName;
            var macro = existingName is null ? null : SmartMacroStore.Load(existingName);

            Name = macro?.Name ?? SmartMacroStore.NextFreeName();
            IntervalMs = macro?.IntervalMs ?? 400;

            if (macro is not null)
            {
                foreach (var detector in macro.Detectors)
                    Detectors.Add(new DetectorRowViewModel(detector, RemoveDetector));

                foreach (var rule in macro.Rules)
                    Rules.Add(new RuleRowViewModel(this, rule));

                // show the screenshot the macro was made from, if it was kept
                string screenshot = Path.Combine(SmartMacroStore.FolderFor(macro.Name), "screenshot.jpg");

                if (File.Exists(screenshot))
                    SetFrame(ScreenFrame.Load(screenshot));
            }
        }

        public void SetFrame(ScreenFrame frame)
        {
            Frame = frame;

            using var bitmap = frame.ToBitmap();
            var hBitmap = bitmap.GetHbitmap();

            try
            {
                var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                Screenshot = source;
            }
            finally
            {
                DeleteObject(hBitmap);
            }

            OnPropertyChanged(nameof(Screenshot));
            OnPropertyChanged(nameof(NoScreenshotVisibility));
            DetectorsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void LoadImage()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp" };

            if (dialog.ShowDialog() == true)
            {
                try { SetFrame(ScreenFrame.Load(dialog.FileName)); }
                catch (Exception ex) { Status = $"Couldn't open that image: {ex.Message}"; }
            }
        }

        /// <summary>Click on the screenshot in pixel mode: the colour there becomes a pixel check.</summary>
        public void AddPixelDetector(double fx, double fy)
        {
            if (Frame is null)
                return;

            var (r, g, b) = Frame.SampleColor((int)(fx * (Frame.Width - 1)), (int)(fy * (Frame.Height - 1)));
            var detector = new Detector
            {
                Kind = DetectorKind.Pixel,
                Name = $"Pixel {Detectors.Count(d => d.Detector.Kind == DetectorKind.Pixel) + 1}",
                X = fx, Y = fy, R = r, G = g, B = b
            };

            AddDetector(detector);
            Status = $"Added {detector.Name} (#{r:X2}{g:X2}{b:X2}). Rename it below so rules read nicely.";
        }

        /// <summary>Drag on the screenshot in picture mode: that area becomes a picture check.</summary>
        public void AddImageDetector(double fx1, double fy1, double fx2, double fy2)
        {
            if (Frame is null)
                return;

            double left = Math.Min(fx1, fx2), top = Math.Min(fy1, fy2);
            double width = Math.Abs(fx2 - fx1), height = Math.Abs(fy2 - fy1);
            int pxWidth = (int)(width * Frame.Width), pxHeight = (int)(height * Frame.Height);

            if (pxWidth < 8 || pxHeight < 8)
            {
                Status = "That area is too small - drag a box around something recognisable (a button, an icon).";
                return;
            }

            var detector = new Detector
            {
                Kind = DetectorKind.Image,
                Name = $"Picture {Detectors.Count(d => d.Detector.Kind == DetectorKind.Image) + 1}",
                X = left, Y = top, Width = width, Height = height
            };

            detector.TemplateFile = $"{detector.Id}.png";
            _newTemplates[detector.TemplateFile] = Frame.Crop((int)(left * Frame.Width), (int)(top * Frame.Height), pxWidth, pxHeight);

            AddDetector(detector);
            Status = $"Added {detector.Name} ({pxWidth}×{pxHeight}). Rename it below so rules read nicely.";
        }

        private void AddDetector(Detector detector)
        {
            Detectors.Add(new DetectorRowViewModel(detector, RemoveDetector));
            DetectorsChanged?.Invoke(this, EventArgs.Empty);

            foreach (var rule in Rules)
                rule.RefreshDetector();
        }

        private void RemoveDetector(DetectorRowViewModel row)
        {
            Detectors.Remove(row);

            foreach (var rule in Rules.Where(r => r.Rule.DetectorId == row.Detector.Id).ToList())
                Rules.Remove(rule);

            DetectorsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Checks every detector against the current screenshot.</summary>
        private void Test()
        {
            if (Frame is null)
            {
                Status = "Take a screenshot from Roblox first, then Test checks the detectors against it.";
                return;
            }

            var evaluator = new DetectorEvaluator(_originalName is null ? "" : SmartMacroStore.FolderFor(_originalName));

            foreach (var pair in _newTemplates)
                evaluator.SetTemplate(pair.Key, pair.Value);

            foreach (var row in Detectors)
            {
                var result = evaluator.Evaluate(row.Detector, Frame);

                row.TestResult = row.Detector.Kind == DetectorKind.Text
                    ? $"{(result.Seen ? "✔ seen" : "✘ not seen")} - read \"{(evaluator.LastReadText.TryGetValue(row.Detector.Id, out var read) ? read.Replace('\n', ' ') : "")}\""
                    : $"{(result.Seen ? "✔ seen" : "✘ not seen")} ({result.Score:P0} alike)";
            }

            Status = "Tested against the current screenshot. Take a new one in a different situation to check the opposite.";
        }

        public bool Save()
        {
            string name = Name.Trim();

            if (String.IsNullOrEmpty(name))
            {
                Status = "Give the smart macro a name.";
                return false;
            }

            bool renamed = _originalName is not null && !String.Equals(_originalName, name, StringComparison.OrdinalIgnoreCase);

            if ((_originalName is null || renamed) && SmartMacroStore.ListNames().Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                Status = $"There's already a smart macro called \"{name}\".";
                return false;
            }

            if (Rules.Any(rule => rule.Action == RuleAction.PlayMacro && String.IsNullOrWhiteSpace(rule.Parameter)))
            {
                Status = "A \"Play macro\" rule needs a macro name.";
                return false;
            }

            if (Detectors.Any(row => row.Detector.Kind == DetectorKind.Text && String.IsNullOrWhiteSpace(row.Detector.ExpectedText)))
            {
                Status = "A text check needs the text to look for.";
                return false;
            }

            if (Rules.Any(rule => rule.Action == RuleAction.PressKey && SmartMacroRunner.ParseKey(rule.Parameter) is null))
            {
                Status = "A \"Press key\" rule has a key PSYstrap doesn't know - use names like E, Space, F, 1, LeftShift.";
                return false;
            }

            var macro = new SmartMacro
            {
                Name = name,
                IntervalMs = Math.Clamp(IntervalMs, 100, 10000),
                Detectors = Detectors.Select(d => d.Detector).ToList(),
                Rules = Rules.Select(r => r.Rule).Where(r => macro_HasDetector(r)).ToList()
            };

            bool macro_HasDetector(SmartRule rule) => Detectors.Any(d => d.Detector.Id == rule.DetectorId);

            string folder = SmartMacroStore.FolderFor(name);
            Directory.CreateDirectory(folder);

            // templates: new ones from memory, existing ones carried over from the old folder when renamed
            foreach (var detector in macro.Detectors.Where(d => d.Kind == DetectorKind.Image))
            {
                string target = Path.Combine(folder, detector.TemplateFile);

                if (_newTemplates.TryGetValue(detector.TemplateFile, out var template))
                    template.Save(target);
                else if (renamed)
                {
                    string old = Path.Combine(SmartMacroStore.FolderFor(_originalName!), detector.TemplateFile);

                    if (File.Exists(old))
                        File.Copy(old, target, true);
                }
            }

            // keep the screenshot so editing later shows where things are
            if (Frame is not null)
            {
                using var bitmap = Frame.ToBitmap();
                bitmap.Save(Path.Combine(folder, "screenshot.jpg"), System.Drawing.Imaging.ImageFormat.Jpeg);
            }

            SmartMacroStore.Save(macro);

            if (renamed)
                SmartMacroStore.Delete(_originalName!);

            SavedName = name;
            return true;
        }

        public string? SavedName { get; private set; }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr handle);
    }
}
