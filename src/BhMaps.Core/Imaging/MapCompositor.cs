using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BhMaps.Core.LevelData;
using BhMaps.Core.Settings;

namespace BhMaps.Core.Imaging;

/// <summary>Decides where each asset comes from: the game folder, a pack folder,
/// or a specific background image. Safe to share across threads and across concurrent renders.</summary>
public sealed class AssetSources
{
    private readonly ConcurrentDictionary<string, bool> _transparency = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>mapArtPath is the game's mapArt folder, which every relative path from a LevelDesc hangs off.</summary>
    public AssetSources(string mapArtPath, string? packRoot = null, string? backgroundOverride = null)
    {
        GameRoot = mapArtPath;
        PackRoot = packRoot;
        BackgroundOverride = backgroundOverride;
    }

    public string GameRoot { get; }

    public string? PackRoot { get; }

    public string? BackgroundOverride { get; }

    /// <summary>The pack file first unless it is fully transparent, then the game file.
    /// Null when neither exists.</summary>
    public string? ResolveAsset(string relativePath)
    {
        if (!string.IsNullOrEmpty(PackRoot))
        {
            var packPath = Path.Combine(PackRoot, relativePath);
            if (File.Exists(packPath) && !IsFullyTransparent(packPath))
            {
                return packPath;
            }
        }

        var gamePath = Path.Combine(GameRoot, relativePath);
        return File.Exists(gamePath) ? gamePath : null;
    }

    /// <summary>The override first, then the pack's copy, then the game's. Null when none of them exists.</summary>
    public string? ResolveBackground(string assetName)
    {
        if (!string.IsNullOrEmpty(BackgroundOverride) && File.Exists(BackgroundOverride))
        {
            return BackgroundOverride;
        }

        var relativePath = AssetPath.Background(assetName);
        if (!string.IsNullOrEmpty(PackRoot))
        {
            var packPath = Path.Combine(PackRoot, relativePath);
            if (File.Exists(packPath))
            {
                return packPath;
            }
        }

        var gamePath = Path.Combine(GameRoot, relativePath);
        return File.Exists(gamePath) ? gamePath : null;
    }

    /// <summary>Memoised because one render asks about the same pack file once per slot that names it.
    /// Concurrent because one instance serves both the caller thread collecting inputs and the render thread
    /// drawing them, and two previews may share it. A racing pair may both decode the file; the check is pure,
    /// so the only cost is one wasted decode.</summary>
    private bool IsFullyTransparent(string path) =>
        _transparency.GetOrAdd(path, static p => TransparentPng.IsFullyTransparent(p));
}

/// <summary>One drawn image of a level, in draw order. <see cref="Transform"/> takes the asset's node space to
/// level space. <see cref="Group"/> is 0 for everything that stands still and one number per moving platform,
/// because pieces of different groups do not keep their places relative to each other in game.</summary>
public sealed record PlacedAsset(LevelAsset Asset, string RelativePath, Matrix Transform, int Group);

/// <summary>Draws a map the way the game composes it: the background stretched over the camera bounds,
/// then the platform tree walked depth-first. Safe to call from any thread; every bitmap it returns is frozen.</summary>
public static class MapCompositor
{
    public static readonly Color TileColour = (Color)ColorConverter.ConvertFromString("#2F2D2B");

    public const int CardWidth = 640;
    public const int CardHeight = 360;
    public const int PanelWidth = 1280;
    public const int PanelHeight = 720;

    /// <summary>What a piece outside the focus set is drawn at (spec 3.2): visible enough to place the shape,
    /// faint enough that the focused pieces read as the subject.</summary>
    public const double GhostOpacity = 0.15;

    /// <summary>3.2 P2: the checkerboard Platforms mode draws where the background would be, the usual sign for
    /// nothing there. Two quiet greys from the tile colour's family, in squares of <see cref="CheckerSquare" />
    /// pixels at card width (about 12 px on a medium card), big enough that nobody reads them as the fine grid
    /// missing art draws.</summary>
    public static readonly Color CheckerDark = TileColour;

    public static readonly Color CheckerLight = (Color)ColorConverter.ConvertFromString("#3B3936");

    public const double CheckerSquare = 24;

    /// <summary>Every file this render will read, in draw order, background first.
    /// Missing files are omitted.</summary>
    public static IReadOnlyList<string> CollectInputs(
        LevelDesc level, AssetSources sources, PreviewMode mode = PreviewMode.Both)
    {
        var inputs = new List<string>();

        // The same gate Render draws behind: a level with missing or degenerate CameraBounds has nowhere to draw,
        // so it reads nothing, and listing its assets anyway would churn the preview cache key for no bitmap.
        // 3.2: Backgrounds draws the picture alone, cropped to fill, so it needs no camera; Platforms reads no
        // background at all.
        var camera = level.Camera;
        if (mode != PreviewMode.Backgrounds && (camera.W <= 0 || camera.H <= 0))
        {
            return inputs;
        }

        if (mode != PreviewMode.Platforms && level.Backgrounds.Count > 0)
        {
            var background = sources.ResolveBackground(level.Backgrounds[0].AssetName);
            if (background is not null)
            {
                inputs.Add(background);
            }
        }

        if (mode == PreviewMode.Backgrounds)
        {
            return inputs;
        }

        foreach (var node in level.Platforms)
        {
            CollectNode(node, level.AssetDir, sources, inputs);
        }

        return inputs;
    }

    /// <summary>Frozen Bgr24 bitmap. Safe on any thread. Never throws for a missing or bad asset; that asset is
    /// skipped. <paramref name="viewport" /> is the part of the level to draw, in the level's own coordinates,
    /// and null means the whole camera, which is what every caller but the Platforms page wants (addendum C).
    /// <paramref name="focus" /> is the set of asset paths relative to the map art root that draw at full
    /// strength; null means every asset does, which is what every 2.3 caller wants. An asset outside a non-null
    /// set draws at <paramref name="ghostOpacity" />. Pass a set built with
    /// <see cref="StringComparer.OrdinalIgnoreCase" />: asset paths come from a file system that ignores case.
    /// <paramref name="mode" /> is the 3.2 switch: Platforms draws no background, only the pieces on a checkerboard,
    /// and Backgrounds draws the background alone, cropped to fill the whole bitmap.</summary>
    public static BitmapSource Render(
        LevelDesc level,
        int width,
        int height,
        AssetSources sources,
        CameraBounds? viewport = null,
        IReadOnlySet<string>? focus = null,
        double ghostOpacity = GhostOpacity,
        PreviewMode mode = PreviewMode.Both)
    {
        var camera = viewport ?? level.Camera;
        var tile = new SolidColorBrush(TileColour);
        tile.Freeze();

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(tile, null, new Rect(0, 0, width, height));
            if (mode == PreviewMode.Platforms)
            {
                DrawChecker(dc, width, height);
            }

            // A level whose CameraBounds are missing or degenerate has nowhere to draw; the tile fill is the whole
            // preview. Backgrounds needs no camera: the picture is fitted to the bitmap, not to the level.
            if (mode == PreviewMode.Backgrounds)
            {
                DrawBackgroundFill(dc, level, sources, width, height);
            }
            else if (camera.W > 0 && camera.H > 0)
            {
                var scale = Math.Min(width / camera.W, height / camera.H);
                var offsetX = (width - (camera.W * scale)) / 2;
                var offsetY = (height - (camera.H * scale)) / 2;
                var decoded = new Dictionary<string, BitmapSource?>(StringComparer.OrdinalIgnoreCase);

                dc.PushClip(new RectangleGeometry(new Rect(offsetX, offsetY, camera.W * scale, camera.H * scale)));
                dc.PushTransform(new TransformGroup
                {
                    Children =
                    {
                        new TranslateTransform(-camera.X, -camera.Y),
                        new ScaleTransform(scale, scale),
                        new TranslateTransform(offsetX, offsetY),
                    },
                });

                if (mode == PreviewMode.Both)
                {
                    DrawBackground(dc, level, sources, decoded);
                }

                foreach (var placed in Walk(level))
                {
                    dc.PushTransform(new MatrixTransform(placed.Transform));
                    DrawAsset(dc, placed.Asset, placed.RelativePath, sources, decoded, focus, ghostOpacity);
                    dc.Pop();
                }

                dc.Pop();
                dc.Pop();
            }
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var rgb = new FormatConvertedBitmap(target, PixelFormats.Bgr24, null, 0);
        rgb.Freeze();
        return rgb;
    }

    private static void CollectNode(PlatformNode node, string assetDir, AssetSources sources, List<string> inputs)
    {
        if (node.IsThemed)
        {
            return;
        }

        foreach (var asset in node.Assets)
        {
            var path = sources.ResolveAsset(AssetPath.Resolve(assetDir, asset.AssetName));
            if (path is not null)
            {
                inputs.Add(path);
            }
        }

        foreach (var child in node.Children)
        {
            CollectNode(child, assetDir, sources, inputs);
        }
    }

    /// <summary>3.2 P2: the checkerboard under the pieces in Platforms mode. The tile fill is the dark square, so
    /// only the light ones are drawn. Scaled with the bitmap, so a card and the panel show the same board.</summary>
    private static void DrawChecker(DrawingContext dc, int width, int height)
    {
        var light = new SolidColorBrush(CheckerLight);
        light.Freeze();
        var square = Math.Max(2, CheckerSquare * width / CardWidth);
        for (var row = 0; row * square < height; row++)
        {
            for (var col = 1 - (row % 2); col * square < width; col += 2)
            {
                dc.DrawRectangle(light, null, new Rect(col * square, row * square, square, square));
            }
        }
    }

    /// <summary>3.2 P1 and P2: the first background alone, scaled to cover the whole bitmap and centred, so what
    /// does not fit is cropped rather than letterboxed. Nothing for a level with no background, which leaves the
    /// tile fill.</summary>
    private static void DrawBackgroundFill(DrawingContext dc, LevelDesc level, AssetSources sources, int width, int height)
    {
        if (level.Backgrounds.Count == 0)
        {
            return;
        }

        var path = sources.ResolveBackground(level.Backgrounds[0].AssetName);
        var decoded = new Dictionary<string, BitmapSource?>(StringComparer.OrdinalIgnoreCase);
        var image = path is null ? null : Decode(path, decoded);
        if (image is null || image.PixelWidth <= 0 || image.PixelHeight <= 0)
        {
            return;
        }

        var scale = Math.Max((double)width / image.PixelWidth, (double)height / image.PixelHeight);
        var w = image.PixelWidth * scale;
        var h = image.PixelHeight * scale;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
        dc.DrawImage(image, new Rect((width - w) / 2, (height - h) / 2, w, h));
        dc.Pop();
    }

    /// <summary>Only the first Background element is drawn; the rest are parallax layers the preview leaves out.</summary>
    private static void DrawBackground(DrawingContext dc, LevelDesc level, AssetSources sources, Dictionary<string, BitmapSource?> decoded)
    {
        if (level.Backgrounds.Count == 0)
        {
            return;
        }

        var path = sources.ResolveBackground(level.Backgrounds[0].AssetName);
        var image = path is null ? null : Decode(path, decoded);
        if (image is not null)
        {
            var camera = level.Camera;
            dc.DrawImage(image, new Rect(camera.X, camera.Y, camera.W, camera.H));
        }
    }

    /// <summary>The platform tree walked depth-first the way it is drawn: a node's own assets, then its children,
    /// each node applying Scale, Rotate, Translate inside its parent. A seasonal node is left out with everything
    /// under it, because seasonal art never appears in game outside its event. The seam mask walks the same tree
    /// (3.2 O1).</summary>
    public static IReadOnlyList<PlacedAsset> Walk(LevelDesc level)
    {
        var placed = new List<PlacedAsset>();
        var groups = 0;
        foreach (var node in level.Platforms)
        {
            WalkNode(node, level.AssetDir, Matrix.Identity, 0, ref groups, placed);
        }

        return placed;
    }

    /// <summary>Takes a piece's own pixels (0..pixelWidth by 0..pixelHeight) to its node's space: stretched over
    /// the asset's rectangle, where a missing W or H means the image's own size and a negative one is a flip about
    /// the rectangle's centre.</summary>
    public static Matrix AssetTransform(LevelAsset asset, int pixelWidth, int pixelHeight)
    {
        var w = asset.W == 0 ? pixelWidth : Math.Abs(asset.W);
        var h = asset.H == 0 ? pixelHeight : Math.Abs(asset.H);
        var matrix = Matrix.Identity;
        matrix.Scale(pixelWidth > 0 ? w / pixelWidth : 1, pixelHeight > 0 ? h / pixelHeight : 1);
        matrix.Translate(asset.X, asset.Y);
        if (asset.W < 0 || asset.H < 0)
        {
            matrix.ScaleAt(asset.W < 0 ? -1 : 1, asset.H < 0 ? -1 : 1, asset.X + (w / 2), asset.Y + (h / 2));
        }

        return matrix;
    }

    private static void WalkNode(
        PlatformNode node, string assetDir, Matrix parent, int group, ref int groups, List<PlacedAsset> placed)
    {
        if (node.IsThemed)
        {
            return;
        }

        var transform = Matrix.Identity;
        transform.Scale(node.EffectiveScaleX, node.EffectiveScaleY);
        transform.Rotate(node.Rotation);
        transform.Translate(node.X, node.Y);
        transform.Append(parent);
        if (node.Moving)
        {
            group = ++groups;
        }

        foreach (var asset in node.Assets)
        {
            placed.Add(new PlacedAsset(asset, AssetPath.Resolve(assetDir, asset.AssetName), transform, group));
        }

        foreach (var child in node.Children)
        {
            WalkNode(child, assetDir, transform, group, ref groups, placed);
        }
    }

    private static void DrawAsset(
        DrawingContext dc,
        LevelAsset asset,
        string relativePath,
        AssetSources sources,
        Dictionary<string, BitmapSource?> decoded,
        IReadOnlySet<string>? focus,
        double ghostOpacity)
    {
        var path = sources.ResolveAsset(relativePath);
        var image = path is null ? null : Decode(path, decoded);
        if (image is null)
        {
            return;
        }

        // Spec 3.2: a piece outside the focus set is a ghost. A null set is every 2.3 caller and ghosts nothing.
        var ghost = focus is not null && !focus.Contains(relativePath);
        if (ghost)
        {
            dc.PushOpacity(ghostOpacity);
        }

        dc.PushTransform(new MatrixTransform(AssetTransform(asset, image.PixelWidth, image.PixelHeight)));
        dc.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
        dc.Pop();

        if (ghost)
        {
            dc.Pop();
        }
    }

    /// <summary>Decoded once per render. Null for a file that will not decode, which is drawn as nothing.</summary>
    private static BitmapSource? Decode(string path, Dictionary<string, BitmapSource?> decoded)
    {
        if (decoded.TryGetValue(path, out var known))
        {
            return known;
        }

        BitmapSource? image = null;
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            if (frame.CanFreeze)
            {
                frame.Freeze();
            }

            image = frame;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException or OverflowException)
        {
            // One unreadable asset must not cost the whole preview, so the slot is left empty.
        }

        decoded[path] = image;
        return image;
    }
}
