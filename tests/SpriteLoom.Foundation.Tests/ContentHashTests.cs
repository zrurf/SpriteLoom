using SpriteLoom.Foundation;

namespace SpriteLoom.Foundation.Tests;

public sealed class ContentHashTests
{
    [Fact]
    public void Equal_content_produces_equal_hash()
    {
        var data = new byte[] { 1, 2, 3, 4 };
        Assert.Equal(ContentHash.Compute(data), ContentHash.Compute(data));
    }

    [Fact]
    public void Different_content_produces_different_hash()
    {
        Assert.NotEqual(
            ContentHash.Compute(new byte[] { 1, 2, 3 }),
            ContentHash.Compute(new byte[] { 1, 2, 4 }));
    }

    [Fact]
    public void Hex_form_is_32_lowercase_characters()
    {
        var hex = ContentHash.Compute(new byte[] { 0 }).ToHex();

        Assert.Equal(32, hex.Length);
        Assert.All(hex, character => Assert.True(character is >= '0' and <= '9' or >= 'a' and <= 'f'));
    }
}
