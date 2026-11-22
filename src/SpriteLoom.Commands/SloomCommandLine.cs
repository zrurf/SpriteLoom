using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using SpriteLoom.Bundle;
using SpriteLoom.Codecs;
using SpriteLoom.Foundation;
using SpriteLoom.Hosting;
using SpriteLoom.Model.Manifest;
using SpriteLoom.Model.Serialization;
using SpriteLoom.Security;
using SpriteLoom.Textures;

namespace SpriteLoom.Commands;

public static class SloomCommandLine
{
    private const string Version = "0.1.0";

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Length == 1 && args[0] is "--version" or "-v")
        {
            output.WriteLine($"sloom {Version}");
            return 0;
        }

        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintHelp(output);
            return 0;
        }

        var command = args[0];
        var options = ParseOptions(args.AsSpan(1));

        try
        {
            return command switch
            {
                "init" => RunInit(options, output),
                "pack" => await RunPackAsync(options, output, error, cancellation),
                "validate" => RunValidate(options, output),
                "inspect" => RunInspect(options, output),
                "keygen" => RunKeygen(options, output),
                "encrypt" => RunEncrypt(options, output),
                "decrypt" => RunDecrypt(options, output),
                "sign" => RunSign(options, output),
                "verify" => RunVerify(options, output),
                "diff" => RunDiff(options, output),
                "formats" => RunFormats(options, output),
                _ => UnknownCommand(command, error)
            };
        }
        catch (Exception exception)
        {
            error.WriteLine($"error: {exception.Message}");
            return 4;
        }
    }

    private static async Task<int> RunPackAsync(
        CommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellation)
    {
        if (options.Watch)
        {
            return await RunPackWatchAsync(options, output, error, cancellation);
        }

        var application = SloomApplication.Load(ResolveConfigPath(options.ConfigPath));
        var result = await application.PackAsync(
            cancellation,
            refresh: options.Refresh,
            useCache: !options.NoCache);

        return WritePackResult(result, options.Json, output);
    }

    private static async Task<int> RunPackWatchAsync(
        CommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellation)
    {
        var configPath = Path.GetFullPath(ResolveConfigPath(options.ConfigPath));

        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                string state;

                try
                {
                    var application = SloomApplication.Load(configPath);
                    var result = await application.PackAsync(
                        cancellation,
                        refresh: options.Refresh,
                        useCache: !options.NoCache);
                    WritePackResult(result, options.Json, output);
                    state = ComputeWatchState(application.Config, configPath);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    error.WriteLine($"error: {exception.Message}");
                    state = TryComputeWatchState(configPath);
                }

                output.WriteLine("Watching for changes. Press Ctrl+C to stop.");

                while (!cancellation.IsCancellationRequested)
                {
                    await Task.Delay(500, cancellation);

                    if (TryComputeWatchState(configPath) != state)
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            return 0;
        }

        return 0;
    }

    private static int WritePackResult(PackResult result, bool json, TextWriter output)
    {
        var failed = result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        if (json)
        {
            WriteJson(output, writer =>
            {
                writer.WriteBoolean("ok", !failed);
                writer.WriteString("output", result.OutputPath);
                writer.WriteNumber("cache-hits", result.CacheHits);
                writer.WriteNumber("cache-misses", result.CacheMisses);
                WriteDiagnostics(writer, result.Diagnostics);
            });
        }
        else
        {
            output.WriteLine($"Packed {result.OutputPath}");
            WriteDiagnostics(output, result.Diagnostics);

            var cacheTotal = result.CacheHits + result.CacheMisses;

            if (cacheTotal > 0)
            {
                output.WriteLine(
                    $"Cache: {result.CacheHits} hit, {result.CacheMisses} miss " +
                    $"({(double)result.CacheHits / cacheTotal:P1})");
            }
        }

        return failed ? 4 : 0;
    }

    private static string TryComputeWatchState(string configPath)
    {
        try
        {
            var config = SpriteLoom.Pipeline.Configuration.ProjectConfigLoader.Load(configPath);
            return ComputeWatchState(config, configPath);
        }
        catch (Exception)
        {
            return ComputeWatchState(null, configPath);
        }
    }

    private static string ComputeWatchState(
        SpriteLoom.Pipeline.Configuration.ProjectConfig? config,
        string configPath)
    {
        var builder = new StringBuilder();
        builder.Append(FileState(configPath)).Append('\n');

        if (config is null)
        {
            return builder.ToString();
        }

        foreach (var source in config.Sources)
        {
            var path = Path.GetFullPath(Path.Combine(config.BaseDirectory, source.Path));

            if (File.Exists(path))
            {
                builder.Append(FileState(path)).Append('\n');
                continue;
            }

            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                         .OrderBy(value => value, StringComparer.Ordinal))
            {
                builder.Append(FileState(file)).Append('\n');
            }
        }

        return builder.ToString();
    }

    private static string FileState(string path)
    {
        var info = new FileInfo(path);
        return $"{path}|{info.Length.ToString(CultureInfo.InvariantCulture)}|" +
               $"{info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)}";
    }

    private static int RunValidate(CommandOptions options, TextWriter output)
    {
        var application = SloomApplication.Load(ResolveConfigPath(options.ConfigPath));
        var diagnostics = application.Validate();
        var failed = diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

        if (options.Json)
        {
            WriteJson(output, writer =>
            {
                writer.WriteBoolean("ok", !failed);
                WriteDiagnostics(writer, diagnostics);
            });
        }
        else
        {
            WriteDiagnostics(output, diagnostics);

            if (!failed)
            {
                output.WriteLine("Configuration is valid.");
            }
        }

        return failed ? 4 : 0;
    }

    private static int RunInspect(CommandOptions options, TextWriter output)
    {
        if (string.IsNullOrWhiteSpace(options.Path))
        {
            throw new ArgumentException("inspect requires a package directory or manifest path.");
        }

        var package = PackageFiles.Open(options.Path, LoadDecryptionKey(options));
        var format = package.HasJsonManifest ? ManifestFormat.Json : ManifestFormat.Cbor;

        using var stream = new MemoryStream(package.ManifestBytes);
        var summary = ManifestReader.ReadSummary(stream, format);

        if (options.Json)
        {
            WriteJson(output, writer =>
            {
                writer.WriteString("format", summary.Format);
                writer.WriteNumber("format-major", summary.FormatMajor);
                writer.WriteNumber("format-minor", summary.FormatMinor);
                writer.WriteNumber("uris", summary.UriCount);
                writer.WriteNumber("pages", summary.PageCount);
                writer.WriteNumber("frames", summary.FrameCount);
                writer.WriteNumber("animations", summary.AnimationCount);
            });
        }
        else
        {
            output.WriteLine($"Format: {summary.Format} {summary.FormatMajor}.{summary.FormatMinor}");
            output.WriteLine($"URIs: {summary.UriCount}");
            output.WriteLine($"Pages: {summary.PageCount}");
            output.WriteLine($"Frames: {summary.FrameCount}");
            output.WriteLine($"Animations: {summary.AnimationCount}");

            if (options.Stats)
            {
                output.WriteLine($"Package: {package.SourcePath}");

                if (package.HasJsonManifest)
                {
                    PackageFiles? baseline = null;

                    if (!string.IsNullOrWhiteSpace(options.BaselinePath))
                    {
                        baseline = PackageFiles.Open(options.BaselinePath, LoadDecryptionKey(options));
                    }

                    WriteStats(package, output, baseline);
                }
                else
                {
                    output.WriteLine("Detailed statistics require a JSON manifest.");
                }
            }
        }

        return 0;
    }

    private static PackageMetrics Measure(PackageFiles package)
    {
        var total = package.Files.Values.Sum(data => (long)data.Length);

        if (!package.HasJsonManifest)
        {
            return new PackageMetrics(total, new Dictionary<string, long>(StringComparer.Ordinal));
        }

        using var manifest = JsonDocument.Parse(package.ManifestBytes);
        var variants = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var page in manifest.RootElement.GetProperty("pages").EnumerateArray())
        {
            foreach (var variant in page.GetProperty("variants").EnumerateArray())
            {
                var file = variant.GetProperty("file").GetString()!;

                if (package.Files.TryGetValue(file, out var data))
                {
                    var id = variant.GetProperty("id").GetString()!;
                    variants[id] = variants.GetValueOrDefault(id) + data.Length;
                }
            }
        }

        return new PackageMetrics(total, variants);
    }

    private static void WriteSizeComparison(TextWriter output, string label, long current, long baseline)
    {
        var delta = current - baseline;
        var percent = baseline == 0 ? 0 : (double)delta / baseline * 100;
        output.WriteLine(
            $"{label}: {current} bytes, baseline {baseline} bytes, " +
            $"delta {delta.ToString("+0;-0;0", CultureInfo.InvariantCulture)} bytes " +
            $"({percent.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture)}%)");
    }

    private static void WriteStats(PackageFiles package, TextWriter output, PackageFiles? baseline)
    {
        using var manifest = JsonDocument.Parse(package.ManifestBytes);
        var root = manifest.RootElement;
        var records = root.GetProperty("frames").GetProperty("records");
        var recordPages = records
            .EnumerateArray()
            .Select(record => record.GetProperty("page").GetInt32())
            .ToArray();

        long usedArea = 0;
        long pageArea = 0;
        var storedCount = 0;

        foreach (var page in root.GetProperty("pages").EnumerateArray())
        {
            var containerPath = page.GetProperty("container").GetString()!;
            using var container = JsonDocument.Parse(package.Files[containerPath]);
            var size = page.GetProperty("size");
            pageArea += (long)size[0].GetInt32() * size[1].GetInt32();

            foreach (var stored in container.RootElement.GetProperty("stored").EnumerateArray())
            {
                var rect = stored.GetProperty("rect");
                usedArea += (long)rect[2].GetInt32() * rect[3].GetInt32();
                storedCount++;
            }
        }

        var frameCount = records.GetArrayLength();
        var dedupRatio = frameCount == 0 ? 0 : 1.0 - (double)storedCount / frameCount;
        var utilization = pageArea == 0 ? 0 : (double)usedArea / pageArea;

        output.WriteLine($"Stored frames: {storedCount}");
        output.WriteLine($"Dedup ratio: {dedupRatio.ToString("P1", CultureInfo.InvariantCulture)}");
        output.WriteLine($"Page utilization: {utilization.ToString("P1", CultureInfo.InvariantCulture)}");

        var variantSizes = new SortedDictionary<string, long>(StringComparer.Ordinal);
        long totalSize = 0;

        foreach (var page in root.GetProperty("pages").EnumerateArray())
        {
            foreach (var variant in page.GetProperty("variants").EnumerateArray())
            {
                var file = variant.GetProperty("file").GetString()!;

                if (!package.Files.TryGetValue(file, out var data))
                {
                    continue;
                }

                var id = variant.GetProperty("id").GetString()!;
                variantSizes[id] = variantSizes.GetValueOrDefault(id) + data.Length;
            }
        }

        foreach (var data in package.Files.Values)
        {
            totalSize += data.Length;
        }

        output.WriteLine($"Package size: {totalSize} bytes");

        foreach (var (id, size) in variantSizes)
        {
            output.WriteLine($"Variant {id}: {size} bytes");
        }

        if (baseline is not null)
        {
            var baselineMetrics = Measure(baseline);
            WriteSizeComparison(output, "Package", totalSize, baselineMetrics.Total);

            foreach (var id in variantSizes.Keys.Union(baselineMetrics.Variants.Keys).OrderBy(value => value, StringComparer.Ordinal))
            {
                WriteSizeComparison(
                    output,
                    $"Variant {id}",
                    variantSizes.GetValueOrDefault(id),
                    baselineMetrics.Variants.GetValueOrDefault(id));
            }
        }

        foreach (var animation in root.GetProperty("animations").EnumerateArray())
        {
            var animationPath = animation.GetProperty("file").GetString()!;
            using var document = JsonDocument.Parse(package.Files[animationPath]);
            var frames = document.RootElement.GetProperty("frames");
            var switches = 0;
            var previousPage = -1;

            foreach (var frame in frames.EnumerateArray())
            {
                var recordIndex = root.GetProperty("frames").GetProperty("to-record")[frame.GetInt32()].GetInt32();
                var page = recordPages[recordIndex];

                if (previousPage >= 0 && page != previousPage)
                {
                    switches++;
                }

                previousPage = page;
            }

            output.WriteLine(
                $"Animation {document.RootElement.GetProperty("uri").GetString()}: " +
                $"{frames.GetArrayLength()} frames, {switches} page switches");
        }
    }

    private static int RunKeygen(CommandOptions options, TextWriter output)
    {
        if (options.Algorithm is not null)
        {
            if (options.Algorithm is not ("aes-256" or "xchacha20" or "symmetric"))
            {
                throw new ArgumentException($"Unsupported key algorithm: {options.Algorithm}");
            }

            if (string.IsNullOrWhiteSpace(options.OutPath))
            {
                throw new ArgumentException("Symmetric keygen requires --out <key-file>.");
            }

            var symmetricKey = SymmetricKey.Generate();
            var keyPath = Path.GetFullPath(options.OutPath);
            symmetricKey.Save(keyPath);
            output.WriteLine($"Symmetric key: {keyPath}");
            output.WriteLine($"Key id: {symmetricKey.KeyId:X4}");
            return 0;
        }

        if (string.IsNullOrWhiteSpace(options.Path))
        {
            throw new ArgumentException("keygen requires an output prefix.");
        }

        var prefix = Path.GetFullPath(options.Path);
        var directory = Path.GetDirectoryName(prefix);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var privatePath = prefix + ".private.pem";
        var publicPath = prefix + ".public.pem";

        if (File.Exists(privatePath) || File.Exists(publicPath))
        {
            throw new InvalidOperationException($"Key files already exist for prefix: {prefix}");
        }

        var keyPair = Ed25519Keys.Generate();
        Ed25519Keys.SavePrivate(keyPair.PrivateKey, privatePath);
        Ed25519Keys.SavePublic(keyPair.PublicKey, publicPath);

        output.WriteLine($"Private key: {privatePath}");
        output.WriteLine($"Public key: {publicPath}");
        output.WriteLine($"Key id: {keyPair.KeyId}");
        return 0;
    }

    private static int RunEncrypt(CommandOptions options, TextWriter output)
    {
        if (string.IsNullOrWhiteSpace(options.Path))
        {
            throw new ArgumentException("encrypt requires an input file or directory.");
        }

        if (string.IsNullOrWhiteSpace(options.KeyPath))
        {
            throw new ArgumentException("encrypt requires --key <symmetric-key>.");
        }

        var key = SymmetricKey.Load(Path.GetFullPath(options.KeyPath));
        var algorithm = SymmetricAlgorithms.Parse(options.Algorithm ?? "aes-256-gcm");
        var chunkSize = options.ChunkSize ?? AeadChunker.DefaultChunkSize;
        var input = Path.GetFullPath(options.Path);

        if (File.Exists(input))
        {
            var outputPath = ResolveEncryptFileOutput(input, options.OutPath);
            EncryptFile(input, outputPath, key, algorithm, chunkSize);
            output.WriteLine($"Encrypted {input} -> {outputPath}");
            return 0;
        }

        if (!Directory.Exists(input))
        {
            throw new FileNotFoundException("Input path not found.", input);
        }

        var destination = Path.GetFullPath(options.OutPath ?? input + ".encrypted");
        var inputRoot = Path.TrimEndingDirectorySeparator(input);
        var destinationRoot = Path.TrimEndingDirectorySeparator(destination);

        if (string.Equals(inputRoot, destinationRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Encryption output must differ from the input directory.");
        }

        var count = 0;

        foreach (var file in Directory.EnumerateFiles(inputRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(inputRoot, file);

            if (IsUnder(file, destinationRoot))
            {
                continue;
            }

            var target = Path.Combine(destinationRoot, relative + SencFile.Extension);
            EncryptFile(file, target, key, algorithm, chunkSize);
            count++;
        }

        output.WriteLine($"Encrypted {count} files -> {destinationRoot}");
        return 0;
    }

    private static int RunDecrypt(CommandOptions options, TextWriter output)
    {
        if (string.IsNullOrWhiteSpace(options.Path))
        {
            throw new ArgumentException("decrypt requires an input file or directory.");
        }

        if (string.IsNullOrWhiteSpace(options.KeyPath))
        {
            throw new ArgumentException("decrypt requires --key <symmetric-key>.");
        }

        var key = SymmetricKey.Load(Path.GetFullPath(options.KeyPath));
        var input = Path.GetFullPath(options.Path);

        if (File.Exists(input))
        {
            var outputPath = ResolveDecryptFileOutput(input, options.OutPath);
            DecryptFile(input, outputPath, key);
            output.WriteLine($"Decrypted {input} -> {outputPath}");
            return 0;
        }

        if (!Directory.Exists(input))
        {
            throw new FileNotFoundException("Input path not found.", input);
        }

        var destination = Path.GetFullPath(options.OutPath ?? Path.TrimEndingDirectorySeparator(input) + ".decrypted");
        var inputRoot = Path.TrimEndingDirectorySeparator(input);
        var destinationRoot = Path.TrimEndingDirectorySeparator(destination);
        var count = 0;

        foreach (var file in Directory.EnumerateFiles(inputRoot, "*" + SencFile.Extension, SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(inputRoot, file);
            var target = Path.Combine(destinationRoot, relative[..^SencFile.Extension.Length]);
            DecryptFile(file, target, key);
            count++;
        }

        if (count == 0)
        {
            throw new InvalidDataException("No .senc files found.");
        }

        output.WriteLine($"Decrypted {count} files -> {destinationRoot}");
        return 0;
    }

    private static void EncryptFile(
        string input,
        string output,
        SymmetricKey key,
        SymmetricAlgorithmKind algorithm,
        int chunkSize)
    {
        var encrypted = SencFile.Encrypt(File.ReadAllBytes(input), key, algorithm, chunkSize);
        WriteFile(output, encrypted);
    }

    private static void DecryptFile(string input, string output, SymmetricKey key)
    {
        var decrypted = SencFile.Decrypt(File.ReadAllBytes(input), key);
        WriteFile(output, decrypted);
    }

    private static void WriteFile(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(path, bytes);
    }

    private static string ResolveEncryptFileOutput(string input, string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return input + SencFile.Extension;
        }

        var output = Path.GetFullPath(requested);

        if (Directory.Exists(output))
        {
            return Path.Combine(output, Path.GetFileName(input) + SencFile.Extension);
        }

        return output;
    }

    private static string ResolveDecryptFileOutput(string input, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return Path.GetFullPath(requested);
        }

        if (input.EndsWith(SencFile.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return input[..^SencFile.Extension.Length];
        }

        return input + ".decrypted";
    }

    private static bool IsUnder(string path, string root) =>
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static int RunSign(CommandOptions options, TextWriter output)
    {
        if (string.IsNullOrWhiteSpace(options.Path))
        {
            throw new ArgumentException("sign requires a package directory.");
        }

        if (string.IsNullOrWhiteSpace(options.KeyPath))
        {
            throw new ArgumentException("sign requires --key <private.pem>.");
        }

        var target = Path.GetFullPath(options.Path);

        if (!Directory.Exists(target))
        {
            throw new ArgumentException("sign currently supports directory packages only.");
        }

        var signer = Ed25519Keys.CreateSigner(
            Ed25519Keys.LoadPrivate(Path.GetFullPath(options.KeyPath)));
        var signaturePath = DirectorySigner.Sign(target, signer);

        output.WriteLine($"Signed {target}");
        output.WriteLine($"Signature: {signaturePath}");
        return 0;
    }

    private static int RunVerify(CommandOptions options, TextWriter output)
    {
        if (string.IsNullOrWhiteSpace(options.Path))
        {
            throw new ArgumentException("verify requires a package directory, manifest, or bundle.");
        }

        if (string.IsNullOrWhiteSpace(options.KeyPath))
        {
            throw new ArgumentException("verify requires --key <public.pem>.");
        }

        var target = Path.GetFullPath(options.Path);
        var verifier = Ed25519Keys.CreateVerifier(
            Ed25519Keys.LoadPublic(Path.GetFullPath(options.KeyPath)));
        var errors = new List<string>();
        var warnings = new List<string>();

        try
        {
            if (File.Exists(target) && target.EndsWith(".slb", StringComparison.OrdinalIgnoreCase))
            {
                var archive = SlbReader.ReadFile(target, decryptionKey: LoadDecryptionKey(options));
                errors.AddRange(SlbVerifier.Verify(archive, verifier));
            }
            else
            {
                var result = DirectoryVerifier.Verify(
                    target,
                    verifier,
                    new VerificationOptions
                    {
                        Level = options.Permissive
                            ? VerificationLevel.Permissive
                            : VerificationLevel.Strict,
                        AllowedExtraPaths = options.AllowedPaths
                    });
                errors.AddRange(result.Errors);
                warnings.AddRange(result.Warnings);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException)
        {
            errors.Add(exception.Message);
        }

        if (options.Json)
        {
            WriteJson(output, writer =>
            {
                writer.WriteBoolean("ok", errors.Count == 0);
                WriteStringArray(writer, "errors", errors);
                WriteStringArray(writer, "warnings", warnings);
            });
        }
        else
        {
            foreach (var message in errors)
            {
                output.WriteLine($"error: {message}");
            }

            foreach (var message in warnings)
            {
                output.WriteLine($"warning: {message}");
            }

            output.WriteLine(errors.Count == 0 ? "Signature is valid." : "Signature verification failed.");
        }

        return errors.Count == 0 ? 0 : 4;
    }

    private static SymmetricKey? LoadDecryptionKey(CommandOptions options) =>
        string.IsNullOrWhiteSpace(options.DecryptionKeyPath)
            ? null
            : SymmetricKey.Load(Path.GetFullPath(options.DecryptionKeyPath));

    private static int RunDiff(CommandOptions options, TextWriter output)
    {
        if (options.Paths.Count < 2)
        {
            throw new ArgumentException("diff requires two package paths.");
        }

        var decryptionKey = LoadDecryptionKey(options);
        var left = PackageFiles.Open(options.Paths[0], decryptionKey);
        var right = PackageFiles.Open(options.Paths[1], decryptionKey);
        var result = PackageDiff.Compare(left, right);

        if (options.Json)
        {
            WriteJson(output, writer =>
            {
                WriteStringArray(writer, "added", result.Added);
                WriteStringArray(writer, "removed", result.Removed);
                WriteStringArray(writer, "changed", result.Changed);
            });
        }
        else
        {
            output.WriteLine($"Added: {result.Added.Count}");
            foreach (var uri in result.Added)
            {
                output.WriteLine($"  + {uri}");
            }

            output.WriteLine($"Removed: {result.Removed.Count}");
            foreach (var uri in result.Removed)
            {
                output.WriteLine($"  - {uri}");
            }

            output.WriteLine($"Changed: {result.Changed.Count}");
            foreach (var uri in result.Changed)
            {
                output.WriteLine($"  ~ {uri}");
            }
        }

        return 0;
    }

    private static void WriteStringArray(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        writer.WriteStartArray(name);

        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static string ResolveConfigPath(string? path)
    {
        var value = string.IsNullOrWhiteSpace(path) ? "sloom.toml" : path;

        if (Directory.Exists(value))
        {
            var config = Path.Combine(value, "sloom.toml");

            if (!File.Exists(config))
            {
                throw new FileNotFoundException("Configuration file not found in project directory.", config);
            }

            return config;
        }

        return value;
    }

    private static int RunInit(CommandOptions options, TextWriter output)
    {
        var root = Path.GetFullPath(options.Path ?? Directory.GetCurrentDirectory());
        var configPath = Path.Combine(root, "sloom.toml");

        if (File.Exists(configPath))
        {
            throw new InvalidOperationException($"Configuration already exists: {configPath}");
        }

        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "art", "run"));
        File.WriteAllText(configPath, SampleConfig, new UTF8Encoding(false));
        output.WriteLine($"Created {configPath}");
        return 0;
    }

    private static int RunFormats(CommandOptions options, TextWriter output)
    {
        var imageCodecs = ImageCodecs.CreateDefault();
        var native = NativeTextureEncoder.TryCreate();
        var imageFormats = imageCodecs.EncodableFormats.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var targets = TextureFormats.All;

        if (options.Json)
        {
            WriteJson(output, writer =>
            {
                WriteStringArray(writer, "image-formats", imageFormats);
                writer.WriteBoolean("native-texture-encoder", native is not null);
                writer.WriteStartArray("texture-targets");

                foreach (var target in targets)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", target.Id);
                    writer.WriteString("codec", target.Codec);
                    writer.WriteBoolean("available", target.IsRaw || (native?.CanEncode(target) ?? false));
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                WriteStringArray(writer, "packers", ["pack.basic", "pack.maxrects"]);
            });

            return 0;
        }

        output.WriteLine($"Image formats: {string.Join(", ", imageFormats)}");
        output.WriteLine($"Native texture encoder: {(native is null ? "unavailable" : "available")}");

        foreach (var target in targets)
        {
            output.WriteLine(
                $"Texture {target.Id}: {target.Codec} " +
                $"({(target.IsRaw || native?.CanEncode(target) == true ? "available" : "unavailable")})");
        }

        output.WriteLine("Packers: pack.basic, pack.maxrects");
        return 0;
    }

    private static int UnknownCommand(string command, TextWriter error)
    {
        error.WriteLine($"Unknown command: {command}");
        return 2;
    }

    private static CommandOptions ParseOptions(ReadOnlySpan<string> args)
    {
        var options = new CommandOptions();

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--config":
                    if (index + 1 >= args.Length)
                    {
                        throw new ArgumentException("--config requires a path.");
                    }

                    options.ConfigPath = args[++index];
                    break;
                case "--json":
                    options.Json = true;
                    break;
                case "--stats":
                    options.Stats = true;
                    break;
                case "--baseline":
                    if (index + 1 >= args.Length)
                    {
                        throw new ArgumentException("--baseline requires a package path.");
                    }

                    options.BaselinePath = args[++index];
                    break;
                case "--refresh":
                    options.Refresh = true;
                    break;
                case "--no-cache":
                    options.NoCache = true;
                    break;
                case "--watch":
                    options.Watch = true;
                    break;
                case "--permissive":
                    options.Permissive = true;
                    break;
                case "--key":
                    if (index + 1 >= args.Length)
                    {
                        throw new ArgumentException("--key requires a path.");
                    }

                    options.KeyPath = args[++index];
                    break;
                case "--out":
                    if (index + 1 >= args.Length)
                    {
                        throw new ArgumentException("--out requires a path.");
                    }

                    options.OutPath = args[++index];
                    break;
                case "--algorithm":
                    if (index + 1 >= args.Length)
                    {
                        throw new ArgumentException("--algorithm requires a name.");
                    }

                    options.Algorithm = args[++index];
                    break;
                case "--decrypt-key":
                    if (index + 1 >= args.Length)
                    {
                        throw new ArgumentException("--decrypt-key requires a path.");
                    }

                    options.DecryptionKeyPath = args[++index];
                    break;
                case "--chunk-size":
                    if (index + 1 >= args.Length ||
                        !int.TryParse(
                            args[++index],
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out var chunkSize) ||
                        chunkSize <= 0)
                    {
                        throw new ArgumentException("--chunk-size requires a positive integer.");
                    }

                    options.ChunkSize = chunkSize;
                    break;
                case "--allow":
                    if (index + 1 >= args.Length)
                    {
                        throw new ArgumentException("--allow requires a path.");
                    }

                    options.AllowedPaths.Add(args[++index].Replace('\\', '/'));
                    break;
                default:
                    if (args[index].StartsWith('-'))
                    {
                        throw new ArgumentException($"Unknown option: {args[index]}");
                    }

                    options.Paths.Add(args[index]);
                    break;
            }
        }

        return options;
    }

    private static void WriteDiagnostics(TextWriter output, IReadOnlyList<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics)
        {
            output.WriteLine($"{diagnostic.Severity.ToString().ToLowerInvariant()}: {diagnostic.Code}: {diagnostic.Message}");
        }
    }

    private static void WriteDiagnostics(Utf8JsonWriter writer, IReadOnlyList<Diagnostic> diagnostics)
    {
        writer.WriteStartArray("diagnostics");

        foreach (var diagnostic in diagnostics)
        {
            writer.WriteStartObject();
            writer.WriteString("code", diagnostic.Code);
            writer.WriteString("severity", diagnostic.Severity.ToString().ToLowerInvariant());
            writer.WriteString("message", diagnostic.Message);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteJson(TextWriter output, Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        output.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static void PrintHelp(TextWriter output)
    {
        output.WriteLine("SpriteLoom");
        output.WriteLine("Usage: sloom <command> [options]");
        output.WriteLine();
        output.WriteLine("Commands:");
        output.WriteLine("  init [path]        Create a minimal project");
        output.WriteLine("  pack               Build an atlas package");
        output.WriteLine("  validate           Validate configuration and inputs");
        output.WriteLine("  inspect <path>     Inspect a built package");
        output.WriteLine("  keygen <prefix>    Generate an Ed25519 key pair");
        output.WriteLine("  keygen --algorithm aes-256 --out <key>");
        output.WriteLine("                     Generate a symmetric key file");
        output.WriteLine("  encrypt <path>     Encrypt a file or directory into .senc files");
        output.WriteLine("  decrypt <path>     Decrypt .senc files");
        output.WriteLine("  sign <path>        Sign a directory package");
        output.WriteLine("  verify <path>      Verify a directory package or bundle");
        output.WriteLine("  diff <a> <b>       Compare frame records of two packages");
        output.WriteLine("  formats            List image formats, texture targets, and packers");
        output.WriteLine();
        output.WriteLine("Options:");
        output.WriteLine("  --config <path>    Configuration file or project directory (default: sloom.toml)");
        output.WriteLine("  --version          Print the CLI version");
        output.WriteLine("  --key <path>       Private, public, or symmetric key path");
        output.WriteLine("  --decrypt-key <p>  Symmetric key for encrypted bundles in inspect/diff/verify");
        output.WriteLine("  --out <path>       Output path for keygen, encrypt, and decrypt");
        output.WriteLine("  --algorithm <name> aes-256, xchacha20, aes-256-gcm, or xchacha20-poly1305");
        output.WriteLine("  --chunk-size <n>   AEAD chunk size in bytes (default: 65536)");
        output.WriteLine("  --json             Emit JSON");
        output.WriteLine("  --stats            Include additional details for inspect");
        output.WriteLine("  --baseline <path>  Compare inspect statistics with a baseline package");
        output.WriteLine("  --refresh          Rebuild all passes and refresh the cache");
        output.WriteLine("  --no-cache         Disable the pipeline cache");
        output.WriteLine("  --watch            Rebuild when configuration or sources change");
        output.WriteLine("  --permissive       Allow unsigned extra files during verify");
        output.WriteLine("  --allow <path>     Allow a specific unsigned path during verify");
    }

    private readonly record struct PackageMetrics(
        long Total,
        IReadOnlyDictionary<string, long> Variants);

    private sealed class CommandOptions
    {
        public string? ConfigPath { get; set; }

        public List<string> Paths { get; } = [];

        public string? Path => Paths.Count > 0 ? Paths[0] : null;

        public string? KeyPath { get; set; }

        public string? OutPath { get; set; }

        public string? Algorithm { get; set; }

        public string? DecryptionKeyPath { get; set; }

        public int? ChunkSize { get; set; }

        public bool Json { get; set; }

        public bool Stats { get; set; }

        public string? BaselinePath { get; set; }

        public bool Refresh { get; set; }

        public bool NoCache { get; set; }

        public bool Watch { get; set; }

        public bool Permissive { get; set; }

        public List<string> AllowedPaths { get; } = [];
    }

    private const string SampleConfig = """
        schema = 1

        [project]
        id = "heroes"
        name = "Heroes"

        [[sources]]
        id = "run"
        path = "art/run"
        order = "natural"
        frame-duration-ms = 100

        [[animations]]
        uri = "anim://heroes/run"
        source = "run"
        loop = true
        playback = "forward"

        [[passes]]
        id = "trim"
        use = "trim.fit-pixel"
        params = { threshold = 1 }

        [[passes]]
        id = "pack"
        use = "pack.maxrects"
        params = { max-size = [2048, 2048], padding = 2, pow2 = false, align = 4 }

        [[passes]]
        id = "bleed"
        use = "bleed.page"
        params = { extrude = 1, alpha-dilate = true }

        [output]
        kind = "directory"
        path = "build/heroes"
        manifest = "json"
        """;
}
