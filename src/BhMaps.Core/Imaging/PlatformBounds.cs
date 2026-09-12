using System.Windows;
using System.Windows.Media;
using BhMaps.Core.LevelData;

namespace BhMaps.Core.Imaging;

/// <summary>The part of a level a platform thumbnail shows (addendum C). A whole-level composite at row height
/// draws the platforms as slivers, so a rows page renders the platforms' own bounding box instead: padded,
/// widened to the thumbnail's aspect, and clamped to the level, so every set of one map is cropped the same way
/// and the row compares like with like. The map panel and the pack drawer keep the whole level.</summary>
public static class PlatformBounds
{
    /// <summary>How much of the box's own width and height is added on each side, so the platforms are not
    /// pressed against the edge of the thumbnail.</summary>
    public const double Pad = 0.12;

    /// <summary>Null for a level with no camera, or one whose only platforms are seasonal, which the compositor
    /// does not draw either. Then the caller renders the whole level as before.</summary>
    public static CameraBounds? For(LevelDesc level, double pad, double aspect)
    {
        var camera = level.Camera;
        if (camera.W <= 0 || camera.H <= 0 || aspect <= 0)
        {
            return null;
        }

        var box = Rect.Empty;
        foreach (var node in level.Platforms)
        {
            Union(node, Matrix.Identity, ref box);
        }

        if (box.IsEmpty)
        {
            return null;
        }

        // A level whose assets all declare no size collapses to a point, which would scale to nothing.
        var w = box.Width > 0 ? box.Width : camera.W * 0.1;
        var h = box.Height > 0 ? box.Height : camera.H * 0.1;
        var cx = box.X + (box.Width / 2);
        var cy = box.Y + (box.Height / 2);

        w += w * pad * 2;
        h += h * pad * 2;

        // Grow the short axis to the thumbnail's shape, so the render fills it with no letterbox.
        if (w / h < aspect)
        {
            w = h * aspect;
        }
        else
        {
            h = w / aspect;
        }

        // Nothing outside the level, and the shape kept after the clamp.
        w = Math.Min(w, camera.W);
        h = Math.Min(h, camera.H);
        if (w / h > aspect)
        {
            w = h * aspect;
        }
        else
        {
            h = w / aspect;
        }

        var x = Math.Clamp(cx - (w / 2), camera.X, camera.X + camera.W - w);
        var y = Math.Clamp(cy - (h / 2), camera.Y, camera.Y + camera.H - h);
        return new CameraBounds(x, y, w, h);
    }

    /// <summary>The same transform order the compositor draws with: scale, then rotate, then translate, each node
    /// inside its parent's. A seasonal node is skipped here because it is skipped there.</summary>
    private static void Union(PlatformNode node, Matrix parent, ref Rect box)
    {
        if (node.IsThemed)
        {
            return;
        }

        var local = Matrix.Identity;
        local.Scale(node.EffectiveScaleX, node.EffectiveScaleY);
        local.Rotate(node.Rotation);
        local.Translate(node.X, node.Y);
        var matrix = Matrix.Multiply(local, parent);

        foreach (var asset in node.Assets)
        {
            // A missing W or H parses as 0 and means "the image's own size", which is not known without decoding
            // the file, so the asset contributes its position alone rather than a guessed rectangle.
            var rect = new Rect(asset.X, asset.Y, Math.Abs(asset.W), Math.Abs(asset.H));
            var transformed = Rect.Transform(rect, matrix);
            box.Union(transformed);
        }

        foreach (var child in node.Children)
        {
            Union(child, matrix, ref box);
        }
    }
}
