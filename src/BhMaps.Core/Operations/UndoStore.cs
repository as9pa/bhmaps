using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using BhMaps.Core.Model;

namespace BhMaps.Core.Operations;

/// <summary>One undo set on disk: the game files as they were, under &lt;undo&gt;\&lt;stamp&gt;\&lt;Folder&gt;\, plus _absent.txt for the
/// paths that held no file. Library files live beside them under &lt;undo&gt;\&lt;stamp&gt;\library\, with their own _library_absent.txt.
/// Map-select thumbnails live under &lt;undo&gt;\&lt;stamp&gt;\thumbnails\, with their own _thumbnails_absent.txt.
/// The applied record is copied to &lt;undo&gt;\&lt;stamp&gt;\applied.json, or noted in _record_absent.txt when there was none.</summary>
public sealed class UndoSession
{
    internal const string AbsentFileName = "_absent.txt";

    internal const string RecordFileName = "applied.json";

    internal const string RecordAbsentFileName = "_record_absent.txt";

    internal const string LibraryFolderName = "library";

    internal const string LibraryAbsentFileName = "_library_absent.txt";

    internal const string ThumbnailsFolderName = "thumbnails";

    internal const string ThumbnailsAbsentFileName = "_thumbnails_absent.txt";

    private readonly HashSet<string> _captured = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _capturedLibrary = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _capturedThumbnails = new(StringComparer.OrdinalIgnoreCase);

    private bool _capturedRecord;

    internal UndoSession(string path)
    {
        Path = path;
        _capturedRecord = HasRecordSide;
        foreach (var relativePath in CapturedFiles().Concat(AbsentPaths()))
        {
            _captured.Add(relativePath);
        }

        foreach (var relativePath in CapturedLibraryFiles().Concat(LibraryAbsentPaths()))
        {
            _capturedLibrary.Add(relativePath);
        }

        foreach (var fileName in CapturedThumbnailFiles().Concat(ThumbnailsAbsentPaths()))
        {
            _capturedThumbnails.Add(fileName);
        }
    }

    public string Path { get; }

    /// <summary>Relative paths captured so far, every side: files copied in plus paths recorded as absent.</summary>
    public int Count => _captured.Count + _capturedLibrary.Count + _capturedThumbnails.Count;

    /// <summary>Copies the game file into the session. A path with no file is recorded in _absent.txt.</summary>
    public void Capture(string gamePath, string relativePath) =>
        CaptureInto(_captured, gamePath, Path, AbsentFileName, relativePath);

    public void Capture(string gamePath, IEnumerable<string> relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            Capture(gamePath, relativePath);
        }
    }

    /// <summary>Copies library files (relative to libraryPath) under the session's "library" side; absent ones are
    /// listed in _library_absent.txt so a restore deletes them. First capture of a path wins, as on the game side.</summary>
    public void CaptureLibrary(string libraryPath, IEnumerable<string> relativePaths)
    {
        var librarySide = System.IO.Path.Combine(Path, LibraryFolderName);
        foreach (var relativePath in relativePaths)
        {
            CaptureInto(_capturedLibrary, libraryPath, librarySide, LibraryAbsentFileName, relativePath);
        }
    }

    /// <summary>Copies map-select thumbnails (file names under thumbnailsDir) into the session's "thumbnails" side;
    /// absent ones are listed in _thumbnails_absent.txt so a restore deletes them. First capture of a name wins, as
    /// on the other sides.</summary>
    public void CaptureThumbnails(string thumbnailsDir, IEnumerable<string> fileNames)
    {
        var thumbnailsSide = System.IO.Path.Combine(Path, ThumbnailsFolderName);
        foreach (var fileName in fileNames)
        {
            CaptureInto(_capturedThumbnails, thumbnailsDir, thumbnailsSide, ThumbnailsAbsentFileName, fileName);
        }
    }

    /// <summary>Copies the applied record into the session, or notes in _record_absent.txt that there was none, so
    /// an undo puts the app's memory of what it wrote back with the files it wrote. First capture wins, as on the
    /// other sides. The record is bookkeeping rather than a file the user picked, so it is left out of Count.</summary>
    public void CaptureRecord(string recordPath)
    {
        if (_capturedRecord)
        {
            // The first capture is the one taken before the operation started; a later one would snapshot its own note.
            return;
        }

        _capturedRecord = true;
        try
        {
            if (File.Exists(recordPath))
            {
                File.Copy(recordPath, RecordSidePath, overwrite: true);
            }
            else
            {
                File.WriteAllText(RecordAbsentPath, string.Empty);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A record we cannot snapshot is left out entirely, so undo skips it instead of deleting it.
            _capturedRecord = false;
        }
    }

    /// <summary>One side's capture: copy the file in under targetRoot, or write the path to the side's absent list.</summary>
    private void CaptureInto(HashSet<string> captured, string sourceRoot, string targetRoot, string absentFileName, string relativePath)
    {
        if (!captured.Add(relativePath))
        {
            // The first capture is the one taken before the operation started; a later one would snapshot its own writes.
            return;
        }

        var source = System.IO.Path.Combine(sourceRoot, relativePath);
        var target = System.IO.Path.Combine(targetRoot, relativePath);
        try
        {
            if (File.Exists(source))
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: true);
            }
            else
            {
                File.AppendAllText(System.IO.Path.Combine(Path, absentFileName), relativePath + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file we cannot snapshot is left out of the set entirely, so undo skips it instead of deleting it.
            captured.Remove(relativePath);
        }
    }

    /// <summary>Relative paths whose original game bytes are stored in this session, sorted by name. The library
    /// side, the thumbnails side and the absent lists are session bookkeeping, not game files, so they are left out.</summary>
    internal IReadOnlyList<string> CapturedFiles()
    {
        var librarySide = LibraryFolderName + System.IO.Path.DirectorySeparatorChar;
        var thumbnailsSide = ThumbnailsFolderName + System.IO.Path.DirectorySeparatorChar;
        return CapturedUnder(Path)
            .Where(r => !r.Equals(AbsentFileName, StringComparison.OrdinalIgnoreCase)
                && !r.Equals(LibraryAbsentFileName, StringComparison.OrdinalIgnoreCase)
                && !r.Equals(ThumbnailsAbsentFileName, StringComparison.OrdinalIgnoreCase)
                && !r.Equals(RecordFileName, StringComparison.OrdinalIgnoreCase)
                && !r.Equals(RecordAbsentFileName, StringComparison.OrdinalIgnoreCase)
                && !r.StartsWith(librarySide, StringComparison.OrdinalIgnoreCase)
                && !r.StartsWith(thumbnailsSide, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Relative paths whose original library bytes are stored under the session's library side, sorted by name.</summary>
    internal IReadOnlyList<string> CapturedLibraryFiles() =>
        CapturedUnder(System.IO.Path.Combine(Path, LibraryFolderName));

    /// <summary>File names whose original thumbnail bytes are stored under the session's thumbnails side, sorted by name.</summary>
    internal IReadOnlyList<string> CapturedThumbnailFiles() =>
        CapturedUnder(System.IO.Path.Combine(Path, ThumbnailsFolderName));

    /// <summary>Relative paths that held no game file when the session began, one per line of _absent.txt.</summary>
    internal IReadOnlyList<string> AbsentPaths() => AbsentPathsIn(AbsentFileName);

    /// <summary>Relative paths that held no library file when the session began, one per line of _library_absent.txt.</summary>
    internal IReadOnlyList<string> LibraryAbsentPaths() => AbsentPathsIn(LibraryAbsentFileName);

    /// <summary>File names that held no thumbnail when the session began, one per line of _thumbnails_absent.txt.</summary>
    internal IReadOnlyList<string> ThumbnailsAbsentPaths() => AbsentPathsIn(ThumbnailsAbsentFileName);

    internal string RecordSidePath => System.IO.Path.Combine(Path, RecordFileName);

    internal string RecordAbsentPath => System.IO.Path.Combine(Path, RecordAbsentFileName);

    /// <summary>Whether this session holds the applied record: either the file itself or the note that there was
    /// none. A session begun by an older build holds neither, and a restore leaves the record alone.</summary>
    internal bool HasRecordSide => File.Exists(RecordSidePath) || File.Exists(RecordAbsentPath);

    private static IReadOnlyList<string> CapturedUnder(string root)
    {
        if (!Directory.Exists(root))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(f => System.IO.Path.GetRelativePath(root, f))
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<string> AbsentPathsIn(string absentFileName)
    {
        var absent = System.IO.Path.Combine(Path, absentFileName);
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

    /// <summary>Copies the game side's captured files back and deletes the ones recorded as absent. Copied counts both.
    /// Never throws per file. A restore with no failures discards the snapshot; one with failures keeps it so the user
    /// can retry. A library or thumbnails side the session holds is left alone.</summary>
    public ApplyResult Restore(UndoSession session, string gamePath) =>
        RestoreSides(session, gamePath, libraryPath: null, thumbnailsDir: null);

    /// <summary>Restores the game side under gamePath and, when the session has a library side, the library side
    /// under libraryPath. Clears the session on zero failures.</summary>
    public ApplyResult Restore(UndoSession session, string gamePath, string libraryPath) =>
        Restore(session, gamePath, libraryPath, thumbnailsDir: "");

    /// <summary>The three sides at once. An empty thumbnailsDir means the caller wants no thumbnails side, so one the
    /// session holds stays snapshotted instead of being put back, as a game-only restore leaves the library side.</summary>
    public ApplyResult Restore(UndoSession session, string gamePath, string libraryPath, string thumbnailsDir) =>
        RestoreSides(session, gamePath, libraryPath, thumbnailsDir.Length == 0 ? null : thumbnailsDir);

    /// <summary>The three sides plus the applied record at recordPath, put back the way the session found it. The
    /// record describes writes this restore is undoing, so it goes back with them or it would name files that no
    /// longer hold those bytes.</summary>
    public ApplyResult Restore(
        UndoSession session, string gamePath, string libraryPath, string thumbnailsDir, string recordPath) =>
        RestoreSides(session, gamePath, libraryPath, thumbnailsDir.Length == 0 ? null : thumbnailsDir, recordPath);

    private ApplyResult RestoreSides(
        UndoSession session, string gamePath, string? libraryPath, string? thumbnailsDir, string? recordPath = null)
    {
        IReadOnlyList<string> captured;
        IReadOnlyList<string> absent;
        IReadOnlyList<string> libraryCaptured;
        IReadOnlyList<string> libraryAbsent;
        IReadOnlyList<string> thumbnailsCaptured;
        IReadOnlyList<string> thumbnailsAbsent;
        try
        {
            captured = session.CapturedFiles();
            absent = session.AbsentPaths();
            // A caller that asked for the game side alone leaves the library side snapshotted, not undone.
            libraryCaptured = libraryPath is null ? Array.Empty<string>() : session.CapturedLibraryFiles();
            libraryAbsent = libraryPath is null ? Array.Empty<string>() : session.LibraryAbsentPaths();
            thumbnailsCaptured = thumbnailsDir is null ? Array.Empty<string>() : session.CapturedThumbnailFiles();
            thumbnailsAbsent = thumbnailsDir is null ? Array.Empty<string>() : session.ThumbnailsAbsentPaths();
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

        if (libraryPath is not null)
        {
            var librarySide = Path.Combine(session.Path, UndoSession.LibraryFolderName);
            foreach (var relativePath in libraryCaptured)
            {
                CopyBack(Path.Combine(librarySide, relativePath), Path.Combine(libraryPath, relativePath), ref restored, failures);
            }

            foreach (var relativePath in libraryAbsent)
            {
                DeleteBack(Path.Combine(libraryPath, relativePath), ref restored, failures);
            }

            // A library file that was absent when the session began was made by the write this restore is
            // undoing, and so were the folders it needed. Pruning them upwards is what makes undo of a duplicate
            // or an import leave the library the shape it was, rather than a pack folder with nothing in it.
            PruneEmptyFolders(libraryPath, libraryAbsent);
        }

        if (thumbnailsDir is not null)
        {
            var thumbnailsSide = Path.Combine(session.Path, UndoSession.ThumbnailsFolderName);
            foreach (var fileName in thumbnailsCaptured)
            {
                CopyBack(Path.Combine(thumbnailsSide, fileName), Path.Combine(thumbnailsDir, fileName), ref restored, failures);
            }

            foreach (var fileName in thumbnailsAbsent)
            {
                DeleteBack(Path.Combine(thumbnailsDir, fileName), ref restored, failures);
            }
        }

        if (recordPath is not null && session.HasRecordSide)
        {
            // The record is bookkeeping, not one of the files the count reports, so it is put back without
            // counting; a failure still counts, because a stale record outlives the restore.
            var bookkeeping = 0;
            if (File.Exists(session.RecordSidePath))
            {
                CopyBack(session.RecordSidePath, recordPath, ref bookkeeping, failures);
            }
            else
            {
                DeleteBack(recordPath, ref bookkeeping, failures);
            }
        }

        if (failures.Count == 0)
        {
            // Clear, not a delete of this one folder: a session an earlier Begin could not remove would otherwise
            // become the Latest and put a stale snapshot back on offer.
            Clear();
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

    /// <summary>Deletes the folders of these relative paths, and their parents, while they are empty and still
    /// inside root. Stops one level below root: a folder sitting directly in the library, "packs" above all, is the
    /// library's own shape rather than something the undone write made, so it stays even when it empties. A folder
    /// we cannot delete stops that path and nothing else.</summary>
    private static void PruneEmptyFolders(string root, IReadOnlyList<string> relativePaths)
    {
        var stop = Path.GetFullPath(root);
        foreach (var relativePath in relativePaths)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(root, relativePath)));
            while (IsBelow(directory, stop)
                && IsBelow(Path.GetDirectoryName(directory), stop)
                && Directory.Exists(directory))
            {
                try
                {
                    if (Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        break;
                    }

                    Directory.Delete(directory);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    break;
                }

                directory = Path.GetDirectoryName(directory);
            }
        }
    }

    /// <summary>Whether directory is a real folder somewhere under root, root itself excluded.</summary>
    private static bool IsBelow([NotNullWhen(true)] string? directory, string root) =>
        directory is not null
        && !directory.Equals(root, StringComparison.OrdinalIgnoreCase)
        && directory.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

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
