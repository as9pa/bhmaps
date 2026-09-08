using CommunityToolkit.Mvvm.Input;

namespace BhMaps.App.ViewModels;

/// <summary>One entry in an "Apply from" dropdown: a pack name and the command that applies it.</summary>
public sealed record ApplyFromOption(string Name, IAsyncRelayCommand ApplyCommand);
