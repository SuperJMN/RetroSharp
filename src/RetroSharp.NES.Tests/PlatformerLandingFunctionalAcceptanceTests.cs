namespace RetroSharp.NES.Tests;

using RetroSharp.Sdk;
using Xunit;

public sealed class PlatformerLandingFunctionalAcceptanceTests
{
    [Fact]
    public void Exact_production_rom_crosses_boundaries_returns_jumps_lands_and_resets_only_after_the_authored_fall()
    {
        var sourcePath = RepositoryFile("samples/platformer-landing/src/main.rs");
        var sampleDirectory = RepositoryDirectory("samples/platformer-landing");
        var source = File.ReadAllText(sourcePath);
        var program = RetroSharp.NES.NesRomCompiler.PrepareVideoProgram(
            source,
            sampleDirectory,
            SdkLibraryImportMode.ExplicitOnly,
            null,
            [SdkImportResolver.Portable2D],
            null).VideoProgram;
        var build = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            source,
            sampleDirectory,
            SdkLibraryImportMode.ExplicitOnly,
            null,
            [SdkImportResolver.Portable2D],
            null);
        var trackedRom = File.ReadAllBytes(RepositoryFile("samples/platformer-landing/bin/platformer-landing.nes"));
        Assert.Equal(trackedRom, build.Rom);
        Assert.Equal("nes-mapper-0-current", build.Report.SelectedProfile);
        Assert.Equal("1fadb1ad32e18244f18c52e4bc727387a24c0064c39f0906a06b7b3f6a4adc6d", Sha256(trackedRom));
        Assert.NotNull(program.PackedWorld);
        Assert.Equal(4, program.PackedWorld.Pack.Descriptor.ChunkColumns);
        Assert.Equal(3, program.PackedWorld.Pack.Descriptor.ChunkRows);
        Assert.Equal(12, program.PackedWorld.Pack.Chunks.Count);

        var variables = build.Report.UserVariables.ToDictionary(variable => variable.Name, StringComparer.Ordinal);
        AssertVariable(variables, "player.x", "i16", 2);
        AssertVariable(variables, "player.y", "i16", 2);
        AssertVariable(variables, "player.grounded", "bool", 1);
        AssertVariable(variables, "player.jumpCount", "u8", 1);
        AssertVariable(variables, "player.landingCount", "u8", 1);
        AssertVariable(variables, "player.gameplayResetCount", "u8", 1);
        AssertVariable(variables, "player.supportProbeCount", "u8", 1);
        AssertVariable(variables, "player.wallContactCount", "u8", 1);
        AssertVariable(variables, "view.x", "i16", 2);

        var cpu = new NesTestCpu(trackedRom);
        var snapshots = new Dictionary<int, Snapshot>();
        for (var frame = 1; frame <= 1240; frame++)
        {
            cpu.Held.Clear();
            if (frame is >= 161 and <= 560) cpu.Held.Add("right");
            if (frame is >= 561 and <= 826) cpu.Held.Add("left");
            if (frame is >= 847 and <= 886) cpu.Held.Add("a");
            if (frame is >= 997 and <= 1155) cpu.Held.Add("left");
            cpu.RunFrames(frame);
            snapshots[frame] = new(
                Word(cpu, variables["player.x"]),
                Word(cpu, variables["player.y"]),
                Word(cpu, variables["view.x"]),
                Byte(cpu, variables["player.grounded"]),
                Byte(cpu, variables["player.jumpCount"]),
                Byte(cpu, variables["player.landingCount"]),
                Byte(cpu, variables["player.gameplayResetCount"]),
                Byte(cpu, variables["player.supportProbeCount"]),
                Byte(cpu, variables["player.wallContactCount"]));
        }

        Assert.Equal(72, snapshots[160].PlayerX);
        Assert.Equal(273, snapshots[160].PlayerY);
        Assert.Equal(1, snapshots[160].Grounded);
        Assert.Equal(0, snapshots[160].GameplayResetCount);
        Assert.Equal(366, snapshots[560].PlayerX);
        Assert.Equal(366, snapshots.Values.Max(snapshot => snapshot.PlayerX));
        Assert.Equal(270, snapshots[560].CameraX);
        Assert.True(snapshots[560].WallContactCount > 0);
        Assert.Contains(255, snapshots.Values.Select(snapshot => snapshot.CameraX));
        Assert.Contains(256, snapshots.Values.Select(snapshot => snapshot.CameraX));
        Assert.Equal(100, snapshots[826].PlayerX);
        Assert.Equal(36, snapshots[826].CameraX);
        Assert.Equal(0, snapshots[826].GameplayResetCount);
        Assert.Equal(202, snapshots[875].PlayerY);
        Assert.Equal(0, snapshots[875].Grounded);
        Assert.Equal(202, snapshots.Where(item => item.Key is >= 847 and <= 996).Min(item => item.Value.PlayerY));
        Assert.Equal(273, snapshots[996].PlayerY);
        Assert.Equal(1, snapshots[996].Grounded);
        Assert.Equal(1, snapshots[996].JumpCount);
        Assert.Equal(1, snapshots[996].LandingCount);
        Assert.All(snapshots.Where(item => item.Key < 997), item => Assert.Equal(0, item.Value.GameplayResetCount));
        Assert.Contains(snapshots.Where(item => item.Key >= 997), item => item.Value.PlayerY > 273);
        Assert.All(snapshots.Values, snapshot => Assert.InRange(snapshot.GameplayResetCount, 0, 1));
        Assert.Equal(1, snapshots[1240].GameplayResetCount);
        Assert.Equal(273, snapshots[1240].PlayerY);
        Assert.Equal(1, snapshots[1240].Grounded);
        Assert.Equal(1, snapshots[1240].JumpCount);
        Assert.Equal(1, snapshots[1240].LandingCount);
        Assert.True(snapshots[1240].SupportProbeCount > 0);
        Assert.Equal(1, cpu.ResetCount);
    }

    private static void AssertVariable(
        IReadOnlyDictionary<string, NesRuntimeUserVariable> variables,
        string name,
        string type,
        int size)
    {
        var variable = Assert.Contains(name, variables);
        Assert.Equal(type, variable.Type);
        Assert.Equal(size, variable.Size);
    }

    private static int Word(NesTestCpu cpu, NesRuntimeUserVariable variable) =>
        cpu.Ram(variable.Address) | (cpu.Ram((ushort)(variable.Address + 1)) << 8);

    private static int Byte(NesTestCpu cpu, NesRuntimeUserVariable variable) => cpu.Ram(variable.Address);

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private static string RepositoryFile(string relativePath)
    {
        var path = RepositoryDirectory(relativePath);
        return File.Exists(path)
            ? path
            : throw new InvalidOperationException($"Could not find repository file '{relativePath}'.");
    }

    private static string RepositoryDirectory(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not find repository path '{relativePath}'.");
    }

    private sealed record Snapshot(
        int PlayerX,
        int PlayerY,
        int CameraX,
        int Grounded,
        int JumpCount,
        int LandingCount,
        int GameplayResetCount,
        int SupportProbeCount,
        int WallContactCount);
}
