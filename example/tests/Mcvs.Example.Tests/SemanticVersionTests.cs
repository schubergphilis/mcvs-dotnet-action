namespace Mcvs.Example.Tests;

/// <summary>
/// The unit tests: every test without a category is run by the 'unit' testing
/// type.
/// </summary>
public class SemanticVersionTests
{
    [Theory]
    [InlineData("1.2.3")]
    [InlineData("v1.2.3")]
    [InlineData(" v1.2.3 ")]
    public void ParseAcceptsATagWithAndWithoutThePrefix(string value)
    {
        var version = SemanticVersion.Parse(value);

        Assert.Equal(1, version.Major);
        Assert.Equal(2, version.Minor);
        Assert.Equal(3, version.Patch);
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.x")]
    [InlineData("1.2.-3")]
    [InlineData("")]
    public void ParseRejectsAValueThatIsNotASemanticVersion(string value)
    {
        var exception = Assert.Throws<FormatException>(() => SemanticVersion.Parse(value));

        Assert.Contains($"'{value}'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseRejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => SemanticVersion.Parse(null!));
    }

    [Theory]
    [InlineData("1.2.3", "2.0.0", true)]
    [InlineData("1.2.3", "1.3.0", true)]
    [InlineData("1.2.3", "1.2.4", true)]
    [InlineData("1.2.3", "1.2.3", false)]
    [InlineData("1.2.4", "1.2.3", false)]
    [InlineData("1.3.0", "1.2.3", false)]
    [InlineData("2.0.0", "1.2.3", false)]
    public void IsOlderThanComparesEveryPart(string left, string right, bool expected)
    {
        var older = SemanticVersion.Parse(left).IsOlderThan(SemanticVersion.Parse(right));

        Assert.Equal(expected, older);
    }

    [Theory]
    [InlineData("1.2.3", "2.0.0", -1)]
    [InlineData("1.2.3", "1.3.0", -1)]
    [InlineData("1.2.3", "1.2.4", -1)]
    [InlineData("1.2.3", "1.2.3", 0)]
    [InlineData("2.0.0", "1.2.3", 1)]
    public void CompareToOrdersOnEveryPart(string left, string right, int expected)
    {
        var comparison = SemanticVersion.Parse(left).CompareTo(SemanticVersion.Parse(right));

        Assert.Equal(expected, Math.Sign(comparison));
    }

    [Fact]
    public void CompareToRejectsNull()
    {
        var version = SemanticVersion.Parse("1.2.3");

        Assert.Throws<ArgumentNullException>(() => version.CompareTo(null!));
    }

    [Fact]
    public void IsOlderThanRejectsNull()
    {
        var version = SemanticVersion.Parse("1.2.3");

        Assert.Throws<ArgumentNullException>(() => version.IsOlderThan(null!));
    }

    [Fact]
    public void ToStringOmitsThePrefix()
    {
        Assert.Equal("1.2.3", SemanticVersion.Parse("v1.2.3").ToString());
    }
}
