namespace Bloxstrap.Utility
{
    /// <summary>
    /// PSYstrap: minimal DXT1 (BC1) DDS encoder/decoder.
    /// Output layout matches Roblox's PlatformContent\pc\textures\sky\sky512_*.tex exactly:
    /// "DDS " + 124-byte header, flags 0x000A1007, FourCC DXT1, square, full mip chain down to 1x1.
    /// Pixel buffers are BGRA32, row-major, top row first.
    /// </summary>
    public static class DdsBc1
    {
        public static byte[] Encode(byte[] bgra, int size)
        {
            if (size < 1 || (size & (size - 1)) != 0)
                throw new ArgumentException("Size must be a power of two", nameof(size));

            if (bgra.Length != size * size * 4)
                throw new ArgumentException("Pixel buffer does not match size", nameof(bgra));

            int mipCount = (int)Math.Log2(size) + 1;

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            WriteHeader(writer, size, mipCount);

            byte[] level = bgra;
            int levelSize = size;

            for (int mip = 0; mip < mipCount; mip++)
            {
                EncodeLevel(writer, level, levelSize);

                if (levelSize > 1)
                {
                    level = Downsample(level, levelSize);
                    levelSize /= 2;
                }
            }

            writer.Flush();
            return stream.ToArray();
        }

        /// <summary>
        /// Decodes the top mip level of a DXT1 DDS file to BGRA32. Returns null if the file isn't DXT1.
        /// </summary>
        public static byte[]? DecodeTopLevel(byte[] dds, out int width, out int height)
        {
            width = height = 0;

            if (dds.Length < 128 || dds[0] != 'D' || dds[1] != 'D' || dds[2] != 'S' || dds[3] != ' ')
                return null;

            if (Encoding.ASCII.GetString(dds, 84, 4) != "DXT1")
                return null;

            height = BitConverter.ToInt32(dds, 12);
            width = BitConverter.ToInt32(dds, 16);

            int blocksX = Math.Max(1, (width + 3) / 4);
            int blocksY = Math.Max(1, (height + 3) / 4);

            if (dds.Length < 128 + blocksX * blocksY * 8)
                return null;

            var output = new byte[width * height * 4];
            var palette = new int[4 * 3];
            int offset = 128;

            for (int by = 0; by < blocksY; by++)
            {
                for (int bx = 0; bx < blocksX; bx++, offset += 8)
                {
                    ushort c0 = BitConverter.ToUInt16(dds, offset);
                    ushort c1 = BitConverter.ToUInt16(dds, offset + 2);
                    uint indices = BitConverter.ToUInt32(dds, offset + 4);

                    BuildPalette(c0, c1, palette, out bool transparentIndex3);

                    for (int i = 0; i < 16; i++)
                    {
                        int x = bx * 4 + (i & 3);
                        int y = by * 4 + (i >> 2);

                        if (x >= width || y >= height)
                            continue;

                        int idx = (int)((indices >> (i * 2)) & 3);
                        int p = (y * width + x) * 4;

                        output[p + 0] = (byte)palette[idx * 3 + 2];
                        output[p + 1] = (byte)palette[idx * 3 + 1];
                        output[p + 2] = (byte)palette[idx * 3 + 0];
                        output[p + 3] = (byte)(transparentIndex3 && idx == 3 ? 0 : 255);
                    }
                }
            }

            return output;
        }

        private static void WriteHeader(BinaryWriter writer, int size, int mipCount)
        {
            int blocks = Math.Max(1, size / 4);

            writer.Write(Encoding.ASCII.GetBytes("DDS "));
            writer.Write(124);                    // dwSize
            writer.Write(0x000A1007);             // CAPS | HEIGHT | WIDTH | PIXELFORMAT | MIPMAPCOUNT | LINEARSIZE
            writer.Write(size);                   // dwHeight
            writer.Write(size);                   // dwWidth
            writer.Write(blocks * blocks * 8);    // dwPitchOrLinearSize
            writer.Write(0);                      // dwDepth
            writer.Write(mipCount);               // dwMipMapCount

            for (int i = 0; i < 11; i++)
                writer.Write(0);                  // dwReserved1

            writer.Write(32);                     // ddspf.dwSize
            writer.Write(0x4);                    // DDPF_FOURCC
            writer.Write(Encoding.ASCII.GetBytes("DXT1"));
            writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);

            writer.Write(0x401008);               // COMPLEX | TEXTURE | MIPMAP
            writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
        }

        private static byte[] Downsample(byte[] src, int size)
        {
            int half = size / 2;
            var dst = new byte[half * half * 4];

            for (int y = 0; y < half; y++)
            {
                for (int x = 0; x < half; x++)
                {
                    int a = ((y * 2) * size + x * 2) * 4;
                    int b = a + 4;
                    int c = a + size * 4;
                    int d = c + 4;
                    int o = (y * half + x) * 4;

                    for (int ch = 0; ch < 4; ch++)
                        dst[o + ch] = (byte)((src[a + ch] + src[b + ch] + src[c + ch] + src[d + ch] + 2) / 4);
                }
            }

            return dst;
        }

        private static void EncodeLevel(BinaryWriter writer, byte[] pixels, int size)
        {
            int blocks = Math.Max(1, (size + 3) / 4);
            var block = new float[16 * 3];

            for (int by = 0; by < blocks; by++)
            {
                for (int bx = 0; bx < blocks; bx++)
                {
                    for (int i = 0; i < 16; i++)
                    {
                        int x = Math.Min(bx * 4 + (i & 3), size - 1);
                        int y = Math.Min(by * 4 + (i >> 2), size - 1);
                        int p = (y * size + x) * 4;

                        block[i * 3 + 0] = pixels[p + 2]; // R
                        block[i * 3 + 1] = pixels[p + 1]; // G
                        block[i * 3 + 2] = pixels[p + 0]; // B
                    }

                    EncodeBlock(writer, block);
                }
            }
        }

        private static void EncodeBlock(BinaryWriter writer, float[] px)
        {
            // mean
            float mr = 0, mg = 0, mb = 0;
            for (int i = 0; i < 16; i++) { mr += px[i * 3]; mg += px[i * 3 + 1]; mb += px[i * 3 + 2]; }
            mr /= 16; mg /= 16; mb /= 16;

            // covariance
            float crr = 0, crg = 0, crb = 0, cgg = 0, cgb = 0, cbb = 0;
            for (int i = 0; i < 16; i++)
            {
                float r = px[i * 3] - mr, g = px[i * 3 + 1] - mg, b = px[i * 3 + 2] - mb;
                crr += r * r; crg += r * g; crb += r * b; cgg += g * g; cgb += g * b; cbb += b * b;
            }

            // principal axis by power iteration
            float ar = 1, ag = 1, ab = 1;
            for (int it = 0; it < 6; it++)
            {
                float nr = crr * ar + crg * ag + crb * ab;
                float ng = crg * ar + cgg * ag + cgb * ab;
                float nb = crb * ar + cgb * ag + cbb * ab;
                float len = MathF.Sqrt(nr * nr + ng * ng + nb * nb);
                if (len < 1e-6f) { ar = ag = ab = 0; break; }
                ar = nr / len; ag = ng / len; ab = nb / len;
            }

            // endpoints = extremes along the axis, inset slightly to reduce error
            float minT = float.MaxValue, maxT = float.MinValue;
            for (int i = 0; i < 16; i++)
            {
                float t = (px[i * 3] - mr) * ar + (px[i * 3 + 1] - mg) * ag + (px[i * 3 + 2] - mb) * ab;
                if (t < minT) minT = t;
                if (t > maxT) maxT = t;
            }

            float inset = (maxT - minT) / 16f;
            minT += inset; maxT -= inset;

            float[] e0 = { mr + ar * maxT, mg + ag * maxT, mb + ab * maxT };
            float[] e1 = { mr + ar * minT, mg + ag * minT, mb + ab * minT };

            ushort c0 = To565(e0), c1 = To565(e1);
            uint indices = ComputeIndices(px, ref c0, ref c1);

            // one least-squares refinement pass on the chosen indices
            if (c0 != c1 && Refine(px, indices, c0, c1, out ushort r0, out ushort r1))
            {
                uint refinedIndices = ComputeIndices(px, ref r0, ref r1);

                if (BlockError(px, r0, r1, refinedIndices) < BlockError(px, c0, c1, indices))
                {
                    c0 = r0; c1 = r1; indices = refinedIndices;
                }
            }

            writer.Write(c0);
            writer.Write(c1);
            writer.Write(indices);
        }

        /// <summary>Orders endpoints for 4-colour mode and picks the nearest palette entry per pixel.</summary>
        private static uint ComputeIndices(float[] px, ref ushort c0, ref ushort c1)
        {
            if (c0 < c1)
                (c0, c1) = (c1, c0);

            if (c0 == c1)
                return 0; // solid block: every pixel uses c0

            var palette = new int[12];
            BuildPalette(c0, c1, palette, out _);

            uint indices = 0;

            for (int i = 0; i < 16; i++)
            {
                int best = 0;
                float bestDist = float.MaxValue;

                for (int p = 0; p < 4; p++)
                {
                    float dr = px[i * 3] - palette[p * 3];
                    float dg = px[i * 3 + 1] - palette[p * 3 + 1];
                    float db = px[i * 3 + 2] - palette[p * 3 + 2];
                    float d = dr * dr + dg * dg + db * db;

                    if (d < bestDist) { bestDist = d; best = p; }
                }

                indices |= (uint)best << (i * 2);
            }

            return indices;
        }

        private static bool Refine(float[] px, uint indices, ushort c0, ushort c1, out ushort r0, out ushort r1)
        {
            // weight of endpoint 0 for each index in 4-colour mode
            float[] w0 = { 1f, 0f, 2f / 3f, 1f / 3f };

            float aa = 0, ab = 0, bb = 0;
            float[] ax = new float[3], bx = new float[3];

            for (int i = 0; i < 16; i++)
            {
                float a = w0[(indices >> (i * 2)) & 3], b = 1 - a;
                aa += a * a; ab += a * b; bb += b * b;

                for (int ch = 0; ch < 3; ch++)
                {
                    ax[ch] += a * px[i * 3 + ch];
                    bx[ch] += b * px[i * 3 + ch];
                }
            }

            float det = aa * bb - ab * ab;
            r0 = c0; r1 = c1;

            if (MathF.Abs(det) < 1e-6f)
                return false;

            var e0 = new float[3];
            var e1 = new float[3];

            for (int ch = 0; ch < 3; ch++)
            {
                e0[ch] = (bb * ax[ch] - ab * bx[ch]) / det;
                e1[ch] = (aa * bx[ch] - ab * ax[ch]) / det;
            }

            r0 = To565(e0);
            r1 = To565(e1);
            return true;
        }

        private static float BlockError(float[] px, ushort c0, ushort c1, uint indices)
        {
            var palette = new int[12];
            BuildPalette(c0, c1, palette, out _);

            float error = 0;

            for (int i = 0; i < 16; i++)
            {
                int p = (int)((indices >> (i * 2)) & 3);
                float dr = px[i * 3] - palette[p * 3];
                float dg = px[i * 3 + 1] - palette[p * 3 + 1];
                float db = px[i * 3 + 2] - palette[p * 3 + 2];
                error += dr * dr + dg * dg + db * db;
            }

            return error;
        }

        private static ushort To565(float[] rgb)
        {
            int r = Math.Clamp((int)MathF.Round(rgb[0] * 31f / 255f), 0, 31);
            int g = Math.Clamp((int)MathF.Round(rgb[1] * 63f / 255f), 0, 63);
            int b = Math.Clamp((int)MathF.Round(rgb[2] * 31f / 255f), 0, 31);
            return (ushort)((r << 11) | (g << 5) | b);
        }

        private static void BuildPalette(ushort c0, ushort c1, int[] palette, out bool transparentIndex3)
        {
            Expand565(c0, out palette[0], out palette[1], out palette[2]);
            Expand565(c1, out palette[3], out palette[4], out palette[5]);

            transparentIndex3 = c0 <= c1;

            for (int ch = 0; ch < 3; ch++)
            {
                if (c0 > c1)
                {
                    palette[6 + ch] = (2 * palette[ch] + palette[3 + ch] + 1) / 3;
                    palette[9 + ch] = (palette[ch] + 2 * palette[3 + ch] + 1) / 3;
                }
                else
                {
                    palette[6 + ch] = (palette[ch] + palette[3 + ch]) / 2;
                    palette[9 + ch] = 0;
                }
            }
        }

        private static void Expand565(ushort c, out int r, out int g, out int b)
        {
            int r5 = (c >> 11) & 31, g6 = (c >> 5) & 63, b5 = c & 31;
            r = (r5 << 3) | (r5 >> 2);
            g = (g6 << 2) | (g6 >> 4);
            b = (b5 << 3) | (b5 >> 2);
        }
    }
}
