using TransLink.Lite.Application.Common.Normalization;

namespace TransLink.Lite.UnitTests.Common;

public sealed class EmailNormalizerTests
{
    [Fact]
    public void Normalize_WithOuterWhitespace_TrimsWhitespace()
    {
        var result = EmailNormalizer.Normalize("  User@Example.com  ");

        Assert.Equal("user@example.com", result);
    }

    [Fact]
    public void Normalize_WithUppercaseCharacters_LowercasesInvariantly()
    {
        var result = EmailNormalizer.Normalize("USER@EXAMPLE.COM");

        Assert.Equal("user@example.com", result);
    }

    [Fact]
    public void Normalize_WithPlusTag_PreservesPlusTag()
    {
        var result = EmailNormalizer.Normalize("User+Alerts@Example.com");

        Assert.Equal("user+alerts@example.com", result);
    }

    [Fact]
    public void Normalize_WithProviderSpecificDots_DoesNotRemoveDots()
    {
        var result = EmailNormalizer.Normalize("First.Last@Example.com");

        Assert.Equal("first.last@example.com", result);
    }
}
