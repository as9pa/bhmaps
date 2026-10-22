using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BhMaps.Core.Imaging;
using BhMaps.Core.LevelData;

namespace BhMaps.Tools.SeamAudit;

/// <summary>A decoded piece: its pixel size, its Bgra32 pixels and its straight alpha, row by row.</summary>
internal sealed record PieceImage(int Width, int Height, byte[] Pixels, byte[] Alpha);

/// <summary>Seam pixels left in one layout at one opacity, split by cause. <see cref="Moving"/> and
/// <see cref="Other"/> stay even with the ideal one-layer fade (moving platform groups, and everything else);
/// <see cref="TwiceInMap"/> comes from one file used twice in the layout; <see cref="SharedFile"/> from a file
/// other layouts share. <see cref="Ideal"/> counts seam pixels in the ideal fade itself.</summary>
internal sealed record SeamCounts(long Total, long Moving, long Other, long TwiceInMap, long SharedFile, long Ideal)
{
    public static readonly SeamCounts Zero = new(0, 0, 0, 0, 0, 0);

    public SeamCounts Add(SeamCounts o) =>
        new(
            Total + o.Total,
            Moving + o.Moving,
            Other + o.Other,
            TwiceInMap + o.TwiceInMap,
            SharedFile + o.SharedFile,
            Ideal + o.Ideal
        );
}

/// <summary>Renders each layout with its pieces faded through <see cref="SeamMask"/> and compares it against the
/// ideal fade, where the whole stack composites like one layer. Must run on an STA thread.</summary>
internal sealed class SeamAuditor
{
    /// <summary>A rendered channel counts as a seam when it is off the expected value by more than this.</summary>
    private const int Tolerance = 3;

    /// <summary>Layouts render at most this many pixels on their long side.</summary>
    private const double MaxRenderSide = 6000;

    /// <summary>The decoded image cache is dropped between layouts once it holds more than this many bytes.</summary>
    private const long CacheLimit = 2_000_000_000;

    private readonly AssetSources _sources;
    private readonly IReadOnlyList<LevelDesc> _levels;
    private readonly Dictionary<string, PieceImage> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="mapArtPath">The game's mapArt folder.</param>
    /// <param name="levels">Every layout the mask considers when a file is shared between layouts.</param>
    public SeamAuditor(string mapArtPath, IReadOnlyList<LevelDesc> levels)
    {
        _sources = new AssetSources(mapArtPath);
        _levels = levels;
    }

    /// <summary>Audits one layout at each opacity. Null when none of its pieces resolve. With
    /// <paramref name="diffPath"/>, also writes the coloured diff of the first opacity there: grey correct, yellow
    /// twice in map, cyan shared file, red moving or other.</summary>
    public IReadOnlyDictionary<double, SeamCounts>? Audit(
        LevelDesc level,
        IReadOnlyList<double> opacities,
        string? diffPath = null
    )
    {
        var uses = Place(level);
        if (uses.Count == 0)
        {
            return null;
        }

        var bounds = uses.Select(p => p.Bounds).Aggregate(Rect.Union);
        double scale = Math.Min(1, MaxRenderSide / Math.Max(bounds.Width, bounds.Height));
        int width = (int)Math.Ceiling(bounds.Width * scale),
            height = (int)Math.Ceiling(bounds.Height * scale);
        var reference = Render(uses.Select(p => (p, Full(p.Image))).ToList(), bounds, scale, width, height);
        var result = new Dictionary<double, SeamCounts>();
        foreach (var op in opacities)
        {
            float plain = (float)op;
            var files = uses.Select(p => p.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var pieces = files
                .Select(f =>
                {
                    var d = Get(f)!;
                    return new SeamPiece(f, d.Width, d.Height, d.Alpha, op);
                })
                .ToList();
            var maskWritten = ComputeMask(pieces);

            // The ideal per use: faded under the uses above it in its own group, and ignoring groups.
            var ideal = new List<float[]>();
            var idealUngrouped = new List<float[]>();
            for (int k = 0; k < uses.Count; k++)
            {
                var above = new List<PieceUse>();
                var aboveAny = new List<PieceUse>();
                for (int j = uses.Count - 1; j > k; j--)
                {
                    if (!uses[j].Bounds.IntersectsWith(uses[k].Bounds))
                    {
                        continue;
                    }

                    aboveAny.Add(uses[j]);
                    if (uses[j].Group == uses[k].Group)
                    {
                        above.Add(uses[j]);
                    }
                }

                ideal.Add(Ideal(uses[k], above, plain));
                idealUngrouped.Add(Ideal(uses[k], aboveAny, plain));
            }

            // The best one mask per file can do within this layout under the mask's rule, where moving pieces at
            // rest count like any other: the ungrouped ideal where every use agrees, plain where they differ.
            var maskInMap = new Dictionary<string, float[]?>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                float[]? merged = null;
                for (int k = 0; k < uses.Count; k++)
                {
                    if (!uses[k].RelativePath.Equals(f, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (merged == null)
                    {
                        merged = (float[])idealUngrouped[k].Clone();
                    }
                    else
                    {
                        for (int p = 0; p < merged.Length; p++)
                        {
                            if (Math.Abs(merged[p] - idealUngrouped[k][p]) >= 0.5f / 255)
                            {
                                merged[p] = plain;
                            }
                        }
                    }
                }

                maskInMap[f] = merged;
            }

            var imagesWritten = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
            var imagesInMap = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                var d = Get(f)!;
                imagesWritten[f] = Scaled(d, maskWritten.TryGetValue(f, out var mw) ? mw : null, plain);
                imagesInMap[f] = Scaled(d, maskInMap[f], plain);
            }

            var pxWritten = Render(
                uses.Select(p => (p, imagesWritten[p.RelativePath])).ToList(),
                bounds,
                scale,
                width,
                height
            );
            var pxInMap = Render(
                uses.Select(p => (p, imagesInMap[p.RelativePath])).ToList(),
                bounds,
                scale,
                width,
                height
            );
            var pxIdeal = Render(
                uses.Select((p, k) => (p, Scaled(p.Image, ideal[k], plain))).ToList(),
                bounds,
                scale,
                width,
                height
            );
            var pxUngrouped = Render(
                uses.Select((p, k) => (p, Scaled(p.Image, idealUngrouped[k], plain))).ToList(),
                bounds,
                scale,
                width,
                height
            );

            long total = 0,
                moving = 0,
                other = 0,
                twice = 0,
                shared = 0,
                idealSeams = 0;
            bool draw = diffPath != null && op == opacities[0];
            byte[]? diff = draw ? new byte[width * height * 4] : null;
            for (int i = 0; i < width * height * 4; i += 4)
            {
                bool written = Seam(pxWritten, reference, op, i),
                    inIdeal = Seam(pxIdeal, reference, op, i),
                    inMap = written && Seam(pxInMap, reference, op, i);
                if (inIdeal)
                {
                    idealSeams++;
                }

                if (!written)
                {
                    if (diff != null)
                    {
                        byte g = (byte)(reference[i + 3] / 5);
                        diff[i] = g;
                        diff[i + 1] = g;
                        diff[i + 2] = g;
                        diff[i + 3] = 255;
                    }

                    continue;
                }

                total++;
                if (inIdeal)
                {
                    // Seams the ideal keeps too: from moving platform groups when the ungrouped ideal is clean.
                    if (Seam(pxUngrouped, reference, op, i))
                    {
                        other++;
                    }
                    else
                    {
                        moving++;
                    }

                    Paint(diff, i, r: 255);
                }
                else if (inMap)
                {
                    twice++;
                    Paint(diff, i, r: 255, g: 255);
                }
                else
                {
                    shared++;
                    Paint(diff, i, g: 255, b: 255);
                }
            }

            result[op] = new SeamCounts(total, moving, other, twice, shared, idealSeams);
            if (diff != null)
            {
                SavePng(diff, width, height, diffPath!);
            }
        }

        if (_cache.Values.Sum(v => (long)v.Pixels.Length) > CacheLimit)
        {
            _cache.Clear();
        }

        return result;
    }

    /// <summary>The one place the audit asks <see cref="SeamMask"/> for the written masks, so a later change
    /// (such as passing a preferred layout) lands here only.</summary>
    private IReadOnlyDictionary<string, float[]> ComputeMask(IReadOnlyList<SeamPiece> pieces) =>
        SeamMask.Compute(_levels, pieces);

    /// <summary>Every piece of the layout that resolves, bottom first.</summary>
    private List<PieceUse> Place(LevelDesc level)
    {
        var uses = new List<PieceUse>();
        foreach (var placed in MapCompositor.Walk(level))
        {
            var image = Get(placed.RelativePath);
            if (image == null)
            {
                continue;
            }

            var to = MapCompositor.AssetTransform(placed.Asset, image.Width, image.Height);
            to.Append(placed.Transform);
            var from = to;
            if (!from.HasInverse)
            {
                continue;
            }

            from.Invert();
            var bounds = new Rect(0, 0, image.Width, image.Height);
            bounds.Transform(to);
            uses.Add(new PieceUse(image, placed.RelativePath, to, from, bounds, placed.Group));
        }

        return uses;
    }

    private PieceImage? Get(string relativePath)
    {
        if (_cache.TryGetValue(relativePath, out var cached))
        {
            return cached;
        }

        var path = _sources.ResolveAsset(relativePath);
        if (path == null)
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapDecoder
                .Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad)
                .Frames[0];
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            int w = converted.PixelWidth,
                h = converted.PixelHeight;
            var pixels = new byte[w * h * 4];
            converted.CopyPixels(pixels, w * 4, 0);
            var alpha = new byte[w * h];
            for (int i = 0; i < alpha.Length; i++)
            {
                alpha[i] = pixels[i * 4 + 3];
            }

            return _cache[relativePath] = new PieceImage(w, h, pixels, alpha);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The piece with each pixel's alpha multiplied by its mask factor, or by the plain opacity when
    /// there is no mask.</summary>
    private static BitmapSource Scaled(PieceImage image, float[]? mask, float plain)
    {
        var pixels = (byte[])image.Pixels.Clone();
        for (int i = 0; i < image.Width * image.Height; i++)
        {
            float s = mask == null ? plain : mask[i];
            pixels[i * 4 + 3] = (byte)
                Math.Round(image.Alpha[i] * Math.Clamp(s, 0, 1), MidpointRounding.AwayFromZero);
        }

        var bitmap = BitmapSource.Create(
            image.Width,
            image.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            image.Width * 4
        );
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource Full(PieceImage image)
    {
        var bitmap = BitmapSource.Create(
            image.Width,
            image.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            image.Pixels,
            image.Width * 4
        );
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>The ideal mask of one use: the factor that makes it composite under <paramref name="above"/>
    /// (top first) like one layer at the plain opacity.</summary>
    private static float[] Ideal(PieceUse own, List<PieceUse> above, float plain)
    {
        var mask = new float[own.Image.Width * own.Image.Height];
        Array.Fill(mask, plain);
        if (above.Count == 0)
        {
            return mask;
        }

        var cover = new List<(double, double)>();
        for (int y = 0; y < own.Image.Height; y++)
        {
            for (int x = 0; x < own.Image.Width; x++)
            {
                int ix = y * own.Image.Width + x;
                double s = own.Image.Alpha[ix] / 255.0;
                if (s == 0)
                {
                    continue;
                }

                var pt = own.To.Transform(new Point(x + .5, y + .5));
                cover.Clear();
                foreach (var o in above)
                {
                    if (o.Bounds.Contains(pt) && o.AlphaAt(pt) is > 0 and var sj)
                    {
                        cover.Add((sj, plain));
                    }
                }

                if (cover.Count > 0)
                {
                    mask[ix] = (float)Math.Clamp(SeamMask.Written(s, plain, cover) / s, 0, plain);
                }
            }
        }

        return mask;
    }

    /// <summary>Composites the uses bottom first into a Pbgra32 buffer of the layout's bounds.</summary>
    private static byte[] Render(
        List<(PieceUse Use, BitmapSource Image)> items,
        Rect bounds,
        double scale,
        int width,
        int height
    )
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            foreach (var (use, image) in items)
            {
                var m = use.To;
                m.Append(new Matrix(1, 0, 0, 1, -bounds.X, -bounds.Y));
                m.Append(new Matrix(scale, 0, 0, scale, 0, 0));
                dc.PushTransform(new MatrixTransform(m));
                dc.DrawImage(image, new Rect(0, 0, use.Image.Width, use.Image.Height));
                dc.Pop();
            }
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var pixels = new byte[width * height * 4];
        target.CopyPixels(pixels, width * 4, 0);
        return pixels;
    }

    /// <summary>True when the pixel at byte <paramref name="i"/> differs from the full-strength reference scaled
    /// by the opacity.</summary>
    private static bool Seam(byte[] rendered, byte[] reference, double op, int i)
    {
        for (int c = 0; c < 4; c++)
        {
            int expected = (int)Math.Round(reference[i + c] * op, MidpointRounding.AwayFromZero);
            if (Math.Abs(rendered[i + c] - expected) > Tolerance)
            {
                return true;
            }
        }

        return false;
    }

    private static void Paint(byte[]? diff, int i, byte r = 0, byte g = 0, byte b = 0)
    {
        if (diff == null)
        {
            return;
        }

        diff[i] = b;
        diff[i + 1] = g;
        diff[i + 2] = r;
        diff[i + 3] = 255;
    }

    private static void SavePng(byte[] pixels, int width, int height, string path)
    {
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>One use of a piece in the layout: the matrix from its pixels to level space and back, its
    /// level-space bounds and its group (moving platforms get their own).</summary>
    private sealed record PieceUse(PieceImage Image, string RelativePath, Matrix To, Matrix From, Rect Bounds, int Group)
    {
        /// <summary>The native alpha (0..1) of the pixel this level point falls on, 0 outside the piece.</summary>
        public double AlphaAt(Point q)
        {
            var l = From.Transform(q);
            int x = (int)Math.Floor(l.X),
                y = (int)Math.Floor(l.Y);
            return x < 0 || y < 0 || x >= Image.Width || y >= Image.Height
                ? 0
                : Image.Alpha[y * Image.Width + x] / 255.0;
        }
    }
}
