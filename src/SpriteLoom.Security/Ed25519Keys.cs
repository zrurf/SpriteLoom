using System.Text;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;

namespace SpriteLoom.Security;

public static class Ed25519Keys
{
    public static Ed25519KeyPair Generate()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var pair = generator.GenerateKeyPair();

        return new Ed25519KeyPair
        {
            PrivateKey = (Ed25519PrivateKeyParameters)pair.Private,
            PublicKey = (Ed25519PublicKeyParameters)pair.Public
        };
    }

    public static void SavePrivate(Ed25519PrivateKeyParameters key, string path)
    {
        var writer = new StringWriter();
        var pemWriter = new PemWriter(writer);
        pemWriter.WriteObject(key);
        pemWriter.Writer.Flush();
        File.WriteAllText(path, writer.ToString(), new UTF8Encoding(false));
    }

    public static void SavePublic(Ed25519PublicKeyParameters key, string path)
    {
        var writer = new StringWriter();
        var pemWriter = new PemWriter(writer);
        pemWriter.WriteObject(key);
        pemWriter.Writer.Flush();
        File.WriteAllText(path, writer.ToString(), new UTF8Encoding(false));
    }

    public static Ed25519PrivateKeyParameters LoadPrivate(string path)
    {
        using var reader = new StringReader(File.ReadAllText(path));
        var pemReader = new PemReader(reader);
        var value = pemReader.ReadObject();

        return value as Ed25519PrivateKeyParameters
            ?? throw new InvalidDataException($"File is not an Ed25519 private key: {path}");
    }

    public static Ed25519PublicKeyParameters LoadPublic(string path)
    {
        using var reader = new StringReader(File.ReadAllText(path));
        var pemReader = new PemReader(reader);
        var value = pemReader.ReadObject();

        return value as Ed25519PublicKeyParameters
            ?? throw new InvalidDataException($"File is not an Ed25519 public key: {path}");
    }

    public static Ed25519Signer CreateSigner(Ed25519PrivateKeyParameters privateKey)
    {
        var publicKey = privateKey.GeneratePublicKey();
        var keyId = HashAlgorithms.ToHex(
            HashAlgorithms.Compute(HashAlgorithmKind.XxHash64, publicKey.GetEncoded()));

        return new Ed25519Signer(keyId, privateKey, publicKey);
    }

    public static Ed25519Verifier CreateVerifier(Ed25519PublicKeyParameters publicKey)
    {
        var keyId = HashAlgorithms.ToHex(
            HashAlgorithms.Compute(HashAlgorithmKind.XxHash64, publicKey.GetEncoded()));

        return new Ed25519Verifier(keyId, publicKey);
    }
}
