using BhMaps.Core.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

/// <summary>Spec G4: one line of the legend under the platform editor's stage, which also switches its layer. A
/// click hands the layer to the editor, which keeps the one app-wide set of hidden layers and writes
/// <see cref="IsHidden"/> back.</summary>
public partial class GuideLegendLineViewModel : ObservableObject
{
    private readonly Action<GuideLayer> _toggle;

    public GuideLegendLineViewModel(GuideLayer layer, bool hidden, Action<GuideLayer> toggle)
    {
        Layer = layer;
        Label = LabelOf(layer);
        IsHidden = hidden;
        _toggle = toggle;
    }

    public GuideLayer Layer { get; }

    /// <summary>The layer in plain words, the way a player would name it rather than the level file.</summary>
    public string Label { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Text))]
    public partial bool IsHidden { get; set; }

    /// <summary>What the line reads: its label, and a quiet ", hidden" while its layer is off.</summary>
    public string Text => IsHidden ? $"{Label}, hidden" : Label;

    public static string LabelOf(GuideLayer layer) =>
        layer switch
        {
            GuideLayer.Boxes => "Piece edge",
            GuideLayer.Overlaps => "Overlap",
            GuideLayer.Hard => "Ground",
            GuideLayer.Soft => "Drop-through",
            GuideLayer.NoSlide => "No-slide wall",
            GuideLayer.Bouncy => "Bouncy",
            GuideLayer.Lava => "Lava",
            GuideLayer.PressurePlate => "Pressure plate",
            GuideLayer.GameModeHard => "Game-mode wall",
            GuideLayer.Mud => "Mud",
            _ => layer.ToString(),
        };

    [RelayCommand]
    private void Toggle() => _toggle(Layer);
}
