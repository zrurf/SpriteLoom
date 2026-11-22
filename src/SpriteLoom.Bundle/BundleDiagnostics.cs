namespace SpriteLoom.Bundle;

public static class BundleDiagnostics
{
    public const string MissingSignature = "SL5001";
    public const string MissingSigner = "SL5002";
    public const string SignatureInvalid = "SL5003";
    public const string BlockTableDigestMismatch = "SL5004";
    public const string IndexDigestMismatch = "SL5005";
    public const string MerkleRootMismatch = "SL5006";
    public const string MissingSignedFile = "SL5007";
    public const string SignedFileSizeMismatch = "SL5008";
    public const string SignedFileDigestMismatch = "SL5009";
    public const string UnsignedFilePresent = "SL5010";
    public const string SignatureFileMissing = "SL5011";

    public const string FileTruncated = "SL5020";
    public const string UnsupportedVersion = "SL5021";
    public const string KeyMissing = "SL5022";
    public const string HeaderCrcMismatch = "SL5023";
    public const string FileSizeMismatch = "SL5024";
    public const string FooterMismatch = "SL5025";
    public const string BlockTableOutsideFile = "SL5026";
    public const string BlockIdsNotContiguous = "SL5027";
    public const string IndexOutsideFile = "SL5028";
    public const string IndexCrcMismatch = "SL5029";
    public const string MissingBlock = "SL5030";
    public const string BlockOutsideFile = "SL5031";
    public const string FooterTruncated = "SL5032";
    public const string FooterMagicMismatch = "SL5033";
    public const string FooterCrcMismatch = "SL5034";
    public const string DigestMismatch = "SL5035";
    public const string InvalidIndexEntry = "SL5036";
    public const string UnknownBlockKind = "SL5037";
    public const string HeaderTruncated = "SL5038";
    public const string HeaderMagicMismatch = "SL5039";
    public const string BlockEntryTruncated = "SL5040";
    public const string UnsupportedEncryption = "SL5041";
    public const string InvalidCryptoEntry = "SL5042";
    public const string CryptoKeyMismatch = "SL5043";
    public const string InvalidHeaderSize = "SL5044";
    public const string EncryptionFailed = "SL5045";

    public static InvalidDataException Error(string code, string message) =>
        new($"{code}: {message}");
}
