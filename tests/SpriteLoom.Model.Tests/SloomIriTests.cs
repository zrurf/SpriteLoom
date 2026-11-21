using SpriteLoom.Model.Iri;

namespace SpriteLoom.Model.Tests;

public sealed class SloomIriTests
{
    [Fact]
    public void Parse_exposes_kind_package_and_path()
    {
        var iri = SloomIri.Parse("frame://heroes/run/0001");

        Assert.Equal(SloomIriKind.Frame, iri.Kind);
        Assert.Equal("heroes", iri.PackageId);
        Assert.Equal("run/0001", iri.Path);
        Assert.Equal("frame://heroes/run/0001", iri.Value);
    }

    [Fact]
    public void Package_root_has_empty_path()
    {
        var iri = SloomIri.Parse("pkg://heroes");

        Assert.Equal(SloomIriKind.Package, iri.Kind);
        Assert.Equal(string.Empty, iri.Path);
    }

    [Fact]
    public void Create_builds_canonical_iri()
    {
        var iri = SloomIri.Create(SloomIriKind.Blob, "heroes", "licenses/LICENSE.txt");

        Assert.Equal("blob://heroes/licenses/LICENSE.txt", iri.Value);
    }

    [Fact]
    public void Equality_uses_canonical_value()
    {
        var left = SloomIri.Parse("frame://heroes/run");
        var right = SloomIri.Create(SloomIriKind.Frame, "HEROES", "run");

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
    }

    [Theory]
    [InlineData("FRAME://Heroes/Run", "frame://heroes/Run")]
    [InlineData("frame://heroes/a%31", "frame://heroes/a1")]
    [InlineData("blob://heroes/a%2fb.txt", "blob://heroes/a%2Fb.txt")]
    [InlineData("frame://heroes/\u89D2\u8272/\u5F85\u673A", "frame://heroes/\u89D2\u8272/\u5F85\u673A")]
    [InlineData("frame://heroes/e\u0301", "frame://heroes/\u00E9")]
    public void Normalize_produces_canonical_form(string input, string expected)
    {
        Assert.Equal(expected, SloomIri.Normalize(input));
    }

    [Theory]
    [InlineData("frame://heroes/a?b", IriDiagnostics.ForbiddenCharacter)]
    [InlineData("frame://heroes/a#b", IriDiagnostics.ForbiddenCharacter)]
    [InlineData("frame://heroes/a b", IriDiagnostics.ForbiddenCharacter)]
    [InlineData("frame://heroes/a/", IriDiagnostics.InvalidPath)]
    [InlineData("frame://heroes/a//b", IriDiagnostics.InvalidPath)]
    [InlineData("frame://heroes/../a", IriDiagnostics.InvalidPath)]
    [InlineData("pkg://heroes/a", IriDiagnostics.InvalidPath)]
    [InlineData("sloom://heroes/a", IriDiagnostics.UnknownScheme)]
    public void TryNormalize_rejects_invalid_input(string input, string expectedCode)
    {
        Assert.False(SloomIri.TryNormalize(input, out _, out var diagnostic));
        Assert.NotNull(diagnostic);
        Assert.Equal(expectedCode, diagnostic!.Code);
    }
}
