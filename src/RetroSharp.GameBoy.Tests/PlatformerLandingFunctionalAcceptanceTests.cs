namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Sdk;
using Xunit;

public sealed class PlatformerLandingFunctionalAcceptanceTests
{
    [Fact]
    public void Exact_production_rom_exposes_deterministic_platformer_mechanics_evidence()
    {
        var sourcePath = RepositoryFile("samples/platformer-landing/src/main.rs");
        var sampleDirectory = RepositoryDirectory("samples/platformer-landing");
        var program = RetroSharp.GameBoy.GameBoyRomCompiler.PrepareVideoProgram(
            File.ReadAllText(sourcePath),
            sampleDirectory,
            SdkLibraryImportMode.ExplicitOnly,
            null,
            [SdkImportResolver.Portable2D],
            null);
        var build = GameBoyRomBuilder.BuildWithReport(program);
        Assert.Equal("gb-rom-only-current", build.Report.SelectedProfile);
        var packedWorld = Assert.IsType<GameBoyTiledWorldPack>(program.PackedWorld);
        Assert.Equal(4, packedWorld.Pack.Descriptor.ChunkColumns);
        Assert.Equal(3, packedWorld.Pack.Descriptor.ChunkRows);
        Assert.Equal(12, packedWorld.Pack.Chunks.Count);

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
    }

    [Fact]
    public void Right_wall_path_crosses_the_word_and_chunk_boundaries_without_penetration_or_reset()
    {
        var sourcePath = RepositoryFile("samples/platformer-landing/src/main.rs");
        var sampleDirectory = RepositoryDirectory("samples/platformer-landing");
        var program = RetroSharp.GameBoy.GameBoyRomCompiler.PrepareVideoProgram(
            File.ReadAllText(sourcePath),
            sampleDirectory,
            SdkLibraryImportMode.ExplicitOnly,
            null,
            [SdkImportResolver.Portable2D],
            null);
        var build = GameBoyRomBuilder.BuildWithReport(program);
        var variables = build.Report.UserVariables.ToDictionary(variable => variable.Name, StringComparer.Ordinal);
        var cpu = new GameBoyTestCpu(build.Rom)
        {
            CycleAccurateLy = true,
            EnforceVblankVramWrites = true,
        };
        var initialResetCount = cpu.ResetCount;
        var cameraTrajectory = new List<int>();
        var playerTrajectory = new List<int>();

        cpu.Held.Add("right");
        for (var frame = 1; frame <= 420; frame++)
        {
            cpu.RunFrames(frame);
            cameraTrajectory.Add(Word(cpu, variables["view.x"]));
            playerTrajectory.Add(Word(cpu, variables["player.x"]));
        }

        Assert.Contains(255, cameraTrajectory);
        Assert.Contains(256, cameraTrajectory);
        Assert.True(cameraTrajectory.Max() >= 256);
        Assert.Equal(366, playerTrajectory.Max());
        Assert.Equal(366, playerTrajectory[^1]);
        Assert.True(cpu.Wram(variables["player.wallContactCount"].Address) > 0);
        Assert.True(cpu.Wram(variables["player.supportProbeCount"].Address) > 0);
        Assert.Equal(273, Word(cpu, variables["player.y"]));
        Assert.Equal(1, cpu.Wram(variables["player.grounded"].Address));
        Assert.Equal(0, cpu.Wram(variables["player.gameplayResetCount"].Address));
        Assert.Equal(initialResetCount, cpu.ResetCount);
    }

    [Fact]
    public void Authored_timeline_returns_jumps_lands_then_resets_once_after_the_real_fall()
    {
        var sourcePath = RepositoryFile("samples/platformer-landing/src/main.rs");
        var sampleDirectory = RepositoryDirectory("samples/platformer-landing");
        var program = RetroSharp.GameBoy.GameBoyRomCompiler.PrepareVideoProgram(
            File.ReadAllText(sourcePath),
            sampleDirectory,
            SdkLibraryImportMode.ExplicitOnly,
            null,
            [SdkImportResolver.Portable2D],
            null);
        var build = GameBoyRomBuilder.BuildWithReport(program);
        var variables = build.Report.UserVariables.ToDictionary(variable => variable.Name, StringComparer.Ordinal);
        var cpu = new GameBoyTestCpu(build.Rom)
        {
            CycleAccurateLy = true,
            EnforceVblankVramWrites = true,
        };
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

        Assert.Equal(new Snapshot(72, 273, 0, 1, 0, 0, 0, snapshots[160].SupportProbeCount, 0), snapshots[160]);
        Assert.Equal(366, snapshots[560].PlayerX);
        Assert.Equal(366, snapshots.Values.Max(snapshot => snapshot.PlayerX));
        Assert.Equal(270, snapshots[560].CameraX);
        Assert.True(snapshots[560].WallContactCount > 0);
        Assert.Equal(0, snapshots[560].GameplayResetCount);
        Assert.Equal(127, snapshots[826].PlayerX);
        Assert.Equal(63, snapshots[826].CameraX);
        Assert.Equal(0, snapshots[826].GameplayResetCount);
        Assert.Equal(202, snapshots[876].PlayerY);
        Assert.Equal(0, snapshots[876].Grounded);
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
    }

    private static void AssertVariable(
        IReadOnlyDictionary<string, GameBoyRuntimeUserVariable> variables,
        string name,
        string type,
        int size)
    {
        var variable = Assert.Contains(name, variables);
        Assert.Equal(type, variable.Type);
        Assert.Equal(size, variable.Size);
    }

    private static int Word(GameBoyTestCpu cpu, GameBoyRuntimeUserVariable variable) =>
        cpu.Wram(variable.Address) | (cpu.Wram((ushort)(variable.Address + 1)) << 8);

    private static int Byte(GameBoyTestCpu cpu, GameBoyRuntimeUserVariable variable) => cpu.Wram(variable.Address);

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
}
