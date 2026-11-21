using BcEd25519Signer = Org.BouncyCastle.Crypto.Signers.Ed25519Signer;
using Org.BouncyCastle.Crypto.Parameters;

namespace SpriteLoom.Security;

public sealed class Ed25519Signer : ISigner
{
    private readonly Ed25519PrivateKeyParameters _privateKey;
    private readonly Ed25519PublicKeyParameters _publicKey;

    public Ed25519Signer(
        string keyId,
        Ed25519PrivateKeyParameters privateKey,
        Ed25519PublicKeyParameters publicKey)
    {
        KeyId = keyId;
        _privateKey = privateKey;
        _publicKey = publicKey;
    }

    public string KeyId { get; }

    public string Algorithm => "ed25519";

    public byte[] PublicKey => _publicKey.GetEncoded();

    public byte[] Sign(ReadOnlySpan<byte> data)
    {
        var signer = new BcEd25519Signer();
        signer.Init(true, _privateKey);
        signer.BlockUpdate(data);
        return signer.GenerateSignature();
    }
}
