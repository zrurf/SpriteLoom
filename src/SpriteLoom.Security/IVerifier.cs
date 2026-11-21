namespace SpriteLoom.Security;

public interface IVerifier
{
    string KeyId { get; }

    string Algorithm { get; }

    byte[] PublicKey { get; }

    bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature);
}
