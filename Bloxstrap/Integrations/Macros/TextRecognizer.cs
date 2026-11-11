using System.Runtime.InteropServices.WindowsRuntime;

using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Bloxstrap.Integrations.Macros
{
    /// <summary>
    /// PSYstrap: reads text from part of a captured frame with the OCR engine built into Windows 10/11
    /// (offline, no extra install). Uses the languages from the Windows display-language settings.
    /// </summary>
    public static class TextRecognizer
    {
        private static readonly object Lock = new();
        private static OcrEngine? _engine;
        private static bool _triedCreate;

        /// <summary>Why OCR isn't available, if it isn't.</summary>
        public static string? Error { get; private set; }

        private static OcrEngine? Engine
        {
            get
            {
                lock (Lock)
                {
                    if (!_triedCreate)
                    {
                        _triedCreate = true;
                        _engine = OcrEngine.TryCreateFromUserProfileLanguages();

                        if (_engine is null)
                            Error = "Windows has no text-recognition language installed (Settings → Time & language → Language).";
                    }

                    return _engine;
                }
            }
        }

        /// <summary>
        /// Reads the text in a frame. Small game text is enlarged first, which OCR handles much better.
        /// Returns null when OCR isn't available.
        /// </summary>
        public static string? Read(ScreenFrame frame)
        {
            var engine = Engine;

            if (engine is null)
                return null;

            // OCR copes best with text ~20-40 px tall; game UI text is often smaller
            int scale = frame.Height < 40 ? 3 : frame.Height < 120 ? 2 : 1;
            int maxDimension = (int)OcrEngine.MaxImageDimension;

            while (scale > 1 && (frame.Width * scale > maxDimension || frame.Height * scale > maxDimension))
                scale--;

            var source = scale == 1 ? frame : Upscale(frame, scale);

            if (source.Width > maxDimension || source.Height > maxDimension)
                source = source.Crop(0, 0, Math.Min(source.Width, maxDimension), Math.Min(source.Height, maxDimension));

            using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(source.Pixels.AsBuffer(), BitmapPixelFormat.Bgra8, source.Width, source.Height, BitmapAlphaMode.Premultiplied);

            // WinRT async completes on the thread pool, so blocking here is safe from any thread
            var result = Task.Run(async () => await engine.RecognizeAsync(bitmap)).GetAwaiter().GetResult();

            return result.Text;
        }

        private static ScreenFrame Upscale(ScreenFrame frame, int scale)
        {
            int width = frame.Width * scale, height = frame.Height * scale;
            var pixels = new byte[width * height * 4];

            for (int y = 0; y < height; y++)
            {
                int sourceRow = (y / scale) * frame.Width;

                for (int x = 0; x < width; x++)
                    Buffer.BlockCopy(frame.Pixels, (sourceRow + x / scale) * 4, pixels, (y * width + x) * 4, 4);
            }

            // opaque, so premultiplied and straight alpha are the same
            for (int i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;

            return new ScreenFrame(width, height, pixels);
        }

        /// <summary>Case- and spacing-insensitive "contains", since OCR spacing isn't reliable.</summary>
        public static bool Contains(string? read, string expected)
        {
            if (String.IsNullOrWhiteSpace(read) || String.IsNullOrWhiteSpace(expected))
                return false;

            static string Normalise(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

            return Normalise(read).Contains(Normalise(expected), StringComparison.Ordinal);
        }
    }
}
