using BhMaps.Core.Settings;

namespace BhMaps.Core.Tests;

public class SteamLibraryFoldersTests
{
    /// <summary>Shaped like the real file: three numbered entries, the first with the doubled backslashes VDF
    /// escapes a Windows path with, the second with the forward slashes Steam sometimes writes instead, and a
    /// third that carries no path at all.</summary>
    private const string Sample = """
        "libraryfolders"
        {
        	"0"
        	{
        		"path"		"C:\\Program Files (x86)\\Steam"
        		"label"		""
        		"contentid"		"7906843729545119584"
        		"totalsize"		"0"
        		"apps"
        		{
        			"291550"		"3260468964"
        		}
        	}
        	"1"
        	{
        		"path"		"D:/SteamLibrary"
        		"label"		""
        		"contentid"		"1364139249551431104"
        		"apps"
        		{
        		}
        	}
        	"2"
        	{
        		"label"		"a drive that is not plugged in"
        		"contentid"		"0"
        		"apps"
        		{
        		}
        	}
        }
        """;

    [Fact]
    public void Parse_ReturnsEveryPathValueUnescapedAndSkipsEntriesWithoutOne()
    {
        var paths = SteamLibraryFolders.Parse(Sample);

        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", "D:/SteamLibrary" }, paths);
    }

    [Fact]
    public void Parse_ReturnsEmptyForTextWithNoPaths()
    {
        Assert.Empty(SteamLibraryFolders.Parse(""));
        Assert.Empty(SteamLibraryFolders.Parse("{ not a vdf at all"));
        Assert.Empty(SteamLibraryFolders.Parse("\"libraryfolders\"\r\n{\r\n\t\"0\"\r\n\t{\r\n\t}\r\n}\r\n"));
    }

    /// <summary>The real file is CRLF. Normalised first, so the result does not depend on how this source file
    /// happens to be checked out.</summary>
    [Fact]
    public void Parse_ReadsCrLfLinesWithoutTrailingTheCarriageReturn()
    {
        var crlf = Sample.Replace("\r\n", "\n").Replace("\n", "\r\n");

        var paths = SteamLibraryFolders.Parse(crlf);

        Assert.Equal(new[] { @"C:\Program Files (x86)\Steam", "D:/SteamLibrary" }, paths);
    }
}
