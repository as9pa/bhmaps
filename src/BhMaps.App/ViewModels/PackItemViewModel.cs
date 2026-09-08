using BhMaps.Core.Model;

namespace BhMaps.App.ViewModels;

public sealed class PackItemViewModel(Pack pack)
{
    public Pack Pack { get; } = pack;

    public string Name => Pack.Name;

    public int FileCount => Pack.FileCount;
}
