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
    public static CameraBounds? For(LevelDesc level, double pad, double aspect) =>
        For(level, pad, aspect, null);

    /// <summary>Spec 3.2: with a focus set, the box is the union of the focused assets alone, so Selected only
    /// frames the ticked pieces. A set that matches no asset leaves the box empty and returns null, which is the
    /// same "render the whole level" answer a level with no platforms already gives.</summary>
    public static CameraBounds? For(LevelDesc level, double pad, double aspect, IReadOnlySet<string>? focus)
    {
        var camera = level.Camera;
        if (camera.W <= 0 || camera.H <= 0 || aspect <= 0)
        {
            return null;
        }

        var box = Union(level, focus);
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

    /// <summary>The plain union of the unthemed platform assets in camera space, with no padding and no aspect
    /// growth. Empty when the level places none. SpanFitter.Box hands this straight to the caller.</summary>
    internal static Rect Union(LevelDesc level, IReadOnlySet<string>? focus)
    {
        var box = Rect.Empty;
        foreach (var node in level.Platforms)
        {
            Walk(node, Matrix.Identity, (drawn, matrix) =>
            {
                foreach (var asset in drawn.Assets)
                {
                    // A node outside the set still carries its children's transform, so the walk goes on either way.
                    if (focus is not null && !focus.Contains(AssetPath.Resolve(level.AssetDir, asset.AssetName)))
                    {
                        continue;
                    }

                    // A missing W or H parses as 0 and means "the image's own size", which is not known without
                    // decoding the file, so the asset contributes its position alone rather than a guessed
                    // rectangle.
                    var rect = new Rect(asset.X, asset.Y, Math.Abs(asset.W), Math.Abs(asset.H));
                    box.Union(Rect.Transform(rect, matrix));
                }
            });
        }

        return box;
    }

    /// <summary>The same transform order the compositor draws with: scale, then rotate, then translate, each node
    /// inside its parent's. A seasonal node is skipped here because it is skipped there. SpanFitter.Placements
    /// walks with this too, so a cut lands exactly where the box put the piece.</summary>
    internal static void Walk(PlatformNode node, Matrix parent, Action<PlatformNode, Matrix> visit)
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

        visit(node, matrix);

        foreach (var child in node.Children)
        {
            Walk(child, matrix, visit);
        }
    }
}
