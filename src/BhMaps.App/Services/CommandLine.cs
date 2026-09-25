using System.Globalization;
using BhMaps.Core.Update;

namespace BhMaps.App.Services;

public sealed record CommandLineArgs(
    string? Game, string? Library, string? AppData, bool Quiet = false, int? AfterUpdatePid = null)
{
    /// <summary>
    /// The settings file and hash cache follow --appdata alone, so --game or --library on their own
    /// would still read and write the real %APPDATA% files. Returns the message to show and refuse
    /// that combination with, or null when the combination is safe to run.
    /// </summary>
    public string? MissingAppDataError()
    {
        if (AppData is not null || (Game is null && Library is null))
        {
            return null;
        }

        var flags = (Game, Library) switch
        {
            (not null, not null) => "--game and --library",
            (not null, _) => "--game",
            _ => "--library",
        };
        return $"This run was given {flags} but no --appdata. Only --appdata moves the settings file and the hash cache, "
            + "so without it a development run reads and writes the real ones in %APPDATA%\\BhMaps and can change them, "
            + "for instance by marking the first-run backup as already offered. Pass --appdata <dir> as well so this run "
            + "keeps its own settings.json and hashcache.json in that folder and the real ones are never touched.";
    }
}

/// <summary>Development overrides: --game &lt;path&gt; --library &lt;path&gt; --appdata &lt;dir&gt;. All optional,
/// except that --game or --library also needs --appdata. --quiet opens every window without activating it,
/// for automated captures. --after-update &lt;pid&gt; is not for people: the update passes it to the new exe so it
/// waits for the old process to exit.</summary>
public static class CommandLine
{
    public static CommandLineArgs Parse(string[] args)
    {
        string? game = null;
        string? library = null;
        string? appData = null;
        var quiet = false;
        int? afterUpdate = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--game" when i + 1 < args.Length:
                    game = args[++i];
                    break;
                case "--library" when i + 1 < args.Length:
                    library = args[++i];
                    break;
                case "--appdata" when i + 1 < args.Length:
                    appData = args[++i];
                    break;
                case UpdateInstaller.AfterUpdateArg when i + 1 < args.Length:
                    afterUpdate = int.TryParse(args[++i], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
                        ? pid
                        : null;
                    break;
                case "--quiet":
                    quiet = true;
                    break;
            }
        }

        return new CommandLineArgs(game, library, appData, quiet, afterUpdate);
    }
}
