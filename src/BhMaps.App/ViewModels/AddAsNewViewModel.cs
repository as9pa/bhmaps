using BhMaps.Core.Operations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

/// <summary>3.7.3: the background editor's "Add as new" popup. It asks what to call the new picture and which
/// pack it goes into, and says what is wrong with either before Add can be pressed. The editor does the save.</summary>
public partial class AddAsNewViewModel : ObservableObject
{
    public const string NewPackChoice = BackgroundEditorViewModel.NewPackChoice;

    /// <summary>What every picture the editor writes is saved as.</summary>
    private const string Extension = ".jpg";

    private readonly string _baseName;
    private readonly Func<string, IReadOnlyList<string>> _existingNames;
    private readonly IReadOnlyList<string> _takenPacks;

    /// <summary>The last name this popup offered, so a pack change re-offers only while the user has not typed.</summary>
    private string _suggested = "";

    public AddAsNewViewModel(
        IReadOnlyList<string> packNames, string? startPack, string baseName,
        Func<string, IReadOnlyList<string>> existingNames, IReadOnlyList<string> takenPacks)
    {
        _baseName = baseName;
        _existingNames = existingNames;
        _takenPacks = takenPacks;
        PackChoices = packNames.Concat([NewPackChoice]).ToList();
        PictureName = "";
        NewPackName = PackNames.NextFree(takenPacks);
        TargetPack = packNames.FirstOrDefault(p => p.Equals(startPack, StringComparison.OrdinalIgnoreCase))
            ?? NewPackChoice;
        Suggest();
    }

    public event Action<bool>? CloseRequested;

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

    partial void OnTargetPackChanged(string value) => Suggest();

    /// <summary>The source's name with the next free " (N)" in the chosen pack, offered again on a pack change
    /// unless the user has typed a name of their own.</summary>
    private void Suggest()
    {
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
