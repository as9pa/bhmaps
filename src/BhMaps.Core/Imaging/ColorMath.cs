namespace BhMaps.Core.Imaging;

/// <summary>The colour sums the platform recolour and the background editor share: RGB to HSL and back, and the
/// saturation and contrast curves the background sliders use (3.6 E4).</summary>
internal static class ColorMath
{
    /// <summary>RGB bytes to a hue in degrees (0..360) with saturation and lightness in 0..1.</summary>
    internal static (double H, double S, double L) ToHsl(byte r, byte g, byte b)
    {
        var rd = r / 255.0;
        var gd = g / 255.0;
        var bd = b / 255.0;
        var max = Math.Max(rd, Math.Max(gd, bd));
        var min = Math.Min(rd, Math.Min(gd, bd));
        var lightness = (max + min) / 2;
        var delta = max - min;
        if (delta == 0)
        {
            return (0, 0, lightness);
        }

        var saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);
        double hue;
        if (max == rd)
        {
            hue = ((gd - bd) / delta) + (gd < bd ? 6 : 0);
        }
        else if (max == gd)
        {
            hue = ((bd - rd) / delta) + 2;
        }
        else
        {
            hue = ((rd - gd) / delta) + 4;
        }

        return (hue * 60, saturation, lightness);
    }

    /// <summary>A hue in degrees (0..360) with saturation and lightness in 0..1 back to RGB bytes.</summary>
    internal static (byte R, byte G, byte B) ToRgb(double h, double s, double l)
    {
        if (s == 0)
        {
            var grey = Channel(l);
            return (grey, grey, grey);
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - (l * s);
        var p = (2 * l) - q;
        var turn = h / 360;
        return (Channel(FromTurn(p, q, turn + (1.0 / 3))), Channel(FromTurn(p, q, turn)), Channel(FromTurn(p, q, turn - (1.0 / 3))));
    }

    /// <summary>Negative shifts wrap up, so -120 and +240 name the same rotation.</summary>
    internal static double Wrap(double degrees)
    {
        var wrapped = degrees % 360;
        return wrapped < 0 ? wrapped + 360 : wrapped;
    }

    /// <summary>Scales an HSL saturation by (1 + <paramref name="amount"/>), so -1 greys the colour out, 0 leaves
    /// it alone and +1 doubles it; the result is clamped to 0..1.</summary>
    internal static double Saturate(double s, double amount) => Math.Clamp(s * (1 + amount), 0, 1);

    /// <summary>The slope a contrast of -1..1 stretches each channel by around mid grey: 1 at 0, up to 3 at +1
    /// and down to 0 (flat grey) at -1.</summary>
    internal static double ContrastSlope(double contrast) => contrast >= 0 ? 1 + (contrast * 2) : 1 + contrast;

    /// <summary>One channel pushed away from (or pulled toward) 128 by <paramref name="slope"/>, clamped to a byte.</summary>
    internal static byte Contrast(byte v, double slope) =>
        (byte)Math.Round(Math.Clamp(((v - 128) * slope) + 128, 0, 255), MidpointRounding.AwayFromZero);

    /// <summary>One channel of the HSL to RGB inverse, at <paramref name="t"/> turns around the colour wheel.</summary>
    private static double FromTurn(double p, double q, double t)
    {
        if (t < 0)
        {
            t += 1;
        }
        else if (t > 1)
        {
            t -= 1;
        }

        if (t < 1.0 / 6)
        {
            return p + ((q - p) * 6 * t);
        }

        if (t < 1.0 / 2)
        {
            return q;
        }

        if (t < 2.0 / 3)
        {
            return p + ((q - p) * ((2.0 / 3) - t) * 6);
        }

        return p;
    }

    private static byte Channel(double value) =>
        (byte)Math.Round(Math.Clamp(value, 0, 1) * 255, MidpointRounding.AwayFromZero);
}
