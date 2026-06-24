namespace RetroSharp.GameBoy.Tests;

using System.Text.Json;
using RetroSharp.GameBoy;
using Xunit;

public sealed class GameBoyGbsToGbApuExporterTests
{
    [Fact]
    public void Exports_supported_apu_register_writes_to_gbapu_trace_json()
    {
        var directory = CreateTempDirectory();
        var gbsPath = Path.Combine(directory, "stage.gbs");
        var gbapuPath = Path.Combine(directory, "stage.gbapu.json");
        WriteGbsHeader(gbsPath, subsongs: 1);

        var result = GameBoyGbsToGbApuExporter.Export(
            new GameBoyGbsToGbApuOptions(gbsPath, gbapuPath, Subsong: 1, Seconds: 2, LoopCycle: 0, GbsPlayPath: "fake-gbsplay"),
            new FakeTraceSource(
            [
                "dumping subsong 0",
                "subsong 0",
                "00000000 ff24=77",
                "00000000 ff25=ff",
                "00000100 ff10=16",
                "00000000 ff14=87",
                "00000010 ff30=12",
                "00000000 ff3f=ef",
                "00000000 ff40=91",
            ]));

        Assert.Equal(1, result.Subsong);
        Assert.Equal(6, result.EventCount);
        Assert.True(File.Exists(gbapuPath));

        using var document = JsonDocument.Parse(File.ReadAllText(gbapuPath));
        var root = document.RootElement;
        Assert.Equal("retrosharp.gbapu.v1", root.GetProperty("format").GetString());
        Assert.Equal(4_194_304, root.GetProperty("clockHz").GetInt32());
        Assert.Equal(60, root.GetProperty("framesPerSecond").GetInt32());
        Assert.Equal(0, root.GetProperty("loopCycle").GetInt64());
        Assert.Equal("Fixture Song", root.GetProperty("metadata").GetProperty("title").GetString());

        var events = root.GetProperty("events").EnumerateArray().ToArray();
        Assert.Collection(
            events,
            e => AssertEvent(e, 0, "FF24", "77"),
            e => AssertEvent(e, 0, "FF25", "FF"),
            e => AssertEvent(e, 0x100, "FF10", "16"),
            e => AssertEvent(e, 0, "FF14", "87"),
            e => AssertEvent(e, 0x10, "FF30", "12"),
            e => AssertEvent(e, 0, "FF3F", "EF"));
    }

    private static void AssertEvent(JsonElement element, long deltaCycles, string address, string value)
    {
        Assert.Equal(deltaCycles, element.GetProperty("deltaCycles").GetInt64());
        Assert.Equal(address, element.GetProperty("address").GetString());
        Assert.Equal(value, element.GetProperty("value").GetString());
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "retrosharp-gbs-to-gbapu-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void WriteGbsHeader(string path, byte subsongs)
    {
        var header = new byte[0x70];
        header[0] = (byte)'G';
        header[1] = (byte)'B';
        header[2] = (byte)'S';
        header[3] = 1;
        header[4] = subsongs;
        header[5] = 1;
        WriteUInt16(header, 0x06, 0x4000);
        WriteUInt16(header, 0x08, 0x4000);
        WriteUInt16(header, 0x0A, 0x4010);
        WriteUInt16(header, 0x0C, 0xFFFE);
        WriteAscii(header, 0x10, 32, "Fixture Song");
        WriteAscii(header, 0x30, 32, "RetroSharp");
        WriteAscii(header, 0x50, 32, "Test");
        File.WriteAllBytes(path, header);
    }

    private static void WriteUInt16(byte[] bytes, int offset, ushort value)
    {
        bytes[offset] = (byte)(value & 0xFF);
        bytes[offset + 1] = (byte)(value >> 8);
    }

    private static void WriteAscii(byte[] bytes, int offset, int length, string value)
    {
        var encoded = System.Text.Encoding.ASCII.GetBytes(value);
        Array.Copy(encoded, 0, bytes, offset, Math.Min(encoded.Length, length));
    }

    private sealed class FakeTraceSource(IReadOnlyList<string> lines) : IGameBoyGbsTraceSource
    {
        public IReadOnlyList<string> Capture(GameBoyGbsTraceOptions options) => lines;
    }
}
