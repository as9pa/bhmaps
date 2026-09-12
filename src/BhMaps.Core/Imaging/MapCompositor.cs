using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BhMaps.Core.LevelData;

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

/// <summary>Draws a map the way the game composes it: the background stretched over the camera bounds,
/// then the platform tree walked depth-first. Safe to call from any thread; every bitmap it returns is frozen.</summary>
public static class MapCompositor
{
    public static readonly Color TileColour = (Color)ColorConverter.ConvertFromString("#2F2D2B");

    public const int CardWidth = 640;
    public const int CardHeight = 360;
    public const int PanelWidth = 1280;
    public const int PanelHeight = 720;

    /// <summary>Every file this render will read, in draw order, background first.
    /// Missing files are omitted.</summary>
    public static IReadOnlyList<string> CollectInputs(LevelDesc level, AssetSources sources)
    {
        var inputs = new List<string>();

        // The same gate Render draws behind: a level with missing or degenerate CameraBounds has nowhere to draw,
        // so it reads nothing, and listing its assets anyway would churn the preview cache key for no bitmap.
        var camera = level.Camera;
        if (camera.W <= 0 || camera.H <= 0)
        {
            return inputs;
        }

        if (level.Backgrounds.Count > 0)
        {
            var background = sources.ResolveBackground(level.Backgrounds[0].AssetName);
            if (background is not null)
            {
                inputs.Add(background);
            }
        }

        foreach (var node in level.Platforms)
        {
            CollectNode(node, level.AssetDir, sources, inputs);
        }

        return inputs;
    }

    /// <summary>Frozen Bgr24 bitmap. Safe on any thread. Never throws for a missing or bad asset; that asset is
    /// skipped. <paramref name="viewport" /> is the part of the level to draw, in the level's own coordinates,
    /// and null means the whole camera, which is what every caller but the Platforms page wants (addendum C).</summary>
    public static BitmapSource Render(
        LevelDesc level, int width, int height, AssetSources sources, CameraBounds? viewport = null)
    {
        var camera = viewport ?? level.Camera;
        var tile = new SolidColorBrush(TileColour);
        tile.Freeze();

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(tile, null, new Rect(0, 0, width, height));

            // A level whose CameraBounds are missing or degenerate has nowhere to draw; the tile fill is the whole preview.
            if (camera.W > 0 && camera.H > 0)
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

                DrawBackground(dc, level, sources, decoded);
                foreach (var node in level.Platforms)
                {
                    DrawNode(dc, node, level.AssetDir, sources, decoded);
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

    private static void DrawNode(DrawingContext dc, PlatformNode node, string assetDir, AssetSources sources, Dictionary<string, BitmapSource?> decoded)
    {
        if (node.IsThemed)
        {
            // Seasonal art never appears in game outside its event, so it never appears in a preview.
            return;
        }

        dc.PushTransform(new TransformGroup
        {
            Children =
            {
                new ScaleTransform(node.EffectiveScaleX, node.EffectiveScaleY),
                new RotateTransform(node.Rotation),
                new TranslateTransform(node.X, node.Y),
            },
        });

        foreach (var asset in node.Assets)
        {
            DrawAsset(dc, asset, assetDir, sources, decoded);
        }

        foreach (var child in node.Children)
        {
            DrawNode(dc, child, assetDir, sources, decoded);
        }

        dc.Pop();
    }

    private static void DrawAsset(DrawingContext dc, LevelAsset asset, string assetDir, AssetSources sources, Dictionary<string, BitmapSource?> decoded)
    {
        var path = sources.ResolveAsset(AssetPath.Resolve(assetDir, asset.AssetName));
        var image = path is null ? null : Decode(path, decoded);
        if (image is null)
        {
            return;
        }

        // A missing W or H parses as 0 and means "the image's own size".
        var w = asset.W == 0 ? image.PixelWidth : Math.Abs(asset.W);
        var h = asset.H == 0 ? image.PixelHeight : Math.Abs(asset.H);
        var mirrored = asset.W < 0 || asset.H < 0;
        if (mirrored)
        {
            dc.PushTransform(new ScaleTransform(
                asset.W < 0 ? -1 : 1, asset.H < 0 ? -1 : 1, asset.X + (w / 2), asset.Y + (h / 2)));
        }

        dc.DrawImage(image, new Rect(asset.X, asset.Y, w, h));
        if (mirrored)
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
