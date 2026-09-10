using System.Globalization;
using BhMaps.Core.Model;

namespace BhMaps.Core.Operations;

/// <summary>One undo set on disk: the game files as they were, under &lt;undo&gt;\&lt;stamp&gt;\&lt;Folder&gt;\, plus _absent.txt for the paths that held no file.</summary>
public sealed class UndoSession
{
    internal const string AbsentFileName = "_absent.txt";

    private readonly HashSet<string> _captured = new(StringComparer.OrdinalIgnoreCase);

    internal UndoSession(string path)
    {
        Path = path;
        foreach (var relativePath in CapturedFiles().Concat(AbsentPaths()))
        {
            _captured.Add(relativePath);
        }
    }

    public string Path { get; }

    /// <summary>Relative paths captured so far: files copied in plus paths recorded as absent.</summary>
    public int Count => _captured.Count;

    /// <summary>Copies the game file into the session. A path with no file is recorded in _absent.txt.</summary>
    public void Capture(string gamePath, string relativePath)
    {
        if (!_captured.Add(relativePath))
        {
            // The first capture is the one taken before the operation started; a later one would snapshot its own writes.
            return;
        }

        var source = System.IO.Path.Combine(gamePath, relativePath);
        var target = System.IO.Path.Combine(Path, relativePath);
        try
        {
            if (File.Exists(source))
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: true);
            }
            else
            {
                File.AppendAllText(System.IO.Path.Combine(Path, AbsentFileName), relativePath + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file we cannot snapshot is left out of the set entirely, so undo skips it instead of deleting it.
            _captured.Remove(relativePath);
        }
    }

    public void Capture(string gamePath, IEnumerable<string> relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            Capture(gamePath, relativePath);
        }
    }

    /// <summary>Relative paths whose original bytes are stored in this session, sorted by name.</summary>
    internal IReadOnlyList<string> CapturedFiles()
    {
        if (!Directory.Exists(Path))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories)
            .Select(f => System.IO.Path.GetRelativePath(Path, f))
            .Where(r => !r.Equals(AbsentFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Relative paths that held no file when the session began, one per line of _absent.txt.</summary>
    internal IReadOnlyList<string> AbsentPaths()
    {
        var absent = System.IO.Path.Combine(Path, AbsentFileName);
        if (!File.Exists(absent))
        {
            return Array.Empty<string>();
        }

        return File.ReadAllLines(absent).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
    }
}

/// <summary>Keeps one undo set under &lt;appDataDir&gt;\undo: the files the last operation was about to overwrite or delete.</summary>
public sealed class UndoStore
{
    private const string StampFormat = "yyyyMMdd-HHmmss";

    public UndoStore(string appDataDir)
    {
        Root = Path.Combine(appDataDir, "undo");
    }

    public string Root { get; }

    public UndoSession? Latest
    {
        get
        {
            if (!Directory.Exists(Root))
            {
                return null;
            }

            var newest = Directory.GetDirectories(Root)
                .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
                .FirstOrDefault();
            return newest is null ? null : new UndoSession(newest);
        }
    }

    /// <summary>Deletes every earlier session, then creates &lt;undo&gt;\&lt;yyyyMMdd-HHmmss&gt;, appending -2, -3 on a same-second collision.</summary>
    public UndoSession Begin(DateTimeOffset? now = null)
    {
        Directory.CreateDirectory(Root);
        // The free name is chosen before the delete so a session that will not delete cannot lend its name to the new one.
        var path = FreePath((now ?? DateTimeOffset.Now).ToString(StampFormat, CultureInfo.InvariantCulture));
        Clear();
        Directory.CreateDirectory(path);
        return new UndoSession(path);
    }

    /// <summary>Copies captured files back and deletes the ones recorded as absent. Copied counts both. Never throws per file.</summary>
    public ApplyResult Restore(UndoSession session, string gamePath)
    {
        IReadOnlyList<string> captured;
        IReadOnlyList<string> absent;
        try
        {
            captured = session.CapturedFiles();
            absent = session.AbsentPaths();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable session is one failure for the session, not an aborted batch.
            return new ApplyResult(0, [new FileFailure(session.Path, ex.Message)]);
        }

        var restored = 0;
        var failures = new List<FileFailure>();
        foreach (var relativePath in captured)
        {
            CopyBack(Path.Combine(session.Path, relativePath), Path.Combine(gamePath, relativePath), ref restored, failures);
        }

        foreach (var relativePath in absent)
        {
            DeleteBack(Path.Combine(gamePath, relativePath), ref restored, failures);
        }

        return new ApplyResult(restored, failures);
    }

    public void Clear()
    {
        if (!Directory.Exists(Root))
        {
            return;
        }

        foreach (var directory in Directory.GetDirectories(Root))
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A session we cannot delete is stale, not fatal; the next Begin takes a different name.
            }
        }
    }

    private string FreePath(string stamp)
    {
        var path = Path.Combine(Root, stamp);
        for (var n = 2; Directory.Exists(path); n++)
        {
            path = Path.Combine(Root, $"{stamp}-{n}");
        }

        return path;
    }

    private static void CopyBack(string source, string target, ref int restored, List<FileFailure> failures)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
            restored++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add(new FileFailure(target, ex.Message));
        }
    }

    private static void DeleteBack(string target, ref int restored, List<FileFailure> failures)
    {
        if (!File.Exists(target))
        {
            return;
        }

        try
        {
            File.Delete(target);
            restored++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add(new FileFailure(target, ex.Message));
        }
    }
}
