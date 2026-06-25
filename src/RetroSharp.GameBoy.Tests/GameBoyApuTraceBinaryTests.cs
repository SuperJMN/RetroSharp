namespace RetroSharp.GameBoy.Tests;

using RetroSharp.GameBoy;
using Xunit;

public sealed class GameBoyApuTraceBinaryTests
{
    [Fact]
    public void Round_trips_trace_through_binary_container()
    {
        var trace = new GameBoyApuTrace(
            4_194_304,
            60,
            DurationCycles: 140_448,
            LoopCycle: 70_224,
            new GameBoyApuTraceMetadata("Theme", "Composer", "1992", 3, "theme.gbs", 59.7275),
            [
                new GameBoyApuTraceEvent(0, 0xFF24, 0x77),
                new GameBoyApuTraceEvent(70_224, 0xFF12, 0xF0),
                new GameBoyApuTraceEvent(0, 0xFF30, 0x12),
            ]);

        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "theme.gbapu");
        GameBoyApuTraceBinary.Write(path, trace);

        Assert.True(GameBoyApuTraceBinary.LooksLikeBinary(path));
        var read = GameBoyApuTraceBinary.Read(path);

        Assert.Equal(trace.ClockHz, read.ClockHz);
        Assert.Equal(trace.DurationCycles, read.DurationCycles);
        Assert.Equal(trace.LoopCycle, read.LoopCycle);
        Assert.Equal("Theme", read.Metadata.Title);
        Assert.Equal("Composer", read.Metadata.Author);
        Assert.Equal(3, read.Metadata.Subsong);
        Assert.Equal(59.7275, read.Metadata.ReplayHz!.Value, 2);
        Assert.Equal(trace.Events.Count, read.Events.Count);
        Assert.Collection(
            read.Events,
            e => AssertEvent(e, 0, 0xFF24, 0x77),
            e => AssertEvent(e, 70_224, 0xFF12, 0xF0),
            e => AssertEvent(e, 0, 0xFF30, 0x12));
    }

    [Fact]
    public void Binary_and_json_sources_compile_to_identical_assets()
    {
        var trace = new GameBoyApuTrace(
            4_194_304,
            60,
            DurationCycles: 140_448,
            LoopCycle: 0,
            new GameBoyApuTraceMetadata("Theme"),
            [
                new GameBoyApuTraceEvent(0, 0xFF24, 0x77),
                new GameBoyApuTraceEvent(0, 0xFF25, 0xFF),
                new GameBoyApuTraceEvent(70_224, 0xFF12, 0xF0),
                new GameBoyApuTraceEvent(0, 0xFF14, 0x87),
            ]);

        var directory = CreateTempDirectory();
        var binaryPath = Path.Combine(directory, "theme.gbapu");
        var jsonPath = Path.Combine(directory, "theme.gbapu.json");
        GameBoyApuTraceBinary.Write(binaryPath, trace);
        GameBoyApuTraceFile.Write(jsonPath, trace);

        var fromBinary = GameBoyMusicAssetCompiler.CompileFromFile("theme", binaryPath);
        var fromJson = GameBoyMusicAssetCompiler.CompileFromFile("theme", jsonPath);

        Assert.Equal(GameBoyMusicAssetKind.ApuTrace, fromBinary.Kind);
        Assert.Equal(fromJson.Data, fromBinary.Data);
    }

    [Fact]
    public void LooksLikeBinary_is_false_for_json_traces()
    {
        var directory = CreateTempDirectory();
        var jsonPath = Path.Combine(directory, "theme.gbapu.json");
        File.WriteAllText(jsonPath, "{ \"format\": \"retrosharp.gbapu.v1\" }");

        Assert.False(GameBoyApuTraceBinary.LooksLikeBinary(jsonPath));
    }

    private static void AssertEvent(GameBoyApuTraceEvent traceEvent, long delta, ushort address, byte value)
    {
        Assert.Equal(delta, traceEvent.DeltaCycles);
        Assert.Equal(address, traceEvent.Address);
        Assert.Equal(value, traceEvent.Value);
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "retrosharp-gbapu-binary-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
