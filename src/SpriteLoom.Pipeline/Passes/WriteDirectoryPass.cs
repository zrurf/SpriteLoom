using SpriteLoom.Abstractions;
using SpriteLoom.Bundle;
using SpriteLoom.Security;

namespace SpriteLoom.Pipeline.Passes;

public sealed class WriteDirectoryPass : IPass
{
    public PassDescriptor Descriptor { get; } = new(
        "package.directory",
        "1.0",
        [new PortSpec("in", ArtifactKind.Manifest)],
        [new PortSpec("out", ArtifactKind.Package)],
        PassDeterminism.Deterministic)
    {
        Cacheable = false
    };

    public ValueTask<IReadOnlyDictionary<string, Artifact>> ExecuteAsync(
        PassContext context,
        IReadOnlyDictionary<string, Artifact> inputs)
    {
        var package = (BuildPackage)inputs["in"].Payload;
        var outputPath = PassParameters.GetString(context.Parameters, "outputPath");
        var outputKind = PassParameters.GetString(context.Parameters, "outputKind", "directory");
        var signingKeyPath = PassParameters.GetString(context.Parameters, "signingKeyPath", string.Empty);
        var encryptionAlgorithm = PassParameters.GetString(context.Parameters, "encryptionAlgorithm", string.Empty);
        var encryptionKeyPath = PassParameters.GetString(context.Parameters, "encryptionKeyPath", string.Empty);
        var encryptionChunkSize = PassParameters.GetInt(
            context.Parameters,
            "encryptionChunkSize",
            AeadChunker.DefaultChunkSize);
        var target = Path.GetFullPath(Path.Combine(context.BaseDirectory, outputPath));
        var signer = LoadSigner(context.BaseDirectory, signingKeyPath);
        var bundle = string.Equals(outputKind, "bundle", StringComparison.OrdinalIgnoreCase);
        SymmetricKey? encryptionKey = null;
        SymmetricAlgorithmKind encryptionKind = SymmetricAlgorithmKind.Aes256Gcm;

        if (!string.IsNullOrWhiteSpace(encryptionAlgorithm))
        {
            encryptionKind = SymmetricAlgorithms.Parse(encryptionAlgorithm);
            var keyPath = Path.GetFullPath(Path.Combine(context.BaseDirectory, encryptionKeyPath));
            encryptionKey = SymmetricKey.Load(keyPath);
        }
        var parent = Path.GetDirectoryName(target) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(parent);

        var staging = Path.Combine(parent, $".sloom-staging-{Guid.NewGuid():N}");

        try
        {
            if (bundle)
            {
                WriteBundle(package, staging, signer, encryptionKey, encryptionKind, encryptionChunkSize);
            }
            else
            {
                WriteDirectory(
                    package,
                    staging,
                    signer,
                    encryptionKey,
                    encryptionKind,
                    encryptionChunkSize,
                    context.Cancellation);
            }

            context.Cancellation.ThrowIfCancellationRequested();
            Replace(target, staging, bundle);
        }
        catch
        {
            TryDelete(staging);
            throw;
        }

        return ValueTask.FromResult<IReadOnlyDictionary<string, Artifact>>(
            new Dictionary<string, Artifact>(StringComparer.Ordinal)
            {
                ["out"] = new Artifact(ArtifactKind.Package, 0, new PackageOutput(target))
            });
    }

    private static void WriteBundle(
        BuildPackage package,
        string target,
        ISigner? signer,
        SymmetricKey? encryptionKey,
        SymmetricAlgorithmKind encryptionKind,
        int encryptionChunkSize)
    {
        var files = package.Files
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new SlbFile(pair.Key, SlbKinds.Infer(pair.Key), pair.Value))
            .ToArray();

        SlbWriter.WriteFile(files, target, new SlbWriteOptions
        {
            Signer = signer,
            EncryptionKey = encryptionKey,
            EncryptionAlgorithm = encryptionKind,
            EncryptionChunkSize = encryptionChunkSize
        });
    }

    private static void WriteDirectory(
        BuildPackage package,
        string target,
        ISigner? signer,
        SymmetricKey? encryptionKey,
        SymmetricAlgorithmKind encryptionKind,
        int encryptionChunkSize,
        CancellationToken cancellation)
    {
        Directory.CreateDirectory(target);

        foreach (var (relativePath, bytes) in package.Files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            cancellation.ThrowIfCancellationRequested();
            var storedPath = relativePath;
            var storedBytes = bytes;

            if (encryptionKey is not null)
            {
                storedPath += SencFile.Extension;
                storedBytes = SencFile.Encrypt(
                    bytes,
                    encryptionKey,
                    encryptionKind,
                    encryptionChunkSize,
                    DeriveSencPrefix(encryptionKey, relativePath, bytes));
            }

            var path = Path.Combine(target, storedPath.Replace('/', Path.DirectorySeparatorChar));
            var directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(path, storedBytes);
        }

        if (signer is not null)
        {
            DirectorySigner.Sign(target, signer);
        }
    }

    private static byte[] DeriveSencPrefix(SymmetricKey key, string path, byte[] content)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256);
        hash.AppendData(key.Bytes.Span);
        hash.AppendData(System.Text.Encoding.UTF8.GetBytes(path));
        hash.AppendData(content);
        return hash.GetHashAndReset()[..SencFile.NoncePrefixStorage];
    }

    private static void Replace(string target, string staging, bool bundle)
    {
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }
        else if (File.Exists(target))
        {
            File.Delete(target);
        }

        if (bundle)
        {
            File.Move(staging, target);
        }
        else
        {
            Directory.Move(staging, target);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
    }

    private static ISigner? LoadSigner(string baseDirectory, string keyPath)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, keyPath));
        return Ed25519Keys.CreateSigner(Ed25519Keys.LoadPrivate(fullPath));
    }
}
