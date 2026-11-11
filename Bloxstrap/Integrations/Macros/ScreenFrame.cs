using System.Runtime.InteropServices;

using Drawing = System.Drawing;
using DrawingImaging = System.Drawing.Imaging;

namespace Bloxstrap.Integrations.Macros
{
    /// <summary>
    /// A captured image as BGRA32 pixels, top row first. Captures copy what's on screen inside the Roblox window's
    /// client area, so the window has to be visible (not minimised or covered) - same as a person looking at it.
    /// </summary>
    public sealed class ScreenFrame
    {
        public int Width { get; }

        public int Height { get; }

        public byte[] Pixels { get; }

        public ScreenFrame(int width, int height, byte[] pixels)
        {
            Width = width;
            Height = height;
            Pixels = pixels;
        }

        public static ScreenFrame Capture(ScreenRect area)
        {
            using var bitmap = new Drawing.Bitmap(area.Width, area.Height, DrawingImaging.PixelFormat.Format32bppArgb);

            using (var graphics = Drawing.Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(area.Left, area.Top, 0, 0, new Drawing.Size(area.Width, area.Height), Drawing.CopyPixelOperation.SourceCopy);

            return FromBitmap(bitmap);
        }

        public static ScreenFrame FromBitmap(Drawing.Bitmap source)
        {
            using var bitmap = source.PixelFormat == DrawingImaging.PixelFormat.Format32bppArgb
                ? (Drawing.Bitmap)source.Clone()
                : ConvertTo32(source);

            var data = bitmap.LockBits(new Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), DrawingImaging.ImageLockMode.ReadOnly, DrawingImaging.PixelFormat.Format32bppArgb);
            var pixels = new byte[bitmap.Width * bitmap.Height * 4];

            for (int y = 0; y < bitmap.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * bitmap.Width * 4, bitmap.Width * 4);

            bitmap.UnlockBits(data);
            return new ScreenFrame(bitmap.Width, bitmap.Height, pixels);
        }

        private static Drawing.Bitmap ConvertTo32(Drawing.Bitmap source)
        {
            var copy = new Drawing.Bitmap(source.Width, source.Height, DrawingImaging.PixelFormat.Format32bppArgb);
            using var graphics = Drawing.Graphics.FromImage(copy);
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
            return copy;
        }

        public static ScreenFrame Load(string path)
        {
            using var bitmap = new Drawing.Bitmap(path);
            return FromBitmap(bitmap);
        }

        public void Save(string path)
        {
            using var bitmap = ToBitmap();
            bitmap.Save(path, DrawingImaging.ImageFormat.Png);
        }

        public Drawing.Bitmap ToBitmap()
        {
            var bitmap = new Drawing.Bitmap(Width, Height, DrawingImaging.PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(new Drawing.Rectangle(0, 0, Width, Height), DrawingImaging.ImageLockMode.WriteOnly, DrawingImaging.PixelFormat.Format32bppArgb);

            for (int y = 0; y < Height; y++)
                Marshal.Copy(Pixels, y * Width * 4, data.Scan0 + y * data.Stride, Width * 4);

            bitmap.UnlockBits(data);
            return bitmap;
        }

        public ScreenFrame Crop(int x, int y, int width, int height)
        {
            x = Math.Clamp(x, 0, Width - 1);
            y = Math.Clamp(y, 0, Height - 1);
            width = Math.Clamp(width, 1, Width - x);
            height = Math.Clamp(height, 1, Height - y);

            var pixels = new byte[width * height * 4];

            for (int row = 0; row < height; row++)
                Buffer.BlockCopy(Pixels, ((y + row) * Width + x) * 4, pixels, row * width * 4, width * 4);

            return new ScreenFrame(width, height, pixels);
        }

        /// <summary>Average colour of a 3x3 block, to be robust against a single odd pixel.</summary>
        public (byte R, byte G, byte B) SampleColor(int x, int y)
        {
            int r = 0, g = 0, b = 0, n = 0;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int px = Math.Clamp(x + dx, 0, Width - 1), py = Math.Clamp(y + dy, 0, Height - 1);
                    int i = (py * Width + px) * 4;
                    b += Pixels[i]; g += Pixels[i + 1]; r += Pixels[i + 2]; n++;
                }
            }

            return ((byte)(r / n), (byte)(g / n), (byte)(b / n));
        }

        /// <summary>Grayscale copy, optionally resized (nearest neighbour), for template matching.</summary>
        public byte[] ToGray(int width, int height)
        {
            var gray = new byte[width * height];

            for (int y = 0; y < height; y++)
            {
                int sy = Math.Min(Height - 1, y * Height / height);

                for (int x = 0; x < width; x++)
                {
                    int sx = Math.Min(Width - 1, x * Width / width);
                    int i = (sy * Width + sx) * 4;
                    gray[y * width + x] = (byte)((Pixels[i] * 29 + Pixels[i + 1] * 150 + Pixels[i + 2] * 77) >> 8);
                }
            }

            return gray;
        }
    }
}
