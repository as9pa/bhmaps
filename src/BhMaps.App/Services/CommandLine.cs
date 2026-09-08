namespace BhMaps.App.Services;

public sealed record CommandLineArgs(string? Game, string? Library, string? AppData);

/// <summary>Development overrides: --game &lt;path&gt; --library &lt;path&gt; --appdata &lt;dir&gt;. All optional.</summary>
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
