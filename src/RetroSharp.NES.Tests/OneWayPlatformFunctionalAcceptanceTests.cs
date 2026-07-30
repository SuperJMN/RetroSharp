namespace RetroSharp.NES.Tests;

using RetroSharp.Sdk;
using Xunit;

public sealed class OneWayPlatformFunctionalAcceptanceTests
{
    private const int PositionTolerance = 8;
    private const int FloorY = 273;
    private const int PlatformY = 225;

    [Fact]
    public void One_way_platform_passes_from_below_lands_on_top_then_falls_off_the_edge()
    {
        var sampleDirectory = RepositoryDirectory("samples/oneway-platform");
        var source = File.ReadAllText(Path.Combine(sampleDirectory, "src", "main.rs"));
        var build = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            source,
            sampleDirectory,
            SdkLibraryImportMode.ExplicitOnly,
            null,
            [SdkImportResolver.Portable2D],
            null);
        var variables = build.Report.UserVariables.ToDictionary(variable => variable.Name, StringComparer.Ordinal);
        AssertVariable(variables, "player.x", "i16", 2);
        AssertVariable(variables, "player.y", "i16", 2);
        AssertVariable(variables, "player.grounded", "bool", 1);
        AssertVariable(variables, "player.landingCount", "u8", 1);

        var cpu = new NesTestCpu(build.Rom);
        var snapshots = new Dictionary<int, Snapshot>();
        var settledResetCount = 0;
        for (var frame = 1; frame <= 360; frame++)
        {
            cpu.Held.Clear();
            if (frame is >= 91 and <= 130) cpu.Held.Add("a");
            if (frame is >= 171 and <= 320) cpu.Held.Add("right");
            cpu.RunFrames(frame);
            if (frame == 90) settledResetCount = cpu.ResetCount;
            snapshots[frame] = new(
                Word(cpu, variables["player.x"]),
                Word(cpu, variables["player.y"]),
                Byte(cpu, variables["player.grounded"]),
                Byte(cpu, variables["player.jumpCount"]),
                Byte(cpu, variables["player.landingCount"]),
                Byte(cpu, variables["player.gameplayResetCount"]),
                Byte(cpu, variables["player.supportProbeCount"]),
                Byte(cpu, variables["player.wallContactCount"]));
        }

        // Grounded on the solid floor before the jump.
        Assert.Equal(1, snapshots[90].Grounded);
        Assert.InRange(snapshots[90].PlayerY, FloorY - PositionTolerance, FloorY + PositionTolerance);
        Assert.Equal(0, snapshots[90].JumpCount);
        Assert.Equal(0, snapshots[90].LandingCount);

        // Passes up through the one-way platform: the rising feet cross above the
        // platform top (world Y 256) without landing while ascending.
        var jumpArc = snapshots.Where(item => item.Key is >= 91 and <= 135).Select(item => item.Value).ToArray();
        Assert.Contains(jumpArc, snapshot => snapshot.Grounded == 0);
        Assert.True(
            jumpArc.Min(snapshot => snapshot.PlayerY) < PlatformY,
            "The jump never carried the feet above the one-way platform top.");

        // Lands on top of the platform when descending: grounded distinctly above
        // the floor, with exactly one jump-driven landing and no wall contact.
        Assert.Equal(1, snapshots[168].Grounded);
        Assert.InRange(snapshots[168].PlayerY, PlatformY - PositionTolerance, PlatformY + PositionTolerance);
        Assert.True(snapshots[168].PlayerY < FloorY - PositionTolerance, "Landed on the floor instead of the one-way platform.");
        Assert.Equal(1, snapshots[168].JumpCount);
        Assert.Equal(1, snapshots[168].LandingCount);
        Assert.Equal(0, snapshots[168].WallContactCount);

        // Walking off the platform edge drops support and the actor falls.
        Assert.Contains(
            snapshots.Where(item => item.Key is > 168 and <= 320),
            item => item.Value.Grounded == 0 && item.Value.PlayerY >= PlatformY + 24);

        // Falls to the solid floor below and stays grounded there, having advanced right.
        Assert.Equal(1, snapshots[320].Grounded);
        Assert.InRange(snapshots[320].PlayerY, FloorY - PositionTolerance, FloorY + PositionTolerance);
        Assert.True(snapshots[320].PlayerX > 150, "The actor did not walk right off the platform.");
        Assert.Equal(1, snapshots[320].JumpCount);

        // The one-way traversal never triggers a hazard/fall reset and never resets the
        // console after boot.
        Assert.All(snapshots.Values, snapshot => Assert.Equal(0, snapshot.GameplayResetCount));
        Assert.True(snapshots[360].SupportProbeCount > 0);
        Assert.Equal(settledResetCount, cpu.ResetCount);
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

    private sealed record Snapshot(
        int PlayerX,
        int PlayerY,
        int Grounded,
        int JumpCount,
        int LandingCount,
        int GameplayResetCount,
        int SupportProbeCount,
        int WallContactCount);

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
