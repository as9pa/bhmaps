using System.Diagnostics;
using System.Globalization;
using BhMaps.Core.LevelData;

namespace BhMaps.Tools.SeamAudit;

/// <summary>Counts the seam pixels BhMaps' faded platforms leave in every layout of the installed game, per
/// opacity and per cause, and lists the worst layouts. Usage:
/// <c>dotnet run --project tools/SeamAudit -c Release -- [--game &lt;path&gt;] [--out &lt;dir&gt;]
/// [--opacity 0.25,0.5,0.75] [--only Level1,Level2]</c></summary>
internal static class Program
{
    private const string DefaultGame = @"C:\Program Files (x86)\Steam\steamapps\common\Brawlhalla";

    /// <summary>The worst layouts are ranked at this opacity when it is audited, else at the first one.</summary>
    private const double RankOpacity = 0.5;

    private const int WorstCount = 10;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static int Main(string[] args)
    {
        var exit = 0;
        // WPF rendering needs an STA thread; the large stack matches the survey this tool grew from.
        var thread = new Thread(() => exit = Run(args), 256 * 1024 * 1024);
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return exit;
    }

    private static int Run(string[] args)
    {
        var game = DefaultGame;
        string? outDir = null;
        double[] opacities = [0.25, 0.5, 0.75];
        string[]? only = null;
        for (int i = 0; i < args.Length; i++)
        {
            var value = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--game" when value != null:
                    game = value;
                    i++;
                    break;
                case "--out" when value != null:
                    outDir = Path.GetFullPath(value);
                    i++;
                    break;
                case "--opacity" when value != null:
                    if (!TryParseOpacities(value, out opacities))
                    {
                        Console.Error.WriteLine($"--opacity wants numbers between 0 and 1, got '{value}'.");
                        return 2;
                    }

                    i++;
                    break;
                case "--only" when value != null:
                    only = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    i++;
                    break;
                default:
                    Console.Error.WriteLine(
                        "Usage: SeamAudit [--game <path>] [--out <dir>] [--opacity 0.25,0.5,0.75] [--only A,B]"
                    );
                    return 2;
            }
        }

        var mapArt = Path.Combine(game, "mapArt");
        if (!Directory.Exists(mapArt))
        {
            Console.Error.WriteLine($"No mapArt folder under {game}. Pass the game folder with --game.");
            return 1;
        }

        var data = LevelDataReader.Read(game, null);
        if (data.Model is not { } model)
        {
            Console.Error.WriteLine($"Could not read the level data: {data.Error}");
            return 1;
        }

        var included = model
            .Types.Where(t => t.Included)
            .Select(t => t.LevelName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var levels = model.Levels.Where(l => included.Contains(l.LevelName)).ToList();
        var work = levels
            .Where(l => only == null || only.Contains(l.LevelName, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var rankOpacity = opacities.Contains(RankOpacity) ? RankOpacity : opacities[0];
        Console.WriteLine($"Seam audit of {work.Count} of {levels.Count} layouts in {game}");

        var auditor = new SeamAuditor(mapArt, levels);
        var totals = opacities.ToDictionary(o => o, _ => SeamCounts.Zero);
        var ranked = new List<(LevelDesc Level, SeamCounts Counts)>();
        var clock = Stopwatch.StartNew();
        for (int n = 0; n < work.Count; n++)
        {
            var level = work[n];
            Console.Error.WriteLine($"[{n + 1}/{work.Count}] {level.LevelName}");
            var result = auditor.Audit(level, opacities);
            if (result == null)
            {
                continue;
            }

            foreach (var (op, counts) in result)
            {
                totals[op] = totals[op].Add(counts);
            }

            ranked.Add((level, result[rankOpacity]));
        }

        Console.WriteLine($"Time {clock.Elapsed:hh\\:mm\\:ss}");
        Console.WriteLine();
        Console.WriteLine("Each layout is measured as the owner sees it: with the masks written while it is open in");
        Console.WriteLine("the editor, so its own uses of a shared file win (S3). A seam pixel gets one cause:");
        Console.WriteLine("  moving        the one-layer ideal fade seams there too, and not without the moving platforms (at rest)");
        Console.WriteLine("  other         the one-layer ideal fade seams there too, anywhere else (no mask can fix these)");
        Console.WriteLine("  twice-in-map  the ideal is clean, but uses of one file in this layout want different values");
        Console.WriteLine("  shared-file   clean within this layout, wrong only because other layouts share the file");
        Console.WriteLine("  other-maps    not in the total: file pixels this layout's masks leave visibly wrong in the");
        Console.WriteLine("                other layouts that share its files, once per use (the cost of editing it)");
        Console.WriteLine();
        foreach (var op in opacities)
        {
            var t = totals[op];
            Console.WriteLine($"Opacity {F(op)}: {N(t.Total)} seam pixels");
            Console.WriteLine($"  moving        {N(t.Moving),14}");
            Console.WriteLine($"  other         {N(t.Other),14}");
            Console.WriteLine($"  twice-in-map  {N(t.TwiceInMap),14}");
            Console.WriteLine($"  shared-file   {N(t.SharedFile),14}");
            Console.WriteLine($"  (ideal fade   {N(t.Ideal),14})");
            Console.WriteLine($"  other-maps    {N(t.OtherMaps),14}");
        }

        Console.WriteLine();
        Console.WriteLine($"Worst {WorstCount} layouts at {F(rankOpacity)}:");
        var names = model
            .Types.GroupBy(t => t.LevelName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().DisplayName, StringComparer.OrdinalIgnoreCase);
        foreach (var (level, c) in ranked.OrderByDescending(r => r.Counts.Total).Take(WorstCount))
        {
            var display = names.GetValueOrDefault(level.LevelName, "");
            Console.WriteLine(
                $"  {level.LevelName,-28} {display,-22} {N(c.Total),11}  moving {N(c.Moving)}  other {N(c.Other)}"
                    + $"  twice-in-map {N(c.TwiceInMap)}  shared-file {N(c.SharedFile)}  other-maps {N(c.OtherMaps)}"
            );
            if (outDir != null)
            {
                var path = Path.Combine(outDir, $"{level.LevelName}_op{(int)Math.Round(rankOpacity * 100)}.png");
                auditor.Audit(level, [rankOpacity], path);
            }
        }

        if (outDir != null)
        {
            Console.WriteLine();
            Console.WriteLine(
                $"Diff pictures in {outDir}: grey correct, yellow twice-in-map, cyan shared-file, red moving or other."
            );
        }

        return 0;
    }

    private static bool TryParseOpacities(string text, out double[] opacities)
    {
        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        opacities = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (
                !double.TryParse(parts[i], NumberStyles.Float, Inv, out opacities[i])
                || opacities[i] <= 0
                || opacities[i] > 1
            )
            {
                return false;
            }
        }

        return parts.Length > 0;
    }

    private static string N(long n) => n.ToString("N0", Inv);

    private static string F(double op) => op.ToString(Inv);
}
