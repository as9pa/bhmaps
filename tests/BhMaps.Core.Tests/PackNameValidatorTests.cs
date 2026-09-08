using BhMaps.Core.Operations;

namespace BhMaps.Core.Tests;

public class PackNameValidatorTests
{
    [Theory]
    [InlineData("flower")]
    [InlineData("My Backgrounds")]
    [InlineData("backup-2026-09-08")]
    [InlineData("b&w maps")]
    public void AcceptsOrdinaryNames(string name)
    {
        Assert.True(PackNameValidator.IsValid(name, out var error));
        Assert.Equal("", error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("a?b")]
    [InlineData("name.")]
    [InlineData(" lead")]
    public void RejectsBadNames(string? name)
    {
        Assert.False(PackNameValidator.IsValid(name, out var error));
        Assert.NotEmpty(error);
    }
}
