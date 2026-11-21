using SpriteLoom.Model.Iri;

namespace SpriteLoom.Model.Tests;

public sealed class SloomIriVectorTests
{
    public static IEnumerable<object[]> NormalizeCases() =>
        IriVectors.ReadAll()
            .Where(vector => vector.Kind == "iri.normalize")
            .Select(vector => new object[] { vector.Name, vector.Input, vector.ExpectedIri! });

    public static IEnumerable<object[]> RejectCases() =>
        IriVectors.ReadAll()
            .Where(vector => vector.Kind == "iri.reject")
            .Select(vector => new object[] { vector.Name, vector.Input, vector.ExpectedCode! });

    [Theory]
    [MemberData(nameof(NormalizeCases))]
    public void Normalize_matches_vector(string name, string input, string expected)
    {
        var actual = SloomIri.Normalize(input);
        Assert.True(expected == actual, $"{name}: expected '{expected}', got '{actual}'.");

        Assert.Equal(expected, SloomIri.Parse(expected).Value);
        Assert.Equal(expected, SloomIri.Normalize(expected));
    }

    [Theory]
    [MemberData(nameof(RejectCases))]
    public void Reject_matches_vector(string name, string input, string expectedCode)
    {
        var accepted = SloomIri.TryNormalize(input, out _, out var diagnostic);

        Assert.False(accepted, $"{name}: expected rejection.");
        Assert.NotNull(diagnostic);
        Assert.Equal(expectedCode, diagnostic!.Code);
    }
}
