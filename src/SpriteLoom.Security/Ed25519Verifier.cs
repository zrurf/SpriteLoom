using BcEd25519Signer = Org.BouncyCastle.Crypto.Signers.Ed25519Signer;
using Org.BouncyCastle.Crypto.Parameters;

namespace SpriteLoom.Security;

public sealed class Ed25519Verifier : IVerifier
{
    private readonly Ed25519PublicKeyParameters _publicKey;

    public Ed25519Verifier(string keyId, Ed25519PublicKeyParameters publicKey)
    {
        KeyId = keyId;
        _publicKey = publicKey;
    }

    public string KeyId { get; }

    public string Algorithm => "ed25519";

    public byte[] PublicKey => _publicKey.GetEncoded();

    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        var verifier = new BcEd25519Signer();
        verifier.Init(false, _publicKey);
        verifier.BlockUpdate(data);
        return verifier.VerifySignature(signature.ToArray());
    }
}
