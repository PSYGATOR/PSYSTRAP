using Bloxstrap.Models.Macros;

namespace Bloxstrap.Integrations.Macros
{
    /// <summary>Result of checking one detector against a frame.</summary>
    public readonly record struct DetectorResult(bool Seen, double Score, double CenterX, double CenterY);

    /// <summary>
    /// Checks pixel and image detectors against a captured frame of the Roblox window.
    /// Image templates are loaded once and matched in grayscale, rescaled to the current window size.
    /// </summary>
    public sealed class DetectorEvaluator
    {
        private readonly string _folder;
        private readonly Dictionary<string, ScreenFrame?> _templates = new();
        private ScreenFrame? _grayFrameSource;
        private byte[]? _grayFrame;

        /// <param name="folder">The smart macro's folder, where template PNGs live.</param>
        public DetectorEvaluator(string folder)
        {
            _folder = folder;
        }

        /// <summary>Supplies a template that isn't saved to disk yet (the editor's Test button).</summary>
        public void SetTemplate(string file, ScreenFrame template) => _templates[file] = template;

        public DetectorResult Evaluate(Detector detector, ScreenFrame frame) => detector.Kind switch
        {
            DetectorKind.Pixel => EvaluatePixel(detector, frame),
            DetectorKind.Image => EvaluateImage(detector, frame),
            DetectorKind.Text => EvaluateText(detector, frame),
            _ => default
        };

        /// <summary>What the last text check read, per detector (shown by the editor's Test button).</summary>
        public Dictionary<string, string> LastReadText { get; } = new();

        public static ScreenFrame CropArea(Detector detector, ScreenFrame frame) => frame.Crop(
            (int)(detector.X * frame.Width), (int)(detector.Y * frame.Height),
            Math.Max(1, (int)(detector.Width * frame.Width)), Math.Max(1, (int)(detector.Height * frame.Height)));

        private DetectorResult EvaluateText(Detector detector, ScreenFrame frame)
        {
            string? read = TextRecognizer.Read(CropArea(detector, frame));
            LastReadText[detector.Id] = read ?? $"(text reading unavailable: {TextRecognizer.Error})";

            bool seen = TextRecognizer.Contains(read, detector.ExpectedText);
            return new DetectorResult(seen, seen ? 1 : 0, detector.X + detector.Width / 2, detector.Y + detector.Height / 2);
        }

        private static DetectorResult EvaluatePixel(Detector detector, ScreenFrame frame)
        {
            int x = (int)Math.Round(detector.X * (frame.Width - 1));
            int y = (int)Math.Round(detector.Y * (frame.Height - 1));
            var (r, g, b) = frame.SampleColor(x, y);

            int difference = Math.Max(Math.Abs(r - detector.R), Math.Max(Math.Abs(g - detector.G), Math.Abs(b - detector.B)));
            bool seen = difference <= detector.Tolerance;

            return new DetectorResult(seen, 1 - difference / 255.0, detector.X, detector.Y);
        }

        private ScreenFrame? GetTemplate(Detector detector)
        {
            if (_templates.TryGetValue(detector.TemplateFile, out var cached))
                return cached;

            ScreenFrame? template = null;

            try
            {
                string path = Path.Combine(_folder, detector.TemplateFile);

                if (File.Exists(path))
                    template = ScreenFrame.Load(path);
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("DetectorEvaluator::GetTemplate", ex);
            }

            _templates[detector.TemplateFile] = template;
            return template;
        }

        /// <summary>
        /// Slides the template over a window around its original position and keeps the best match.
        /// Score = 1 - mean absolute grayscale difference; works on half-resolution images for speed.
        /// </summary>
        private DetectorResult EvaluateImage(Detector detector, ScreenFrame frame)
        {
            var template = GetTemplate(detector);

            if (template is null)
                return default;

            const int scale = 2;

            // template size in this frame, at half resolution
            int tw = Math.Max(2, (int)Math.Round(detector.Width * frame.Width / scale));
            int th = Math.Max(2, (int)Math.Round(detector.Height * frame.Height / scale));
            int fw = frame.Width / scale, fh = frame.Height / scale;

            if (tw >= fw || th >= fh)
                return default;

            byte[] templateGray = template.ToGray(tw, th);

            // every image detector in a tick looks at the same frame, so convert it once
            if (!ReferenceEquals(_grayFrameSource, frame))
            {
                _grayFrame = frame.ToGray(fw, fh);
                _grayFrameSource = frame;
            }

            byte[] frameGray = _grayFrame!;

            int startX = (int)((detector.X - detector.SearchMargin) * fw), endX = (int)((detector.X + detector.SearchMargin) * fw);
            int startY = (int)((detector.Y - detector.SearchMargin) * fh), endY = (int)((detector.Y + detector.SearchMargin) * fh);
            startX = Math.Clamp(startX, 0, fw - tw); endX = Math.Clamp(endX, 0, fw - tw);
            startY = Math.Clamp(startY, 0, fh - th); endY = Math.Clamp(endY, 0, fh - th);

            long pixelCount = (long)tw * th;
            long best = long.MaxValue;
            int bestX = startX, bestY = startY;

            for (int oy = startY; oy <= endY; oy++)
            {
                for (int ox = startX; ox <= endX; ox++)
                {
                    long sum = 0;

                    for (int y = 0; y < th && sum < best; y++)
                    {
                        int frameRow = (oy + y) * fw + ox;
                        int templateRow = y * tw;

                        for (int x = 0; x < tw; x++)
                            sum += Math.Abs(frameGray[frameRow + x] - templateGray[templateRow + x]);
                    }

                    if (sum < best)
                    {
                        best = sum;
                        bestX = ox;
                        bestY = oy;
                    }
                }
            }

            double score = 1 - best / (double)pixelCount / 255.0;
            double centerX = (bestX + tw / 2.0) / fw;
            double centerY = (bestY + th / 2.0) / fh;

            return new DetectorResult(score >= detector.Threshold, score, centerX, centerY);
        }
    }
}
