using BhMaps.Core.Operations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

/// <summary>3.7.3: the background editor's "Add as new" popup. It asks what to call the new picture and which
/// pack it goes into, and says what is wrong with either before Add can be pressed. The editor does the save.
/// 3.9.2: the platform editor's "Add as new" uses it too, through <see cref="ForPlatforms"/>: no picture name,
/// and an existing pack that already has platforms for any of the maps is refused, so nothing is overwritten.</summary>
public partial class AddAsNewViewModel : ObservableObject
{
    public const string NewPackChoice = BackgroundEditorViewModel.NewPackChoice;

    /// <summary>What every picture the editor writes is saved as.</summary>
    private const string Extension = ".jpg";

    private readonly string _baseName;
    private readonly Func<string, IReadOnlyList<string>> _existingNames;
    private readonly IReadOnlyList<string> _takenPacks;

    /// <summary>3.9.2: the platform popup's question, "which of the maps being saved does this pack already have
    /// platforms for?", as display names. Null for the background popup.</summary>
    private readonly Func<string, IReadOnlyList<string>>? _mapsWithPlatforms;

    /// <summary>The last name this popup offered, so a pack change re-offers only while the user has not typed.</summary>
    private string _suggested = "";

    public AddAsNewViewModel(
        IReadOnlyList<string> packNames, string? startPack, string baseName,
        Func<string, IReadOnlyList<string>> existingNames, IReadOnlyList<string> takenPacks)
        : this(packNames, startPack, baseName, existingNames, takenPacks, mapsWithPlatforms: null)
    {
    }

    private AddAsNewViewModel(
        IReadOnlyList<string> packNames, string? startPack, string baseName,
        Func<string, IReadOnlyList<string>> existingNames, IReadOnlyList<string> takenPacks,
        Func<string, IReadOnlyList<string>>? mapsWithPlatforms)
    {
        _baseName = baseName;
        _mapsWithPlatforms = mapsWithPlatforms;
        _existingNames = existingNames;
        _takenPacks = takenPacks;
        PackChoices = packNames.Concat([NewPackChoice]).ToList();
        PictureName = "";
        NewPackName = PackNames.NextFree(takenPacks);
        TargetPack = packNames.FirstOrDefault(p => p.Equals(startPack, StringComparison.OrdinalIgnoreCase))
            ?? NewPackChoice;
        Suggest();
    }

    /// <summary>3.9.2: the platform editor's popup. There is no picture to name, and a pack that already has
    /// platforms for any map being saved says so instead of taking Add.</summary>
    public static AddAsNewViewModel ForPlatforms(
        IReadOnlyList<string> packNames, string? startPack, IReadOnlyList<string> takenPacks,
        Func<string, IReadOnlyList<string>> mapsWithPlatforms) =>
        new(packNames, startPack, "", _ => [], takenPacks, mapsWithPlatforms);

    public event Action<bool>? CloseRequested;

    /// <summary>False for the platform popup, which hides the Picture name label and box.</summary>
    public bool ShowPictureName => _mapsWithPlatforms is null;

    /// <summary>Every writable pack plus the "New pack..." entry that reveals the name field.</summary>
    public IReadOnlyList<string> PackChoices { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Error), nameof(CanAdd))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string PictureName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewPack), nameof(EffectivePackName), nameof(Error), nameof(CanAdd))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string TargetPack { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectivePackName), nameof(Error), nameof(CanAdd))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string NewPackName { get; set; }

    public bool IsNewPack => TargetPack == NewPackChoice;

    public string EffectivePackName => IsNewPack ? NewPackName.Trim() : TargetPack;

    /// <summary>The file the save writes, "Wharf (2).jpg".</summary>
    public string FileName => PictureNames.FileName(PictureName.Trim(), Extension);

    /// <summary>The one line under the fields: the pack's problem first, because the name's depends on the pack.</summary>
    public string Error
    {
        get
        {
            if (IsNewPack)
            {
                if (!PackNameValidator.IsValid(EffectivePackName, out var packError))
                {
                    return packError;
                }

                if (_takenPacks.Any(p => p.Equals(EffectivePackName, StringComparison.OrdinalIgnoreCase)))
                {
                    return $"A pack called {EffectivePackName} already exists.";
                }
            }

            if (_mapsWithPlatforms is not null)
            {
                return IsNewPack ? "" : PlatformsError();
            }

            var name = PictureName.Trim();
            if (name.Length == 0)
            {
                return "Name the picture.";
            }

            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.'))
            {
                return "A picture name cannot contain \\ / : * ? \" < > | or end with a dot.";
            }

            return !IsNewPack && _existingNames(EffectivePackName)
                    .Any(n => n.Equals(FileName, StringComparison.OrdinalIgnoreCase))
                ? $"{name} already exists in {EffectivePackName}."
                : "";
        }
    }

    public bool CanAdd => Error.Length == 0;

    /// <summary>"Pack already has platforms for Wharf and 2 more.", or nothing when the pack has none of them.</summary>
    private string PlatformsError()
    {
        var clashes = _mapsWithPlatforms!(EffectivePackName);
        if (clashes.Count == 0)
        {
            return "";
        }

        var more = clashes.Count > 1 ? $" and {clashes.Count - 1} more" : "";
        return $"{EffectivePackName} already has platforms for {clashes[0]}{more}.";
    }

    partial void OnTargetPackChanged(string value) => Suggest();

    /// <summary>The source's name with the next free " (N)" in the chosen pack, offered again on a pack change
    /// unless the user has typed a name of their own.</summary>
    private void Suggest()
    {
        if (!ShowPictureName)
        {
            return;
        }

        if (PictureName.Length > 0 && PictureName != _suggested)
        {
            return;
        }

        var own = _baseName + Extension;
        var fresh = PictureNames.Copy(own, IsNewPack ? [] : _existingNames(EffectivePackName));
        _suggested = Path.GetFileNameWithoutExtension(fresh);
        PictureName = _suggested;
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add() => CloseRequested?.Invoke(true);
}
