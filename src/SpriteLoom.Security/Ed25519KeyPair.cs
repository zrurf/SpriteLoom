using Org.BouncyCastle.Crypto.Parameters;

namespace SpriteLoom.Security;

public sealed class Ed25519KeyPair
{
    public required Ed25519PrivateKeyParameters PrivateKey { get; init; }

    public required Ed25519PublicKeyParameters PublicKey { get; init; }

    public string KeyId =>
        HashAlgorithms.ToHex(HashAlgorithms.Compute(HashAlgorithmKind.XxHash64, PublicKey.GetEncoded()));
}
