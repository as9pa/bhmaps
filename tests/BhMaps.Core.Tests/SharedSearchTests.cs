using System.ComponentModel;
using System.Net.Http;
using BhMaps.App.Services;
using BhMaps.App.ViewModels;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Tests.Helpers;

namespace BhMaps.Core.Tests;

/// <summary>One search for Maps, Backgrounds and Platforms: the text lives on the shell, so text typed on one
/// page is the text the next one shows.</summary>
public sealed class SharedSearchTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly HttpClient _http = new();
    private readonly AppServices _services;
    private readonly MainViewModel _shell;

    public SharedSearchTests()
    {
        _services = new AppServices(_dir.Sub("appdata"), _dir.Sub("game"), _dir.Sub("library"), _http);
        _shell = new MainViewModel(_services, new NoDialogs());
    }

    public void Dispose()
    {
        _services.Dispose();
        _http.Dispose();
        _dir.Dispose();
    }

    [Fact]
    public void TextTypedOnOnePageIsTheTextOnEveryPage()
    {
        _shell.Backgrounds.SearchText = "wharf";

        Assert.Equal("wharf", _shell.SearchText);
        Assert.Equal("wharf", _shell.Maps.SearchText);
        Assert.Equal("wharf", _shell.Platforms.SearchText);
        Assert.True(_shell.Maps.ShowClearSearch);
        Assert.True(_shell.Platforms.ShowClearSearch);
    }

    [Fact]
    public void EveryPageTellsItsBoxWhenAnotherPageTypes()
    {
        var changed = new List<string>();
        _shell.Maps.PropertyChanged += (_, e) => Record(changed, "maps", e);
        _shell.Platforms.PropertyChanged += (_, e) => Record(changed, "platforms", e);

        _shell.Backgrounds.SearchText = "w";

        Assert.Contains("maps", changed);
        Assert.Contains("platforms", changed);
    }

    [Fact]
    public void ClearingOnOnePageClearsEveryPage()
    {
        _shell.Maps.SearchText = "wharf";

        _shell.Platforms.ClearSearchCommand.Execute(null);

        Assert.Equal("", _shell.Maps.SearchText);
        Assert.Equal("", _shell.Backgrounds.SearchText);
        Assert.False(_shell.Maps.ShowClearSearch);
    }

    [Fact]
    public void AChipClearsTheSearchEverywhere()
    {
        _shell.Maps.SearchText = "wharf";

        _shell.Backgrounds.SelectedChip = MapCatalog.MinigameLabel;

        Assert.Equal("", _shell.SearchText);
        Assert.Equal("", _shell.Maps.SearchText);
        Assert.Equal("", _shell.Platforms.SearchText);
    }

    [Fact]
    public void NullFromTheBoxIsAnEmptySearch()
    {
        _shell.Maps.SearchText = null!;

        Assert.Equal("", _shell.Backgrounds.SearchText);
    }

    private static void Record(List<string> changed, string page, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "SearchText")
        {
            changed.Add(page);
        }
    }

    /// <summary>Nothing here opens a dialog; one that tries is a failure.</summary>
    private sealed class NoDialogs : IDialogs
    {
        public bool Confirm(string title, string message, string primary, bool destructive = false) =>
            throw new InvalidOperationException(title);

        public void Error(string title, string message) => throw new InvalidOperationException(title);

        public void Info(string title, string message) => throw new InvalidOperationException(title);

        public void ShowFailures(string title, IReadOnlyList<FileFailure> failures) =>
            throw new InvalidOperationException(title);

        public string? PickFolder(string title) => throw new InvalidOperationException(title);

        public IReadOnlyList<string>? PickFolders(string title) => throw new InvalidOperationException(title);

        public string? PickImageFile(string title) => throw new InvalidOperationException(title);

        public IReadOnlyList<string>? PickImageFiles(string title) => throw new InvalidOperationException(title);

        public string? PromptText(string title, string message, string initial) =>
            throw new InvalidOperationException(title);

        public bool AddAsNew(AddAsNewViewModel vm) => throw new InvalidOperationException("Add as new");
    }
}
