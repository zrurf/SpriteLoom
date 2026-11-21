namespace SpriteLoom.Security;

public interface ISigner
{
    string KeyId { get; }

    string Algorithm { get; }

    byte[] PublicKey { get; }

    byte[] Sign(ReadOnlySpan<byte> data);
}
