using System.Text.Json;
using SpriteLoom.Commands;

namespace SpriteLoom.Integration.Tests;

public sealed class CliIntegrationTests
{
    [Fact]
    public async Task Pack_validate_and_inspect_succeed()
    {
        using var project = TestProject.Create();
        var output = new StringWriter();
        var error = new StringWriter();

        var packExit = await SloomCommandLine.RunAsync(
            ["pack", "--config", project.ConfigPath],
            output,
            error);

        Assert.Equal(0, packExit);
        Assert.Equal(string.Empty, error.ToString());

        var validateExit = await SloomCommandLine.RunAsync(
            ["validate", "--config", project.ConfigPath],
            output,
            error);

        Assert.Equal(0, validateExit);

        var inspectExit = await SloomCommandLine.RunAsync(
            ["inspect", project.OutputPath, "--stats"],
            output,
            error);

        Assert.Equal(0, inspectExit);
        Assert.Contains("Frames: 3", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Animations: 1", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Dedup ratio:", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Page utilization:", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Inspect_compares_baseline_sizes()
    {
        using var project = TestProject.Create();
        var error = new StringWriter();
        var packOutput = new StringWriter();

        Assert.Equal(0, await SloomCommandLine.RunAsync(
            ["pack", "--config", project.ConfigPath],
            packOutput,
            error));

        var baseline = Path.Combine(project.Root, "build", "baseline");
        CopyDirectory(project.OutputPath, baseline);
        project.AddFrame("a3.png");

        Assert.Equal(0, await SloomCommandLine.RunAsync(
            ["pack", "--config", project.ConfigPath],
            packOutput,
            error));

        var statsOutput = new StringWriter();
        Assert.Equal(0, await SloomCommandLine.RunAsync(
            ["inspect", project.OutputPath, "--stats", "--baseline", baseline],
            statsOutput,
            error));

        Assert.Contains("baseline", statsOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("delta", statsOutput.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Version_and_formats_are_available()
    {
        var versionOutput = new StringWriter();
        Assert.Equal(0, await SloomCommandLine.RunAsync(
            ["--version"],
            versionOutput,
            new StringWriter()));
        Assert.Contains("sloom 0.1.0", versionOutput.ToString(), StringComparison.Ordinal);

        var formatsOutput = new StringWriter();
        Assert.Equal(0, await SloomCommandLine.RunAsync(
            ["formats", "--json"],
            formatsOutput,
            new StringWriter()));

        using var document = JsonDocument.Parse(formatsOutput.ToString());
        var targets = document.RootElement.GetProperty("texture-targets")
            .EnumerateArray()
            .Select(value => value.GetProperty("id").GetString())
            .ToArray();

        Assert.Contains("ktx2-basis-uastc", targets);
        Assert.Contains("webp", document.RootElement.GetProperty("image-formats")
            .EnumerateArray()
            .Select(value => value.GetString()));
    }

    [Fact]
    public async Task Pack_accepts_project_directory_as_config()
    {
        using var project = TestProject.Create();
        var error = new StringWriter();

        var exit = await SloomCommandLine.RunAsync(
            ["pack", "--config", project.Root],
            new StringWriter(),
            error);

        Assert.Equal(0, exit);
        Assert.True(File.Exists(Path.Combine(project.OutputPath, "manifest.json")));
    }

    [Fact]
    public async Task Pack_watch_stops_when_cancelled()
    {
        using var project = TestProject.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exit = await SloomCommandLine.RunAsync(
            ["pack", "--watch", "--config", project.ConfigPath],
            new StringWriter(),
            new StringWriter(),
            cancellation.Token);

        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task Inspect_json_emits_counts()
    {
        using var project = TestProject.Create();
        var error = new StringWriter();

        var packOutput = new StringWriter();
        Assert.Equal(0, await SloomCommandLine.RunAsync(
            ["pack", "--config", project.ConfigPath],
            packOutput,
            error));

        var jsonOutput = new StringWriter();
        var inspectExit = await SloomCommandLine.RunAsync(
            ["inspect", project.OutputPath, "--json"],
            jsonOutput,
            error);

        Assert.Equal(0, inspectExit);

        using var document = JsonDocument.Parse(jsonOutput.ToString());
        Assert.Equal(3, document.RootElement.GetProperty("frames").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("pages").GetInt32());
        Assert.Equal(1, document.RootElement.GetProperty("animations").GetInt32());
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, path);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(path, destination);
        }
    }
}

public sealed class CliEncryptionTests
{
    [Theory]
    [InlineData("aes-256-gcm")]
    [InlineData("xchacha20-poly1305")]
    public async Task Keygen_encrypt_and_decrypt_files(string algorithm)
    {
        var root = Path.Combine(Path.GetTempPath(), "sloom-cli-enc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var keyPath = Path.Combine(root, "asset.key");
            var plainPath = Path.Combine(root, "plain.bin");
            var encryptedPath = Path.Combine(root, "plain.bin.senc");
            var decryptedPath = Path.Combine(root, "plain.out");
            File.WriteAllBytes(plainPath, Enumerable.Range(0, 10_000).Select(value => (byte)value).ToArray());

            Assert.Equal(0, await SloomCommandLine.RunAsync(
                ["keygen", "--algorithm", "aes-256", "--out", keyPath],
                new StringWriter(),
                new StringWriter()));
            Assert.True(File.Exists(keyPath));

            Assert.Equal(0, await SloomCommandLine.RunAsync(
                ["encrypt", plainPath, "--key", keyPath, "--algorithm", algorithm, "--chunk-size", "1024"],
                new StringWriter(),
                new StringWriter()));
            Assert.True(File.Exists(encryptedPath));
            Assert.NotEqual(File.ReadAllBytes(plainPath), File.ReadAllBytes(encryptedPath));

            Assert.Equal(0, await SloomCommandLine.RunAsync(
                ["decrypt", encryptedPath, "--key", keyPath, "--out", decryptedPath],
                new StringWriter(),
                new StringWriter()));
            Assert.Equal(File.ReadAllBytes(plainPath), File.ReadAllBytes(decryptedPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Encrypt_requires_key_and_rejects_wrong_key()
    {
        var root = Path.Combine(Path.GetTempPath(), "sloom-cli-enc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var keyPath = Path.Combine(root, "key");
            var plainPath = Path.Combine(root, "plain.bin");
            File.WriteAllText(plainPath, "sprite loom");
            Assert.Equal(0, await SloomCommandLine.RunAsync(
                ["keygen", "--algorithm", "aes-256", "--out", keyPath],
                new StringWriter(),
                new StringWriter()));

            var missingError = new StringWriter();
            Assert.Equal(4, await SloomCommandLine.RunAsync(
                ["encrypt", plainPath],
                new StringWriter(),
                missingError));
            Assert.Contains("--key", missingError.ToString(), StringComparison.Ordinal);

            var encryptedPath = Path.Combine(root, "plain.senc");
            Assert.Equal(0, await SloomCommandLine.RunAsync(
                ["encrypt", plainPath, "--key", keyPath, "--out", encryptedPath],
                new StringWriter(),
                new StringWriter()));

            var otherKey = Path.Combine(root, "other.key");
            Assert.Equal(0, await SloomCommandLine.RunAsync(
                ["keygen", "--algorithm", "aes-256", "--out", otherKey],
                new StringWriter(),
                new StringWriter()));

            var wrongError = new StringWriter();
            Assert.Equal(4, await SloomCommandLine.RunAsync(
                ["decrypt", encryptedPath, "--key", otherKey, "--out", Path.Combine(root, "wrong.out")],
                new StringWriter(),
                wrongError));
            Assert.Contains("key id", wrongError.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
