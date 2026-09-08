using BhMaps.Core.Model;
using BhMaps.Core.Scanning;

namespace BhMaps.Core.Operations;

/// <summary>Routes loose image files into the game-tree layout and copies them into a pack.</summary>
public static class ImportRouter
{
    /// <summary>Walks <paramref name="sourcePath"/> recursively and routes every image by spec 4.6. Rows are ordered by full source path.</summary>
    public static ImportPlan Plan(string sourcePath, GameTree gameTree)
    {
        var folderNames = new HashSet<string>(gameTree.Folders.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        var foldersByFileName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in gameTree.Folders)
        {
            foreach (var file in folder.Files)
            {
                if (!foldersByFileName.TryGetValue(file.Name, out var list))
                {
                    list = new List<string>();
                    foldersByFileName[file.Name] = list;
                }

                list.Add(folder.Name);
            }
        }

        var rows = new List<ImportRow>();
        if (Directory.Exists(sourcePath))
        {
            var paths = Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                var name = Path.GetFileName(path);
                if (!ImageFiles.IsImage(name))
                {
                    continue;
                }

                var parent = Path.GetFileName(Path.GetDirectoryName(path)!);
                var candidates = foldersByFileName.TryGetValue(name, out var found) ? found : new List<string>();
                if (folderNames.TryGetValue(parent, out var canonicalFolder))
                {
                    rows.Add(ImportRow.Routed(path, canonicalFolder));
                }
                else if (candidates.Count == 1)
                {
                    rows.Add(ImportRow.Routed(path, candidates[0]));
                }
                else if (candidates.Count > 1)
                {
                    rows.Add(ImportRow.Ambiguous(path, candidates.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList()));
                }
                else
                {
                    rows.Add(ImportRow.Unmatched(path));
                }
            }
        }

        return new ImportPlan(sourcePath, rows);
    }

    /// <summary>Copies every included row into &lt;library&gt;\packs\&lt;packName&gt;. Never moves or deletes sources. Overwrites same-named files.</summary>
    public static ApplyResult Execute(ImportPlan plan, string packName, string libraryPath, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!PackNameValidator.IsValid(packName, out var error))
        {
            throw new ArgumentException(error, nameof(packName));
        }

        var packRoot = Path.Combine(PackScanner.PacksRoot(libraryPath), packName);
        var copied = 0;
        var failures = new List<FileFailure>();
        foreach (var row in plan.IncludedRows)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(row.TargetRelativePath!);
            var target = Path.Combine(packRoot, row.TargetRelativePath!);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(row.SourcePath, target, overwrite: true);
                copied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(new FileFailure(target, ex.Message));
            }
        }

        Directory.CreateDirectory(packRoot);
        return new ApplyResult(copied, failures);
    }
}
