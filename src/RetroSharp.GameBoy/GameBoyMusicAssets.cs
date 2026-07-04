namespace RetroSharp.GameBoy;

using System.Globalization;
using System.Text;
using System.Text.Json;
using RetroSharp.Core.Sdk;

internal enum GameBoyMusicAssetKind
{
    UgeRows,
    ApuTrace,
}

internal sealed record GameBoyCompiledMusicAsset(string Name, GameBoyMusicAssetKind Kind, byte[] Data)
{
    public int RowCount => Kind != GameBoyMusicAssetKind.UgeRows || Data.Length < 3 ? 0 : Data[1] | (Data[2] << 8);
}

internal sealed record GameBoyCompiledSoundEffectAsset(string Name, byte[] Data);

internal static class GameBoyMusicAssetCompiler
{
    private const int RowsPerPattern = 64;
    private const int EmptyNote = 90;
    private const int WaveTableCount = 16;
    private const int WaveTableByteCount = 16;
    private const byte Duty1RowMask = 0x01;
    private const byte Duty2RowMask = 0x02;
    private const byte WaveRowMask = 0x04;
    private const byte NoiseRowMask = 0x08;
    private const byte ApuTraceMarker = 0x02;
    private const int ApuTraceHeaderLength = 5;
    private const byte ApuTraceWaveRamBlockCommand = 0xFF;
    private const long DmgClockHz = 4_194_304;
    private const long DmgFrameCycles = 70_224;

    public static GameBoyCompiledMusicAsset CompileFromFile(string name, string path)
    {
        var asset = ResolveMusicAsset(path);
        return asset.Format switch
        {
            "uge" => CompileSong(name, HugeSongReader.Read(asset.Path)),
            "gbapu" => CompileApuTrace(name, ReadApuTrace(asset.Path)),
            "vgm" => CompileApuTrace(name, ReadVgmDmgTrace(asset.Path)),
            _ => throw new InvalidOperationException($"Game Boy music asset '{Path.GetFileName(path)}' has unsupported format '{asset.Format}'."),
        };
    }

    private static GameBoyApuTrace ReadApuTrace(string path)
    {
        return GameBoyApuTraceBinary.LooksLikeBinary(path)
            ? GameBoyApuTraceBinary.Read(path)
            : GameBoyApuTraceFile.Read(path);
    }

    internal static GameBoyApuTrace ReadVgmDmgTrace(string path)
    {
        var stream = VgmImporter.Import(path, VgmChip.GameBoyDmg);
        var events = new List<GameBoyApuTraceEvent>();
        var previousCycle = 0L;
        foreach (var frame in stream.Frames)
        {
            var absoluteCycle = checked(frame.Index * DmgFrameCycles);
            foreach (var write in frame.Writes)
            {
                events.Add(new GameBoyApuTraceEvent(absoluteCycle - previousCycle, write.Address, write.Value));
                previousCycle = absoluteCycle;
            }
        }

        return new GameBoyApuTrace(
            (int)DmgClockHz,
            60,
            checked(stream.DurationFrames * DmgFrameCycles),
            checked(stream.LoopFrame * DmgFrameCycles),
            new GameBoyApuTraceMetadata(Source: Path.GetFileName(path)),
            events);
    }

    private static ResolvedMusicAsset ResolveMusicAsset(string path)
    {
        if (Path.GetExtension(path).Equals(".vgm", StringComparison.OrdinalIgnoreCase) ||
            Path.GetExtension(path).Equals(".vgz", StringComparison.OrdinalIgnoreCase))
        {
            return new ResolvedMusicAsset("vgm", path);
        }

        if (Path.GetExtension(path).Equals(".uge", StringComparison.OrdinalIgnoreCase))
        {
            return new ResolvedMusicAsset("uge", path);
        }

        if (GameBoyApuTraceBinary.LooksLikeBinary(path))
        {
            return new ResolvedMusicAsset("gbapu", path);
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var format = RequiredString(root, "format", Path.GetFileName(path));
        if (string.Equals(format, GameBoyApuTraceFile.Format, StringComparison.Ordinal))
        {
            return new ResolvedMusicAsset("gbapu", path);
        }

        if (!string.Equals(format, "retrosharp.music.v1", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Music asset '{Path.GetFileName(path)}' has unsupported format '{format}'.");
        }

        if (!root.TryGetProperty("platforms", out var platforms) ||
            !platforms.TryGetProperty("gb", out var gb))
        {
            throw new InvalidOperationException($"Music asset '{Path.GetFileName(path)}' does not declare a Game Boy platform variant.");
        }

        var gbFormat = RequiredString(gb, "format", Path.GetFileName(path));
        if (!string.Equals(gbFormat, "uge", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(gbFormat, "gbapu", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(gbFormat, "vgm", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Game Boy music asset '{Path.GetFileName(path)}' must use format 'uge', 'gbapu', or 'vgm', got '{gbFormat}'.");
        }

        var relativePath = RequiredString(gb, "path", Path.GetFileName(path));
        var resolved = Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.Combine(Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory(), relativePath);
        return new ResolvedMusicAsset(gbFormat.ToLowerInvariant(), Path.GetFullPath(resolved));
    }

    private static string RequiredString(JsonElement element, string property, string assetName)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Music asset '{assetName}' must declare string property '{property}'.");
        }

        return value.GetString() ?? "";
    }

    private static GameBoyCompiledMusicAsset CompileSong(string name, HugeSong song)
    {
        if (song.TimerPlayback)
        {
            throw new InvalidOperationException("hUGETracker timer-based tempo is not supported by the Game Boy BGM v1 runtime.");
        }

        if (song.TicksPerRow is < 1 or > 255)
        {
            throw new InvalidOperationException("hUGETracker ticks-per-row must be between 1 and 255 for the Game Boy BGM v1 runtime.");
        }

        var orderCount = new[] { song.Duty1Orders.Count, song.Duty2Orders.Count, song.WaveOrders.Count, song.NoiseOrders.Count }.Max();
        var rowCount = orderCount * RowsPerPattern;
        if (rowCount is < 1 or > ushort.MaxValue)
        {
            throw new InvalidOperationException("Game Boy BGM runtime supports 1..65535 flattened pattern rows.");
        }

        var data = new List<byte>(3 + WaveTableCount * WaveTableByteCount + rowCount * 15)
        {
            (byte)song.TicksPerRow,
            (byte)(rowCount & 0xFF),
            (byte)(rowCount >> 8),
        };

        foreach (var waveTable in song.WaveTables)
        {
            for (var i = 0; i < WaveTableByteCount; i++)
            {
                data.Add((byte)(((waveTable[i * 2] & 0x0F) << 4) | (waveTable[i * 2 + 1] & 0x0F)));
            }
        }

        data.AddRange(CompileRows(song, rowCount));

        return new GameBoyCompiledMusicAsset(name, GameBoyMusicAssetKind.UgeRows, data.ToArray());
    }

    internal static GameBoyCompiledMusicAsset CompileApuTrace(string name, GameBoyApuTrace trace)
    {
        if (trace.Events.Count == 0)
        {
            throw new InvalidOperationException("Game Boy APU trace must contain at least one event.");
        }

        var optimizedEvents = OptimizeApuTraceEvents(trace);
        var frameGroups = GroupApuTraceEvents(optimizedEvents, trace.LoopCycle);

        var loopGroupIndex = trace.LoopCycle <= 0
            ? 0
            : frameGroups.FindIndex(group => group.Events[0].AbsoluteCycles >= trace.LoopCycle);
        if (loopGroupIndex < 0)
        {
            loopGroupIndex = 0;
        }

        // v2 group-pool layout: deduplicate identical per-frame group bodies into a pool and
        // reference them from an order stream of (bodyOffset, waitAfter) entries. Musical
        // repetition makes most frames reuse a handful of bodies, while the body bytes stay the
        // exact v1 (command-count + commands) form, so the APU writes the runtime performs are
        // bit-identical to a flat trace; only the ROM layout and a small indirection differ.
        //
        //   [0]    marker 0x02
        //   [1..2] orderStartOffset (u16, from data start)
        //   [3..4] loopOrderOffset  (u16, from data start)
        //   [5..]  pool: concatenated unique group bodies (cmdCount + commands)
        //   order: { bodyOffsetLo, bodyOffsetHi, waitAfter } per frame, bodyOffset 0 = loop end
        var pool = new List<byte>();
        var bodyOffsets = new Dictionary<string, int>();
        var orderEntries = new List<(int BodyOffset, byte Wait)>(frameGroups.Count);

        var totalCycles = optimizedEvents[^1].AbsoluteCycles;
        var durationFrame = FrameForCycle(Math.Max(trace.DurationCycles, totalCycles), trace.ClockHz);
        for (var i = 0; i < frameGroups.Count; i++)
        {
            var body = BuildApuTraceGroupBody(frameGroups[i]);
            var key = Convert.ToHexString(body);
            if (!bodyOffsets.TryGetValue(key, out var bodyOffset))
            {
                bodyOffset = ApuTraceHeaderLength + pool.Count;
                if (bodyOffset > ushort.MaxValue)
                {
                    throw new InvalidOperationException("Game Boy APU trace pool exceeds the 32 KiB ROM-only music runtime limit.");
                }

                bodyOffsets[key] = bodyOffset;
                pool.AddRange(body);
            }

            var nextFrame = i + 1 < frameGroups.Count ? frameGroups[i + 1].Frame : durationFrame;
            orderEntries.Add((bodyOffset, CheckedFrameDelta(frameGroups[i].Frame, nextFrame)));
        }

        var orderStartOffset = ApuTraceHeaderLength + pool.Count;
        var loopOrderOffset = orderStartOffset + loopGroupIndex * 3;
        if (orderStartOffset + orderEntries.Count * 3 + 3 > ushort.MaxValue)
        {
            throw new InvalidOperationException("Game Boy APU trace order stream exceeds the 32 KiB ROM-only music runtime limit.");
        }

        var data = new List<byte>(ApuTraceHeaderLength + pool.Count + orderEntries.Count * 3 + 3)
        {
            ApuTraceMarker,
            (byte)(orderStartOffset & 0xFF),
            (byte)(orderStartOffset >> 8),
            (byte)(loopOrderOffset & 0xFF),
            (byte)(loopOrderOffset >> 8),
        };
        data.AddRange(pool);
        foreach (var (bodyOffset, wait) in orderEntries)
        {
            data.Add((byte)(bodyOffset & 0xFF));
            data.Add((byte)(bodyOffset >> 8));
            data.Add(wait);
        }

        // Loop sentinel: a body offset of 0 points at the header marker, never a real body.
        data.Add(0);
        data.Add(0);
        data.Add(0);
        return new GameBoyCompiledMusicAsset(name, GameBoyMusicAssetKind.ApuTrace, data.ToArray());
    }

    private static byte[] BuildApuTraceGroupBody(ApuTraceFrameGroup group)
    {
        var body = new List<byte>(1 + group.Events.Count * 2);
        var commandCount = 0;
        body.Add(0);

        for (var i = 0; i < group.Events.Count;)
        {
            if (TryAppendWaveRamBlock(body, group.Events, ref i))
            {
                commandCount++;
            }
            else
            {
                var traceEvent = group.Events[i];
                body.Add((byte)(traceEvent.Address & 0xFF));
                body.Add(traceEvent.Value);
                i++;
                commandCount++;
            }

            if (commandCount > byte.MaxValue)
            {
                throw new InvalidOperationException("Game Boy APU trace contains more than 255 register command groups in one frame.");
            }
        }

        body[0] = (byte)commandCount;
        return body.ToArray();
    }

    private static List<ApuTraceCompiledEvent> OptimizeApuTraceEvents(GameBoyApuTrace trace)
    {
        var events = new List<ApuTraceCompiledEvent>(trace.Events.Count);
        var lastValues = new Dictionary<ushort, byte>();
        var totalCycles = 0L;
        var loopCacheReset = trace.LoopCycle <= 0;

        foreach (var traceEvent in trace.Events)
        {
            if (!GameBoyApuTraceFile.IsSupportedApuAddress(traceEvent.Address))
            {
                throw new InvalidOperationException($"Game Boy APU trace event address {traceEvent.Address:X4} is not supported.");
            }

            totalCycles = checked(totalCycles + traceEvent.DeltaCycles);
            if (!loopCacheReset && totalCycles >= trace.LoopCycle)
            {
                lastValues.Clear();
                loopCacheReset = true;
            }

            if (!IsSideEffectfulApuWrite(traceEvent.Address, traceEvent.Value) &&
                lastValues.TryGetValue(traceEvent.Address, out var lastValue) &&
                lastValue == traceEvent.Value)
            {
                continue;
            }

            lastValues[traceEvent.Address] = traceEvent.Value;
            var frame = FrameForCycle(totalCycles, trace.ClockHz);
            events.Add(new ApuTraceCompiledEvent(totalCycles, frame, traceEvent.Address, traceEvent.Value));
        }

        return events;
    }

    private static bool IsSideEffectfulApuWrite(ushort address, byte value)
    {
        if (address == 0xFF26)
        {
            return true;
        }

        return (value & 0x80) != 0 && (address is 0xFF14 or 0xFF19 or 0xFF1E or 0xFF23);
    }

    private static List<ApuTraceFrameGroup> GroupApuTraceEvents(IReadOnlyList<ApuTraceCompiledEvent> events, long loopCycle)
    {
        var groups = new List<ApuTraceFrameGroup>();
        var currentFrame = events[0].Frame;
        var currentEvents = new List<ApuTraceCompiledEvent>();
        var loopSplitDone = loopCycle <= 0;
        foreach (var traceEvent in events)
        {
            var crossesLoop = !loopSplitDone && traceEvent.AbsoluteCycles >= loopCycle;
            if (currentEvents.Count > 0 && (traceEvent.Frame != currentFrame || crossesLoop))
            {
                groups.Add(new ApuTraceFrameGroup(currentFrame, currentEvents.ToArray()));
                currentFrame = traceEvent.Frame;
                currentEvents = [];
            }

            if (crossesLoop)
            {
                loopSplitDone = true;
            }

            currentEvents.Add(traceEvent);
        }

        groups.Add(new ApuTraceFrameGroup(currentFrame, currentEvents.ToArray()));
        return groups;
    }

    private static bool TryAppendWaveRamBlock(List<byte> data, IReadOnlyList<ApuTraceCompiledEvent> events, ref int index)
    {
        if (index + WaveTableByteCount > events.Count)
        {
            return false;
        }

        for (var i = 0; i < WaveTableByteCount; i++)
        {
            if (events[index + i].Address != 0xFF30 + i)
            {
                return false;
            }
        }

        data.Add(ApuTraceWaveRamBlockCommand);
        for (var i = 0; i < WaveTableByteCount; i++)
        {
            data.Add(events[index + i].Value);
        }

        index += WaveTableByteCount;
        return true;
    }

    private static byte CheckedFrameDelta(int currentFrame, int nextFrame)
    {
        var frames = nextFrame - currentFrame;
        if (frames is < 0 or > byte.MaxValue)
        {
            throw new InvalidOperationException("Game Boy APU trace contains an inter-frame delay longer than 255 frames; split the capture or add compression/banked ROM support first.");
        }

        return (byte)frames;
    }

    private static int FrameForCycle(long cycles, int clockHz)
    {
        // Map absolute APU cycles to the DMG VBlank frame the runtime player runs on.
        // The player ticks once per VBlank (~59.7275 Hz = 70224 cycles at 4.194304 MHz),
        // so use the true DMG frame period rather than a nominal 60 Hz, which drifts ~0.46%.
        var framePeriod = DmgFrameCycles * clockHz / DmgClockHz;
        if (framePeriod <= 0)
        {
            framePeriod = DmgFrameCycles;
        }

        var frames = checked((cycles + framePeriod / 2) / framePeriod);
        if (frames > int.MaxValue)
        {
            throw new InvalidOperationException("Game Boy APU trace duration exceeds the ROM-only music runtime limit.");
        }

        return (int)frames;
    }

    private static IEnumerable<byte> CompileRows(HugeSong song, int rowCount)
    {
        var data = new List<byte>();
        var duty1 = new DutyChannelState();
        var duty2 = new DutyChannelState();
        var wave = new WaveChannelState();
        var noise = new NoiseChannelState();
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var rowStart = data.Count;
            data.Add(0);

            AppendChannelRow(data, rowStart, Duty1RowMask, CompileDutyRow(song, song.Duty1Orders, rowIndex, "Duty 1", duty1));
            AppendChannelRow(data, rowStart, Duty2RowMask, CompileDutyRow(song, song.Duty2Orders, rowIndex, "Duty 2", duty2));
            AppendChannelRow(data, rowStart, WaveRowMask, CompileWaveRow(song, song.WaveOrders, rowIndex, wave));
            AppendChannelRow(data, rowStart, NoiseRowMask, CompileNoiseRow(song, song.NoiseOrders, rowIndex, noise));
        }

        return data;
    }

    private static void AppendChannelRow(List<byte> data, int rowStart, byte mask, MusicChannelRow row)
    {
        if (!row.HasEvent)
        {
            return;
        }

        data[rowStart] = (byte)(data[rowStart] | mask);
        data.AddRange(row.Bytes);
    }

    private static MusicChannelRow CompileDutyRow(HugeSong song, IReadOnlyList<int> orders, int rowIndex, string channelName, DutyChannelState state)
    {
        if (!TryGetRow(song, orders, rowIndex, out var row) || row.Note == EmptyNote)
        {
            return MusicChannelRow.Empty;
        }

        var instrumentIndex = InstrumentIndex(row, state.InstrumentIndex, channelName);
        if (instrumentIndex is < 1 or > 15)
        {
            throw new InvalidOperationException($"hUGETracker {channelName} rows must reference duty instrument 1..15.");
        }

        var instrument = song.DutyInstruments[instrumentIndex - 1];
        var frequency = FrequencyForNote(row.Note);
        state.InstrumentIndex = instrumentIndex;
        state.Duty = (byte)(instrument.Duty & 0xC0);
        state.Envelope = ApplyVolumeEffect(
            (byte)(((instrument.Volume & 0x0F) << 4) | ((instrument.EnvelopeDirection & 0x01) << 3) | (instrument.EnvelopeSweep & 0x07)),
            row);
        state.FrequencyLow = (byte)(frequency & 0xFF);
        state.Control = (byte)(0x80 | ((frequency >> 8) & 0x07));
        ValidateSupportedEffect(row);
        return new MusicChannelRow(true, state.Bytes());
    }

    private static MusicChannelRow CompileWaveRow(HugeSong song, IReadOnlyList<int> orders, int rowIndex, WaveChannelState state)
    {
        if (!TryGetRow(song, orders, rowIndex, out var row) || row.Note == EmptyNote)
        {
            return MusicChannelRow.Empty;
        }

        var instrumentIndex = InstrumentIndex(row, state.InstrumentIndex, "Wave");
        if (instrumentIndex is < 1 or > 15)
        {
            throw new InvalidOperationException("hUGETracker Wave rows must reference wave instrument 1..15.");
        }

        var instrument = song.WaveInstruments[instrumentIndex - 1];
        var frequency = FrequencyForNote(row.Note);
        state.InstrumentIndex = instrumentIndex;
        state.WaveIndex = (byte)(instrument.WaveIndex & 0x0F);
        state.Volume = ApplyWaveVolumeEffect((byte)((instrument.WaveVolume & 0x03) << 5), row);
        state.FrequencyLow = (byte)(frequency & 0xFF);
        state.Control = (byte)(0x80 | ((frequency >> 8) & 0x07));
        ValidateSupportedEffect(row);
        return new MusicChannelRow(true, state.Bytes());
    }

    private static MusicChannelRow CompileNoiseRow(HugeSong song, IReadOnlyList<int> orders, int rowIndex, NoiseChannelState state)
    {
        if (!TryGetRow(song, orders, rowIndex, out var row) || row.Note == EmptyNote)
        {
            return MusicChannelRow.Empty;
        }

        var instrumentIndex = InstrumentIndex(row, state.InstrumentIndex, "Noise");
        if (instrumentIndex is < 1 or > 15)
        {
            throw new InvalidOperationException("hUGETracker Noise rows must reference noise instrument 1..15.");
        }

        var instrument = song.NoiseInstruments[instrumentIndex - 1];
        state.InstrumentIndex = instrumentIndex;
        state.Envelope = ApplyVolumeEffect(
            (byte)(((instrument.Volume & 0x0F) << 4) | ((instrument.EnvelopeDirection & 0x01) << 3) | (instrument.EnvelopeSweep & 0x07)),
            row);
        state.Polynomial = NoiseRegisterForNote(row.Note, instrument.NoiseMode);
        state.Control = 0x80;
        ValidateSupportedEffect(row);
        return new MusicChannelRow(true, state.Bytes());
    }

    private static bool TryGetRow(HugeSong song, IReadOnlyList<int> orders, int rowIndex, out HugePatternRow row)
    {
        var orderIndex = rowIndex / RowsPerPattern;
        if (orderIndex >= orders.Count)
        {
            row = EmptyRow;
            return false;
        }

        row = Pattern(song, orders[orderIndex]).Rows[rowIndex % RowsPerPattern];
        return true;
    }

    private static int InstrumentIndex(HugePatternRow row, int previousInstrument, string channelName)
    {
        if (row.Instrument != 0)
        {
            return row.Instrument;
        }

        if (previousInstrument != 0)
        {
            return previousInstrument;
        }

        throw new InvalidOperationException($"hUGETracker {channelName} note rows must reference an instrument before instrument-less notes can be used.");
    }

    private static HugePattern Pattern(HugeSong song, int index)
    {
        if (!song.Patterns.TryGetValue(index, out var pattern))
        {
            throw new InvalidOperationException($"hUGETracker order references missing pattern {index.ToString(CultureInfo.InvariantCulture)}.");
        }

        return pattern;
    }

    private static void ValidateSupportedEffect(HugePatternRow row)
    {
        if (row.JumpCommand != 0)
        {
            throw new InvalidOperationException("hUGETracker routine jump command values are not supported by the Game Boy BGM runtime.");
        }

        if (row.EffectCode is 0 or 2 or 3 or 11 or 12 or 14)
        {
            return;
        }

        throw new InvalidOperationException($"hUGETracker effect {row.EffectCode:X} is not supported by the Game Boy BGM runtime.");
    }

    private static byte ApplyVolumeEffect(byte envelope, HugePatternRow row)
    {
        if (row.EffectCode != 12)
        {
            return envelope;
        }

        var volume = row.EffectArgument & 0x0F;
        var envelopeBits = (row.EffectArgument >> 4) & 0x0F;
        return (byte)((volume << 4) | envelopeBits);
    }

    private static byte ApplyWaveVolumeEffect(byte volume, HugePatternRow row)
    {
        if (row.EffectCode != 12)
        {
            return volume;
        }

        return (byte)((Math.Clamp(row.EffectArgument & 0x0F, 0, 3) & 0x03) << 5);
    }

    private static int FrequencyForNote(int note)
    {
        if (note is < 0 or >= EmptyNote)
        {
            throw new InvalidOperationException($"hUGETracker note {note.ToString(CultureInfo.InvariantCulture)} is outside the supported C3..B8 range.");
        }

        var frequencyHz = 130.81278265 * Math.Pow(2.0, note / 12.0);
        var register = (int)Math.Round(2048.0 - 131072.0 / frequencyHz);
        return Math.Clamp(register, 0, 2047);
    }

    private static byte NoiseRegisterForNote(int note, uint noiseMode)
    {
        if (note is < 0 or >= EmptyNote)
        {
            throw new InvalidOperationException($"hUGETracker noise note {note.ToString(CultureInfo.InvariantCulture)} is outside the supported range.");
        }

        var shift = Math.Clamp(14 - note / 6, 0, 15);
        var divisor = note & 0x07;
        return (byte)((shift << 4) | ((noiseMode & 0x01) == 0 ? 0 : 0x08) | divisor);
    }

    private static readonly HugePatternRow EmptyRow = new(EmptyNote, 0, 0, 0, 0);

    private sealed record ResolvedMusicAsset(string Format, string Path);

    private sealed record ApuTraceCompiledEvent(long AbsoluteCycles, int Frame, ushort Address, byte Value);

    private sealed record ApuTraceFrameGroup(int Frame, IReadOnlyList<ApuTraceCompiledEvent> Events);

    private sealed record HugeSong(
        int TicksPerRow,
        bool TimerPlayback,
        IReadOnlyList<HugeInstrument> DutyInstruments,
        IReadOnlyList<HugeInstrument> WaveInstruments,
        IReadOnlyList<HugeInstrument> NoiseInstruments,
        IReadOnlyList<IReadOnlyList<byte>> WaveTables,
        IReadOnlyDictionary<int, HugePattern> Patterns,
        IReadOnlyList<int> Duty1Orders,
        IReadOnlyList<int> Duty2Orders,
        IReadOnlyList<int> WaveOrders,
        IReadOnlyList<int> NoiseOrders);

    private sealed record HugeInstrument(
        byte Volume,
        int EnvelopeDirection,
        byte EnvelopeSweep,
        byte Duty,
        uint WaveVolume,
        uint WaveIndex,
        uint NoiseMode,
        bool SubpatternEnabled);

    private sealed record HugePattern(int Index, IReadOnlyList<HugePatternRow> Rows);

    private sealed record HugePatternRow(int Note, int Instrument, int JumpCommand, int EffectCode, byte EffectArgument);

    private readonly record struct MusicChannelRow(bool HasEvent, byte[] Bytes)
    {
        public static MusicChannelRow Empty { get; } = new(false, []);
    }

    private static class HugeSongReader
    {
        public static HugeSong Read(string path)
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

            var version = checked((int)reader.ReadUInt32());
            if (version is not 6)
            {
                throw new InvalidOperationException($"hUGETracker .uge version {version.ToString(CultureInfo.InvariantCulture)} is not supported. Use v6.");
            }

            _ = ReadShortString(reader);
            _ = ReadShortString(reader);
            _ = ReadShortString(reader);

            var dutyInstruments = Enumerable.Range(0, 15).Select(_ => ReadInstrument(reader)).ToArray();
            var waveInstruments = Enumerable.Range(0, 15).Select(_ => ReadInstrument(reader)).ToArray();
            var noiseInstruments = Enumerable.Range(0, 15).Select(_ => ReadInstrument(reader)).ToArray();

            var waveTables = new List<IReadOnlyList<byte>>();
            for (var wave = 0; wave < 16; wave++)
            {
                waveTables.Add(reader.ReadBytes(32));
            }

            var ticksPerRow = checked((int)reader.ReadUInt32());
            var timerPlayback = reader.ReadBoolean();
            _ = reader.ReadUInt32();

            var patternCount = checked((int)reader.ReadUInt32());
            var patterns = new Dictionary<int, HugePattern>();
            for (var i = 0; i < patternCount; i++)
            {
                var pattern = ReadPattern(reader);
                patterns[pattern.Index] = pattern;
            }

            var duty1Orders = ReadOrders(reader);
            var duty2Orders = ReadOrders(reader);
            var waveOrders = ReadOrders(reader);
            var noiseOrders = ReadOrders(reader);

            for (var i = 0; i < 16 && reader.BaseStream.Position < reader.BaseStream.Length; i++)
            {
                _ = ReadString(reader);
            }

            return new HugeSong(
                ticksPerRow,
                timerPlayback,
                dutyInstruments,
                waveInstruments,
                noiseInstruments,
                waveTables,
                patterns,
                duty1Orders,
                duty2Orders,
                waveOrders,
                noiseOrders);
        }

        private static HugeInstrument ReadInstrument(BinaryReader reader)
        {
            _ = reader.ReadUInt32();
            _ = ReadShortString(reader);
            _ = reader.ReadUInt32();
            _ = reader.ReadBoolean();
            var volume = reader.ReadByte();
            var envelopeDirection = checked((int)reader.ReadUInt32());
            var envelopeSweep = reader.ReadByte();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            var duty = reader.ReadByte();
            var waveVolume = reader.ReadUInt32();
            var waveIndex = reader.ReadUInt32();
            var unusedOrNoiseMode = reader.ReadUInt32();
            var subpatternEnabled = reader.ReadBoolean();
            for (var i = 0; i < RowsPerPattern; i++)
            {
                _ = ReadPatternRow(reader);
            }

            return new HugeInstrument(volume, envelopeDirection, envelopeSweep, duty, waveVolume, waveIndex, unusedOrNoiseMode, subpatternEnabled);
        }

        private static HugePattern ReadPattern(BinaryReader reader)
        {
            var index = checked((int)reader.ReadUInt32());
            var rows = new HugePatternRow[RowsPerPattern];
            for (var i = 0; i < RowsPerPattern; i++)
            {
                rows[i] = ReadPatternRow(reader);
            }

            return new HugePattern(index, rows);
        }

        private static HugePatternRow ReadPatternRow(BinaryReader reader)
        {
            return new HugePatternRow(
                checked((int)reader.ReadUInt32()),
                checked((int)reader.ReadUInt32()),
                checked((int)reader.ReadUInt32()),
                checked((int)reader.ReadUInt32()),
                reader.ReadByte());
        }

        private static IReadOnlyList<int> ReadOrders(BinaryReader reader)
        {
            var count = checked((int)reader.ReadUInt32());
            var orders = new int[count];
            for (var i = 0; i < count; i++)
            {
                orders[i] = checked((int)reader.ReadUInt32());
            }

            return orders.Take(Math.Max(0, count - 1)).ToArray();
        }

        private static string ReadShortString(BinaryReader reader)
        {
            var length = reader.ReadByte();
            var bytes = reader.ReadBytes(255);
            return Encoding.UTF8.GetString(bytes, 0, Math.Min(length, bytes.Length));
        }

        private static string ReadString(BinaryReader reader)
        {
            var length = checked((int)reader.ReadUInt32());
            var bytes = reader.ReadBytes(length);
            if (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                _ = reader.ReadByte();
            }

            return Encoding.UTF8.GetString(bytes);
        }
    }

    private sealed class DutyChannelState
    {
        public int InstrumentIndex { get; set; }

        public byte Duty { get; set; }

        public byte Envelope { get; set; }

        public byte FrequencyLow { get; set; }

        public byte Control { get; set; }

        public byte[] Bytes() => [Duty, Envelope, FrequencyLow, Control];

        public byte[] WithoutTrigger() => [Duty, Envelope, FrequencyLow, (byte)(Control & 0x7F)];
    }

    private sealed class WaveChannelState
    {
        public int InstrumentIndex { get; set; }

        public byte WaveIndex { get; set; }

        public byte Volume { get; set; }

        public byte FrequencyLow { get; set; }

        public byte Control { get; set; }

        public byte[] Bytes() => [WaveIndex, Volume, FrequencyLow, Control];

        public byte[] WithoutTrigger() => [WaveIndex, Volume, FrequencyLow, (byte)(Control & 0x7F)];
    }

    private sealed class NoiseChannelState
    {
        public int InstrumentIndex { get; set; }

        public byte Envelope { get; set; }

        public byte Polynomial { get; set; }

        public byte Control { get; set; }

        public byte[] Bytes() => [Envelope, Polynomial, Control];

        public byte[] WithoutTrigger() => [Envelope, Polynomial, (byte)(Control & 0x7F)];
    }
}

internal static class GameBoySoundEffectAssetCompiler
{
    public static GameBoyCompiledSoundEffectAsset CompileFromFile(string name, string path)
    {
        var resolvedPath = PlatformAssetPathResolver.ResolveVariant(path, "gb");
        if (!Path.GetExtension(resolvedPath).Equals(".vgm", StringComparison.OrdinalIgnoreCase) &&
            !Path.GetExtension(resolvedPath).Equals(".vgz", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Game Boy SFX asset '{Path.GetFileName(path)}' must use VGM/VGZ input.");
        }

        var trace = GameBoyMusicAssetCompiler.ReadVgmDmgTrace(resolvedPath);
        var asset = GameBoyMusicAssetCompiler.CompileApuTrace(name, trace);
        return new GameBoyCompiledSoundEffectAsset(name, asset.Data);
    }
}
