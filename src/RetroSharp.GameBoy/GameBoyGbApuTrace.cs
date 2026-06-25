namespace RetroSharp.GameBoy;

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

public sealed record GameBoyGbsToGbApuOptions(
    string InputPath,
    string OutputPath,
    int Subsong = 1,
    int Seconds = 60,
    long LoopCycle = 0,
    string GbsPlayPath = "gbsplay",
    bool AutoLoop = true,
    bool EmitJson = false);

public sealed record GameBoyGbsToGbApuExportResult(
    GameBoyGbsHeader Header,
    int Subsong,
    int EventCount,
    long DurationCycles,
    long LoopCycle);

public sealed record GameBoyApuTrace(
    int ClockHz,
    int FramesPerSecond,
    long DurationCycles,
    long LoopCycle,
    GameBoyApuTraceMetadata Metadata,
    IReadOnlyList<GameBoyApuTraceEvent> Events);

public sealed record GameBoyApuTraceMetadata(
    string? Title = null,
    string? Author = null,
    string? Copyright = null,
    int? Subsong = null,
    string? Source = null,
    double? ReplayHz = null);

public sealed record GameBoyApuTraceEvent(long DeltaCycles, ushort Address, byte Value);

public static partial class GameBoyGbsToGbApuExporter
{
    private const int DefaultClockHz = 4_194_304;
    private const int DefaultFramesPerSecond = 60;

    internal static GameBoyGbsToGbApuExportResult Export(GameBoyGbsToGbApuOptions options, IGameBoyGbsTraceSource traceSource)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(traceSource);

        var header = GameBoyGbsFile.ReadHeader(options.InputPath);
        ValidateOptions(options, header);

        var traceLines = traceSource.Capture(new GameBoyGbsTraceOptions(
            options.InputPath,
            options.Subsong,
            options.Seconds,
            options.GbsPlayPath));
        var events = ParseTraceEvents(traceLines);
        if (events.Count == 0)
        {
            throw new InvalidOperationException("GBS trace did not contain Game Boy APU register writes.");
        }

        var loopCycle = options.LoopCycle;
        var durationCycles = checked((long)DefaultClockHz * options.Seconds);

        // Auto-detect the musical loop unless the caller pinned it manually. On success the
        // capture is trimmed to intro + exactly one loop body, which is the dominant size win.
        if (options.AutoLoop && options.LoopCycle <= 0)
        {
            var loop = GameBoyApuLoopDetector.Detect(events, DefaultClockHz);
            if (loop is not null)
            {
                loopCycle = loop.LoopStartCycle;
                durationCycles = loop.LoopEndCycle;
                events = TrimEventsToCycle(events, loop.LoopEndCycle);
            }
        }

        var trace = new GameBoyApuTrace(
            DefaultClockHz,
            DefaultFramesPerSecond,
            durationCycles,
            loopCycle,
            new GameBoyApuTraceMetadata(
                header.Title,
                header.Author,
                header.Copyright,
                options.Subsong,
                Path.GetFileName(options.InputPath),
                GameBoyGbsReplayRate.FromHeader(header)),
            events);

        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(options.OutputPath));
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var wantsJson = options.EmitJson
            || Path.GetExtension(options.OutputPath).Equals(".json", StringComparison.OrdinalIgnoreCase);
        if (wantsJson)
        {
            GameBoyApuTraceFile.Write(options.OutputPath, trace);
        }
        else
        {
            GameBoyApuTraceBinary.Write(options.OutputPath, trace);
        }

        return new GameBoyGbsToGbApuExportResult(header, options.Subsong, events.Count, durationCycles, loopCycle);
    }

    private static List<GameBoyApuTraceEvent> TrimEventsToCycle(List<GameBoyApuTraceEvent> events, long endCycle)
    {
        var trimmed = new List<GameBoyApuTraceEvent>(events.Count);
        var totalCycles = 0L;
        foreach (var traceEvent in events)
        {
            totalCycles = checked(totalCycles + traceEvent.DeltaCycles);
            if (totalCycles >= endCycle)
            {
                break;
            }

            trimmed.Add(traceEvent);
        }

        return trimmed.Count == 0 ? events : trimmed;
    }

    public static GameBoyGbsToGbApuExportResult Export(GameBoyGbsToGbApuOptions options)
    {
        return Export(options, new GbsPlayTraceSource());
    }

    private static void ValidateOptions(GameBoyGbsToGbApuOptions options, GameBoyGbsHeader header)
    {
        if (string.IsNullOrWhiteSpace(options.OutputPath))
        {
            throw new ArgumentException("GBS to GBAPU export requires --out <file.gbapu.json>.");
        }

        if (options.Subsong < 1 || options.Subsong > header.SubsongCount)
        {
            throw new ArgumentException(
                $"GBS subsong {options.Subsong.ToString(CultureInfo.InvariantCulture)} is outside the available 1..{header.SubsongCount.ToString(CultureInfo.InvariantCulture)} range.");
        }

        if (options.Seconds < 1)
        {
            throw new ArgumentException("GBS to GBAPU export requires --seconds to be at least 1.");
        }

        if (options.LoopCycle < 0)
        {
            throw new ArgumentException("GBS to GBAPU export requires --loop-cycle to be zero or greater.");
        }

        if (options.LoopCycle > checked((long)DefaultClockHz * options.Seconds))
        {
            throw new ArgumentException("GBS to GBAPU export requires --loop-cycle to be within the captured duration.");
        }

        if (string.IsNullOrWhiteSpace(options.GbsPlayPath))
        {
            throw new ArgumentException("GBS to GBAPU export requires a non-empty --gbsplay path.");
        }
    }

    private static List<GameBoyApuTraceEvent> ParseTraceEvents(IReadOnlyList<string> traceLines)
    {
        var events = new List<GameBoyApuTraceEvent>();
        foreach (var line in traceLines)
        {
            var match = TraceLineRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var address = System.Convert.ToUInt16(match.Groups["address"].Value, 16);
            if (!GameBoyApuTraceFile.IsSupportedApuAddress(address))
            {
                continue;
            }

            events.Add(new GameBoyApuTraceEvent(
                System.Convert.ToInt64(match.Groups["delta"].Value, 16),
                address,
                System.Convert.ToByte(match.Groups["value"].Value, 16)));
        }

        return events;
    }

    [GeneratedRegex(@"^\s*(?<delta>[0-9a-fA-F]+)\s+(?<address>[0-9a-fA-F]{4})=(?<value>[0-9a-fA-F]{2})\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex TraceLineRegex();
}

public static class GameBoyApuTraceFile
{
    public const string Format = "retrosharp.gbapu.v1";

    public static GameBoyApuTrace Read(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var assetName = Path.GetFileName(path);
        var format = RequiredString(root, "format", assetName);
        if (!string.Equals(format, Format, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Game Boy APU trace '{assetName}' has unsupported format '{format}'.");
        }

        var clockHz = RequiredInt(root, "clockHz", assetName, 1, int.MaxValue);
        var framesPerSecond = RequiredInt(root, "framesPerSecond", assetName, 1, 255);
        var durationCycles = RequiredLong(root, "durationCycles", assetName, 0, long.MaxValue);
        var loopCycle = RequiredLong(root, "loopCycle", assetName, 0, durationCycles);
        var metadata = ReadMetadata(root);

        if (!root.TryGetProperty("events", out var eventsElement) || eventsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Game Boy APU trace '{assetName}' must declare array property 'events'.");
        }

        var events = new List<GameBoyApuTraceEvent>();
        foreach (var eventElement in eventsElement.EnumerateArray())
        {
            var deltaCycles = RequiredLong(eventElement, "deltaCycles", assetName, 0, long.MaxValue);
            var address = RequiredHexUInt16(eventElement, "address", assetName);
            var value = RequiredHexByte(eventElement, "value", assetName);
            if (!IsSupportedApuAddress(address))
            {
                throw new InvalidOperationException(
                    $"Game Boy APU trace '{assetName}' contains unsupported APU address {Hex16(address)}.");
            }

            events.Add(new GameBoyApuTraceEvent(deltaCycles, address, value));
        }

        if (events.Count == 0)
        {
            throw new InvalidOperationException($"Game Boy APU trace '{assetName}' must contain at least one event.");
        }

        return new GameBoyApuTrace(clockHz, framesPerSecond, durationCycles, loopCycle, metadata, events);
    }

    public static void Write(string path, GameBoyApuTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        writer.WriteString("format", Format);
        writer.WriteNumber("clockHz", trace.ClockHz);
        writer.WriteNumber("framesPerSecond", trace.FramesPerSecond);
        writer.WriteNumber("durationCycles", trace.DurationCycles);
        writer.WriteNumber("loopCycle", trace.LoopCycle);
        WriteMetadata(writer, trace.Metadata);
        writer.WriteStartArray("events");
        foreach (var traceEvent in trace.Events)
        {
            writer.WriteStartObject();
            writer.WriteNumber("deltaCycles", traceEvent.DeltaCycles);
            writer.WriteString("address", Hex16(traceEvent.Address));
            writer.WriteString("value", Hex8(traceEvent.Value));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    internal static bool IsSupportedApuAddress(ushort address)
    {
        return address is >= 0xFF10 and <= 0xFF26 or >= 0xFF30 and <= 0xFF3F;
    }

    private static GameBoyApuTraceMetadata ReadMetadata(JsonElement root)
    {
        if (!root.TryGetProperty("metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object)
        {
            return new GameBoyApuTraceMetadata();
        }

        return new GameBoyApuTraceMetadata(
            OptionalString(metadata, "title"),
            OptionalString(metadata, "author"),
            OptionalString(metadata, "copyright"),
            metadata.TryGetProperty("subsong", out var subsong) && subsong.ValueKind == JsonValueKind.Number ? subsong.GetInt32() : null,
            OptionalString(metadata, "source"),
            metadata.TryGetProperty("replayHz", out var replayHz) && replayHz.ValueKind == JsonValueKind.Number ? replayHz.GetDouble() : null);
    }

    private static void WriteMetadata(Utf8JsonWriter writer, GameBoyApuTraceMetadata metadata)
    {
        writer.WriteStartObject("metadata");
        if (!string.IsNullOrEmpty(metadata.Title)) writer.WriteString("title", metadata.Title);
        if (!string.IsNullOrEmpty(metadata.Author)) writer.WriteString("author", metadata.Author);
        if (!string.IsNullOrEmpty(metadata.Copyright)) writer.WriteString("copyright", metadata.Copyright);
        if (metadata.Subsong.HasValue) writer.WriteNumber("subsong", metadata.Subsong.Value);
        if (!string.IsNullOrEmpty(metadata.Source)) writer.WriteString("source", metadata.Source);
        if (metadata.ReplayHz.HasValue) writer.WriteNumber("replayHz", Math.Round(metadata.ReplayHz.Value, 4));
        writer.WriteEndObject();
    }

    private static string? OptionalString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string RequiredString(JsonElement element, string property, string assetName)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Game Boy APU trace '{assetName}' must declare string property '{property}'.");
        }

        return value.GetString() ?? "";
    }

    private static int RequiredInt(JsonElement element, string property, string assetName, int min, int max)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var parsed) || parsed < min || parsed > max)
        {
            throw new InvalidOperationException($"Game Boy APU trace '{assetName}' must declare integer property '{property}' in range {min.ToString(CultureInfo.InvariantCulture)}..{max.ToString(CultureInfo.InvariantCulture)}.");
        }

        return parsed;
    }

    private static long RequiredLong(JsonElement element, string property, string assetName, long min, long max)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var parsed) || parsed < min || parsed > max)
        {
            throw new InvalidOperationException($"Game Boy APU trace '{assetName}' must declare integer property '{property}' in range {min.ToString(CultureInfo.InvariantCulture)}..{max.ToString(CultureInfo.InvariantCulture)}.");
        }

        return parsed;
    }

    private static ushort RequiredHexUInt16(JsonElement element, string property, string assetName)
    {
        var value = RequiredString(element, property, assetName);
        if (!ushort.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidOperationException($"Game Boy APU trace '{assetName}' property '{property}' must be a four-digit hex value.");
        }

        return parsed;
    }

    private static byte RequiredHexByte(JsonElement element, string property, string assetName)
    {
        var value = RequiredString(element, property, assetName);
        if (!byte.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidOperationException($"Game Boy APU trace '{assetName}' property '{property}' must be a two-digit hex value.");
        }

        return parsed;
    }

    private static string Hex16(ushort value)
    {
        return value.ToString("X4", CultureInfo.InvariantCulture);
    }

    private static string Hex8(byte value)
    {
        return value.ToString("X2", CultureInfo.InvariantCulture);
    }
}
