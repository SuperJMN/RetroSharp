namespace RetroSharp.Cli.Tests;

using System.Diagnostics;
using Xunit;

public sealed class GbsCliTests
{
    [Fact]
    public void Gbs_to_gbapu_command_writes_gbapu_trace_from_gbsplay_iodump()
    {
        using var workspace = TemporaryWorkspace();
        var input = Path.Combine(workspace.Path, "stage.gbs");
        var output = Path.Combine(workspace.Path, "stage.gbapu.json");
        var fakeGbsPlay = Path.Combine(workspace.Path, "fake-gbsplay");
        WriteGbsHeader(input);
        WriteFakeGbsPlay(fakeGbsPlay);

        var result = RunCli(
            "gbs-to-gbapu",
            "--in",
            input,
            "--out",
            output,
            "--seconds",
            "1",
            "--loop-cycle",
            "0",
            "--gbsplay",
            fakeGbsPlay);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(output), result.CombinedOutput);
        Assert.Contains("\"format\": \"retrosharp.gbapu.v1\"", File.ReadAllText(output), StringComparison.Ordinal);
        Assert.Contains("Wrote Game Boy APU trace:", result.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Gbs_to_gbapu_writes_binary_by_default_and_dump_reads_it_back()
    {
        using var workspace = TemporaryWorkspace();
        var input = Path.Combine(workspace.Path, "stage.gbs");
        var output = Path.Combine(workspace.Path, "stage.gbapu");
        var fakeGbsPlay = Path.Combine(workspace.Path, "fake-gbsplay");
        WriteGbsHeader(input);
        WriteFakeGbsPlay(fakeGbsPlay);

        var export = RunCli(
            "gbs-to-gbapu", "--in", input, "--out", output,
            "--seconds", "1", "--gbsplay", fakeGbsPlay);

        Assert.Equal(0, export.ExitCode);
        Assert.True(File.Exists(output), export.CombinedOutput);
        var head = File.ReadAllBytes(output)[..4];
        Assert.Equal("GBAP"u8.ToArray(), head);

        var dump = RunCli("gbapu-dump", output);
        Assert.Equal(0, dump.ExitCode);
        Assert.Contains("ff11=80", dump.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("ff14=87", dump.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Gbs_to_gbapu_emit_json_writes_json_even_with_binary_extension()
    {
        using var workspace = TemporaryWorkspace();
        var input = Path.Combine(workspace.Path, "stage.gbs");
        var output = Path.Combine(workspace.Path, "stage.gbapu");
        var fakeGbsPlay = Path.Combine(workspace.Path, "fake-gbsplay");
        WriteGbsHeader(input);
        WriteFakeGbsPlay(fakeGbsPlay);

        var result = RunCli(
            "gbs-to-gbapu", "--in", input, "--out", output,
            "--seconds", "1", "--emit-json", "--gbsplay", fakeGbsPlay);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"format\": \"retrosharp.gbapu.v1\"", File.ReadAllText(output), StringComparison.Ordinal);
    }

    [Fact]
    public void Gbs_to_uge_command_is_not_supported()
    {
        using var workspace = TemporaryWorkspace();
        var input = Path.Combine(workspace.Path, "stage.gbs");
        var output = Path.Combine(workspace.Path, "stage.uge");
        var fakeGbsPlay = Path.Combine(workspace.Path, "fake-gbsplay");
        WriteGbsHeader(input);
        WriteFakeGbsPlay(fakeGbsPlay);

        var result = RunCli(
            "gbs-to-uge",
            "--in",
            input,
            "--out",
            output,
            "--seconds",
            "1",
            "--gbsplay",
            fakeGbsPlay);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(output), result.CombinedOutput);
        Assert.Contains("Unknown command 'gbs-to-uge'.", result.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void Gbs_to_gbapu_command_reports_missing_gbsplay_path()
    {
        using var workspace = TemporaryWorkspace();
        var input = Path.Combine(workspace.Path, "stage.gbs");
        var output = Path.Combine(workspace.Path, "stage.gbapu.json");
        WriteGbsHeader(input);

        var result = RunCli(
            "gbs-to-gbapu",
            "--in",
            input,
            "--out",
            output,
            "--gbsplay",
            Path.Combine(workspace.Path, "missing-gbsplay"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(output), result.CombinedOutput);
        Assert.Contains("Could not start gbsplay", result.CombinedOutput, StringComparison.Ordinal);
    }

    private static CliResult RunCli(params string[] args)
    {
        var processArgs = new List<string>
        {
            CliAssembly(),
        };
        processArgs.AddRange(args);

        return RunProcess("dotnet", processArgs.ToArray());
    }

    private static CliResult RunProcess(string fileName, params string[] args)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = RepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(TimeSpan.FromSeconds(120)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{fileName} command timed out: {string.Join(" ", args)}");
        }

        return new CliResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    private static TemporaryDirectory TemporaryWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "retrosharp-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return new TemporaryDirectory(path);
    }

    private static void WriteGbsHeader(string path)
    {
        var header = new byte[0x70];
        header[0] = (byte)'G';
        header[1] = (byte)'B';
        header[2] = (byte)'S';
        header[3] = 1;
        header[4] = 1;
        header[5] = 1;
        WriteUInt16(header, 0x06, 0x4000);
        WriteUInt16(header, 0x08, 0x4000);
        WriteUInt16(header, 0x0A, 0x4010);
        WriteUInt16(header, 0x0C, 0xFFFE);
        File.WriteAllBytes(path, header);
    }

    private static void WriteFakeGbsPlay(string path)
    {
        File.WriteAllText(
            path,
            """
            #!/bin/sh
            printf '%s\n' 'dumping subsong 0'
            printf '%s\n' 'subsong 0'
            printf '%s\n' '00000000 ff11=80'
            printf '%s\n' '00000000 ff12=f0'
            printf '%s\n' '00000000 ff13=05'
            printf '%s\n' '00000000 ff14=87'
            """);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    private static void WriteUInt16(byte[] bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)(value & 0xFF);
        bytes[offset + 1] = (byte)(value >> 8);
    }

    private static string CliAssembly()
    {
        var configuration = TestConfiguration();
        return RepositoryFile($"src/RetroSharp.Cli/bin/{configuration}/net10.0/RetroSharp.Cli.dll");
    }

    private static string TestConfiguration()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        var configurationDirectory = directory.Parent;
        return configurationDirectory?.Name
            ?? throw new InvalidOperationException($"Could not infer test configuration from '{AppContext.BaseDirectory}'.");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RetroSharp.sln")) &&
                Directory.Exists(Path.Combine(directory.FullName, "samples")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }

    private static string RepositoryFile(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Could not find repository file '{relativePath}'.");
        }

        return path;
    }

    private sealed record CliResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public string CombinedOutput => $"{StandardOutput}{StandardError}";
    }

    private sealed class TemporaryDirectory(string path) : IDisposable
    {
        public string Path { get; } = path;

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
