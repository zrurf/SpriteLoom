namespace SpriteLoom.Bundle;

public static class SlbFormat
{
    public static ReadOnlySpan<byte> Magic => [0x53, 0x4C, 0x42, 0x1A];

    public const int HeaderSize = 64;

    public const int BlockEntrySize = 64;

    public const int FooterSize = 64;

    public const int FormatMajor = 1;

    public const int FormatMinor = 0;

    public const int PayloadAlignment = 64;
}
