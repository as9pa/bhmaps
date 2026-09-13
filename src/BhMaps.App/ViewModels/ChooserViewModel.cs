using System.Collections.ObjectModel;
using System.Windows.Media;
using BhMaps.App.Services;
using BhMaps.Core.Maps;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BhMaps.App.ViewModels;

/// <summary>One row of the chooser: what it is called, the muted right-hand word, the file behind it and, in map
/// mode, the map it stands for. Picture mode leaves Map null and the caller reads Path.</summary>
public sealed partial class ChooserRow : ObservableObject
{
    public ChooserRow(string name, string detail, string path, MapEntry? map)
    {
        Name = name;
        Detail = detail;
        Path = path;
        Map = map;
    }

    public string Name { get; }

    /// <summary>Map mode: the applied pack name or "Default" (spec 9.2, owner answer q4). Picture mode: the pack.</summary>
    public string Detail { get; }

    /// <summary>The picture the thumbnail is decoded from, and what picture mode returns.</summary>
    public string Path { get; }

    public MapEntry? Map { get; }

    [ObservableProperty]
    public partial ImageSource? Thumbnail { get; set; }
}

/// <summary>Spec 4.2: one small window that picks one map or one picture. It never writes anything: it returns a
/// choice and the menu that opened it applies through the path it already used.</summary>
public sealed partial class ChooserViewModel : ObservableObject
{
    private readonly IReadOnlyList<ChooserRow> _all;
    private readonly string _noun;

    public ChooserViewModel(string title, string subtitle, string noun, IReadOnlyList<ChooserRow> rows)
    {
        Title = title;
        Subtitle = subtitle;
        _noun = noun;
        _all = rows;
        SearchText = "";
        Rows = [.. rows];
    }

    /// <summary>"Apply BG_Dojo.jpg to a map" or "Apply a picture to Brawlhaven". The window's UIA name too.</summary>
    public string Title { get; }

    public string Subtitle { get; }

    public ObservableCollection<ChooserRow> Rows { get; }

    [ObservableProperty]
    public partial string SearchText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryText))]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    public partial ChooserRow? Selected { get; set; }

    /// <summary>Spec 4.2: the Maps page's own empty wording, with the noun swapped.</summary>
    public string EmptyText => $"No {_noun} matches '{SearchText}'.";

    public bool ShowEmpty => Rows.Count == 0;

    /// <summary>"Apply to" in map mode, "Apply" in picture mode, so this class holds no mode flag.</summary>
    public string PrimaryPrefix { get; init; } = "Apply";

    public string PrimaryText => Selected is null ? PrimaryPrefix : $"{PrimaryPrefix} {Selected.Name}";

    public bool CanApply => Selected is not null;

    partial void OnSearchTextChanged(string value)
    {
        Rows.Clear();
        foreach (var row in _all.Where(r => NameFilter.Matches(r.Name, value)))
        {
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(ShowEmpty));
        if (Selected is { } chosen && !Rows.Contains(chosen))
        {
            Selected = null;
        }
    }

    /// <summary>Every decode is off the UI thread and through the shared cache, as a tile's is.</summary>
    public async Task LoadThumbnailsAsync(AppServices services, CancellationToken ct)
    {
        foreach (var row in _all)
        {
            if (ct.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var mtime = await Task.Run(() => File.GetLastWriteTimeUtc(row.Path).Ticks, ct);
                if (await services.Thumbnails.GetAsync(row.Path, mtime, ct) is { } image)
                {
                    row.Thumbnail = image;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // A row whose file vanished still picks; a blank thumbnail is not worth a dialog.
            }
        }
    }
}
