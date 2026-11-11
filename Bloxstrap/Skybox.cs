using System.Windows.Media;
using System.Windows.Media.Imaging;

using Drawing = System.Drawing;
using DrawingImaging = System.Drawing.Imaging;

namespace Bloxstrap
{
    /// <summary>
    /// PSYstrap custom skybox.
    ///
    /// Roblox draws its default sky (the one used when a game has no Sky object of its own) from six cube faces in
    /// PlatformContent\pc\textures\sky\sky512_{bk,dn,ft,lf,rt,up}.tex - DXT1 DDS files, 1024x1024 with mipmaps.
    /// We write replacements into the Modifications folder; the normal mod system copies them over the
    /// Roblox files at launch and restores the originals when they're removed.
    ///
    /// Face pixel buffers are BGRA32, FaceSize x FaceSize, top row first. Side faces have the sky at the top,
    /// same convention as a Roblox Sky object's SkyboxBk/Dn/Ft/Lf/Rt/Up textures.
    /// </summary>
    public static class Skybox
    {
        public const int FaceSize = 1024;

        public static readonly string[] Faces = { "bk", "dn", "ft", "lf", "rt", "up" };

        private static readonly Dictionary<string, string[]> FaceAliases = new()
        {
            { "bk", new[] { "bk", "back" } },
            { "dn", new[] { "dn", "down", "bottom" } },
            { "ft", new[] { "ft", "front" } },
            { "lf", new[] { "lf", "left" } },
            { "rt", new[] { "rt", "right" } },
            { "up", new[] { "up", "top" } }
        };

        public static string ModDirectory => Path.Combine(Paths.Modifications, "PlatformContent", "pc", "textures", "sky");

        public static string GetFacePath(string face) => Path.Combine(ModDirectory, $"sky512_{face}.tex");

        public static bool IsInstalled => Faces.All(face => File.Exists(GetFacePath(face)));

        #region Install / remove
        public static void Install(IReadOnlyDictionary<string, byte[]> faces)
        {
            const string LOG_IDENT = "Skybox::Install";

            Directory.CreateDirectory(ModDirectory);

            foreach (string face in Faces)
            {
                string path = GetFacePath(face);

                if (File.Exists(path))
                    Filesystem.AssertReadOnly(path);

                File.WriteAllBytes(path, DdsBc1.Encode(faces[face], FaceSize));
            }

            App.Logger.WriteLine(LOG_IDENT, $"Wrote skybox to {ModDirectory}");
        }

        public static void Remove()
        {
            const string LOG_IDENT = "Skybox::Remove";

            foreach (string face in Faces)
            {
                string path = GetFacePath(face);

                if (!File.Exists(path))
                    continue;

                Filesystem.AssertReadOnly(path);
                File.Delete(path);
            }

            App.Logger.WriteLine(LOG_IDENT, "Removed custom skybox");
        }

        /// <summary>Decodes the currently installed custom skybox, or null if there isn't a complete one.</summary>
        public static Dictionary<string, byte[]>? LoadInstalled()
        {
            if (!IsInstalled)
                return null;

            var faces = new Dictionary<string, byte[]>();

            foreach (string face in Faces)
            {
                var pixels = DdsBc1.DecodeTopLevel(File.ReadAllBytes(GetFacePath(face)), out int width, out int height);

                if (pixels is null || width != height)
                    return null;

                faces[face] = pixels;
            }

            return faces;
        }
        #endregion

        #region Custom images
        /// <summary>
        /// Loads either 1 image (used for every face) or 6 images whose file names say which face they are.
        /// Throws InvalidOperationException with a user-readable message when the selection doesn't work.
        /// </summary>
        public static Dictionary<string, byte[]> LoadImages(IReadOnlyList<string> files)
        {
            if (files.Count == 1)
            {
                byte[] pixels = LoadImage(files[0]);
                return Faces.ToDictionary(face => face, _ => pixels);
            }

            if (files.Count != 6)
                throw new InvalidOperationException($"Pick 6 images (one per side) or 1 image to use on every side. You picked {files.Count}.");

            var assigned = new Dictionary<string, string>();

            foreach (string file in files)
            {
                string? face = DetectFace(Path.GetFileNameWithoutExtension(file).ToLowerInvariant());

                if (face is null)
                {
                    throw new InvalidOperationException(
                        $"Couldn't tell which side \"{Path.GetFileName(file)}\" is.\n\n" +
                        "Name each image with bk, dn, ft, lf, rt or up (or back, down, front, left, right, top), " +
                        "for example sky_bk.png or SkyboxUp.png.");
                }

                if (assigned.TryGetValue(face, out string? other))
                    throw new InvalidOperationException($"\"{Path.GetFileName(other)}\" and \"{Path.GetFileName(file)}\" are both the \"{face}\" side.");

                assigned[face] = file;
            }

            return assigned.ToDictionary(pair => pair.Key, pair => LoadImage(pair.Value));
        }

        /// <summary>
        /// Works out which face a lower-cased file name refers to, or null if it's unclear.
        /// A whole-word alias ("sky_bk", "my sky down") beats one that's only at the end ("SkyboxBk", "cloudsUp"),
        /// and longer aliases beat shorter ones, so "left" is the left face even though it also ends in "ft".
        /// </summary>
        private static string? DetectFace(string name)
        {
            string? best = null;
            int bestScore = 0;
            bool tie = false;

            foreach (string face in Faces)
            {
                foreach (string alias in FaceAliases[face])
                {
                    int score = 0;

                    if (Regex.IsMatch(name, $"(?<![a-z]){alias}(?![a-z])"))
                        score = 1000 + alias.Length;
                    else if (name.EndsWith(alias, StringComparison.Ordinal))
                        score = alias.Length;

                    if (score > bestScore)
                    {
                        best = face;
                        bestScore = score;
                        tie = false;
                    }
                    else if (score > 0 && score == bestScore && face != best)
                    {
                        tie = true;
                    }
                }
            }

            return tie ? null : best;
        }

        private static byte[] LoadImage(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();

            if (extension is ".tex" or ".dds")
            {
                var pixels = DdsBc1.DecodeTopLevel(File.ReadAllBytes(path), out int width, out int height)
                    ?? throw new InvalidOperationException($"\"{Path.GetFileName(path)}\" isn't a DXT1 texture. Use a PNG or JPG instead.");

                if (width == FaceSize && height == FaceSize)
                    return pixels;

                using var bitmap = ToBitmap(pixels, width, height);
                return Resample(bitmap);
            }

            Drawing.Image image;

            try
            {
                image = Drawing.Image.FromFile(path);
            }
            catch (Exception ex) when (ex is OutOfMemoryException or ArgumentException or FileNotFoundException)
            {
                throw new InvalidOperationException($"\"{Path.GetFileName(path)}\" couldn't be opened as an image.");
            }

            using (image)
                return Resample(image);
        }

        private static byte[] Resample(Drawing.Image source)
        {
            using var bitmap = new Drawing.Bitmap(FaceSize, FaceSize, DrawingImaging.PixelFormat.Format32bppArgb);

            using (var graphics = Drawing.Graphics.FromImage(bitmap))
            using (var attributes = new DrawingImaging.ImageAttributes())
            {
                graphics.InterpolationMode = Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.CompositingMode = Drawing.Drawing2D.CompositingMode.SourceCopy;

                // stops the bicubic filter pulling in transparent black at the edges, which would show as seams
                attributes.SetWrapMode(Drawing.Drawing2D.WrapMode.TileFlipXY);

                graphics.DrawImage(source, new Drawing.Rectangle(0, 0, FaceSize, FaceSize),
                    0, 0, source.Width, source.Height, Drawing.GraphicsUnit.Pixel, attributes);
            }

            var data = bitmap.LockBits(new Drawing.Rectangle(0, 0, FaceSize, FaceSize), DrawingImaging.ImageLockMode.ReadOnly, DrawingImaging.PixelFormat.Format32bppArgb);
            var pixels = new byte[FaceSize * FaceSize * 4];

            for (int y = 0; y < FaceSize; y++)
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * FaceSize * 4, FaceSize * 4);

            bitmap.UnlockBits(data);

            // the sky is opaque; DXT1 has no use for alpha here
            for (int i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;

            return pixels;
        }

        private static Drawing.Bitmap ToBitmap(byte[] pixels, int width, int height)
        {
            var bitmap = new Drawing.Bitmap(width, height, DrawingImaging.PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(new Drawing.Rectangle(0, 0, width, height), DrawingImaging.ImageLockMode.WriteOnly, DrawingImaging.PixelFormat.Format32bppArgb);

            for (int y = 0; y < height; y++)
                System.Runtime.InteropServices.Marshal.Copy(pixels, y * width * 4, data.Scan0 + y * data.Stride, width * 4);

            bitmap.UnlockBits(data);
            return bitmap;
        }
        #endregion

        #region Generated presets
        private record struct Stop(float Elevation, byte R, byte G, byte B);

        private record PresetStyle(Stop[] Stops, float StarDensity, byte StarR, byte StarG, byte StarB);

        // stops are by elevation in degrees, -90 (straight down) to 90 (straight up)
        private static PresetStyle GetStyle(SkyboxPreset preset) => preset switch
        {
            SkyboxPreset.PsyGreen => new(new Stop[]
            {
                new(-90, 4, 12, 6), new(-12, 10, 36, 16), new(0, 72, 196, 58), new(5, 34, 120, 40),
                new(28, 12, 54, 22), new(90, 4, 16, 8)
            }, 0.010f, 242, 193, 78),

            SkyboxPreset.Midnight => new(new Stop[]
            {
                new(-90, 2, 3, 8), new(-6, 8, 12, 28), new(0, 42, 56, 104), new(10, 15, 23, 54),
                new(45, 5, 8, 24), new(90, 2, 3, 10)
            }, 0.016f, 220, 228, 255),

            SkyboxPreset.Sunset => new(new Stop[]
            {
                new(-90, 30, 14, 30), new(-6, 118, 50, 52), new(0, 255, 152, 72), new(7, 240, 112, 82),
                new(24, 142, 82, 140), new(55, 52, 50, 122), new(90, 24, 30, 80)
            }, 0.002f, 255, 240, 220),

            SkyboxPreset.CottonCandy => new(new Stop[]
            {
                new(-90, 255, 228, 242), new(0, 255, 196, 226), new(22, 202, 190, 255), new(90, 122, 178, 255)
            }, 0f, 0, 0, 0),

            SkyboxPreset.Void => new(new Stop[] { new(-90, 0, 0, 0), new(90, 0, 0, 0) }, 0f, 0, 0, 0),

            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Not a generated preset")
        };

        public static bool IsGenerated(SkyboxPreset preset) => preset is not (SkyboxPreset.Default or SkyboxPreset.Custom);

        /// <summary>Renders a preset. Pass a small size for previews; installs use FaceSize.</summary>
        public static Dictionary<string, byte[]> Generate(SkyboxPreset preset, int size = FaceSize)
        {
            var style = GetStyle(preset);
            var faces = new Dictionary<string, byte[]>();

            for (int i = 0; i < Faces.Length; i++)
                faces[Faces[i]] = GenerateFace(Faces[i], i, style, size);

            return faces;
        }

        private static byte[] GenerateFace(string face, int faceIndex, PresetStyle style, int size)
        {
            var pixels = new byte[size * size * 4];
            int starCell = Math.Max(2, size / 256); // ~1 star cell per 4 texels at full size

            for (int y = 0; y < size; y++)
            {
                float v = (y + 0.5f) / size * 2 - 1; // -1 top .. 1 bottom

                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2 - 1;

                    // vertical component of the view direction through this texel
                    float dirY = face switch
                    {
                        "up" => 1,
                        "dn" => -1,
                        _ => -v
                    };

                    float horizontal = face is "up" or "dn" ? MathF.Sqrt(u * u + v * v) : MathF.Sqrt(u * u + 1);
                    float elevation = MathF.Atan2(dirY, horizontal) * 180f / MathF.PI;

                    SampleGradient(style.Stops, elevation, out float r, out float g, out float b);

                    // stars: one candidate per cell, only well above the horizon
                    if (style.StarDensity > 0 && elevation > 4)
                    {
                        int cellX = x / starCell, cellY = y / starCell;
                        uint hash = Hash((uint)faceIndex, (uint)cellX, (uint)cellY);

                        if ((hash & 0xFFFF) / 65535f < style.StarDensity
                            && x % starCell == (int)((hash >> 16) % (uint)starCell)
                            && y % starCell == (int)((hash >> 20) % (uint)starCell))
                        {
                            float fade = Math.Clamp((elevation - 4) / 16f, 0, 1);
                            float brightness = (0.45f + ((hash >> 24) & 0xFF) / 255f * 0.55f) * fade;

                            r += (style.StarR - r) * brightness;
                            g += (style.StarG - g) * brightness;
                            b += (style.StarB - b) * brightness;
                        }
                    }

                    // a little noise so the gradient doesn't band after 16-bit DXT1 quantisation
                    float dither = (Hash((uint)faceIndex + 7, (uint)x, (uint)y) & 0xFF) / 255f * 4f - 2f;

                    int p = (y * size + x) * 4;
                    pixels[p + 0] = ToByte(b + dither);
                    pixels[p + 1] = ToByte(g + dither * 0.5f);
                    pixels[p + 2] = ToByte(r + dither);
                    pixels[p + 3] = 255;
                }
            }

            return pixels;
        }

        private static void SampleGradient(Stop[] stops, float elevation, out float r, out float g, out float b)
        {
            int i = 0;
            while (i < stops.Length - 2 && elevation > stops[i + 1].Elevation)
                i++;

            var lo = stops[i];
            var hi = stops[i + 1];
            float t = Math.Clamp((elevation - lo.Elevation) / Math.Max(0.0001f, hi.Elevation - lo.Elevation), 0, 1);
            t = t * t * (3 - 2 * t); // smoothstep

            r = lo.R + (hi.R - lo.R) * t;
            g = lo.G + (hi.G - lo.G) * t;
            b = lo.B + (hi.B - lo.B) * t;
        }

        private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value), 0, 255);

        private static uint Hash(uint a, uint b, uint c)
        {
            uint h = a * 0x9E3779B1u ^ b * 0x85EBCA77u ^ c * 0xC2B2AE3Du;
            h ^= h >> 15; h *= 0x2C1B3C6Du;
            h ^= h >> 12; h *= 0x297A2D39u;
            h ^= h >> 15;
            return h;
        }
        #endregion

        #region Preview
        /// <summary>Box-downscales a square face to a small frozen bitmap for the settings UI.</summary>
        public static BitmapSource MakePreview(byte[] pixels, int previewSize = 96)
        {
            int size = (int)Math.Sqrt(pixels.Length / 4);
            int step = Math.Max(1, size / previewSize);
            int outSize = size / step;
            var output = new byte[outSize * outSize * 4];

            for (int y = 0; y < outSize; y++)
            {
                for (int x = 0; x < outSize; x++)
                {
                    int r = 0, g = 0, b = 0;

                    for (int sy = 0; sy < step; sy++)
                    {
                        for (int sx = 0; sx < step; sx++)
                        {
                            int p = ((y * step + sy) * size + x * step + sx) * 4;
                            b += pixels[p]; g += pixels[p + 1]; r += pixels[p + 2];
                        }
                    }

                    int n = step * step, o = (y * outSize + x) * 4;
                    output[o] = (byte)(b / n); output[o + 1] = (byte)(g / n); output[o + 2] = (byte)(r / n); output[o + 3] = 255;
                }
            }

            var bitmap = BitmapSource.Create(outSize, outSize, 96, 96, PixelFormats.Bgra32, null, output, outSize * 4);
            bitmap.Freeze();
            return bitmap;
        }
        #endregion
    }
}
