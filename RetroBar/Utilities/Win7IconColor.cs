using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RetroBar.Utilities
{
    public static class Win7IconColor
    {
        // Aero TaskBand2/Button GLOWCOLOR；原版在图标没有明显主色时使用此颜色。
        public static readonly Color DefaultColor = Color.FromRgb(0x33, 0xC2, 0xFF);
        private static readonly ConditionalWeakTable<ImageSource, CachedColor> Cache = new ConditionalWeakTable<ImageSource, CachedColor>();

        public static Color GetDominantColor(ImageSource icon)
        {
            if (icon == null) return DefaultColor;
            return icon.IsFrozen ? Cache.GetValue(icon, source => new CachedColor(Sample(source))).Value : Sample(icon);
        }

        private static Color Sample(ImageSource icon)
        {
            BitmapSource bitmap;
            if (icon is BitmapSource original && original.PixelWidth == 32 && original.PixelHeight == 32)
            {
                bitmap = original.Format == PixelFormats.Pbgra32 ? original : new FormatConvertedBitmap(original, PixelFormats.Pbgra32, null, 0);
            }
            else
            {
                DrawingVisual visual = new DrawingVisual();
                using (DrawingContext context = visual.RenderOpen()) context.DrawImage(icon, new Rect(0, 0, 32, 32));
                RenderTargetBitmap rendered = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
                rendered.Render(visual);
                bitmap = rendered;
            }

            byte[] pixels = new byte[32 * 32 * 4];
            bitmap.CopyPixels(pixels, 32 * 4, 0);
            int[] counts = new int[27], red = new int[27], green = new int[27], blue = new int[27];
            int total = 0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int alpha = pixels[i + 3];
                if (alpha == 0) continue;
                int r = pixels[i + 2] * 255 / alpha;
                int g = pixels[i + 1] * 255 / alpha;
                int b = pixels[i] * 255 / alpha;
                // 对应 Explorer!GetDominantColorFromHICON：不按透明度加权，排除近灰色像素。
                if (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) <= 48) continue;
                int bucket = 9 * (r / 86) + 3 * (g / 86) + b / 86;
                counts[bucket]++;
                red[bucket] += r;
                green[bucket] += g;
                blue[bucket] += b;
                total++;
            }
            int best = 0;
            for (int i = 1; i < counts.Length; i++) if (counts[i] > counts[best]) best = i;
            int count = counts[best];
            if (count == 0 || (count * 100 + total / 2) / total < 7) return DefaultColor;
            return Color.FromRgb((byte)(red[best] / count), (byte)(green[best] / count), (byte)(blue[best] / count));
        }

        private sealed class CachedColor
        {
            public Color Value { get; }
            public CachedColor(Color value) => Value = value;
        }
    }
}
