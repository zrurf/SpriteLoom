using System.Text.Json;
using SpriteLoom.Commands;

namespace SpriteLoom.Integration.Tests;

public sealed class SigningCliTests
{
    [Fact]
    public async Task Bundle_pack_inspect_and_verify()
    {
        using var project = TestProject.Create(
            outputKind: "bundle",
            outputPath: "build/heroes.slb",
            signingKey: "keys/dev.private.pem");
        var output = new StringWriter();
        var error = new StringWriter();
        var keys = Path.Combine(project.Root, "keys", "dev");

        Assert.Equal(0, await SloomCommandLine.RunAsync(["keygen", keys], output, error));
        Assert.True(File.Exists(keys + ".private.pem"));
        Assert.True(File.Exists(keys + ".public.pem"));

        Assert.Equal(0, await SloomCommandLine.RunAsync(
            ["pack", "--config", project.ConfigPath],
            output,
            error));
        Assert.True(File.Exists(project.OutputPath));

        var inspectOutput = new StringWriter();
        Assert.Equal(0, await SloomCommandLine.RunAsync(
            ["inspect", project.OutputPath, "--stats"],
            inspectOutput,
            error));
        Assert.Contains("Frames: 3", inspectOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("Dedup ratio:", inspectOutput.ToString(), StringComparison.Ordinal);

        var verifyOutput = new StringWriter();
        Assert.Equal(0, await SloomCommandLine.RunAsync(
            ["verify", project.OutputPath, "--key", keys + ".public.pem"],
            verifyOutput,
            error));
        Assert.Contains("Signature is valid", verifyOutput.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Directory_sign_and_verify_detects_tampering()
    {
        using var project = TestProject.Create();
        var output = new StringWriter();
        var error = new StringWriter();
        var keys = Path.Combine(project.Root, "keys", "dev");

        Assert.Equal(0, await SloomCommandLine.RunAsync(["keygen", keys], output, error));
        Assert.Equal(0, await SloomCommandLine.RunAsync(
            ["pack", "--config", project.ConfigPath],
            output,
            error));
        Assert.Equal(0, await SloomCommandLine.RunAsync(
            ["sign", project.OutputPath, "--key", keys + ".private.pem"],
            output,
            error));

        var verifyOutput = new StringWriter();
        Assert.Equal(0, await SloomCommandLine.RunAsync(
            ["verify", project.OutputPath, "--key", keys + ".public.pem"],
            verifyOutput,
            error));

        File.WriteAllText(Path.Combine(project.OutputPath, "manifest.json"), "[]");

        var tamperedOutput = new StringWriter();
        Assert.Equal(4, await SloomCommandLine.RunAsync(
            ["verify", project.OutputPath, "--key", keys + ".public.pem"],
            tamperedOutput,
            error));
        Assert.Contains("verification failed", tamperedOutput.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Diff_reports_added_frames()
    {
        using var project = TestProject.Create();
        var output = new StringWriter();
        var error = new StringWriter();

        Assert.Equal(0, await SloomCommandLine.RunAsync(
            ["pack", "--config", project.ConfigPath],
            output,
            error));

        var previous = Path.Combine(project.Root, "build", "previous");
        CopyDirectory(project.OutputPath, previous);
        project.AddFrame("a3.png");

        Assert.Equal(0, await SloomCommandLine.RunAsync(
            ["pack", "--config", project.ConfigPath],
            output,
            error));

        var diffOutput = new StringWriter();
        Assert.Equal(0, await SloomCommandLine.RunAsync(
            ["diff", previous, project.OutputPath, "--json"],
            diffOutput,
            error));

        using var document = JsonDocument.Parse(diffOutput.ToString());
        var added = document.RootElement.GetProperty("added")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();

        Assert.Contains("frame://heroes/run/a3", added);
    }

    [Fact]
    public async Task Cache_restores_unaffected_passes()
    {
        using var project = TestProject.Create();
        var output = new StringWriter();
        var error = new StringWriter();

        Assert.Equal(0, await SloomCommandLine.RunAsync(
            ["pack", "--config", project.ConfigPath],
            output,
            error));

        var cachedOutput = new StringWriter();
        Assert.Equal(0, await SloomCommandLine.RunAsync(
            ["pack", "--config", project.ConfigPath],
            cachedOutput,
            error));
        Assert.Contains("Pass 'import' restored from cache.", cachedOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("Cache:", cachedOutput.ToString(), StringComparison.Ordinal);

        File.WriteAllText(
            project.ConfigPath,
            File.ReadAllText(project.ConfigPath).Replace("threshold = 1", "threshold = 2"));

        var incrementalOutput = new StringWriter();
        Assert.Equal(0, await SloomCommandLine.RunAsync(
            ["pack", "--config", project.ConfigPath],
            incrementalOutput,
            error));
        Assert.Contains("Pass 'import' restored from cache.", incrementalOutput.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Pass 'trim' restored from cache.", incrementalOutput.ToString(), StringComparison.Ordinal);

        var refreshedOutput = new StringWriter();
        Assert.Equal(0, await SloomCommandLine.RunAsync(
            ["pack", "--config", project.ConfigPath, "--refresh"],
            refreshedOutput,
            error));
        Assert.DoesNotContain("restored from cache", refreshedOutput.ToString(), StringComparison.Ordinal);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(source, path);
            var destination = Path.Combine(target, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(path, destination);
        }
    }
}
