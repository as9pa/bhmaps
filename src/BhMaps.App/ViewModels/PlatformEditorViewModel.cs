using System.Windows.Media;
using BhMaps.App.Services;
using BhMaps.Core.Imaging;
using BhMaps.Core.LevelData;
using BhMaps.Core.Maps;
using BhMaps.Core.Model;
using BhMaps.Core.Operations;
using BhMaps.Core.Scanning;
using BhMaps.Core.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

/// <summary>What a set tile's Edit hands the editor: the map whose own pieces are recoloured, and the pack the
/// set came from, or null for the set the game is showing (spec 6).</summary>
public sealed record PlatformEditorRequest(MapEntry Map, Pack? Pack);

/// <summary>What one Save left in the library: the pack it was saved into, which is the pack the shell stamps
/// when it applies it, the map folder inside that pack, and whether the user asked for it to go into the game as
/// well. The shell does that part, so the editor never writes into the game folder.</summary>
public sealed record PlatformSave(string PackName, string SetFolder, bool ApplyToGame);

/// <summary>Spec 6: one map's own platform pieces, faded and recoloured by one number each. Every slider change
/// writes the processed pieces into a temp set and composes the map from it, so the preview is the same render
/// the panel and the rows draw, over the background the game is showing for that map today.</summary>
public partial class PlatformEditorViewModel : ObservableObject
{
    public const string NoPackText = "In game";
    public const string NoFilesText = "This map has no platform art of its own.";

    /// <summary>Long enough that a dragged thumb is not one render per pixel, short enough that the preview
    /// still follows the thumb. Every piece is decoded and re-encoded per render, so this is slower work than
    /// the background editor's one bitmap (spec 6).</summary>
    private static readonly TimeSpan PreviewInterval = TimeSpan.FromMilliseconds(60);

    private readonly AppServices _services;
    private readonly IDialogs _dialogs;
    private readonly PlatformEditorRequest _request;
    private readonly IReadOnlyList<PlatformPiece> _pieces;
    private readonly string _tempRoot;
    private readonly string? _backgroundPath;
    private readonly Throttler _preview = new(PreviewInterval);

    private long _sequence;

    public PlatformEditorViewModel(
        AppServices services,
        IDialogs dialogs,
        IReadOnlyList<string> packNames,
        PlatformEditorRequest request)
    {
        _services = services;
        _dialogs = dialogs;
        _request = request;
        _tempRoot = Path.Combine(Path.GetTempPath(), "BhMaps", "platform-editor", Guid.NewGuid().ToString("N"));
        _pieces = Pieces(services, request);
        _backgroundPath = CurrentBackground(services, request.Map);
        PackChoices = packNames.Concat([BackgroundEditorViewModel.NewPackChoice]).ToList();
        Error = "";
        ApplyNow = true;

        // The background editor's rule: the pack the user would mean is there, or it is a new pack already named.
        var existingDefault = packNames.FirstOrDefault(
            p => p.Equals(BackgroundEditorViewModel.DefaultPackName, StringComparison.OrdinalIgnoreCase));
        TargetPack = existingDefault ?? BackgroundEditorViewModel.NewPackChoice;
        NewPackName = existingDefault is null ? BackgroundEditorViewModel.DefaultPackName : "";

        Opacity = 100;
        Hue = 0;
        SchedulePreview();
    }

    public event Action<bool>? CloseRequested;

    public IReadOnlyList<string> PackChoices { get; }

    /// <summary>What Save wrote into the library, or null while nothing has been saved.</summary>
    public PlatformSave? Saved { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpacityText))]
    public partial int Opacity { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HueText))]
    public partial int Hue { get; set; }

    [ObservableProperty]
    public partial ImageSource? Preview { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewPack), nameof(PackNameError), nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string TargetPack { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackNameError), nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string NewPackName { get; set; }

    [ObservableProperty]
    public partial bool ApplyNow { get; set; }

    [ObservableProperty]
    public partial string Error { get; set; }

    public string Title => $"Edit platforms, {_request.Map.DisplayName}";

    /// <summary>The set Edit was pressed on: a pack's, or the one the game is showing (spec 6).</summary>
    public string SourceText =>
        $"{_request.Pack?.Name ?? NoPackText}, {MainViewModel.Count(_pieces.Count, "file")}";

    /// <summary>False for a map with no pieces of its own: there is nothing for a slider to move (spec 14).</summary>
    public bool CanEdit => _pieces.Count > 0;

    public string OpacityText => $"{Opacity}%";

    /// <summary>The sign is part of the reading: "+140" is a turn one way and "-30" the other, and "0" is neither.</summary>
    public string HueText => Hue > 0 ? $"+{Hue}" : Hue.ToString();

    /// <summary>What the preview says instead of a picture, which is only ever the map having no art of its own.</summary>
    public string EmptyText => CanEdit ? "" : NoFilesText;

    public bool IsNewPack => TargetPack == BackgroundEditorViewModel.NewPackChoice;

    public string EffectivePackName => IsNewPack ? NewPackName.Trim() : TargetPack;

    public string PackNameError =>
        IsNewPack && !PackNameValidator.IsValid(EffectivePackName, out var error) ? error : "";

    public bool CanSave => CanEdit && PackNameError.Length == 0;

    /// <summary>Drops the temp set the previews were drawn from, whichever button closed the window. A file the
    /// render still holds open is not worth a dialog: the folder is under %TEMP% and Windows clears it.</summary>
    public void Cleanup()
    {
        _preview.Cancel();
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The temp set outlives the window rather than the window outliving the user's patience.
        }
    }

    /// <summary>The render a drag ends with: the throttle can only have dropped values that are stale by now, so
    /// the newest ones are drawn with nothing queued behind them.</summary>
    public void RenderFinal()
    {
        _preview.Cancel();
        SchedulePreview();
    }

    partial void OnOpacityChanged(int value) => SchedulePreview();

    partial void OnHueChanged(int value) => SchedulePreview();

    /// <summary>Where each piece is read from: the pack's copy when the pack has one that draws something, the
    /// game's otherwise, which is the rule every composite already resolves by (spec 6).</summary>
    private static IReadOnlyList<PlatformPiece> Pieces(AppServices services, PlatformEditorRequest request)
    {
        var sources = new AssetSources(services.GamePath, request.Pack?.FullPath);
        var pieces = new List<PlatformPiece>();
        foreach (var relativePath in request.Map.PlatformFiles.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (sources.ResolveAsset(relativePath) is { } source)
            {
                pieces.Add(new PlatformPiece(source, relativePath));
            }
        }

        return pieces;
    }

    /// <summary>The background the game is showing for the map today, so the preview sits over what the user sees
    /// rather than over the tile colour. Null for a map whose slot the game folder has nothing for.</summary>
    private static string? CurrentBackground(AppServices services, MapEntry map)
    {
        if (map.BackgroundSlots.Count == 0)
        {
            return null;
        }

        var path = Path.Combine(services.GamePath, AssetPath.Background(map.BackgroundSlots[0]));
        return File.Exists(path) ? path : null;
    }

    [RelayCommand]
    private void ResetOpacity() => Opacity = 100;

    [RelayCommand]
    private void ResetHue() => Hue = 0;

    /// <summary>Saves the recoloured set into the pack and nothing else; the "Apply to game now" box is the
    /// shell's business, because a game write needs the boundary, the snapshot and the undo (spec 8).</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var packRoot = Path.Combine(PackScanner.PacksRoot(_services.LibraryPath), EffectivePackName);
        var destination = Path.Combine(packRoot, _request.Map.FolderName);
        if (Directory.Exists(destination)
            && Directory.EnumerateFiles(destination).Any()
            && !_dialogs.Confirm(
                "Replace platforms?",
                $"{EffectivePackName} already has platforms for {_request.Map.DisplayName}. Replace them?"))
        {
            return;
        }

        var pieces = _pieces;
        var opacity = Opacity / 100.0;
        double hue = Hue;
        try
        {
            await Task.Run(() =>
            {
                foreach (var piece in pieces)
                {
                    PlatformRecolor.Apply(piece.SourcePath, Path.Combine(packRoot, piece.RelativePath), opacity, hue);
                }
            });
            Saved = new PlatformSave(EffectivePackName, destination, ApplyNow);
            CloseRequested?.Invoke(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            _dialogs.Error("Could not save the platforms", ex.Message);
        }
    }

    /// <summary>Spec 6: 60 ms between renders, newest values win. The stamp is what makes the second half of that
    /// true, because a render already on the queue still finishes after a newer one has been asked for.</summary>
    private void SchedulePreview()
    {
        var opacity = Opacity / 100.0;
        double hue = Hue;
        var stamp = ++_sequence;
        _preview.Run(ct => RenderAsync(opacity, hue, stamp, ct));
    }

    /// <summary>The pieces are processed into the temp set and the map is composed from it, so the preview is the
    /// real composite rather than a drawing of one. The temp set is the map art root of the render: a piece faded
    /// to nothing has to draw nothing, and a pack file that is fully transparent falls back to the game's.</summary>
    private async Task RenderAsync(double opacity, double hue, long stamp, CancellationToken ct)
    {
        var pieces = _pieces;
        var root = _tempRoot;
        var level = _request.Map.BaseLevel;
        var background = _backgroundPath;
        try
        {
            await Task.Run(
                () =>
                {
                    foreach (var piece in pieces)
                    {
                        ct.ThrowIfCancellationRequested();
                        PlatformRecolor.Apply(piece.SourcePath, Path.Combine(root, piece.RelativePath), opacity, hue);
                    }
                },
                ct);

            var sources = new AssetSources(root, backgroundOverride: background);
            var bitmap = await _services.Renderer.RunAsync(
                () => MapCompositor.Render(level, MapCompositor.PanelWidth, MapCompositor.PanelHeight, sources), ct);
            if (ct.IsCancellationRequested || stamp != _sequence)
            {
                return;
            }

            Preview = bitmap;
            Error = "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
        {
            if (!ct.IsCancellationRequested)
            {
                Error = "Could not read the platform art: " + ex.Message;
            }
        }
    }

    /// <summary>One piece of the set: where it is read from, and the path under a set root it is written to.</summary>
    private sealed record PlatformPiece(string SourcePath, string RelativePath);
}
