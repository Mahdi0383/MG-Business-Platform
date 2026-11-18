using Mg.Platform.Api.Contracts;
using Xunit;

namespace Mg.Platform.Api.Tests;

public sealed class BusinessSlugTests
{
    [Theory]
    [InlineData("shop", "shop")]
    [InlineData("MG-Cafe-01", "mg-cafe-01")]
    [InlineData("  branch-9  ", "branch-9")]
    public void AcceptsAndNormalizesValidSlugs(string input, string expected)
    {
        Assert.True(BusinessSlug.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("two words")]
    [InlineData("with_underscore")]
    [InlineData("ümlaut")]
    [InlineData("a/b")]
    public void RejectsInvalidSlugs(string input)
    {
        Assert.False(BusinessSlug.TryNormalize(input, out _));
    }

    [Fact]
    public void RejectsSlugsLongerThanEightyCharacters()
    {
        Assert.False(BusinessSlug.TryNormalize(new string('a', 81), out _));
    }
}
