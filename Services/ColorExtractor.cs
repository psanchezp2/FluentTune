using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FluentTune.Services;

/// <summary>Pulls a single vibrant accent colour out of album artwork.</summary>
public static class ColorExtractor
{
    public static Color GetAccent(BitmapSource? source, Color fallback)
    {
        if (source is null || source.PixelWidth == 0 || source.PixelHeight == 0) return fallback;

        try
        {
            const int target = 32;
            double scale = (double)target / Math.Max(source.PixelWidth, source.PixelHeight);
            BitmapSource small = scale < 1.0 ? new TransformedBitmap(source, new ScaleTransform(scale, scale)) : source;
            var bgra = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);

            int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
            var px = new byte[h * stride];
            bgra.CopyPixels(px, stride, 0);

            // Bucket vibrant pixels by hue, then take the heaviest bucket's weighted mean.
            const int buckets = 12;
            var wsum = new double[buckets];
            var rsum = new double[buckets];
            var gsum = new double[buckets];
            var bsum = new double[buckets];

            for (int i = 0; i < px.Length; i += 4)
            {
                double b = px[i], g = px[i + 1], r = px[i + 2];
                var (hue, s, v) = ToHsv(r, g, b);
                if (v < 0.18 || v > 0.97 || s < 0.22) continue; // skip washed-out / near black-white

                int bk = (int)(hue / (360.0 / buckets)) % buckets;
                double weight = s * s * v;
                wsum[bk] += weight;
                rsum[bk] += r * weight;
                gsum[bk] += g * weight;
                bsum[bk] += b * weight;
            }

            int best = -1;
            double bestW = 0;
            for (int k = 0; k < buckets; k++)
                if (wsum[k] > bestW) { bestW = wsum[k]; best = k; }

            if (best < 0) return fallback;

            byte R = (byte)(rsum[best] / wsum[best]);
            byte G = (byte)(gsum[best] / wsum[best]);
            byte B = (byte)(bsum[best] / wsum[best]);
            return Vivify(Color.FromRgb(R, G, B));
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>Boost saturation and clamp brightness so the accent always reads clearly.</summary>
    private static Color Vivify(Color c)
    {
        var (h, s, v) = ToHsv(c.R, c.G, c.B);
        s = Math.Min(1.0, s * 1.25 + 0.05);
        v = Math.Clamp(v, 0.55, 0.95);
        return FromHsv(h, s, v);
    }

    private static (double h, double s, double v) ToHsv(double r, double g, double b)
    {
        r /= 255; g /= 255; b /= 255;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = 0;
        if (d > 1e-6)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * (((b - r) / d) + 2);
            else h = 60 * (((r - g) / d) + 4);
        }
        if (h < 0) h += 360;
        double s = max <= 0 ? 0 : d / max;
        return (h, s, max);
    }

    private static Color FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
        double r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
