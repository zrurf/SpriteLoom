using SpriteLoom.Foundation;

namespace SpriteLoom.Foundation.Tests;

public sealed class NaturalStringComparerTests
{
    [Fact]
    public void Sorts_numeric_runs_by_value()
    {
        var values = new[] { "a10", "a2", "a1" };
        Array.Sort(values, NaturalStringComparer.Instance);

        Assert.Equal(new[] { "a1", "a2", "a10" }, values);
    }

    [Fact]
    public void Orders_more_leading_zeros_first_when_values_match()
    {
        var values = new[] { "f01", "f001" };
        Array.Sort(values, NaturalStringComparer.Instance);

        Assert.Equal(new[] { "f001", "f01" }, values);
    }

    [Fact]
    public void Handles_unicode_prefixes()
    {
        Assert.True(NaturalStringComparer.Instance.Compare("\u89D2\u82722", "\u89D2\u827210") < 0);
    }

    [Fact]
    public void Falls_back_to_ordinal_for_equal_tokens()
    {
        Assert.Equal(0, NaturalStringComparer.Instance.Compare("run01", "run01"));
        Assert.NotEqual(0, NaturalStringComparer.Instance.Compare("run01", "run02"));
    }
}
