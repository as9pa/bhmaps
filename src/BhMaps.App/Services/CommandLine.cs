namespace BhMaps.App.Services;

public sealed record CommandLineArgs(string? Game, string? Library, string? AppData)
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
/// except that --game or --library also needs --appdata.</summary>
public static class CommandLine
{
    public static CommandLineArgs Parse(string[] args)
    {
        string? game = null;
        string? library = null;
        string? appData = null;
        for (var i = 0; i + 1 < args.Length; i++)
        {
            switch (args[i])
            {
                case "--game":
                    game = args[++i];
                    break;
                case "--library":
                    library = args[++i];
                    break;
                case "--appdata":
                    appData = args[++i];
                    break;
            }
        }

        return new CommandLineArgs(game, library, appData);
    }
}
