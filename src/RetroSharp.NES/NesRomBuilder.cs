using System.Globalization;
using RetroSharp.Core;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;
using RetroSharp.Sdk;

namespace RetroSharp.NES;

internal static class NesRomBuilder
{
    private const int PrgRomSize = 32 * 1024;
    private const int ChrRomSize = 8 * 1024;

    public static byte[] Build(NesVideoProgram program, bool useFourScreenNametables)
    {
        var prg = BuildPrgRom(program, useFourScreenNametables);
        var chr = BuildChrRom(program);

        var rom = new byte[16 + PrgRomSize + ChrRomSize];
        rom[0] = (byte)'N';
        rom[1] = (byte)'E';
        rom[2] = (byte)'S';
        rom[3] = 0x1A;
        rom[4] = PrgRomSize / (16 * 1024);
        rom[5] = 1;
        rom[6] = (byte)(useFourScreenNametables ? 0x09 : 0x01);

        prg.CopyTo(rom, 16);
        chr.CopyTo(rom, 16 + PrgRomSize);
        return rom;
    }

    private static byte[] BuildPrgRom(NesVideoProgram program, bool useFourScreenNametables)
    {
        var longForLoopIds = new HashSet<int>();
        while (true)
        {
            try
            {
                return BuildPrgRom(program, longForLoopIds, useFourScreenNametables);
            }
            catch (BranchOutOfRangeException ex) when (TryForEndLabelId(ex.Label, out var id) && longForLoopIds.Add(id))
            {
            }
        }
    }

    private static byte[] BuildPrgRom(NesVideoProgram program, IReadOnlySet<int> longForLoopIds, bool useFourScreenNametables)
    {
        var builder = new PrgBuilder();
        var nameTableUploadByteCount = useFourScreenNametables ? 4096 : 2048;

        builder.Emit(0x78);                         // SEI
        builder.Emit(0xD8);                         // CLD
        builder.Emit(0xA2, 0x40);                   // LDX #$40
        builder.Emit(0x8E, 0x17, 0x40);             // STX $4017
        builder.Emit(0xA2, 0xFF);                   // LDX #$FF
        builder.Emit(0x9A);                         // TXS
        builder.Emit(0xE8);                         // INX
        builder.Emit(0x8E, 0x00, 0x20);             // STX $2000
        builder.Emit(0x8E, 0x01, 0x20);             // STX $2001
        builder.Emit(0x8E, 0x10, 0x40);             // STX $4010

        EmitWaitVBlank(builder, "vblank1");
        EmitWaitVBlank(builder, "vblank2");
        EmitPaletteUpload(builder);
        EmitNameTableUpload(builder, nameTableUploadByteCount);

        var runtimeCompiler = new NesRuntimeCompiler(builder, program, longForLoopIds, useFourScreenNametables);
        runtimeCompiler.EmitInitialization();

        builder.Emit(0xA9, 0x00);                   // LDA #$00
        builder.Emit(0x8D, 0x05, 0x20);             // STA $2005
        builder.Emit(0x8D, 0x05, 0x20);             // STA $2005
        builder.Emit(0x8D, 0x00, 0x20);             // STA $2000
        builder.Emit(0xA9, 0x1E);                   // LDA #$1E
        builder.Emit(0x8D, 0x01, 0x20);             // STA $2001

        runtimeCompiler.Emit(program.MainBlock);

        builder.Label("forever");
        builder.JumpAbsolute("forever");

        runtimeCompiler.EmitAudioSubroutines();

        builder.Label("palette");
        builder.Emit(program.Palette);
        builder.Label("nametable");
        builder.Emit(program.NameTable.Take(nameTableUploadByteCount).ToArray());
        EmitWorldMapRows(builder, program.WorldMap, program.WorldTileGrid);
        EmitWorldMapRowPointerTables(builder, program.WorldMap);
        EmitPpuRowAddressTables(builder, program.WorldMap);
        EmitWorldMapFlagRows(builder, program.WorldMap);
        EmitWorldMapFlagRowPointerTables(builder, program.WorldMap);
        EmitAudioAssets(builder, program.MusicAssetsInLoadOrder, program.SoundEffectAssetsInLoadOrder);

        var prg = new byte[PrgRomSize];
        var code = builder.Build();
        if (code.Length > PrgRomSize - 6)
        {
            throw new InvalidOperationException($"NES PRG ROM overflow: {code.Length} bytes emitted, {PrgRomSize - 6} bytes available.");
        }

        code.CopyTo(prg, 0);
        SetVector(prg, PrgRomSize - 6, PrgBuilder.BaseAddress);
        SetVector(prg, PrgRomSize - 4, PrgBuilder.BaseAddress);
        SetVector(prg, PrgRomSize - 2, PrgBuilder.BaseAddress);
        return prg;
    }

    private static bool TryForEndLabelId(string label, out int id)
    {
        const string prefix = "for_end_";
        if (label.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(label[prefix.Length..], CultureInfo.InvariantCulture, out id))
        {
            return true;
        }

        id = 0;
        return false;
    }

    private static void EmitWaitVBlank(PrgBuilder builder, string label)
    {
        builder.Label(label);
        builder.Emit(0x2C, 0x02, 0x20);             // BIT $2002
        builder.BranchRelative(0x10, label);        // BPL label
    }

    private static void EmitPaletteUpload(PrgBuilder builder)
    {
        builder.Emit(0xAD, 0x02, 0x20);             // LDA $2002
        builder.Emit(0xA9, 0x3F);                   // LDA #$3F
        builder.Emit(0x8D, 0x06, 0x20);             // STA $2006
        builder.Emit(0xA9, 0x00);                   // LDA #$00
        builder.Emit(0x8D, 0x06, 0x20);             // STA $2006
        builder.Emit(0xA2, 0x00);                   // LDX #$00
        builder.Label("palette_loop");
        builder.LdaAbsoluteX("palette");
        builder.Emit(0x8D, 0x07, 0x20);             // STA $2007
        builder.Emit(0xE8);                         // INX
        builder.Emit(0xE0, 0x20);                   // CPX #$20
        builder.BranchRelative(0xD0, "palette_loop"); // BNE palette_loop
    }

    private static void EmitNameTableUpload(PrgBuilder builder, int byteCount)
    {
        builder.Emit(0xAD, 0x02, 0x20);             // LDA $2002
        builder.Emit(0xA9, 0x20);                   // LDA #$20
        builder.Emit(0x8D, 0x06, 0x20);             // STA $2006
        builder.Emit(0xA9, 0x00);                   // LDA #$00
        builder.Emit(0x8D, 0x06, 0x20);             // STA $2006

        if (byteCount % 256 != 0)
        {
            throw new InvalidOperationException("NES nametable upload size must be page-aligned.");
        }

        for (var page = 0; page < byteCount / 256; page++)
        {
            builder.Emit(0xA2, 0x00);               // LDX #$00
            var label = $"nametable_loop_{page}";
            builder.Label(label);
            builder.LdaAbsoluteX("nametable", page * 256);
            builder.Emit(0x8D, 0x07, 0x20);         // STA $2007
            builder.Emit(0xE8);                     // INX
            builder.BranchRelative(0xD0, label);    // BNE label
        }
    }

    private static void EmitWorldMapRows(PrgBuilder builder, WorldMap2D? worldMap, WorldTileGrid? tileGrid)
    {
        if (worldMap is null || tileGrid is null)
        {
            return;
        }

        for (var row = 0; row < worldMap.Height; row++)
        {
            builder.Label(WorldMapRowLabel(row));
            for (var column = 0; column < worldMap.Width; column++)
            {
                var tileId = tileGrid.TileIdAt(column, row);
                if (tileId is < 0 or > 255)
                {
                    throw new InvalidOperationException("NES world map tile ids must fit one byte.");
                }

                builder.Emit((byte)tileId);
            }
        }
    }

    internal static string WorldMapRowLabel(int row) => $"world_map_row_{row}";

    internal const string WorldMapRowPointerLowLabel = "world_map_row_ptr_lo";
    internal const string WorldMapRowPointerHighLabel = "world_map_row_ptr_hi";
    internal const string PpuRowAddressLowLabel = "ppu_row_addr_lo";
    internal const string PpuRowAddressHighLabel = "ppu_row_addr_hi";
    internal const string PpuAttributeRowAddressLowLabel = "ppu_attr_row_addr_lo";
    internal const string PpuAttributeRowAddressHighLabel = "ppu_attr_row_addr_hi";

    private static void EmitWorldMapRowPointerTables(PrgBuilder builder, WorldMap2D? worldMap)
    {
        if (worldMap is null || worldMap.Height <= 60)
        {
            return;
        }

        builder.Label(WorldMapRowPointerLowLabel);
        for (var row = 0; row < worldMap.Height; row++)
        {
            builder.EmitLabelLowByte(WorldMapRowLabel(row));
        }

        builder.Label(WorldMapRowPointerHighLabel);
        for (var row = 0; row < worldMap.Height; row++)
        {
            builder.EmitLabelHighByte(WorldMapRowLabel(row));
        }
    }

    private static void EmitPpuRowAddressTables(PrgBuilder builder, WorldMap2D? worldMap)
    {
        if (worldMap is null || worldMap.Height <= 60)
        {
            return;
        }

        builder.Label(PpuRowAddressLowLabel);
        for (var row = 0; row < 60; row++)
        {
            var address = 0x2000 + row / 30 * 0x800 + row % 30 * 32;
            builder.Emit((byte)(address & 0xFF));
        }

        builder.Label(PpuRowAddressHighLabel);
        for (var row = 0; row < 60; row++)
        {
            var address = 0x2000 + row / 30 * 0x800 + row % 30 * 32;
            builder.Emit((byte)(address >> 8));
        }

        builder.Label(PpuAttributeRowAddressLowLabel);
        for (var row = 0; row < 60; row++)
        {
            var address = 0x23C0 + row / 30 * 0x800 + row % 30 / 4 * 8;
            builder.Emit((byte)(address & 0xFF));
        }

        builder.Label(PpuAttributeRowAddressHighLabel);
        for (var row = 0; row < 60; row++)
        {
            var address = 0x23C0 + row / 30 * 0x800 + row % 30 / 4 * 8;
            builder.Emit((byte)(address >> 8));
        }
    }

    private static void EmitWorldMapFlagRows(PrgBuilder builder, WorldMap2D? worldMap)
    {
        if (worldMap is null)
        {
            return;
        }

        for (var row = 0; row < worldMap.Height; row++)
        {
            builder.Label(WorldMapFlagRowLabel(row));
            for (var column = 0; column < worldMap.Width; column++)
            {
                builder.Emit((byte)worldMap.FlagsAt(column, row));
            }
        }
    }

    internal static string WorldMapFlagRowLabel(int row) => $"world_map_flags_row_{row}";

    internal const string WorldMapFlagRowPointerLowLabel = "world_map_flags_row_ptr_lo";
    internal const string WorldMapFlagRowPointerHighLabel = "world_map_flags_row_ptr_hi";

    private static void EmitWorldMapFlagRowPointerTables(PrgBuilder builder, WorldMap2D? worldMap)
    {
        if (worldMap is null)
        {
            return;
        }

        builder.Label(WorldMapFlagRowPointerLowLabel);
        for (var row = 0; row < worldMap.Height; row++)
        {
            builder.EmitLabelLowByte(WorldMapFlagRowLabel(row));
        }

        builder.Label(WorldMapFlagRowPointerHighLabel);
        for (var row = 0; row < worldMap.Height; row++)
        {
            builder.EmitLabelHighByte(WorldMapFlagRowLabel(row));
        }
    }

    private static void EmitAudioAssets(
        PrgBuilder builder,
        IReadOnlyList<NesCompiledMusicAsset> musicAssets,
        IReadOnlyList<NesCompiledSoundEffectAsset> soundEffectAssets)
    {
        // DPCM samples must live in the $C000-$FFF9 window and are placed right after the music data.
        // Sound-effect data is position-independent (walked from a start label at runtime), so it is
        // emitted after the DPCM blocks and does not shrink the DPCM window.
        var musicDataLength = musicAssets.Sum(asset => asset.Data.Length);
        var dpcmLayout = BuildDpcmSampleLayout(builder.CurrentAddress + musicDataLength, musicAssets);
        foreach (var asset in musicAssets)
        {
            var data = PatchDpcmAddressRegisters(asset, dpcmLayout.AddressRegisterMap);
            var label = MusicDataLabel(asset.Name);
            builder.Label(label);
            builder.Emit(data[0]);
            builder.EmitLabelLowByte(label, asset.OrderStartOffset);
            builder.EmitLabelHighByte(label, asset.OrderStartOffset);
            builder.EmitLabelLowByte(label, asset.LoopOrderOffset);
            builder.EmitLabelHighByte(label, asset.LoopOrderOffset);

            var poolLength = asset.OrderStartOffset - 5;
            builder.Emit(data.Skip(5).Take(poolLength).ToArray());
            foreach (var entry in asset.OrderEntries)
            {
                builder.EmitLabelLowByte(label, entry.BodyOffset);
                builder.EmitLabelHighByte(label, entry.BodyOffset);
                builder.Emit(entry.Wait);
            }

            builder.Emit(0, 0, 0);
        }

        EmitDpcmSampleBlocks(builder, dpcmLayout.Placements);

        foreach (var asset in soundEffectAssets)
        {
            builder.Label(SoundEffectDataLabel(asset.Name));
            builder.Emit(asset.Data);
        }
    }

    private static DpcmSampleLayout BuildDpcmSampleLayout(
        int musicDataEndAddress,
        IReadOnlyList<NesCompiledMusicAsset> musicAssets)
    {
        var uniqueBlocks = new List<NesDpcmSampleBlock>();
        foreach (var block in musicAssets.SelectMany(asset => asset.DpcmBlocks))
        {
            var existingIndex = uniqueBlocks.FindIndex(candidate => candidate.SourceAddress == block.SourceAddress);
            if (existingIndex >= 0)
            {
                if (!uniqueBlocks[existingIndex].Data.SequenceEqual(block.Data))
                {
                    throw new InvalidOperationException($"NES DPCM sample blocks at ${block.SourceAddress:X4} contain conflicting data.");
                }

                continue;
            }

            uniqueBlocks.Add(block);
        }

        if (uniqueBlocks.Count == 0)
        {
            return new DpcmSampleLayout([], new Dictionary<byte, byte>());
        }

        var placements = new List<DpcmSamplePlacement>(uniqueBlocks.Count);
        var addressRegisterMap = new Dictionary<byte, byte>();
        var cursor = AlignToDmcAddress(Math.Max(musicDataEndAddress, 0xC000));
        foreach (var block in uniqueBlocks.OrderByDescending(block => block.Data.Length))
        {
            cursor = AlignToDmcAddress(cursor);
            var endAddress = cursor + block.Data.Length;
            var vectorStartAddress = PrgBuilder.BaseAddress + PrgRomSize - 6;
            if (cursor < 0xC000 || endAddress > vectorStartAddress)
            {
                throw new InvalidOperationException(
                    $"NES DPCM sample block from ${block.SourceAddress:X4} with {block.Data.Length} bytes cannot fit in PRG ROM after music data ending at ${musicDataEndAddress:X4}.");
            }

            if ((cursor - 0xC000) % 64 != 0)
            {
                throw new InvalidOperationException($"NES DPCM sample placement ${cursor:X4} is not 64-byte aligned.");
            }

            var placementAddress = (ushort)cursor;
            placements.Add(new DpcmSamplePlacement(block, placementAddress));
            AddDpcmAddressRegisterTranslations(addressRegisterMap, block.SourceAddress, placementAddress, block.Data.Length);
            cursor = endAddress;
        }

        return new DpcmSampleLayout(placements.OrderBy(placement => placement.Address).ToArray(), addressRegisterMap);
    }

    private static int AlignToDmcAddress(int address)
    {
        const int alignment = 64;
        return (address + alignment - 1) / alignment * alignment;
    }

    private static void AddDpcmAddressRegisterTranslations(
        Dictionary<byte, byte> addressRegisterMap,
        ushort sourceAddress,
        ushort placementAddress,
        int byteCount)
    {
        for (var offset = 0; offset < byteCount; offset += 64)
        {
            var source = sourceAddress + offset;
            var target = placementAddress + offset;
            if (source > 0xFFC0 || target > 0xFFC0)
            {
                break;
            }

            var sourceRegister = (byte)((source - 0xC000) / 64);
            var targetRegister = (byte)((target - 0xC000) / 64);
            if (addressRegisterMap.TryGetValue(sourceRegister, out var existing) && existing != targetRegister)
            {
                throw new InvalidOperationException($"NES DPCM sample address register ${sourceRegister:X2} maps to conflicting relocated addresses.");
            }

            addressRegisterMap[sourceRegister] = targetRegister;
        }
    }

    private static byte[] PatchDpcmAddressRegisters(NesCompiledMusicAsset asset, IReadOnlyDictionary<byte, byte> addressRegisterMap)
    {
        return PatchDpcmAddressRegisters(asset.Data, asset.OrderEntries, addressRegisterMap);
    }

    private static byte[] PatchDpcmAddressRegisters(
        byte[] sourceData,
        IReadOnlyList<NesApuTraceOrderEntry> orderEntries,
        IReadOnlyDictionary<byte, byte> addressRegisterMap)
    {
        if (addressRegisterMap.Count == 0)
        {
            return sourceData;
        }

        var data = sourceData.ToArray();
        foreach (var bodyOffset in orderEntries.Select(entry => entry.BodyOffset).Distinct())
        {
            var commandCount = data[bodyOffset];
            var offset = bodyOffset + 1;
            for (var i = 0; i < commandCount; i++)
            {
                var registerOffset = data[offset];
                if (registerOffset == 0x12 && addressRegisterMap.TryGetValue(data[offset + 1], out var relocatedAddressRegister))
                {
                    data[offset + 1] = relocatedAddressRegister;
                }

                offset += 2;
            }
        }

        return data;
    }

    private static void EmitDpcmSampleBlocks(PrgBuilder builder, IReadOnlyList<DpcmSamplePlacement> placements)
    {
        foreach (var placement in placements)
        {
            if (builder.CurrentAddress > placement.Address)
            {
                throw new InvalidOperationException(
                    $"NES DPCM sample block at ${placement.Address:X4} overlaps earlier PRG ROM data ending at ${builder.CurrentAddress:X4}.");
            }

            builder.PadToAddress(placement.Address);
            builder.Emit(placement.Block.Data);
        }
    }

    internal static string MusicDataLabel(string name) => $"music_data_{name}";

    internal static string SoundEffectDataLabel(string name) => $"sfx_data_{name}";

    private sealed record DpcmSampleLayout(
        IReadOnlyList<DpcmSamplePlacement> Placements,
        IReadOnlyDictionary<byte, byte> AddressRegisterMap);

    private readonly record struct DpcmSamplePlacement(NesDpcmSampleBlock Block, ushort Address);

    private static byte[] BuildChrRom(NesVideoProgram program)
    {
        var chr = new byte[ChrRomSize];
        WriteSolidTile(chr, 1, 1);
        WriteSolidTile(chr, 2, 2);
        WriteSolidTile(chr, 3, 3);
        WriteCheckerTile(chr, 4, 1, 2);
        WriteFrameTile(chr, 5, 3);
        foreach (var asset in program.SpriteAssetsInLoadOrder)
        {
            var offset = asset.FirstTile * 16;
            if (offset + asset.TileData.Length > chr.Length)
            {
                throw new InvalidOperationException($"NES sprite asset '{asset.Name}' exceeds CHR ROM size.");
            }

            asset.TileData.CopyTo(chr, offset);
        }

        foreach (var background in program.GeneratedBackgroundTiles)
        {
            var offset = background.FirstTile * 16;
            if (offset + background.Data.Length > chr.Length)
            {
                throw new InvalidOperationException("NES generated background tiles exceed CHR ROM size.");
            }

            background.Data.CopyTo(chr, offset);
        }

        return chr;
    }

    private static void WriteSolidTile(byte[] chr, int tile, int color)
    {
        for (var row = 0; row < 8; row++)
        {
            chr[tile * 16 + row] = (byte)((color & 1) != 0 ? 0xFF : 0x00);
            chr[tile * 16 + 8 + row] = (byte)((color & 2) != 0 ? 0xFF : 0x00);
        }
    }

    private static void WriteCheckerTile(byte[] chr, int tile, int colorA, int colorB)
    {
        for (var row = 0; row < 8; row++)
        {
            var plane0 = 0;
            var plane1 = 0;
            for (var col = 0; col < 8; col++)
            {
                var color = ((row + col) & 1) == 0 ? colorA : colorB;
                var bit = 7 - col;
                if ((color & 1) != 0) plane0 |= 1 << bit;
                if ((color & 2) != 0) plane1 |= 1 << bit;
            }

            chr[tile * 16 + row] = (byte)plane0;
            chr[tile * 16 + 8 + row] = (byte)plane1;
        }
    }

    private static void WriteFrameTile(byte[] chr, int tile, int color)
    {
        for (var row = 0; row < 8; row++)
        {
            var bits = row is 0 or 7 ? 0xFF : 0x81;
            chr[tile * 16 + row] = (byte)((color & 1) != 0 ? bits : 0x00);
            chr[tile * 16 + 8 + row] = (byte)((color & 2) != 0 ? bits : 0x00);
        }
    }

    private static void SetVector(byte[] prg, int offset, ushort address)
    {
        prg[offset] = (byte)(address & 0xFF);
        prg[offset + 1] = (byte)(address >> 8);
    }
}

internal sealed class NesRuntimeCompiler
{
    private sealed record StructArrayLayout(int Stride, IReadOnlyDictionary<string, int> FieldOffsets);

    private const byte FirstVariableAddress = 0x00;
    private const byte RuntimeReservedStateAddress = 0xDE;
    // Sound-effect playback cursor. Kept in zero page so the shared APU body player can read the
    // current frame body through it; the effect walks one frame body per audio-update tick.
    private const byte SfxPointerLowAddress = 0xDE;
    private const byte SfxPointerHighAddress = 0xDF;
    private const byte CameraXAddress = 0xE0;
    private const byte CameraTileColumnAddress = 0xE1;
    private const byte CameraTargetColumnAddress = 0xE2;
    private const byte CameraSourceColumnAddress = 0xE3;
    private const byte SpriteFrameScratchAddress = 0xE4;
    private const byte CollisionColumnScratchAddress = 0xE5;
    private const byte CollisionRowScratchAddress = 0xE6;
    private const byte CameraNewXAddress = 0xE7;
    private const byte RuntimeIndexScratchAddress = 0xE8;
    private const byte ExpressionScratchAddress = 0xE9;
    private const byte CameraYAddress = 0xEA;
    private const byte CameraTileRowAddress = 0xEB;
    private const byte CameraNewYAddress = 0xEC;
    private const byte CameraTargetRowAddress = 0xED;
    private const byte CameraSourceRowAddress = 0xEE;
    private const byte CameraStreamRemainingAddress = 0xEF;
    private const byte InputCurrentAddress = 0xF0;
    private const byte InputPreviousAddress = 0xF1;
    private const byte InputHoldTicksStartAddress = 0xF2;
    private const byte MusicOrderPointerLowAddress = 0xFA;
    private const byte MusicOrderPointerHighAddress = 0xFB;
    private const byte MusicBodyPointerLowAddress = 0xFC;
    private const byte MusicBodyPointerHighAddress = 0xFD;
    private const byte MusicTickAddress = 0xFE;
    private const byte MusicCommandCountAddress = 0xFF;
    private const ushort OamShadowAddress = 0x0200;
    private const ushort PendingCameraStreamFlagsAddress = 0x0300;
    private const ushort PendingCameraNextStreamAddress = 0x0301;
    private const ushort PendingCameraColumnTargetAddress = 0x0302;
    private const ushort PendingCameraColumnSourceAddress = 0x0303;
    private const ushort PendingCameraRowTargetAddress = 0x0304;
    private const ushort PendingCameraRowSourceAddress = 0x0305;
    private const ushort PendingCameraRowTargetColumnAddress = 0x0306;
    private const ushort PendingCameraRowSourceColumnAddress = 0x0307;
    private const ushort PendingCameraRowPhaseAddress = 0x0308;
    private const ushort CameraScrollAppliedAddress = 0x0309;
    private const ushort CameraWalkTargetAddress = 0x030A;
    private const ushort CameraWalkStepsAddress = 0x030B;
    // Camera movement walks the camera toward the requested position one pixel at a time, feeding
    // single-pixel steps to the tracking/streaming routines. A single walk never crosses more than one
    // tile boundary (8 px), matching the per-frame streaming budget (one queued column/row drained by
    // camera apply), so one position update per frame can reach targets several pixels away.
    private const byte CameraWalkMaxStepsPerFrame = 8;
    private const ushort MusicLoopPointerLowAddress = 0x0310;
    private const ushort MusicLoopPointerHighAddress = 0x0311;
    // Non-zero while a sound effect is playing. The music engine reads it to suppress its own pulse 1
    // writes so the effect owns the channel; the SFX cursor itself lives in zero page.
    private const ushort SfxActiveAddress = 0x0312;
    private const ushort OamDmaAddress = 0x4014;
    private const ushort ControllerPortAddress = 0x4016;
    private const byte PendingStreamNone = 0;
    private const byte PendingStreamColumn = 1;
    private const byte PendingStreamRow = 2;

    private static readonly NesButton AButton = new("a", 0x01, InputHoldTicksStartAddress);
    private static readonly NesButton BButton = new("b", 0x02, InputHoldTicksStartAddress + 1);
    private static readonly NesButton SelectButton = new("select", 0x04, InputHoldTicksStartAddress + 2);
    private static readonly NesButton StartButton = new("start", 0x08, InputHoldTicksStartAddress + 3);
    private static readonly NesButton RightButton = new("right", 0x10, InputHoldTicksStartAddress + 4);
    private static readonly NesButton LeftButton = new("left", 0x20, InputHoldTicksStartAddress + 5);
    private static readonly NesButton UpButton = new("up", 0x40, InputHoldTicksStartAddress + 6);
    private static readonly NesButton DownButton = new("down", 0x80, InputHoldTicksStartAddress + 7);

    private static readonly NesButton[] Buttons =
    [
        AButton,
        BButton,
        SelectButton,
        StartButton,
        RightButton,
        LeftButton,
        UpButton,
        DownButton,
    ];

    private static readonly NesButton[] ControllerReadOrder =
    [
        AButton,
        BButton,
        SelectButton,
        StartButton,
        UpButton,
        DownButton,
        LeftButton,
        RightButton,
    ];

    private readonly PrgBuilder builder;
    private readonly NesVideoProgram program;
    private readonly Dictionary<string, byte> variables = [];
    private readonly Dictionary<string, string> variableTypes = [];
    private readonly Dictionary<string, StructArrayLayout> structArrays = [];
    private readonly HashSet<string> declaredVariables = [];
    private readonly HashSet<string> signedByteLocations = [];
    private readonly HashSet<string> immutableVariables = [];
    private readonly HashSet<string> userFunctionCallStack = [];
    private readonly Stack<InlineVariableScope> inlineVariableScopes = [];
    private readonly Stack<LoopTarget> loopTargets = [];
    private readonly IReadOnlySet<int> longForLoopIds;
    private readonly bool useFourScreenNametables;
    private byte nextVariableAddress = FirstVariableAddress;
    private int nextForLoopId;
    private int nextHardwareSprite;
    private int nextInlineVariableScopeId;
    private bool apuBodySubroutineReferenced;
    private NesCameraConfig? cameraConfig;

    private const string ApuBodySubroutineLabel = "nes_apu_body";

    public NesRuntimeCompiler(
        PrgBuilder builder,
        NesVideoProgram program,
        IReadOnlySet<int>? longForLoopIds = null,
        bool useFourScreenNametables = false)
    {
        this.builder = builder;
        this.program = program;
        this.longForLoopIds = longForLoopIds ?? new HashSet<int>();
        this.useFourScreenNametables = useFourScreenNametables;
    }

    public void EmitInitialization()
    {
        EmitCameraStateInitialization();
        EmitInputStateInitialization();
        if (program.MusicAssetsInLoadOrder.Count > 0 || program.SoundEffectAssetsInLoadOrder.Count > 0)
        {
            EmitAudioStateInitialization();
        }

        EmitOamShadowClear();
    }

    public void Emit(BlockSyntax block)
    {
        EmitBlock(block);
    }

    private void EmitCameraStateInitialization()
    {
        builder.LoadAImmediate(0);
        builder.StoreAZeroPage(CameraXAddress);
        builder.StoreAZeroPage(CameraTileColumnAddress);
        builder.StoreAZeroPage(CameraTargetColumnAddress);
        builder.StoreAZeroPage(CameraSourceColumnAddress);
        builder.StoreAZeroPage(CameraNewXAddress);
        builder.StoreAAbsolute(CameraScrollAppliedAddress);
        if (useFourScreenNametables)
        {
            builder.StoreAZeroPage(CameraYAddress);
            builder.StoreAZeroPage(CameraTileRowAddress);
            builder.StoreAZeroPage(CameraNewYAddress);
            builder.StoreAZeroPage(CameraTargetRowAddress);
            builder.StoreAZeroPage(CameraSourceRowAddress);
            builder.StoreAZeroPage(CameraStreamRemainingAddress);
        }
    }

    private void EmitInputStateInitialization()
    {
        builder.LoadAImmediate(0);
        builder.StoreAZeroPage(InputCurrentAddress);
        builder.StoreAZeroPage(InputPreviousAddress);
        foreach (var button in Buttons)
        {
            builder.StoreAZeroPage(button.HoldTicksAddress);
        }
    }

    private void EmitAudioStateInitialization()
    {
        builder.LoadAImmediate(0);
        builder.StoreAZeroPage(MusicOrderPointerLowAddress);
        builder.StoreAZeroPage(MusicOrderPointerHighAddress);
        builder.StoreAZeroPage(MusicTickAddress);
        builder.StoreAAbsolute(MusicLoopPointerLowAddress);
        builder.StoreAAbsolute(MusicLoopPointerHighAddress);
        if (program.SoundEffectAssetsInLoadOrder.Count > 0)
        {
            builder.StoreAAbsolute(SfxActiveAddress);
        }
    }

    private void EmitOamShadowClear()
    {
        var clearLabel = builder.CreateLabel("oam_clear");

        builder.LoadAImmediate(0xFF);
        builder.LoadXImmediate(0);
        builder.Label(clearLabel);
        builder.StoreAAbsoluteX(OamShadowAddress);
        builder.IncrementX();
        builder.BranchRelative(0xD0, clearLabel);   // BNE clearLabel
        EmitOamDma();
    }

    private bool EmitBlock(BlockSyntax block)
    {
        foreach (var statement in block.Statements)
        {
            if (EmitStatement(statement))
            {
                return true;
            }
        }

        return false;
    }

    private bool EmitStatement(StatementSyntax statement)
    {
        switch (statement)
        {
            case DeclarationSyntax declaration:
                EmitDeclaration(declaration);
                return false;
            case ExpressionStatementSyntax expressionStatement:
                EmitExpressionStatement(expressionStatement);
                return false;
            case WhileSyntax whileSyntax:
                EmitWhile(whileSyntax);
                return false;
            case DoWhileSyntax doWhileSyntax:
                EmitDoWhile(doWhileSyntax);
                return false;
            case RangeForSyntax rangeForSyntax:
                EmitFor(RangeForLowerer.Lower(rangeForSyntax));
                return false;
            case ForSyntax forSyntax:
                EmitFor(forSyntax);
                return false;
            case IfElseSyntax ifElseSyntax:
                return EmitIf(ifElseSyntax);
            case BreakSyntax:
                EmitBreak();
                return true;
            case ContinueSyntax:
                EmitContinue();
                return true;
            case ReturnSyntax:
                return true;
            default:
                throw new InvalidOperationException($"Unsupported NES statement '{statement.GetType().Name}'.");
        }
    }

    private void EmitDeclaration(DeclarationSyntax declaration)
    {
        if (declaration.ArrayLength.HasValue)
        {
            EmitArrayDeclaration(declaration);
            return;
        }

        if (IsByteBackedLocalType(declaration.Type))
        {
            EmitByteBackedDeclaration(declaration);
            return;
        }

        if (program.Structs.TryGetValue(declaration.Type, out var structSyntax))
        {
            EmitStructDeclaration(declaration, structSyntax);
            return;
        }

        throw new InvalidOperationException($"NES target does not support local type '{declaration.Type}' yet.");
    }

    private void EmitArrayDeclaration(DeclarationSyntax declaration)
    {
        if (program.Structs.TryGetValue(declaration.Type, out var structSyntax))
        {
            EmitStructArrayDeclaration(declaration, structSyntax);
            return;
        }

        if (!IsScalarLocalType(declaration.Type))
        {
            throw new InvalidOperationException($"NES target only supports scalar fixed-size arrays; '{declaration.Type}' is not supported yet.");
        }

        var declarationName = DeclareScopedVariableName(declaration.Name);
        if (!declaredVariables.Add(declarationName))
        {
            throw new InvalidOperationException($"Variable '{declaration.Name}' is already declared.");
        }

        TrackImmutable(declaration);

        var length = CheckedRange(NesVideoProgram.ConstValue(declaration.ArrayLength.Value, $"{declaration.Name} array length"), 1, 255, $"{declaration.Name} array length");
        var elementAddresses = new List<byte>();
        for (var index = 0; index < length; index++)
        {
            var sourceName = IndexedElementName(declaration.Name, index);
            var scopedName = IndexedElementName(declarationName, index);
            MapScopedVariableName(sourceName, scopedName);
            var address = DeclareVariable(scopedName, declaration.Type);
            elementAddresses.Add(address);
            EmitZeroToStorage(address, declaration.Type);
        }

        if (declaration.Initialization.HasValue)
        {
            EmitArrayInitializer(declaration, declaration.Initialization.Value, length, elementAddresses);
        }
    }

    private void EmitStructArrayDeclaration(DeclarationSyntax declaration, StructSyntax structSyntax)
    {
        var declarationName = DeclareScopedVariableName(declaration.Name);
        if (!declaredVariables.Add(declarationName))
        {
            throw new InvalidOperationException($"Variable '{declaration.Name}' is already declared.");
        }

        TrackImmutable(declaration);

        var length = CheckedRange(NesVideoProgram.ConstValue(declaration.ArrayLength.Value, $"{declaration.Name} array length"), 1, 255, $"{declaration.Name} array length");
        var fieldOffsets = StructFieldOffsets(structSyntax);
        var stride = StructStride(structSyntax);
        if (length * stride > 255)
        {
            throw new InvalidOperationException($"NES target struct array '{declaration.Name}' uses {length * stride} byte slot(s), but runtime indexed struct arrays are limited to 255 byte slots.");
        }

        structArrays.Add(declarationName, new StructArrayLayout(stride, fieldOffsets));

        var fieldNames = structSyntax.Fields.Select(field => field.Name).ToList();
        for (var index = 0; index < length; index++)
        {
            foreach (var field in structSyntax.Fields)
            {
                var sourceName = IndexedMemberName(declaration.Name, index, field.Name);
                var scopedName = IndexedMemberName(declarationName, index, field.Name);
                MapScopedVariableName(sourceName, scopedName);
                var address = DeclareVariable(scopedName, field.Type);
                TrackSignedByteType(field.Type, scopedName);
                EmitZeroToStorage(address, field.Type);
            }
        }

        if (declaration.Initialization.HasValue)
        {
            EmitStructArrayInitializer(declaration, declaration.Initialization.Value, length, fieldNames);
        }
    }

    private void EmitStructArrayInitializer(
        DeclarationSyntax declaration,
        ExpressionSyntax initialization,
        int length,
        IReadOnlyList<string> fieldNames)
    {
        if (initialization is not ArrayInitializerSyntax arrayInitializer)
        {
            throw new InvalidOperationException($"NES target requires an array initializer for local struct array '{declaration.Name}'.");
        }

        if (arrayInitializer.Elements.Count > length)
        {
            throw new InvalidOperationException($"NES target struct array initializer for '{declaration.Name}' has {arrayInitializer.Elements.Count} element(s), but the array length is {length}.");
        }

        var knownFields = fieldNames.ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < arrayInitializer.Elements.Count; index++)
        {
            if (arrayInitializer.Elements[index] is not StructInitializerSyntax structInitializer)
            {
                throw new InvalidOperationException($"NES target requires struct initializers for elements of struct array '{declaration.Name}'.");
            }

            var initializedFields = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
            foreach (var field in structInitializer.Fields)
            {
                if (!initializedFields.TryAdd(field.Name, field.Expression))
                {
                    throw new InvalidOperationException($"NES target struct array initializer for '{declaration.Name}' element {index} supplies field '{field.Name}' more than once.");
                }

                if (!knownFields.Contains(field.Name))
                {
                    throw new InvalidOperationException($"NES target struct array initializer for '{declaration.Name}' has no field named '{field.Name}'.");
                }
            }

            foreach (var fieldName in fieldNames)
            {
                if (!initializedFields.TryGetValue(fieldName, out var expression))
                {
                    continue;
                }

                var storageName = IndexedMemberName(declaration.Name, index, fieldName);
                var address = VariableAddress(storageName);
                EmitExpressionToStorage(expression, address, VariableStorageType(storageName));
            }
        }
    }

    private void EmitArrayInitializer(DeclarationSyntax declaration, ExpressionSyntax initialization, int length, IReadOnlyList<byte> elementAddresses)
    {
        if (initialization is not ArrayInitializerSyntax arrayInitializer)
        {
            throw new InvalidOperationException($"NES target requires an array initializer for local array '{declaration.Name}'.");
        }

        if (arrayInitializer.Elements.Count > length)
        {
            throw new InvalidOperationException($"NES target array initializer for '{declaration.Name}' has {arrayInitializer.Elements.Count} element(s), but the array length is {length}.");
        }

        for (var index = 0; index < arrayInitializer.Elements.Count; index++)
        {
            EmitExpressionToStorage(arrayInitializer.Elements[index], elementAddresses[index], declaration.Type);
        }
    }

    private void EmitByteBackedDeclaration(DeclarationSyntax declaration)
    {
        var scopedName = DeclareScopedVariableName(declaration.Name);
        var address = DeclareVariable(scopedName, declaration.Type);
        TrackImmutable(declaration);
        TrackSignedByteType(declaration.Type, scopedName);

        if (declaration.Initialization.HasValue)
        {
            EmitExpressionToStorage(declaration.Initialization.Value, address, declaration.Type);
            return;
        }

        EmitZeroToStorage(address, declaration.Type);
    }

    private void EmitStructDeclaration(DeclarationSyntax declaration, StructSyntax structSyntax)
    {
        var declarationName = DeclareScopedVariableName(declaration.Name);
        if (!declaredVariables.Add(declarationName))
        {
            throw new InvalidOperationException($"Variable '{declaration.Name}' is already declared.");
        }

        TrackImmutable(declaration);

        var fieldAddresses = new Dictionary<string, byte>(StringComparer.Ordinal);
        var fieldNames = new List<string>();
        foreach (var field in structSyntax.Fields)
        {
            if (!IsScalarLocalType(field.Type))
            {
                throw new InvalidOperationException($"NES target does not support struct field type '{field.Type}' yet.");
            }

            var sourceName = $"{declaration.Name}.{field.Name}";
            var scopedName = $"{declarationName}.{field.Name}";
            MapScopedVariableName(sourceName, scopedName);
            var address = DeclareVariable(scopedName, field.Type);
            TrackSignedByteType(field.Type, scopedName);
            fieldAddresses.Add(field.Name, address);
            fieldNames.Add(field.Name);
            EmitZeroToStorage(address, field.Type);
        }

        if (declaration.Initialization.HasValue)
        {
            EmitStructInitializer(declaration, declaration.Initialization.Value, fieldNames, fieldAddresses);
        }
    }

    private void EmitStructInitializer(
        DeclarationSyntax declaration,
        ExpressionSyntax initialization,
        IReadOnlyList<string> fieldNames,
        IReadOnlyDictionary<string, byte> fieldAddresses)
    {
        if (initialization is not StructInitializerSyntax structInitializer)
        {
            throw new InvalidOperationException($"NES target requires a struct initializer for local struct '{declaration.Name}'.");
        }

        var initializedFields = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
        foreach (var field in structInitializer.Fields)
        {
            if (!initializedFields.TryAdd(field.Name, field.Expression))
            {
                throw new InvalidOperationException($"NES target struct initializer for '{declaration.Name}' supplies field '{field.Name}' more than once.");
            }

            if (!fieldAddresses.ContainsKey(field.Name))
            {
                throw new InvalidOperationException($"NES target struct initializer for '{declaration.Name}' has no field named '{field.Name}'.");
            }
        }

        foreach (var fieldName in fieldNames)
        {
            if (!initializedFields.TryGetValue(fieldName, out var expression))
            {
                continue;
            }

            var storageName = $"{declaration.Name}.{fieldName}";
            EmitExpressionToStorage(expression, fieldAddresses[fieldName], VariableStorageType(storageName));
        }
    }

    private byte DeclareVariable(string name, string type)
    {
        if (!declaredVariables.Add(name))
        {
            throw new InvalidOperationException($"Variable '{name}' is already declared.");
        }

        if (variables.ContainsKey(name))
        {
            throw new InvalidOperationException($"Variable '{name}' is already declared.");
        }

        var size = StorageSize(type);
        if (nextVariableAddress + size > RuntimeReservedStateAddress)
        {
            throw new InvalidOperationException("NES target local variables exceed the current prototype zero-page allocation.");
        }

        var address = nextVariableAddress;
        nextVariableAddress = (byte)(nextVariableAddress + size);
        variables.Add(name, address);
        variableTypes.Add(name, type);
        return address;
    }

    private void EmitZeroToStorage(byte address, string type)
    {
        builder.LoadAImmediate(0);
        builder.StoreAZeroPage(address);
        if (IsWordBackedType(type))
        {
            builder.StoreAZeroPage(HighAddress(address));
        }
    }

    private void EmitExpressionToStorage(ExpressionSyntax expression, byte address, string type)
    {
        if (IsWordBackedType(type))
        {
            EmitWordExpressionToStorage(expression, address, type);
            return;
        }

        EmitExpressionToA(expression);
        builder.StoreAZeroPage(address);
    }

    private void EmitWordExpressionToStorage(ExpressionSyntax expression, byte address, string targetType)
    {
        if (TryConst(expression, out var constant))
        {
            EmitStoreWordImmediate(address, constant);
            return;
        }

        switch (expression)
        {
            case CastSyntax cast:
                if (IsWordBackedType(cast.Type))
                {
                    EmitWordExpressionToStorage(cast.Expression, address, cast.Type);
                    return;
                }

                EmitExpressionToA(cast.Expression);
                builder.StoreAZeroPage(address);
                EmitHighByteFromLowAToStorage(HighAddress(address), cast.Type);
                return;
            case IdentifierSyntax { Identifier: "true" }:
                EmitStoreWordImmediate(address, 1);
                return;
            case IdentifierSyntax { Identifier: "false" }:
                EmitStoreWordImmediate(address, 0);
                return;
            case IdentifierSyntax or MemberAccessSyntax or IndexExpressionSyntax when TryDirectStorageExpression(expression, out var sourceAddress, out var sourceType):
                EmitCopyToWordStorage(sourceAddress, sourceType, address);
                return;
            case MemberAccessSyntax memberAccess when TryRuntimeIndexedMemberAccess(memberAccess, out var indexedBase, out var fieldName):
                EmitRuntimeMemberIndexToX(indexedBase);
                EmitRuntimeStorageFromZeroPageXToWordStorage(RuntimeIndexedMemberBaseAddress(indexedBase, fieldName), VariableStorageType(IndexedMemberName(indexedBase.BaseIdentifier, 0, fieldName)), address);
                return;
            case IndexExpressionSyntax indexExpression:
                EmitRuntimeIndexToX(indexExpression.BaseIdentifier, indexExpression.Index);
                EmitRuntimeStorageFromZeroPageXToWordStorage(ArrayBaseAddress(indexExpression.BaseIdentifier), VariableStorageType(IndexedElementName(indexExpression.BaseIdentifier, 0)), address);
                return;
            case FunctionCall call:
                EmitValueCallToA(call);
                builder.StoreAZeroPage(address);
                builder.LoadAImmediate(0);
                builder.StoreAZeroPage(HighAddress(address));
                return;
            case ConditionalExpressionSyntax conditional:
                EmitWordConditionalExpressionToStorage(conditional, address, targetType);
                return;
            case UnaryExpressionSyntax unary when IsBooleanValueExpression(unary):
                EmitBooleanExpressionToA(unary);
                builder.StoreAZeroPage(address);
                builder.LoadAImmediate(0);
                builder.StoreAZeroPage(HighAddress(address));
                return;
            case BinaryExpressionSyntax binary when IsBooleanValueExpression(binary):
                EmitBooleanExpressionToA(binary);
                builder.StoreAZeroPage(address);
                builder.LoadAImmediate(0);
                builder.StoreAZeroPage(HighAddress(address));
                return;
            case BinaryExpressionSyntax { Operator.Symbol: "+" } binary:
                EmitWordExpressionToStorage(binary.Left, address, targetType);
                EmitAddWordIntoStorage(address, binary.Right);
                return;
            case BinaryExpressionSyntax { Operator.Symbol: "-" } binary:
                EmitWordExpressionToStorage(binary.Left, address, targetType);
                EmitSubtractWordFromStorage(address, binary.Right);
                return;
            default:
                EmitExpressionToA(expression);
                builder.StoreAZeroPage(address);
                builder.LoadAImmediate(0);
                builder.StoreAZeroPage(HighAddress(address));
                return;
        }
    }

    private void EmitRuntimeStorageFromZeroPageXToWordStorage(byte baseAddress, string sourceType, byte targetAddress)
    {
        builder.LoadAZeroPageX(baseAddress);
        builder.StoreAZeroPage(targetAddress);
        if (IsWordBackedType(sourceType))
        {
            builder.LoadAZeroPageX(HighAddress(baseAddress));
        }
        else if (sourceType == "i8")
        {
            EmitSignExtensionFromA();
        }
        else
        {
            builder.LoadAImmediate(0);
        }

        builder.StoreAZeroPage(HighAddress(targetAddress));
    }

    private void EmitWordConditionalExpressionToStorage(ConditionalExpressionSyntax conditional, byte address, string targetType)
    {
        var falseLabel = builder.CreateLabel("word_conditional_false");
        var endLabel = builder.CreateLabel("word_conditional_end");

        EmitConditionFalseJump(conditional.Condition, falseLabel);
        EmitWordExpressionToStorage(conditional.WhenTrue, address, targetType);
        builder.JumpAbsolute(endLabel);
        builder.Label(falseLabel);
        EmitWordExpressionToStorage(conditional.WhenFalse, address, targetType);
        builder.Label(endLabel);
    }

    private void EmitStoreWordImmediate(byte address, int value)
    {
        builder.LoadAImmediate(value & 0xFF);
        builder.StoreAZeroPage(address);
        builder.LoadAImmediate((value >> 8) & 0xFF);
        builder.StoreAZeroPage(HighAddress(address));
    }

    private void EmitCopyToWordStorage(byte sourceAddress, string sourceType, byte targetAddress)
    {
        builder.LoadAZeroPage(sourceAddress);
        builder.StoreAZeroPage(targetAddress);

        if (IsWordBackedType(sourceType))
        {
            builder.LoadAZeroPage(HighAddress(sourceAddress));
        }
        else if (sourceType == "i8")
        {
            builder.LoadAZeroPage(sourceAddress);
            EmitSignExtensionFromA();
        }
        else
        {
            builder.LoadAImmediate(0);
        }

        builder.StoreAZeroPage(HighAddress(targetAddress));
    }

    private void EmitAddWordIntoStorage(byte address, ExpressionSyntax right)
    {
        if (TryConst(right, out var constant))
        {
            builder.LoadAZeroPage(address);
            builder.ClearCarry();
            builder.AddImmediate(constant & 0xFF);
            builder.StoreAZeroPage(address);
            builder.LoadAZeroPage(HighAddress(address));
            builder.AddImmediate((constant >> 8) & 0xFF);
            builder.StoreAZeroPage(HighAddress(address));
            return;
        }

        if (!TryDirectStorageExpression(right, out var rightAddress, out var rightType))
        {
            EmitWordExpressionToStorage(right, RuntimeIndexScratchAddress, WordExpressionType(right));
            rightAddress = RuntimeIndexScratchAddress;
            rightType = "u16";
        }

        // Sign-extending an i8 addend clobbers the carry flag, so its high byte must be materialized
        // to scratch before the low-byte add. Wider operands load the high byte with carry-safe loads
        // after the low-byte add, leaving their emission unchanged.
        var hoistI8HighByte = rightType == "i8";
        if (hoistI8HighByte)
        {
            EmitHighByteToScratch(rightAddress, rightType);
        }

        builder.LoadAZeroPage(address);
        builder.ClearCarry();
        builder.AddZeroPage(rightAddress);
        builder.StoreAZeroPage(address);

        if (!hoistI8HighByte)
        {
            EmitHighByteToScratch(rightAddress, rightType);
        }

        builder.LoadAZeroPage(HighAddress(address));
        builder.AddZeroPage(ExpressionScratchAddress);
        builder.StoreAZeroPage(HighAddress(address));
    }

    private void EmitSubtractWordFromStorage(byte address, ExpressionSyntax right)
    {
        if (TryConst(right, out var constant))
        {
            builder.LoadAZeroPage(address);
            builder.SetCarry();
            builder.SubtractImmediate(constant & 0xFF);
            builder.StoreAZeroPage(address);
            builder.LoadAZeroPage(HighAddress(address));
            builder.SubtractImmediate((constant >> 8) & 0xFF);
            builder.StoreAZeroPage(HighAddress(address));
            return;
        }

        if (!TryDirectStorageExpression(right, out var rightAddress, out var rightType))
        {
            EmitWordExpressionToStorage(right, RuntimeIndexScratchAddress, WordExpressionType(right));
            rightAddress = RuntimeIndexScratchAddress;
            rightType = "u16";
        }

        // Sign-extending an i8 operand clobbers the carry/borrow flag, so its high byte must be
        // materialized to scratch before the low-byte subtract. Wider operands load the high byte with
        // carry-safe loads after the low-byte subtract, leaving their emission unchanged.
        var hoistI8HighByte = rightType == "i8";
        if (hoistI8HighByte)
        {
            EmitHighByteToScratch(rightAddress, rightType);
        }

        builder.LoadAZeroPage(address);
        builder.SetCarry();
        builder.SubtractZeroPage(rightAddress);
        builder.StoreAZeroPage(address);

        if (!hoistI8HighByte)
        {
            EmitHighByteToScratch(rightAddress, rightType);
        }

        builder.LoadAZeroPage(HighAddress(address));
        builder.SubtractZeroPage(ExpressionScratchAddress);
        builder.StoreAZeroPage(HighAddress(address));
    }

    private void EmitHighByteToScratch(byte address, string type)
    {
        if (IsWordBackedType(type))
        {
            builder.LoadAZeroPage(HighAddress(address));
        }
        else if (type == "i8")
        {
            builder.LoadAZeroPage(address);
            EmitSignExtensionFromA();
        }
        else
        {
            builder.LoadAImmediate(0);
        }

        builder.StoreAZeroPage(ExpressionScratchAddress);
    }

    private void EmitHighByteFromLowAToStorage(byte highAddress, string sourceType)
    {
        if (sourceType == "i8")
        {
            EmitSignExtensionFromA();
        }
        else
        {
            builder.LoadAImmediate(0);
        }

        builder.StoreAZeroPage(highAddress);
    }

    private void EmitSignExtensionFromA()
    {
        var negativeLabel = builder.CreateLabel("sign_extend_negative");
        var endLabel = builder.CreateLabel("sign_extend_end");

        builder.AndImmediate(0x80);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xD0, negativeLabel); // BNE negativeLabel
        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(negativeLabel);
        builder.LoadAImmediate(0xFF);
        builder.Label(endLabel);
    }

    private static byte HighAddress(byte lowAddress) => (byte)(lowAddress + 1);

    private void TrackImmutable(DeclarationSyntax declaration)
    {
        if (declaration.IsImmutable)
        {
            immutableVariables.Add(ScopedVariableName(declaration.Name));
        }
    }

    // Signed scalar locations use sign-bit-flipped ordering in relational compares. Unsigned
    // scalars, bools, and enums keep ordinary unsigned comparison.
    private void TrackSignedByteType(string type, string scopedName)
    {
        if (type == "i8")
        {
            signedByteLocations.Add(scopedName);
        }
    }

    private bool IsSignedRelationalOperand(ExpressionSyntax expression)
    {
        if (TryExpressionStorageType(expression, out var type))
        {
            return type is "i8" or "i16";
        }

        return expression switch
        {
            IdentifierSyntax identifier => signedByteLocations.Contains(ScopedVariableName(identifier.Identifier)),
            MemberAccessSyntax memberAccess when HasIdentifierRoot(memberAccess) =>
                signedByteLocations.Contains(ScopedVariableName(NesVideoProgram.MemberAccessName(memberAccess))),
            _ => false,
        };
    }

    private static bool HasIdentifierRoot(MemberAccessSyntax memberAccess)
    {
        ExpressionSyntax current = memberAccess;
        while (current is MemberAccessSyntax member)
        {
            current = member.Target;
        }

        return current is IdentifierSyntax;
    }

    private void PushInlineVariableScope()
    {
        inlineVariableScopes.Push(new InlineVariableScope($"__retrosharp_inline_{++nextInlineVariableScopeId}"));
    }

    private void PopInlineVariableScope()
    {
        inlineVariableScopes.Pop();
    }

    private string DeclareScopedVariableName(string name)
    {
        if (!inlineVariableScopes.TryPeek(out var scope))
        {
            return name;
        }

        if (scope.Names.TryGetValue(name, out var scopedName))
        {
            return scopedName;
        }

        scopedName = $"{scope.Prefix}_{name}";
        scope.Names.Add(name, scopedName);
        return scopedName;
    }

    private void MapScopedVariableName(string name, string scopedName)
    {
        if (inlineVariableScopes.TryPeek(out var scope))
        {
            scope.Names[name] = scopedName;
        }
    }

    private string ScopedVariableName(string name)
    {
        foreach (var scope in inlineVariableScopes)
        {
            if (scope.Names.TryGetValue(name, out var scopedName))
            {
                return scopedName;
            }
        }

        return name;
    }

    private static bool IsByteBackedType(string type)
    {
        return type is "i8" or "u8" or "i16" or "u16" or "bool";
    }

    private bool IsByteBackedLocalType(string type)
    {
        return IsByteBackedType(type) || program.Enums.ContainsKey(type);
    }

    private bool IsScalarLocalType(string type)
    {
        return IsByteBackedLocalType(type);
    }

    private static bool IsWordBackedType(string type)
    {
        return type is "i16" or "u16";
    }

    private int StorageSize(string type)
    {
        return IsWordBackedType(type) ? 2 : 1;
    }

    private string VariableStorageType(string name)
    {
        var scopedName = ScopedVariableName(name);
        if (!variableTypes.TryGetValue(scopedName, out var type))
        {
            throw new InvalidOperationException($"Use of undeclared variable '{name}'.");
        }

        return type;
    }

    private bool TryDirectStorageExpression(ExpressionSyntax expression, out byte address, out string type)
    {
        switch (expression)
        {
            case CastSyntax cast:
                return TryDirectStorageExpression(cast.Expression, out address, out type);
            case IdentifierSyntax identifier:
                address = VariableAddress(identifier.Identifier);
                type = VariableStorageType(identifier.Identifier);
                return true;
            case MemberAccessSyntax memberAccess:
                if (TryRuntimeIndexedMemberAccess(memberAccess, out _, out _))
                {
                    address = 0;
                    type = string.Empty;
                    return false;
                }

                var memberName = NesVideoProgram.MemberAccessName(memberAccess);
                address = VariableAddress(memberName);
                type = VariableStorageType(memberName);
                return true;
            case IndexExpressionSyntax indexExpression when TryConst(indexExpression.Index, out _):
                var elementName = IndexedElementName(indexExpression.BaseIdentifier, indexExpression.Index, $"{indexExpression.BaseIdentifier} array index");
                address = VariableAddress(elementName);
                type = VariableStorageType(elementName);
                return true;
            default:
                address = 0;
                type = string.Empty;
                return false;
        }
    }

    private string WordExpressionType(ExpressionSyntax expression)
    {
        if (TryExpressionStorageType(expression, out var type) && IsWordBackedType(type))
        {
            return type;
        }

        return "u16";
    }

    private bool TryExpressionStorageType(ExpressionSyntax expression, out string type)
    {
        switch (expression)
        {
            case CastSyntax cast:
                type = cast.Type;
                return true;
            case IdentifierSyntax { Identifier: "true" or "false" }:
                break;
            case IdentifierSyntax identifier:
                type = VariableStorageType(identifier.Identifier);
                return true;
            case MemberAccessSyntax memberAccess when !TryRuntimeIndexedMemberAccess(memberAccess, out _, out _):
                type = VariableStorageType(NesVideoProgram.MemberAccessName(memberAccess));
                return true;
            case IndexExpressionSyntax indexExpression when TryConst(indexExpression.Index, out _):
                type = VariableStorageType(IndexedElementName(indexExpression.BaseIdentifier, indexExpression.Index, $"{indexExpression.BaseIdentifier} array index"));
                return true;
            case BinaryExpressionSyntax binary when binary.Operator.Symbol is "+" or "-":
                if (TryExpressionStorageType(binary.Left, out var leftType) && IsWordBackedType(leftType))
                {
                    type = leftType;
                    return true;
                }

                if (TryExpressionStorageType(binary.Right, out var rightType) && IsWordBackedType(rightType))
                {
                    type = rightType;
                    return true;
                }

                break;
        }

        type = string.Empty;
        return false;
    }

    private void RequireSupportedCastTarget(CastSyntax cast)
    {
        if (!IsByteBackedLocalType(cast.Type))
        {
            throw new InvalidOperationException($"NES target only supports explicit casts to byte-backed local types; '{cast.Type}' is not supported yet.");
        }
    }

    private void EmitExpressionStatement(ExpressionStatementSyntax expressionStatement)
    {
        switch (expressionStatement.Expression)
        {
            case AssignmentSyntax assignment:
                EmitAssignment(assignment);
                break;
            case FunctionCall call:
                EmitCall(call);
                break;
            default:
                throw new InvalidOperationException($"Unsupported NES expression statement '{expressionStatement.Expression.GetType().Name}'.");
        }
    }

    private void EmitAssignment(AssignmentSyntax assignment)
    {
        RequireMutableAssignmentTarget(assignment.Left);

        if (assignment.Left is IndexLValue indexLValue && !TryConst(indexLValue.Index, out _))
        {
            EmitRuntimeIndexedAssignment(indexLValue, assignment);
            return;
        }

        if (assignment.Left is MemberAccessLValue memberLValue && TryRuntimeIndexedMemberAccess(memberLValue.MemberAccess, out var indexedBase, out var fieldName))
        {
            EmitRuntimeIndexedMemberAssignment(indexedBase, fieldName, assignment);
            return;
        }

        var address = LValueAddress(assignment.Left);
        var targetType = LValueStorageType(assignment.Left);
        if (IsWordBackedType(targetType))
        {
            EmitWordAssignment(assignment, address, targetType);
            return;
        }

        EmitAssignmentRightToA(assignment);
        builder.StoreAZeroPage(address);
    }

    private void RequireMutableAssignmentTarget(LValue lValue)
    {
        if (AssignedRoot(lValue) is { } name && immutableVariables.Contains(ScopedVariableName(name)))
        {
            throw new InvalidOperationException($"Cannot assign to immutable local '{name}'.");
        }
    }

    private static string? AssignedRoot(LValue lValue)
    {
        return lValue switch
        {
            IdentifierLValue identifier => identifier.Identifier,
            IndexLValue index => index.BaseIdentifier,
            MemberAccessLValue memberAccess => MemberAccessRoot(memberAccess.MemberAccess),
            _ => null,
        };
    }

    private static string? MemberAccessRoot(MemberAccessSyntax memberAccess)
    {
        return memberAccess.Target switch
        {
            IdentifierSyntax identifier => identifier.Identifier,
            IndexExpressionSyntax indexExpression => indexExpression.BaseIdentifier,
            MemberAccessSyntax nested => MemberAccessRoot(nested),
            _ => null,
        };
    }

    private void EmitRuntimeIndexedAssignment(IndexLValue indexLValue, AssignmentSyntax assignment)
    {
        var baseAddress = ArrayBaseAddress(indexLValue.BaseIdentifier);
        EmitRuntimeIndexToX(indexLValue.BaseIdentifier, indexLValue.Index);
        var elementType = VariableStorageType(IndexedElementName(indexLValue.BaseIdentifier, 0));
        if (IsWordBackedType(elementType))
        {
            EmitRuntimeIndexedWordAssignment(baseAddress, assignment, elementType, "runtime indexed assignment");
            return;
        }

        switch (assignment.OperatorSymbol)
        {
            case "=":
                RequireExpressionPreservesX(assignment.Right, "runtime indexed assignment");
                EmitExpressionToA(assignment.Right);
                builder.StoreAZeroPageX(baseAddress);
                return;
            case "+=":
                if (TryConst(assignment.Right, out var addRight))
                {
                    builder.LoadAZeroPageX(baseAddress);
                    builder.ClearCarry();
                    builder.AddImmediate(addRight);
                    builder.StoreAZeroPageX(baseAddress);
                    return;
                }

                if (TryDirectAddress(assignment.Right, out var addAddress))
                {
                    builder.LoadAZeroPage(addAddress);
                    builder.ClearCarry();
                    builder.AddZeroPageX(baseAddress);
                    builder.StoreAZeroPageX(baseAddress);
                    return;
                }

                throw new InvalidOperationException("NES target only supports constants or direct byte-backed values on the right side of runtime indexed += assignments.");
            case "-=":
                builder.LoadAZeroPageX(baseAddress);
                builder.SetCarry();
                if (TryConst(assignment.Right, out var subtractRight))
                {
                    builder.SubtractImmediate(subtractRight);
                    builder.StoreAZeroPageX(baseAddress);
                    return;
                }

                if (TryDirectAddress(assignment.Right, out var subtractAddress))
                {
                    builder.SubtractZeroPage(subtractAddress);
                    builder.StoreAZeroPageX(baseAddress);
                    return;
                }

                throw new InvalidOperationException("NES target only supports constants or direct byte-backed values on the right side of runtime indexed -= assignments.");
            case "&=":
                EmitRuntimeIndexedBitwiseCompoundAssignment(baseAddress, assignment.Right, "&", "runtime indexed &= assignment");
                return;
            case "|=":
                EmitRuntimeIndexedBitwiseCompoundAssignment(baseAddress, assignment.Right, "|", "runtime indexed |= assignment");
                return;
            case "^=":
                EmitRuntimeIndexedBitwiseCompoundAssignment(baseAddress, assignment.Right, "^", "runtime indexed ^= assignment");
                return;
            default:
                throw new InvalidOperationException($"Unsupported NES assignment operator '{assignment.OperatorSymbol}'.");
        }
    }

    private void EmitRuntimeIndexedBitwiseCompoundAssignment(byte baseAddress, ExpressionSyntax right, string op, string context)
    {
        builder.LoadAZeroPageX(baseAddress);
        if (TryConst(right, out var constant))
        {
            EmitBitwiseImmediate(op, constant);
            builder.StoreAZeroPageX(baseAddress);
            return;
        }

        if (TryDirectAddress(right, out var address))
        {
            EmitBitwiseZeroPage(op, address);
            builder.StoreAZeroPageX(baseAddress);
            return;
        }

        throw new InvalidOperationException($"NES target only supports constants or direct byte-backed values on the right side of {context}.");
    }

    private void EmitRuntimeIndexedMemberAssignment(IndexExpressionSyntax indexExpression, string fieldName, AssignmentSyntax assignment)
    {
        var baseAddress = RuntimeIndexedMemberBaseAddress(indexExpression, fieldName);
        EmitRuntimeMemberIndexToX(indexExpression);
        var fieldType = VariableStorageType(IndexedMemberName(indexExpression.BaseIdentifier, 0, fieldName));
        if (IsWordBackedType(fieldType))
        {
            EmitRuntimeIndexedWordAssignment(baseAddress, assignment, fieldType, "runtime indexed struct field assignment");
            return;
        }

        switch (assignment.OperatorSymbol)
        {
            case "=":
                RequireExpressionPreservesX(assignment.Right, "runtime indexed struct field assignment");
                EmitExpressionToA(assignment.Right);
                builder.StoreAZeroPageX(baseAddress);
                return;
            case "+=":
                if (TryConst(assignment.Right, out var addRight))
                {
                    builder.LoadAZeroPageX(baseAddress);
                    builder.ClearCarry();
                    builder.AddImmediate(addRight);
                    builder.StoreAZeroPageX(baseAddress);
                    return;
                }

                if (TryDirectAddress(assignment.Right, out var addAddress))
                {
                    builder.LoadAZeroPage(addAddress);
                    builder.ClearCarry();
                    builder.AddZeroPageX(baseAddress);
                    builder.StoreAZeroPageX(baseAddress);
                    return;
                }

                throw new InvalidOperationException("NES target only supports constants or direct byte-backed values on the right side of runtime indexed struct field += assignments.");
            case "-=":
                builder.LoadAZeroPageX(baseAddress);
                builder.SetCarry();
                if (TryConst(assignment.Right, out var subtractRight))
                {
                    builder.SubtractImmediate(subtractRight);
                    builder.StoreAZeroPageX(baseAddress);
                    return;
                }

                if (TryDirectAddress(assignment.Right, out var subtractAddress))
                {
                    builder.SubtractZeroPage(subtractAddress);
                    builder.StoreAZeroPageX(baseAddress);
                    return;
                }

                throw new InvalidOperationException("NES target only supports constants or direct byte-backed values on the right side of runtime indexed struct field -= assignments.");
            case "&=":
                EmitRuntimeIndexedBitwiseCompoundAssignment(baseAddress, assignment.Right, "&", "runtime indexed struct field &= assignment");
                return;
            case "|=":
                EmitRuntimeIndexedBitwiseCompoundAssignment(baseAddress, assignment.Right, "|", "runtime indexed struct field |= assignment");
                return;
            case "^=":
                EmitRuntimeIndexedBitwiseCompoundAssignment(baseAddress, assignment.Right, "^", "runtime indexed struct field ^= assignment");
                return;
            default:
                throw new InvalidOperationException($"Unsupported NES assignment operator '{assignment.OperatorSymbol}'.");
        }
    }

    private void EmitRuntimeIndexedWordAssignment(byte baseAddress, AssignmentSyntax assignment, string targetType, string context)
    {
        if (assignment.OperatorSymbol != "=")
        {
            throw new InvalidOperationException($"NES target only supports direct '=' for 16-bit {context}.");
        }

        RequireExpressionPreservesX(assignment.Right, context);
        EmitWordExpressionToZeroPageX(baseAddress, assignment.Right, targetType);
    }

    private void EmitWordExpressionToZeroPageX(byte baseAddress, ExpressionSyntax expression, string targetType)
    {
        if (TryConst(expression, out var constant))
        {
            builder.LoadAImmediate(constant & 0xFF);
            builder.StoreAZeroPageX(baseAddress);
            builder.LoadAImmediate((constant >> 8) & 0xFF);
            builder.StoreAZeroPageX(HighAddress(baseAddress));
            return;
        }

        if (TryDirectStorageExpression(expression, out var sourceAddress, out var sourceType))
        {
            builder.LoadAZeroPage(sourceAddress);
            builder.StoreAZeroPageX(baseAddress);
            if (IsWordBackedType(sourceType))
            {
                builder.LoadAZeroPage(HighAddress(sourceAddress));
            }
            else if (sourceType == "i8")
            {
                builder.LoadAZeroPage(sourceAddress);
                EmitSignExtensionFromA();
            }
            else
            {
                builder.LoadAImmediate(0);
            }

            builder.StoreAZeroPageX(HighAddress(baseAddress));
            return;
        }

        EmitWordExpressionToStorage(expression, RuntimeIndexScratchAddress, targetType);
        builder.LoadAZeroPage(RuntimeIndexScratchAddress);
        builder.StoreAZeroPageX(baseAddress);
        builder.LoadAZeroPage(ExpressionScratchAddress);
        builder.StoreAZeroPageX(HighAddress(baseAddress));
    }

    private void EmitAssignmentRightToA(AssignmentSyntax assignment)
    {
        switch (assignment.OperatorSymbol)
        {
            case "=":
                EmitExpressionToA(assignment.Right);
                return;
            case "+=":
                EmitExpressionToA(new BinaryExpressionSyntax(ExpressionFromLValue(assignment.Left), assignment.Right, Operator.Get("+")));
                return;
            case "-=":
                EmitExpressionToA(new BinaryExpressionSyntax(ExpressionFromLValue(assignment.Left), assignment.Right, Operator.Get("-")));
                return;
            case "&=":
                EmitExpressionToA(new BinaryExpressionSyntax(ExpressionFromLValue(assignment.Left), assignment.Right, Operator.Get("&")));
                return;
            case "|=":
                EmitExpressionToA(new BinaryExpressionSyntax(ExpressionFromLValue(assignment.Left), assignment.Right, Operator.Get("|")));
                return;
            case "^=":
                EmitExpressionToA(new BinaryExpressionSyntax(ExpressionFromLValue(assignment.Left), assignment.Right, Operator.Get("^")));
                return;
            default:
                throw new InvalidOperationException($"Unsupported NES assignment operator '{assignment.OperatorSymbol}'.");
        }
    }

    private void EmitWordAssignment(AssignmentSyntax assignment, byte address, string targetType)
    {
        switch (assignment.OperatorSymbol)
        {
            case "=":
                EmitWordExpressionToStorage(assignment.Right, address, targetType);
                return;
            case "+=":
                EmitAddWordIntoStorage(address, assignment.Right);
                return;
            case "-=":
                EmitSubtractWordFromStorage(address, assignment.Right);
                return;
            default:
                throw new InvalidOperationException($"NES target does not support 16-bit assignment operator '{assignment.OperatorSymbol}'.");
        }
    }

    private byte LValueAddress(LValue lValue)
    {
        return lValue switch
        {
            IdentifierLValue identifier => VariableAddress(identifier.Identifier),
            MemberAccessLValue memberAccess => VariableAddress(NesVideoProgram.MemberAccessName(memberAccess.MemberAccess)),
            IndexLValue index => VariableAddress(IndexedElementName(index.BaseIdentifier, index.Index, $"{index.BaseIdentifier} array index")),
            _ => throw new InvalidOperationException("NES target only supports assignments to local variables, struct fields, or constant array indices."),
        };
    }

    private string LValueStorageType(LValue lValue)
    {
        return lValue switch
        {
            IdentifierLValue identifier => VariableStorageType(identifier.Identifier),
            MemberAccessLValue memberAccess => VariableStorageType(NesVideoProgram.MemberAccessName(memberAccess.MemberAccess)),
            IndexLValue index => VariableStorageType(IndexedElementName(index.BaseIdentifier, index.Index, $"{index.BaseIdentifier} array index")),
            _ => throw new InvalidOperationException("NES target only supports assignments to local variables, struct fields, or constant array indices."),
        };
    }

    private static ExpressionSyntax ExpressionFromLValue(LValue lValue)
    {
        return lValue switch
        {
            IdentifierLValue identifier => new IdentifierSyntax(identifier.Identifier),
            MemberAccessLValue memberAccess => memberAccess.MemberAccess,
            IndexLValue index => new IndexExpressionSyntax(index.BaseIdentifier, index.Index),
            _ => throw new InvalidOperationException("Compound assignment target must be readable."),
        };
    }

    private void EmitCall(FunctionCall call)
    {
        if (IsResourceDeclarationCall(call))
        {
            return;
        }

        switch (call.Name)
        {
            case "video_init":
            case "video_present":
                NesVideoProgram.RequireArity(call, 0);
                break;
            case "palette_set":
                NesVideoProgram.RequireArity(call, 2);
                break;
            case "palette_background":
            case "palette_sprite":
                NesVideoProgram.RequireArity(call, 5);
                break;
            case "tilemap_set":
                NesVideoProgram.RequireArity(call, 3);
                break;
            case "tilemap_fill":
                NesVideoProgram.RequireArity(call, 5);
                break;
            case "map_column":
            case "world_column":
            case "world_flags":
            case "world_map":
                break;
            case "map_stream_column":
                EmitSdkOperation(Sdk2DOperationCollector.ReadStreamMapColumn(call));
                break;
            case "map_stream_row":
                EmitSdkOperation(Sdk2DOperationCollector.ReadStreamMapRow(call));
                break;
            case "world_load":
                NesVideoProgram.RequireArity(call, 1);
                break;
            case "sprite_asset":
                var count = call.Parameters.Count();
                if (count is not 2 and not 4)
                {
                    throw new InvalidOperationException($"sprite_asset expects 2 or 4 arguments, got {count}.");
                }

                break;
            case "animation_clip":
                if (call.Parameters.Count() < 3)
                {
                    throw new InvalidOperationException($"animation_clip expects at least 3 arguments, got {call.Parameters.Count()}.");
                }

                break;
            case "music_asset":
            case "sfx_asset":
                NesVideoProgram.RequireArity(call, 2);
                break;
            case "audio_init":
                NesVideoProgram.RequireArity(call, 0);
                EmitAudioInit();
                break;
            case "audio_update":
                NesVideoProgram.RequireArity(call, 0);
                EmitAudioUpdate();
                break;
            case "music_stop":
                NesVideoProgram.RequireArity(call, 0);
                EmitMusicStop();
                break;
            case "music_play":
                NesVideoProgram.RequireArity(call, 1);
                EmitMusicPlay(call);
                break;
            case "sfx_play":
                NesVideoProgram.RequireArity(call, 1);
                EmitSoundEffectPlay(call);
                break;
            case "hud_set_tile":
                NesVideoProgram.ValidateHudSetTile(call);
                break;
            case "camera_init":
                EmitCameraInit(call);
                break;
            case "camera_set_position":
                EmitSdkOperation(Sdk2DOperationCollector.ReadSetCameraPosition(call));
                break;
            case "camera_apply":
                NesVideoProgram.RequireArity(call, 0);
                EmitSdkOperation(new Sdk2DOperation.ApplyCamera(ScrollAxes.Horizontal));
                break;
            case "video_wait_vblank":
                NesVideoProgram.RequireArity(call, 0);
                EmitSdkOperation(new Sdk2DOperation.WaitFrame());
                break;
            case "input_poll":
                NesVideoProgram.RequireArity(call, 0);
                EmitSdkOperation(new Sdk2DOperation.PollInput());
                break;
            case "sprite_draw":
                EmitSdkOperation(Sdk2DOperationCollector.ReadDrawLogicalSprite(call));
                break;
            default:
                if (TryEmitTargetIntrinsic(call))
                {
                    break;
                }

                if (TryEmitUserFunction(call))
                {
                    break;
                }

                throw new InvalidOperationException($"Unsupported NES video API call '{call.Name}'.");
        }
    }

    private bool IsResourceDeclarationCall(FunctionCall call)
    {
        return program.Functions.TryGetValue(call.Name, out var function)
               && SdkResourceDeclarationResolver.TryResolve(function, out _);
    }

    private bool TryEmitUserFunction(FunctionCall call)
    {
        if (!program.Functions.TryGetValue(call.Name, out var function))
        {
            return false;
        }

        if (function.IsExtern)
        {
            return false;
        }

        if (!userFunctionCallStack.Add(function.Name))
        {
            throw new InvalidOperationException($"Recursive NES user function call '{function.Name}' is not supported.");
        }

        try
        {
            PushInlineVariableScope();
            EmitBlock(ParameterSubstitution.Substitute(function, call, "NES"));
        }
        finally
        {
            PopInlineVariableScope();
            userFunctionCallStack.Remove(function.Name);
        }

        return true;
    }

    private bool TryEmitUserValueFunction(FunctionCall call)
    {
        if (!program.Functions.TryGetValue(call.Name, out var function))
        {
            return false;
        }

        if (function.IsExtern)
        {
            return false;
        }

        if (!userFunctionCallStack.Add(function.Name))
        {
            throw new InvalidOperationException($"Recursive NES user function call '{function.Name}' is not supported.");
        }

        try
        {
            EmitExpressionToA(ParameterSubstitution.SubstituteReturnExpression(function, call, "NES"));
        }
        finally
        {
            userFunctionCallStack.Remove(function.Name);
        }

        return true;
    }

    private void EmitWhile(WhileSyntax whileSyntax)
    {
        var conditionIsConstant = TryConst(whileSyntax.Condition, out var condition);
        if (conditionIsConstant && condition == 0)
        {
            return;
        }

        var loopLabel = builder.CreateLabel("while");
        var endLabel = builder.CreateLabel("while_end");
        builder.Label(loopLabel);
        if (!conditionIsConstant)
        {
            EmitConditionFalseJump(whileSyntax.Condition, endLabel);
        }

        loopTargets.Push(new LoopTarget(endLabel, loopLabel));
        try
        {
            EmitBlock(whileSyntax.Body);
        }
        finally
        {
            loopTargets.Pop();
        }

        builder.JumpAbsolute(loopLabel);
        builder.Label(endLabel);
    }

    private void EmitDoWhile(DoWhileSyntax doWhileSyntax)
    {
        var loopLabel = builder.CreateLabel("do");
        var continueLabel = builder.CreateLabel("do_continue");
        var endLabel = builder.CreateLabel("do_end");

        builder.Label(loopLabel);
        loopTargets.Push(new LoopTarget(endLabel, continueLabel));
        try
        {
            EmitBlock(doWhileSyntax.Body);
        }
        finally
        {
            loopTargets.Pop();
        }

        builder.Label(continueLabel);
        EmitConditionFalseJump(doWhileSyntax.Condition, endLabel);
        builder.JumpAbsolute(loopLabel);
        builder.Label(endLabel);
    }

    private void EmitFor(ForSyntax forSyntax)
    {
        if (forSyntax.Initializer.HasValue)
        {
            EmitStatement(forSyntax.Initializer.Value);
        }

        var forLoopId = nextForLoopId++;
        var loopLabel = $"for_{forLoopId}";
        var continueLabel = $"for_continue_{forLoopId}";
        var endLabel = $"for_end_{forLoopId}";

        builder.Label(loopLabel);
        if (forSyntax.Condition.HasValue)
        {
            if (longForLoopIds.Contains(forLoopId))
            {
                EmitConditionFalseJumpToFarLabel(forSyntax.Condition.Value, endLabel, $"for_condition_false_{forLoopId}");
            }
            else
            {
                EmitConditionFalseJump(forSyntax.Condition.Value, endLabel);
            }
        }

        loopTargets.Push(new LoopTarget(endLabel, continueLabel));
        try
        {
            EmitBlock(forSyntax.Body);
        }
        finally
        {
            loopTargets.Pop();
        }

        builder.Label(continueLabel);
        if (forSyntax.Increment.HasValue)
        {
            if (forSyntax.Increment.Value is not AssignmentSyntax increment)
            {
                throw new InvalidOperationException($"Unsupported NES for increment '{forSyntax.Increment.Value.GetType().Name}'.");
            }

            EmitAssignment(increment);
        }

        builder.JumpAbsolute(loopLabel);
        builder.Label(endLabel);
    }

    private void EmitConditionFalseJumpToFarLabel(ExpressionSyntax condition, string targetLabel, string prefix)
    {
        var trampolineLabel = builder.CreateLabel(prefix);
        var continueLabel = builder.CreateLabel($"{prefix}_continue");
        EmitConditionFalseJump(condition, trampolineLabel);
        builder.JumpAbsolute(continueLabel);
        builder.Label(trampolineLabel);
        builder.JumpAbsolute(targetLabel);
        builder.Label(continueLabel);
    }

    private bool EmitIf(IfElseSyntax ifElseSyntax)
    {
        var trueLabel = builder.CreateLabel("if_true");
        var falseTrampolineLabel = builder.CreateLabel("if_false_trampoline");
        var falseLabel = builder.CreateLabel("if_false");
        var endLabel = builder.CreateLabel("if_end");

        EmitConditionFalseJump(ifElseSyntax.Condition, falseTrampolineLabel);
        builder.JumpAbsolute(trueLabel);
        builder.Label(falseTrampolineLabel);
        builder.JumpAbsolute(falseLabel);
        builder.Label(trueLabel);
        var thenTerminates = EmitBlock(ifElseSyntax.ThenBlock);
        if (ifElseSyntax.ElseBlock.HasValue)
        {
            if (!thenTerminates)
            {
                builder.JumpAbsolute(endLabel);
            }

            builder.Label(falseLabel);
            var elseTerminates = EmitBlock(ifElseSyntax.ElseBlock.Value);
            builder.Label(endLabel);
            return thenTerminates && elseTerminates;
        }

        builder.Label(falseLabel);
        return false;
    }

    private void EmitBreak()
    {
        if (loopTargets.Count == 0)
        {
            throw new InvalidOperationException("break can only be used inside a loop.");
        }

        builder.JumpAbsolute(loopTargets.Peek().BreakLabel);
    }

    private void EmitContinue()
    {
        if (loopTargets.Count == 0)
        {
            throw new InvalidOperationException("continue can only be used inside a loop.");
        }

        builder.JumpAbsolute(loopTargets.Peek().ContinueLabel);
    }

    private void EmitConditionFalseJump(ExpressionSyntax condition, string falseLabel)
    {
        if (TryConst(condition, out var constant))
        {
            if (constant == 0)
            {
                builder.JumpAbsolute(falseLabel);
            }

            return;
        }

        if (condition is BinaryExpressionSyntax binary)
        {
            switch (binary.Operator.Symbol)
            {
                case "&&":
                    EmitConditionFalseJump(binary.Left, falseLabel);
                    EmitConditionFalseJump(binary.Right, falseLabel);
                    return;
                case "||":
                    var trueLabel = builder.CreateLabel("or_true");
                    EmitConditionTrueJump(binary.Left, trueLabel);
                    EmitConditionFalseJump(binary.Right, falseLabel);
                    builder.Label(trueLabel);
                    return;
                case "==":
                    if (IsWordComparison(binary))
                    {
                        EmitWordEqualityFalseJump(binary.Left, binary.Right, falseLabel);
                        return;
                    }

                    EmitCompare(binary.Left, binary.Right);
                    builder.BranchRelative(0xD0, falseLabel); // BNE falseLabel
                    return;
                case "!=":
                    if (IsWordComparison(binary))
                    {
                        EmitWordInequalityFalseJump(binary.Left, binary.Right, falseLabel);
                        return;
                    }

                    EmitCompare(binary.Left, binary.Right);
                    builder.BranchRelative(0xF0, falseLabel); // BEQ falseLabel
                    return;
                case "<":
                case "<=":
                case ">":
                case ">=":
                    if (IsWordComparison(binary))
                    {
                        EmitWordRelationalFalseJump(binary, falseLabel);
                        return;
                    }

                    EmitRelationalFalseJump(binary, falseLabel);
                    return;
            }
        }

        if (condition is UnaryExpressionSyntax { OperatorSymbol: "!" } unary)
        {
            EmitConditionTrueJump(unary.Operand, falseLabel);
            return;
        }

        EmitExpressionToA(condition);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, falseLabel);   // BEQ falseLabel
    }

    private void EmitConditionTrueJump(ExpressionSyntax condition, string trueLabel)
    {
        if (TryConst(condition, out var constant))
        {
            if (constant != 0)
            {
                builder.JumpAbsolute(trueLabel);
            }

            return;
        }

        if (condition is BinaryExpressionSyntax binary)
        {
            switch (binary.Operator.Symbol)
            {
                case "&&":
                    var falseLabel = builder.CreateLabel("and_false");
                    EmitConditionFalseJump(binary.Left, falseLabel);
                    EmitConditionTrueJump(binary.Right, trueLabel);
                    builder.Label(falseLabel);
                    return;
                case "||":
                    EmitConditionTrueJump(binary.Left, trueLabel);
                    EmitConditionTrueJump(binary.Right, trueLabel);
                    return;
                case "==":
                    if (IsWordComparison(binary))
                    {
                        EmitWordEqualityTrueJump(binary.Left, binary.Right, trueLabel);
                        return;
                    }

                    EmitCompare(binary.Left, binary.Right);
                    builder.BranchRelative(0xF0, trueLabel); // BEQ trueLabel
                    return;
                case "!=":
                    if (IsWordComparison(binary))
                    {
                        EmitWordInequalityTrueJump(binary.Left, binary.Right, trueLabel);
                        return;
                    }

                    EmitCompare(binary.Left, binary.Right);
                    builder.BranchRelative(0xD0, trueLabel); // BNE trueLabel
                    return;
            }
        }

        if (condition is UnaryExpressionSyntax { OperatorSymbol: "!" } unary)
        {
            EmitConditionFalseJump(unary.Operand, trueLabel);
            return;
        }

        EmitExpressionToA(condition);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xD0, trueLabel);     // BNE trueLabel
    }

    private void EmitCompare(ExpressionSyntax left, ExpressionSyntax right)
    {
        if (TryConst(right, out var rightConstant))
        {
            EmitExpressionToA(left);
            builder.CompareImmediate(rightConstant);
            return;
        }

        if (TryConst(left, out var leftConstant))
        {
            EmitExpressionToA(right);
            builder.CompareImmediate(leftConstant);
            return;
        }

        EmitVariableOperandsToAAndScratch(left, right);
        builder.CompareZeroPage(ExpressionScratchAddress);
    }

    private void EmitVariableOperandsToAAndScratch(ExpressionSyntax left, ExpressionSyntax right)
    {
        EmitExpressionToA(left);
        builder.PushA();
        EmitExpressionToA(right);
        builder.StoreAZeroPage(ExpressionScratchAddress);
        builder.PullA();
    }

    private bool IsWordComparison(BinaryExpressionSyntax binary)
    {
        return IsWordExpression(binary.Left) || IsWordExpression(binary.Right);
    }

    private bool IsWordExpression(ExpressionSyntax expression)
    {
        return TryExpressionStorageType(expression, out var type) && IsWordBackedType(type);
    }

    private void EmitWordEqualityFalseJump(ExpressionSyntax left, ExpressionSyntax right, string falseLabel)
    {
        EmitCompareWordByte(left, right, highByte: true, signedHighByte: false);
        builder.BranchRelative(0xD0, falseLabel); // BNE falseLabel
        EmitCompareWordByte(left, right, highByte: false, signedHighByte: false);
        builder.BranchRelative(0xD0, falseLabel); // BNE falseLabel
    }

    private void EmitWordInequalityFalseJump(ExpressionSyntax left, ExpressionSyntax right, string falseLabel)
    {
        var trueLabel = builder.CreateLabel("word_neq_true");
        EmitCompareWordByte(left, right, highByte: true, signedHighByte: false);
        builder.BranchRelative(0xD0, trueLabel); // BNE trueLabel
        EmitCompareWordByte(left, right, highByte: false, signedHighByte: false);
        builder.BranchRelative(0xD0, trueLabel); // BNE trueLabel
        builder.JumpAbsolute(falseLabel);
        builder.Label(trueLabel);
    }

    private void EmitWordEqualityTrueJump(ExpressionSyntax left, ExpressionSyntax right, string trueLabel)
    {
        var endLabel = builder.CreateLabel("word_eq_end");
        EmitCompareWordByte(left, right, highByte: true, signedHighByte: false);
        builder.BranchRelative(0xD0, endLabel); // BNE endLabel
        EmitCompareWordByte(left, right, highByte: false, signedHighByte: false);
        builder.BranchRelative(0xF0, trueLabel); // BEQ trueLabel
        builder.Label(endLabel);
    }

    private void EmitWordInequalityTrueJump(ExpressionSyntax left, ExpressionSyntax right, string trueLabel)
    {
        EmitCompareWordByte(left, right, highByte: true, signedHighByte: false);
        builder.BranchRelative(0xD0, trueLabel); // BNE trueLabel
        EmitCompareWordByte(left, right, highByte: false, signedHighByte: false);
        builder.BranchRelative(0xD0, trueLabel); // BNE trueLabel
    }

    private void EmitWordRelationalFalseJump(BinaryExpressionSyntax binary, string falseLabel)
    {
        var trueLabel = builder.CreateLabel("word_rel_true");
        var localFalseLabel = builder.CreateLabel("word_rel_false");
        EmitWordRelationalJump(binary, trueLabel, localFalseLabel);
        builder.Label(localFalseLabel);
        builder.JumpAbsolute(falseLabel);
        builder.Label(trueLabel);
    }

    private void EmitWordRelationalJump(BinaryExpressionSyntax binary, string trueLabel, string falseLabel)
    {
        var signed = IsSignedRelationalOperand(binary.Left) || IsSignedRelationalOperand(binary.Right);
        EmitCompareWordByte(binary.Left, binary.Right, highByte: true, signedHighByte: signed);

        switch (binary.Operator.Symbol)
        {
            case "<":
                builder.BranchRelative(0x90, trueLabel);  // BCC trueLabel
                builder.BranchRelative(0xD0, falseLabel); // BNE falseLabel
                EmitCompareWordByte(binary.Left, binary.Right, highByte: false, signedHighByte: false);
                builder.BranchRelative(0x90, trueLabel);  // BCC trueLabel
                builder.JumpAbsolute(falseLabel);
                return;
            case "<=":
                builder.BranchRelative(0x90, trueLabel);  // BCC trueLabel
                builder.BranchRelative(0xD0, falseLabel); // BNE falseLabel
                EmitCompareWordByte(binary.Left, binary.Right, highByte: false, signedHighByte: false);
                builder.BranchRelative(0x90, trueLabel);  // BCC trueLabel
                builder.BranchRelative(0xF0, trueLabel);  // BEQ trueLabel
                builder.JumpAbsolute(falseLabel);
                return;
            case ">":
                builder.BranchRelative(0x90, falseLabel); // BCC falseLabel
                builder.BranchRelative(0xD0, trueLabel);  // BNE trueLabel
                EmitCompareWordByte(binary.Left, binary.Right, highByte: false, signedHighByte: false);
                builder.BranchRelative(0x90, falseLabel); // BCC falseLabel
                builder.BranchRelative(0xF0, falseLabel); // BEQ falseLabel
                builder.JumpAbsolute(trueLabel);
                return;
            case ">=":
                builder.BranchRelative(0x90, falseLabel); // BCC falseLabel
                builder.BranchRelative(0xD0, trueLabel);  // BNE trueLabel
                EmitCompareWordByte(binary.Left, binary.Right, highByte: false, signedHighByte: false);
                builder.BranchRelative(0x90, falseLabel); // BCC falseLabel
                builder.JumpAbsolute(trueLabel);
                return;
            default:
                throw new InvalidOperationException($"Unsupported NES relational operator '{binary.Operator.Symbol}'.");
        }
    }

    private void EmitCompareWordByte(ExpressionSyntax left, ExpressionSyntax right, bool highByte, bool signedHighByte)
    {
        EmitLoadWordByteToA(left, highByte, signedHighByte);
        if (TryConst(right, out var rightConstant))
        {
            var value = WordByte(rightConstant, highByte);
            builder.CompareImmediate(signedHighByte ? value ^ 0x80 : value);
            return;
        }

        builder.PushA();
        EmitLoadWordByteToScratch(right, highByte, signedHighByte);
        builder.PullA();
        builder.CompareZeroPage(ExpressionScratchAddress);
    }

    private void EmitLoadWordByteToScratch(ExpressionSyntax expression, bool highByte, bool signedHighByte)
    {
        EmitLoadWordByteToA(expression, highByte, signedHighByte);
        builder.StoreAZeroPage(ExpressionScratchAddress);
    }

    private void EmitLoadWordByteToA(ExpressionSyntax expression, bool highByte, bool signedHighByte)
    {
        if (TryConst(expression, out var constant))
        {
            var value = WordByte(constant, highByte);
            builder.LoadAImmediate(signedHighByte ? value ^ 0x80 : value);
            return;
        }

        if (TryDirectStorageExpression(expression, out var address, out var type))
        {
            if (!highByte)
            {
                builder.LoadAZeroPage(address);
            }
            else if (IsWordBackedType(type))
            {
                builder.LoadAZeroPage(HighAddress(address));
            }
            else if (type == "i8")
            {
                builder.LoadAZeroPage(address);
                EmitSignExtensionFromA();
            }
            else
            {
                builder.LoadAImmediate(0);
            }

            if (signedHighByte)
            {
                builder.XorImmediate(0x80);
            }

            return;
        }

        EmitWordExpressionToStorage(expression, RuntimeIndexScratchAddress, WordExpressionType(expression));
        builder.LoadAZeroPage(highByte ? ExpressionScratchAddress : RuntimeIndexScratchAddress);
        if (signedHighByte)
        {
            builder.XorImmediate(0x80);
        }
    }

    private static int WordByte(int value, bool highByte)
    {
        return highByte ? (value >> 8) & 0xFF : value & 0xFF;
    }

    private void EmitRelationalFalseJump(BinaryExpressionSyntax binary, string falseLabel)
    {
        var signed = IsSignedRelationalOperand(binary.Left) || IsSignedRelationalOperand(binary.Right);

        if (TryConst(binary.Right, out var rightConstant))
        {
            EmitExpressionToA(binary.Left);
            if (signed)
            {
                builder.XorImmediate(0x80);
            }

            builder.CompareImmediate(signed ? rightConstant ^ 0x80 : rightConstant);
            EmitRelationalFalseJump(binary.Operator.Symbol, falseLabel);
            return;
        }

        if (TryConst(binary.Left, out var leftConstant))
        {
            EmitExpressionToA(binary.Right);
            if (signed)
            {
                builder.XorImmediate(0x80);
            }

            builder.CompareImmediate(signed ? leftConstant ^ 0x80 : leftConstant);
            EmitRelationalFalseJump(FlipRelationalOperator(binary.Operator.Symbol), falseLabel);
            return;
        }

        EmitRelationalCompare(binary.Left, binary.Right, signed);
        EmitRelationalFalseJump(binary.Operator.Symbol, falseLabel);
    }

    // Signed relational comparison reuses the unsigned CMP path after flipping both operands' sign bit
    // (EOR #$80), which maps signed [-128,127] onto unsigned [0,255] while preserving order.
    private void EmitRelationalCompare(ExpressionSyntax left, ExpressionSyntax right, bool signed)
    {
        if (!signed)
        {
            EmitCompare(left, right);
            return;
        }

        EmitExpressionToA(left);
        builder.XorImmediate(0x80);
        builder.PushA();
        EmitExpressionToA(right);
        builder.XorImmediate(0x80);
        builder.StoreAZeroPage(ExpressionScratchAddress);
        builder.PullA();
        builder.CompareZeroPage(ExpressionScratchAddress);
    }

    private void EmitRelationalFalseJump(string op, string falseLabel)
    {
        switch (op)
        {
            case "<":
                builder.BranchRelative(0xB0, falseLabel); // BCS falseLabel
                return;
            case "<=":
                var trueLabel = builder.CreateLabel("rel_true");
                builder.BranchRelative(0x90, trueLabel);  // BCC trueLabel
                builder.BranchRelative(0xF0, trueLabel);  // BEQ trueLabel
                builder.JumpAbsolute(falseLabel);
                builder.Label(trueLabel);
                return;
            case ">":
                builder.BranchRelative(0x90, falseLabel); // BCC falseLabel
                builder.BranchRelative(0xF0, falseLabel); // BEQ falseLabel
                return;
            case ">=":
                builder.BranchRelative(0x90, falseLabel); // BCC falseLabel
                return;
            default:
                throw new InvalidOperationException($"Unsupported NES relational operator '{op}'.");
        }
    }

    private static string FlipRelationalOperator(string op)
    {
        return op switch
        {
            "<" => ">",
            "<=" => ">=",
            ">" => "<",
            ">=" => "<=",
            _ => throw new InvalidOperationException($"Unsupported NES relational operator '{op}'."),
        };
    }

    private void EmitSdkOperation(Sdk2DOperation operation)
    {
        NesSdkOperationLowerer.Emit(this, operation);
    }

    internal void EmitWaitFrame(bool applyPendingCameraScroll = false)
    {
        var clearLabel = builder.CreateLabel("vblank_clear");
        var setLabel = builder.CreateLabel("vblank");
        builder.Label(clearLabel);
        builder.Emit(0x2C, 0x02, 0x20);             // BIT $2002
        builder.BranchRelative(0x30, clearLabel);   // BMI clearLabel
        builder.Label(setLabel);
        builder.Emit(0x2C, 0x02, 0x20);             // BIT $2002
        builder.BranchRelative(0x10, setLabel);     // BPL setLabel
        if (applyPendingCameraScroll)
        {
            EmitApplyPendingCameraScrollAtVBlank();
        }
    }

    private bool TryEmitTargetIntrinsic(FunctionCall call)
    {
        if (!program.Functions.TryGetValue(call.Name, out var function) || !function.IsExtern)
        {
            return false;
        }

        var intrinsic = TargetIntrinsicResolver.Resolve(function, NesTarget.Intrinsics);
        NesVideoProgram.RequireArity(call, intrinsic.Arity);
        switch (intrinsic.Operation)
        {
            case TargetIntrinsicOperation.InitializeVideo:
            case TargetIntrinsicOperation.PresentVideo:
                return true;
            case TargetIntrinsicOperation.WaitFrame:
                EmitWaitFrame(applyPendingCameraScroll: true);
                return true;
            case TargetIntrinsicOperation.PollInput:
                EmitPollInput();
                return true;
            case TargetIntrinsicOperation.UpdateAudio:
                EmitAudioUpdate();
                return true;
            case TargetIntrinsicOperation.InitializeAudio:
                EmitAudioInit();
                return true;
            case TargetIntrinsicOperation.PlayMusic:
                EmitMusicPlay(call);
                return true;
            case TargetIntrinsicOperation.PlaySoundEffect:
                EmitSoundEffectPlay(call);
                return true;
            case TargetIntrinsicOperation.StopMusic:
                EmitMusicStop();
                return true;
            case TargetIntrinsicOperation.InitializeCamera:
                EmitCameraInit(call);
                return true;
            case TargetIntrinsicOperation.SetCameraPosition:
                EmitSdkOperation(Sdk2DOperationCollector.ReadSetCameraPosition(call));
                return true;
            case TargetIntrinsicOperation.ApplyCamera:
                EmitSdkOperation(new Sdk2DOperation.ApplyCamera(ScrollAxes.Horizontal));
                return true;
            case TargetIntrinsicOperation.DrawLogicalSprite:
                EmitSdkOperation(Sdk2DOperationCollector.ReadDrawLogicalSprite(
                    TargetIntrinsicResolver.ResolveCall(function, call, NesTarget.Intrinsics)));
                return true;
            default:
                throw new NotSupportedException($"NES intrinsic lowering does not support {intrinsic.Operation} yet.");
        }
    }

    private void EmitAudioInit()
    {
        builder.LoadAImmediate(0x0F);
        builder.StoreAAbsolute(0x4015);
        builder.LoadAImmediate(0x40);
        builder.StoreAAbsolute(0x4017);
        EmitAudioStateInitialization();
    }

    private void EmitMusicPlay(FunctionCall call)
    {
        var themeId = NesVideoProgram.IdentifierArg(call.Parameters.ElementAt(0), "music_play argument 1");
        if (!program.MusicAssets.TryGetValue(themeId, out var asset))
        {
            throw new InvalidOperationException($"Unknown NES music asset '{themeId}'. Declare it with music_asset(...).");
        }

        var label = NesRomBuilder.MusicDataLabel(asset.Name);
        builder.LoadAImmediateLabelLowByte(label, asset.OrderStartOffset);
        builder.StoreAZeroPage(MusicOrderPointerLowAddress);
        builder.LoadAImmediateLabelHighByte(label, asset.OrderStartOffset);
        builder.StoreAZeroPage(MusicOrderPointerHighAddress);
        builder.LoadAImmediateLabelLowByte(label, asset.LoopOrderOffset);
        builder.StoreAAbsolute(MusicLoopPointerLowAddress);
        builder.LoadAImmediateLabelHighByte(label, asset.LoopOrderOffset);
        builder.StoreAAbsolute(MusicLoopPointerHighAddress);
        builder.LoadAImmediate(0);
        builder.StoreAZeroPage(MusicTickAddress);
    }

    private void EmitSoundEffectPlay(FunctionCall call)
    {
        var soundId = NesVideoProgram.IdentifierArg(call.Parameters.ElementAt(0), "sfx_play argument 1");
        if (!program.SoundEffectAssets.TryGetValue(soundId, out var asset))
        {
            throw new InvalidOperationException($"Unknown NES SFX asset '{soundId}'. Declare it with sfx_asset(...).");
        }

        // Arm the SFX engine only: point the cursor at the effect's first frame body and mark it
        // active. The next audio-update tick plays it. This must not touch the music order/body/tick
        // state, otherwise the background music desyncs on every trigger.
        var label = NesRomBuilder.SoundEffectDataLabel(asset.Name);
        builder.LoadAImmediateLabelLowByte(label);
        builder.StoreAZeroPage(SfxPointerLowAddress);
        builder.LoadAImmediateLabelHighByte(label);
        builder.StoreAZeroPage(SfxPointerHighAddress);
        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(SfxActiveAddress);
    }

    private void EmitMusicStop()
    {
        builder.LoadAImmediate(0);
        builder.StoreAZeroPage(MusicOrderPointerHighAddress);
        builder.StoreAZeroPage(MusicTickAddress);
        builder.StoreAAbsolute(0x4015);
    }

    private void EmitAudioUpdate()
    {
        var hasSfx = program.SoundEffectAssetsInLoadOrder.Count > 0;
        var doneLabel = builder.CreateLabel("nes_audio_done");
        var processOrderLabel = builder.CreateLabel("nes_audio_process_order");
        var hasActiveMusicLabel = builder.CreateLabel("nes_audio_active");
        var tickExpiredLabel = builder.CreateLabel("nes_audio_tick_expired");
        var hasBodyLabel = builder.CreateLabel("nes_audio_has_body");
        var tickNonZeroLabel = builder.CreateLabel("nes_audio_tick_nonzero");

        builder.LoadAZeroPage(MusicOrderPointerHighAddress);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xD0, hasActiveMusicLabel); // BNE hasActiveMusicLabel
        builder.JumpAbsolute(doneLabel);
        builder.Label(hasActiveMusicLabel);

        builder.LoadAZeroPage(MusicTickAddress);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, processOrderLabel); // BEQ processOrderLabel
        builder.DecrementZeroPage(MusicTickAddress);
        builder.LoadAZeroPage(MusicTickAddress);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, tickExpiredLabel); // BEQ tickExpiredLabel
        builder.JumpAbsolute(doneLabel);
        builder.Label(tickExpiredLabel);

        builder.Label(processOrderLabel);
        builder.LoadYImmediate(0);
        builder.LoadAIndirectY(MusicOrderPointerLowAddress);
        builder.StoreAZeroPage(MusicBodyPointerLowAddress);
        builder.IncrementY();
        builder.LoadAIndirectY(MusicOrderPointerLowAddress);
        builder.StoreAZeroPage(MusicBodyPointerHighAddress);
        builder.LoadAZeroPage(MusicBodyPointerLowAddress);
        builder.OrZeroPage(MusicBodyPointerHighAddress);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xD0, hasBodyLabel); // BNE hasBodyLabel
        builder.LoadAAbsolute(MusicLoopPointerLowAddress);
        builder.StoreAZeroPage(MusicOrderPointerLowAddress);
        builder.LoadAAbsolute(MusicLoopPointerHighAddress);
        builder.StoreAZeroPage(MusicOrderPointerHighAddress);
        builder.JumpAbsolute(processOrderLabel);

        builder.Label(hasBodyLabel);
        builder.IncrementY();
        builder.LoadAIndirectY(MusicOrderPointerLowAddress);
        builder.StoreAZeroPage(MusicTickAddress);
        EmitAdvanceMusicOrderPointer();
        EmitPlayApuBody(muteSfxChannel: hasSfx);

        builder.LoadAZeroPage(MusicTickAddress);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xD0, tickNonZeroLabel); // BNE tickNonZeroLabel
        builder.JumpAbsolute(processOrderLabel);
        builder.Label(tickNonZeroLabel);

        builder.Label(doneLabel);

        if (hasSfx)
        {
            var endLabel = builder.CreateLabel("nes_audio_end");
            EmitSoundEffectUpdate(endLabel);
            builder.Label(endLabel);
        }
    }

    // Compact one-shot pulse 1 SFX engine, ticked right after the music engine each audio-update tick.
    // It walks one frame body per tick from a zero-page cursor, keeping state fully independent from
    // the music sequencer (it never touches the music order/body/tick pointers, which previously
    // corrupted the background music). A 0xFF marker byte ends the effect.
    private void EmitSoundEffectUpdate(string endLabel)
    {
        var stopLabel = builder.CreateLabel("nes_sfx_stop");
        var noCarryLabel = builder.CreateLabel("nes_sfx_no_carry");

        builder.LoadAAbsolute(SfxActiveAddress);
        builder.BranchRelative(0xF0, endLabel); // BEQ endLabel (no effect playing; LDA sets Z)

        builder.LoadAZeroPage(SfxPointerLowAddress);
        builder.StoreAZeroPage(MusicBodyPointerLowAddress);
        builder.LoadAZeroPage(SfxPointerHighAddress);
        builder.StoreAZeroPage(MusicBodyPointerHighAddress);

        builder.LoadYImmediate(0);
        builder.LoadAIndirectY(MusicBodyPointerLowAddress);
        builder.CompareImmediate(0xFF);
        builder.BranchRelative(0xF0, stopLabel); // BEQ stopLabel (end-of-effect marker)

        // Play this frame's body on pulse 1 (X = 0, never suppressed). The shared body player returns
        // Y = bytes consumed (1 + 2 * writeCount), which advances the cursor to the next frame body.
        EmitPlayApuBody(muteSfxChannel: false);
        builder.TransferYToA();
        builder.ClearCarry();
        builder.AddZeroPage(SfxPointerLowAddress);
        builder.StoreAZeroPage(SfxPointerLowAddress);
        builder.BranchRelative(0x90, noCarryLabel); // BCC noCarryLabel
        builder.IncrementZeroPage(SfxPointerHighAddress);
        builder.Label(noCarryLabel);
        builder.JumpAbsolute(endLabel);

        builder.Label(stopLabel);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(SfxActiveAddress);
    }

    // Plays the APU trace body pointed to by MusicBodyPointer via the shared subroutine. X selects
    // whether the music's own pulse 1 writes are suppressed while an SFX owns the channel: for the
    // music path X = SfxActive (non-zero suppresses pulse 1), for the SFX path X = 0 (writes freely).
    private void EmitPlayApuBody(bool muteSfxChannel)
    {
        if (muteSfxChannel)
        {
            builder.LoadXAbsolute(SfxActiveAddress);
        }
        else
        {
            builder.LoadXImmediate(0);
        }

        builder.CallSubroutine(ApuBodySubroutineLabel);
        apuBodySubroutineReferenced = true;
    }

    // Shared APU trace body player. Reads [count, (registerOffset, value) * count] at MusicBodyPointer
    // and writes each command to $40xx. Emitted once and called (JSR) by both the music and SFX
    // engines so the sequencer body loop is not duplicated. Returns with Y = 1 + 2 * count.
    public void EmitAudioSubroutines()
    {
        if (!apuBodySubroutineReferenced)
        {
            return;
        }

        var muteSfxChannel = program.SoundEffectAssetsInLoadOrder.Count > 0;
        var commandLoopLabel = builder.CreateLabel("nes_apu_body_loop");
        var afterBodyLabel = builder.CreateLabel("nes_apu_body_after");

        builder.Label(ApuBodySubroutineLabel);
        builder.LoadYImmediate(0);
        builder.LoadAIndirectY(MusicBodyPointerLowAddress);
        builder.StoreAZeroPage(MusicCommandCountAddress);
        builder.IncrementY();
        builder.LoadAZeroPage(MusicCommandCountAddress);
        builder.BranchRelative(0xF0, afterBodyLabel); // BEQ afterBodyLabel (empty frame body)

        builder.Label(commandLoopLabel);
        builder.LoadAIndirectY(MusicBodyPointerLowAddress);
        builder.StoreAZeroPage(ExpressionScratchAddress);
        builder.IncrementY();
        builder.LoadAIndirectY(MusicBodyPointerLowAddress);
        builder.IncrementY();
        EmitNesApuRegisterWrite(muteSfxChannel);
        builder.DecrementZeroPage(MusicCommandCountAddress);
        builder.BranchRelative(0xD0, commandLoopLabel); // BNE commandLoopLabel

        builder.Label(afterBodyLabel);
        builder.Return();
    }

    private void EmitAdvanceMusicOrderPointer()
    {
        var noCarryLabel = builder.CreateLabel("nes_audio_order_no_carry");
        builder.LoadAZeroPage(MusicOrderPointerLowAddress);
        builder.ClearCarry();
        builder.AddImmediate(3);
        builder.StoreAZeroPage(MusicOrderPointerLowAddress);
        builder.BranchRelative(0x90, noCarryLabel); // BCC noCarryLabel
        builder.LoadAZeroPage(MusicOrderPointerHighAddress);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.StoreAZeroPage(MusicOrderPointerHighAddress);
        builder.Label(noCarryLabel);
    }

    private void EmitNesApuRegisterWrite(bool muteSfxChannel)
    {
        builder.StoreAZeroPage(CollisionColumnScratchAddress);
        builder.StoreYZeroPage(SpriteFrameScratchAddress);

        string? skipWriteLabel = null;
        if (muteSfxChannel)
        {
            var doWriteLabel = builder.CreateLabel("nes_apu_write");
            skipWriteLabel = builder.CreateLabel("nes_apu_skip_write");
            // X != 0 means an SFX owns pulse 1: drop the music's own pulse 1 writes (register offset
            // $00-$03) so the effect note is not overwritten. Every other channel plays normally.
            builder.CompareXImmediate(0);
            builder.BranchRelative(0xF0, doWriteLabel); // BEQ doWriteLabel (X == 0, no SFX active)
            builder.LoadAZeroPage(ExpressionScratchAddress);
            builder.CompareImmediate(0x04);
            builder.BranchRelative(0x90, skipWriteLabel); // BCC skipWriteLabel (offset < 4, pulse 1)
            builder.Label(doWriteLabel);
        }

        builder.LoadAZeroPage(ExpressionScratchAddress);
        builder.StoreAZeroPage(RuntimeIndexScratchAddress);
        builder.LoadAImmediate(0x40);
        builder.StoreAZeroPage(ExpressionScratchAddress);
        builder.LoadYImmediate(0);
        builder.LoadAZeroPage(CollisionColumnScratchAddress);
        builder.StoreAIndirectY(RuntimeIndexScratchAddress);

        if (skipWriteLabel is not null)
        {
            builder.Label(skipWriteLabel);
        }

        builder.LoadYZeroPage(SpriteFrameScratchAddress);
    }

    internal void EmitPollInput()
    {
        builder.LoadAZeroPage(InputCurrentAddress);
        builder.StoreAZeroPage(InputPreviousAddress);

        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(ControllerPortAddress);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(ControllerPortAddress);
        builder.StoreAZeroPage(InputCurrentAddress);

        foreach (var button in ControllerReadOrder)
        {
            EmitReadControllerButton(button);
        }

        foreach (var button in Buttons)
        {
            EmitUpdateButtonHoldTicks(button);
        }
    }

    private void EmitReadControllerButton(NesButton button)
    {
        var skipLabel = builder.CreateLabel("input_button_skip");

        builder.LoadAAbsolute(ControllerPortAddress);
        builder.AndImmediate(0x01);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, skipLabel);    // BEQ skipLabel
        builder.LoadAZeroPage(InputCurrentAddress);
        builder.OrImmediate(button.SnapshotMask);
        builder.StoreAZeroPage(InputCurrentAddress);
        builder.Label(skipLabel);
    }

    private void EmitUpdateButtonHoldTicks(NesButton button)
    {
        var resetLabel = builder.CreateLabel("button_hold_reset");
        var endLabel = builder.CreateLabel("button_hold_end");

        builder.LoadAZeroPage(InputCurrentAddress);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, resetLabel);   // BEQ resetLabel

        builder.LoadAZeroPage(button.HoldTicksAddress);
        builder.CompareImmediate(0xFF);
        builder.BranchRelative(0xF0, endLabel);     // BEQ endLabel
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.StoreAZeroPage(button.HoldTicksAddress);
        builder.JumpAbsolute(endLabel);

        builder.Label(resetLabel);
        builder.LoadAImmediate(0);
        builder.StoreAZeroPage(button.HoldTicksAddress);
        builder.Label(endLabel);
    }

    private void EmitCameraInit(FunctionCall call)
    {
        NesVideoProgram.RequireArity(call, 3);
        var worldMap = program.WorldMap
                       ?? throw new InvalidOperationException("camera_init requires world_map(...) data for the NES target.");
        var mapWidth = NesVideoProgram.ConstValue(call.Parameters.ElementAt(0), "camera_init argument 1");
        if (mapWidth is < 1 or > 255)
        {
            throw new InvalidOperationException("NES camera_init map width must fit one byte in the current horizontal streaming runtime.");
        }

        if (mapWidth > worldMap.Width)
        {
            throw new InvalidOperationException($"camera_init argument 1 must not exceed the declared world_map width ({worldMap.Width}).");
        }

        var streamY = NesVideoProgram.ConstValue(call.Parameters.ElementAt(1), "camera_init argument 2");
        var height = NesVideoProgram.ConstValue(call.Parameters.ElementAt(2), "camera_init argument 3");
        if (height > worldMap.Height)
        {
            throw new InvalidOperationException($"camera_init argument 3 must not exceed the declared world_map height ({worldMap.Height}).");
        }

        var bufferedHeight = height;
        if (useFourScreenNametables)
        {
            if (streamY < 0 || height < 1 || streamY >= 60)
            {
                throw new InvalidOperationException("NES four-screen free scroll stream area must fit within the 60-row four-screen height.");
            }

            bufferedHeight = Math.Min(height, 60 - streamY);
        }
        else if (streamY < 0 || height < 1 || streamY + height > 30)
        {
            throw new InvalidOperationException("camera_init stream area must fit within the NES visible nametable height.");
        }

        cameraConfig = new NesCameraConfig(mapWidth, worldMap.Height, streamY, bufferedHeight, useFourScreenNametables);
        builder.LoadAImmediate(0);
        builder.StoreAZeroPage(CameraXAddress);
        builder.StoreAZeroPage(CameraTileColumnAddress);
        builder.StoreAZeroPage(CameraTargetColumnAddress);
        builder.StoreAZeroPage(CameraSourceColumnAddress);
        builder.StoreAZeroPage(CameraNewXAddress);
        builder.StoreAAbsolute(PendingCameraStreamFlagsAddress);
        builder.StoreAAbsolute(PendingCameraRowPhaseAddress);
        if (useFourScreenNametables)
        {
            builder.StoreAZeroPage(CameraYAddress);
            builder.StoreAZeroPage(CameraTileRowAddress);
            builder.StoreAZeroPage(CameraNewYAddress);
            builder.StoreAZeroPage(CameraTargetRowAddress);
            builder.StoreAZeroPage(CameraSourceRowAddress);
            builder.StoreAZeroPage(CameraStreamRemainingAddress);
        }

        builder.LoadAImmediate(PendingStreamColumn);
        builder.StoreAAbsolute(PendingCameraNextStreamAddress);
    }

    internal void EmitSetCameraPosition(Sdk2DOperation.SetCameraPosition operation)
    {
        var config = EnsureCameraConfigured("camera_set_position");

        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(CameraScrollAppliedAddress);
        EmitSdkByteExpressionToA(operation.X);
        if (ShouldStreamColumnsForCamera(config))
        {
            EmitWalkCameraAxisToTarget(CameraXAddress, CameraNewXAddress, () => EmitStreamColumnForCameraPosition(config), "nes_camera_x");
        }
        else if (config.MapWidth > 32)
        {
            EmitWalkCameraAxisToTarget(CameraXAddress, CameraNewXAddress, () => EmitTrackCameraXPosition(config), "nes_camera_x");
        }
        else
        {
            builder.StoreAZeroPage(CameraNewXAddress);
            builder.LoadAZeroPage(CameraNewXAddress);
            builder.StoreAZeroPage(CameraXAddress);
            builder.LoadAZeroPage(CameraNewXAddress);
            builder.ShiftRightA();
            builder.ShiftRightA();
            builder.ShiftRightA();
            builder.StoreAZeroPage(CameraTileColumnAddress);
        }

        if (operation.Axes.HasFlag(ScrollAxes.Vertical))
        {
            if (!config.UseFourScreenNametables)
            {
                throw new InvalidOperationException("NES vertical camera movement requires four-screen nametable VRAM.");
            }

            EmitSdkByteExpressionToA(operation.Y);
            if (config.MapHeight > 30)
            {
                EmitWalkCameraAxisToTarget(CameraYAddress, CameraNewYAddress, () => EmitTrackCameraYPosition(config), "nes_camera_y");
            }
            else
            {
                EmitWalkCameraAxisToTarget(CameraYAddress, CameraNewYAddress, EmitTrackShortCameraYPosition, "nes_camera_y");
            }
        }
    }

    // Walks the camera axis one pixel per step toward the target held in A on entry, driving the
    // existing single-pixel tracking/streaming routine through the axis' "new position" scratch. The
    // per-frame step budget bounds the walk to at most one tile crossing, honoring the streaming slot.
    private void EmitWalkCameraAxisToTarget(byte currentAddress, byte stepTargetAddress, Action emitTrackStep, string labelPrefix)
    {
        var loopLabel = builder.CreateLabel(labelPrefix + "_walk");
        var budgetOkLabel = builder.CreateLabel(labelPrefix + "_walk_budget_ok");
        var reachedCheckLabel = builder.CreateLabel(labelPrefix + "_walk_moving");
        var stepForwardLabel = builder.CreateLabel(labelPrefix + "_walk_forward");
        var doStepLabel = builder.CreateLabel(labelPrefix + "_walk_step");
        var endLabel = builder.CreateLabel(labelPrefix + "_walk_end");

        builder.StoreAAbsolute(CameraWalkTargetAddress);
        builder.LoadAImmediate(CameraWalkMaxStepsPerFrame);
        builder.StoreAAbsolute(CameraWalkStepsAddress);

        builder.Label(loopLabel);
        builder.LoadAAbsolute(CameraWalkStepsAddress);
        builder.BranchRelative(0xD0, budgetOkLabel); // BNE budgetOkLabel
        builder.JumpAbsolute(endLabel);              // step budget spent (far jump)

        builder.Label(budgetOkLabel);
        builder.SetCarry();
        builder.SubtractImmediate(1);
        builder.StoreAAbsolute(CameraWalkStepsAddress);

        builder.LoadAAbsolute(CameraWalkTargetAddress);
        builder.SetCarry();
        builder.SubtractZeroPage(currentAddress); // A = target - current (signed 8-bit)
        builder.BranchRelative(0xD0, reachedCheckLabel); // BNE reachedCheckLabel
        builder.JumpAbsolute(endLabel);                  // reached target (far jump)

        builder.Label(reachedCheckLabel);
        builder.CompareImmediate(0x80);
        builder.BranchRelative(0x90, stepForwardLabel); // BCC stepForwardLabel: positive delta => step +1

        builder.LoadAZeroPage(currentAddress);
        builder.SetCarry();
        builder.SubtractImmediate(1);
        builder.StoreAZeroPage(stepTargetAddress);
        builder.JumpAbsolute(doStepLabel);

        builder.Label(stepForwardLabel);
        builder.LoadAZeroPage(currentAddress);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.StoreAZeroPage(stepTargetAddress);

        builder.Label(doStepLabel);
        emitTrackStep();
        builder.JumpAbsolute(loopLabel);

        builder.Label(endLabel);
    }

    internal void EmitApplyCamera(Sdk2DOperation.ApplyCamera operation)
    {
        var config = EnsureCameraConfigured("camera_apply");
        var applyLabel = builder.CreateLabel("camera_apply_now");
        var doneLabel = builder.CreateLabel("camera_apply_done");

        builder.LoadAAbsolute(CameraScrollAppliedAddress);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, applyLabel); // BEQ applyLabel
        builder.JumpAbsolute(doneLabel);

        builder.Label(applyLabel);
        EmitApplyCameraNow(config);
        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(CameraScrollAppliedAddress);

        builder.Label(doneLabel);
    }

    private void EmitApplyPendingCameraScrollAtVBlank()
    {
        if (cameraConfig is not { } config)
        {
            return;
        }

        EmitApplyCameraNow(config);
        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(CameraScrollAppliedAddress);
    }

    private void EmitApplyCameraNow(NesCameraConfig config)
    {
        EmitCommitPendingCameraStream(config);
        if (useFourScreenNametables)
        {
            EmitRestoreFourScreenCameraScroll();
        }
        else
        {
            EmitRestoreCameraScroll();
        }
    }

    private void EmitSdkByteExpressionToA(SdkByteExpression expression)
    {
        switch (expression)
        {
            case SdkByteExpression.Constant constant:
                builder.LoadAImmediate(constant.Value);
                break;
            case SdkByteExpression.Variable variable:
                EmitSdkStorageLocationToA(variable.Location);
                break;
            default:
                throw new InvalidOperationException($"Unsupported SDK byte expression '{expression.GetType().Name}'.");
        }
    }

    private void EmitSdkStorageLocationToA(SdkStorageLocation location)
    {
        switch (location)
        {
            case SdkStorageLocation.RuntimeIndexedField runtimeIndexed:
                EmitRuntimeMemberIndexToX(runtimeIndexed.BaseName, runtimeIndexed.Index);
                builder.LoadAZeroPageX(RuntimeIndexedMemberBaseAddress(runtimeIndexed.BaseName, runtimeIndexed.FieldName));
                break;
            default:
                builder.LoadAZeroPage(VariableAddress(StorageKey(location)));
                break;
        }
    }

    internal void EmitStreamMapColumn(Sdk2DOperation.StreamMapColumn operation)
    {
        var worldMap = program.WorldMap
                       ?? throw new InvalidOperationException("map_stream_column requires world_map(...) data for the NES target.");
        var y = CheckedRange(operation.Y, 0, 29, "map_stream_column argument 3");
        var height = CheckedRange(operation.Height, 1, worldMap.Height, "map_stream_column argument 4");
        if (y + height > 30)
        {
            throw new InvalidOperationException("map_stream_column stream area must fit within the NES visible nametable height.");
        }

        Sdk2DOperationValidator.Validate(
            NesTarget.Capabilities,
            new Sdk2DOperation.StreamMapColumn(TargetColumn: 0, SourceColumn: 0, Y: y, Height: height));

        EmitSdkByteExpressionToA(operation.TargetColumn);
        builder.StoreAZeroPage(CameraTargetColumnAddress);
        EmitSdkByteExpressionToA(operation.SourceColumn);
        builder.StoreAZeroPage(CameraSourceColumnAddress);
        EmitWaitFrame();
        EmitStreamColumnFromAddresses(new NesCameraConfig(worldMap.Width, worldMap.Height, y, height, UseFourScreenNametables: false));
    }

    internal void EmitStreamMapRow(Sdk2DOperation.StreamMapRow operation)
    {
        var worldMap = program.WorldMap
                       ?? throw new InvalidOperationException("map_stream_row requires world_map(...) data for the NES target.");
        var targetRow = CheckedRange(operation.TargetRow, 0, 59, "map_stream_row argument 1");
        var sourceRow = CheckedRange(operation.SourceRow, 0, worldMap.Height - 1, "map_stream_row argument 2");
        var x = CheckedRange(operation.X, 0, 63, "map_stream_row argument 3");
        var width = CheckedRange(operation.Width, 1, worldMap.Width, "map_stream_row argument 4");
        if (x + width > 64)
        {
            throw new InvalidOperationException("map_stream_row stream area must fit within the NES four-screen row width.");
        }

        if (x + width > worldMap.Width)
        {
            throw new InvalidOperationException("map_stream_row source span must fit within the declared world map width.");
        }

        Sdk2DOperationValidator.Validate(
            NesTarget.Capabilities,
            new Sdk2DOperation.StreamMapRow(targetRow, sourceRow, x, width));

        EmitWaitFrame();
        builder.LoadAAbsolute(0x2002);              // reset PPU address latch
        var remaining = width;
        var segmentX = x;
        while (remaining > 0)
        {
            var segmentWidth = Math.Min(remaining, 32 - segmentX % 32);
            EmitStreamMapRowSegment(targetRow, sourceRow, segmentX, segmentWidth);
            segmentX += segmentWidth;
            remaining -= segmentWidth;
        }

        EmitStreamMapRowAttributes(targetRow, x, width);
    }

    private static bool ShouldStreamColumnsForCamera(NesCameraConfig config)
    {
        return config.UseFourScreenNametables ? config.MapWidth > 64 : config.MapWidth > 32;
    }

    private static bool ShouldStreamRowsForCamera(NesCameraConfig config)
    {
        return config.UseFourScreenNametables && config.MapHeight > 60;
    }

    private void EmitTrackCameraXPosition(NesCameraConfig config)
    {
        EmitStreamColumnForCameraPosition(config, streamColumns: false);
    }

    private void EmitStreamColumnForCameraPosition(NesCameraConfig config, bool streamColumns = true)
    {
        var moveRightLabel = builder.CreateLabel("nes_camera_move_right");
        var moveLeftLabel = builder.CreateLabel("nes_camera_move_left");
        var fallbackLabel = builder.CreateLabel("nes_camera_move_fallback");
        var changedLabel = builder.CreateLabel("nes_camera_changed");
        var rightCrossedTileLabel = builder.CreateLabel("nes_camera_right_crossed_tile");
        var leftCrossedTileLabel = builder.CreateLabel("nes_camera_left_crossed_tile");
        var storeOnlyLabel = builder.CreateLabel("nes_camera_store_only");
        var storeAndStreamRightLabel = builder.CreateLabel("nes_camera_store_stream_right");
        var storeAndStreamLeftLabel = builder.CreateLabel("nes_camera_store_stream_left");
        var endLabel = builder.CreateLabel("nes_camera_stream_end");

        builder.LoadAZeroPage(CameraNewXAddress);
        builder.CompareZeroPage(CameraXAddress);
        builder.BranchRelative(0xD0, changedLabel); // BNE changedLabel
        builder.JumpAbsolute(endLabel);

        builder.Label(changedLabel);

        builder.LoadAZeroPage(CameraXAddress);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.CompareZeroPage(CameraNewXAddress);
        builder.BranchRelative(0xF0, moveRightLabel); // BEQ moveRightLabel

        builder.LoadAZeroPage(CameraNewXAddress);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.CompareZeroPage(CameraXAddress);
        builder.BranchRelative(0xF0, moveLeftLabel); // BEQ moveLeftLabel

        builder.JumpAbsolute(fallbackLabel);

        builder.Label(moveRightLabel);
        builder.LoadAZeroPage(CameraXAddress);
        builder.AndImmediate(0x07);
        builder.CompareImmediate(0x07);
        builder.BranchRelative(0xF0, rightCrossedTileLabel); // BEQ rightCrossedTileLabel
        builder.JumpAbsolute(storeOnlyLabel);
        builder.Label(rightCrossedTileLabel);
        EmitIncrementCameraTile(config.MapWidth);
        builder.JumpAbsolute(storeAndStreamRightLabel);

        builder.Label(moveLeftLabel);
        builder.LoadAZeroPage(CameraXAddress);
        builder.AndImmediate(0x07);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, leftCrossedTileLabel); // BEQ leftCrossedTileLabel
        builder.JumpAbsolute(storeOnlyLabel);
        builder.Label(leftCrossedTileLabel);
        EmitDecrementCameraTile(config.MapWidth);
        builder.JumpAbsolute(storeAndStreamLeftLabel);

        builder.Label(fallbackLabel);
        builder.LoadAZeroPage(CameraNewXAddress);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.StoreAZeroPage(CameraTileColumnAddress);
        builder.JumpAbsolute(storeOnlyLabel);

        builder.Label(storeAndStreamRightLabel);
        builder.LoadAZeroPage(CameraNewXAddress);
        builder.StoreAZeroPage(CameraXAddress);
        if (streamColumns)
        {
            EmitPrepareRightStreamColumn(config);
            EmitQueuePendingCameraColumn();
        }
        builder.JumpAbsolute(endLabel);

        builder.Label(storeAndStreamLeftLabel);
        builder.LoadAZeroPage(CameraNewXAddress);
        builder.StoreAZeroPage(CameraXAddress);
        if (streamColumns)
        {
            EmitPrepareLeftStreamColumn();
            EmitQueuePendingCameraColumn();
        }
        builder.JumpAbsolute(endLabel);

        builder.Label(storeOnlyLabel);
        builder.LoadAZeroPage(CameraNewXAddress);
        builder.StoreAZeroPage(CameraXAddress);
        builder.Label(endLabel);
    }

    private void EmitTrackCameraYPosition(NesCameraConfig config)
    {
        var streamRows = ShouldStreamRowsForCamera(config);
        var moveDownLabel = builder.CreateLabel("nes_camera_move_down");
        var moveUpLabel = builder.CreateLabel("nes_camera_move_up");
        var fallbackMoveDownLabel = builder.CreateLabel("nes_camera_y_fallback_down");
        var fallbackLabel = builder.CreateLabel("nes_camera_y_fallback");
        var changedLabel = builder.CreateLabel("nes_camera_y_changed");
        var downCrossedTileLabel = builder.CreateLabel("nes_camera_down_crossed_tile");
        var upCrossedTileLabel = builder.CreateLabel("nes_camera_up_crossed_tile");
        var storeOnlyLabel = builder.CreateLabel("nes_camera_y_store_only");
        var storeAfterTileChangeLabel = builder.CreateLabel("nes_camera_y_store_after_tile");
        var storeAndStreamLabel = builder.CreateLabel("nes_camera_y_store_stream");
        var endLabel = builder.CreateLabel("nes_camera_y_end");

        builder.LoadAZeroPage(CameraNewYAddress);
        builder.CompareZeroPage(CameraYAddress);
        builder.BranchRelative(0xD0, changedLabel); // BNE changedLabel
        builder.JumpAbsolute(endLabel);

        builder.Label(changedLabel);

        builder.LoadAZeroPage(CameraYAddress);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.CompareZeroPage(CameraNewYAddress);
        builder.BranchRelative(0xF0, moveDownLabel); // BEQ moveDownLabel

        builder.LoadAZeroPage(CameraNewYAddress);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.CompareZeroPage(CameraYAddress);
        builder.BranchRelative(0xF0, moveUpLabel); // BEQ moveUpLabel

        builder.JumpAbsolute(fallbackLabel);

        builder.Label(moveDownLabel);
        builder.LoadAZeroPage(CameraYAddress);
        builder.AndImmediate(0x07);
        builder.CompareImmediate(0x07);
        builder.BranchRelative(0xF0, downCrossedTileLabel); // BEQ downCrossedTileLabel
        builder.JumpAbsolute(storeOnlyLabel);
        builder.Label(downCrossedTileLabel);
        EmitIncrementCameraRow(config.MapHeight);
        if (streamRows)
        {
            EmitPrepareDownStreamRow(config);
            builder.JumpAbsolute(storeAndStreamLabel);
        }

        builder.JumpAbsolute(storeAfterTileChangeLabel);

        builder.Label(moveUpLabel);
        builder.LoadAZeroPage(CameraYAddress);
        builder.AndImmediate(0x07);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, upCrossedTileLabel); // BEQ upCrossedTileLabel
        builder.JumpAbsolute(storeOnlyLabel);
        builder.Label(upCrossedTileLabel);
        EmitDecrementCameraRow(config.MapHeight);
        if (streamRows)
        {
            EmitPrepareUpStreamRow();
            builder.JumpAbsolute(storeAndStreamLabel);
        }

        builder.JumpAbsolute(storeAfterTileChangeLabel);

        builder.Label(fallbackLabel);
        builder.LoadAZeroPage(CameraNewYAddress);
        builder.SetCarry();
        builder.SubtractZeroPage(CameraYAddress);
        builder.CompareImmediate(0x80);
        builder.BranchRelative(0x90, fallbackMoveDownLabel); // BCC fallbackMoveDownLabel
        builder.JumpAbsolute(moveUpLabel);
        builder.Label(fallbackMoveDownLabel);
        builder.JumpAbsolute(moveDownLabel);

        builder.Label(storeAfterTileChangeLabel);
        builder.JumpAbsolute(storeOnlyLabel);

        if (streamRows)
        {
            builder.Label(storeAndStreamLabel);
            builder.LoadAZeroPage(CameraNewYAddress);
            builder.StoreAZeroPage(CameraYAddress);
            EmitQueuePendingCameraRow();
            builder.JumpAbsolute(endLabel);
        }

        builder.Label(storeOnlyLabel);
        builder.LoadAZeroPage(CameraNewYAddress);
        builder.StoreAZeroPage(CameraYAddress);
        builder.Label(endLabel);
    }

    private void EmitTrackShortCameraYPosition()
    {
        var changedLabel = builder.CreateLabel("nes_camera_y_short_changed");
        var moveDownLabel = builder.CreateLabel("nes_camera_y_short_down");
        var moveUpLabel = builder.CreateLabel("nes_camera_y_short_up");
        var storeLabel = builder.CreateLabel("nes_camera_y_short_store");
        var endLabel = builder.CreateLabel("nes_camera_y_short_end");

        builder.LoadAZeroPage(CameraNewYAddress);
        builder.CompareZeroPage(CameraYAddress);
        builder.BranchRelative(0xD0, changedLabel); // BNE changedLabel
        builder.JumpAbsolute(endLabel);

        builder.Label(changedLabel);
        builder.LoadAZeroPage(CameraNewYAddress);
        builder.SetCarry();
        builder.SubtractZeroPage(CameraYAddress);
        builder.CompareImmediate(0x80);
        builder.BranchRelative(0x90, moveDownLabel); // BCC moveDownLabel
        builder.JumpAbsolute(moveUpLabel);

        builder.Label(moveDownLabel);
        builder.LoadAZeroPage(CameraYAddress);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.JumpAbsolute(storeLabel);

        builder.Label(moveUpLabel);
        builder.LoadAZeroPage(CameraYAddress);
        builder.SetCarry();
        builder.SubtractImmediate(1);

        builder.Label(storeLabel);
        builder.StoreAZeroPage(CameraYAddress);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.StoreAZeroPage(CameraTileRowAddress);

        builder.Label(endLabel);
    }

    private void EmitIncrementCameraTile(int mapWidth)
    {
        var noWrapLabel = builder.CreateLabel("nes_camera_tile_inc_no_wrap");
        builder.LoadAZeroPage(CameraTileColumnAddress);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.CompareImmediate(mapWidth);
        builder.BranchRelative(0x90, noWrapLabel);  // BCC noWrapLabel
        builder.LoadAImmediate(0);
        builder.Label(noWrapLabel);
        builder.StoreAZeroPage(CameraTileColumnAddress);
    }

    private void EmitDecrementCameraTile(int mapWidth)
    {
        var decrementLabel = builder.CreateLabel("nes_camera_tile_dec");
        var storeLabel = builder.CreateLabel("nes_camera_tile_dec_store");

        builder.LoadAZeroPage(CameraTileColumnAddress);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xD0, decrementLabel); // BNE decrementLabel
        builder.LoadAImmediate(mapWidth - 1);
        builder.JumpAbsolute(storeLabel);

        builder.Label(decrementLabel);
        builder.LoadAZeroPage(CameraTileColumnAddress);
        builder.SetCarry();
        builder.SubtractImmediate(1);

        builder.Label(storeLabel);
        builder.StoreAZeroPage(CameraTileColumnAddress);
    }

    private void EmitIncrementCameraRow(int mapHeight)
    {
        var noWrapLabel = builder.CreateLabel("nes_camera_row_inc_no_wrap");
        builder.LoadAZeroPage(CameraTileRowAddress);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.CompareImmediate(mapHeight);
        builder.BranchRelative(0x90, noWrapLabel);  // BCC noWrapLabel
        builder.LoadAImmediate(0);
        builder.Label(noWrapLabel);
        builder.StoreAZeroPage(CameraTileRowAddress);
    }

    private void EmitDecrementCameraRow(int mapHeight)
    {
        var decrementLabel = builder.CreateLabel("nes_camera_row_dec");
        var storeLabel = builder.CreateLabel("nes_camera_row_dec_store");

        builder.LoadAZeroPage(CameraTileRowAddress);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xD0, decrementLabel); // BNE decrementLabel
        builder.LoadAImmediate(mapHeight - 1);
        builder.JumpAbsolute(storeLabel);

        builder.Label(decrementLabel);
        builder.LoadAZeroPage(CameraTileRowAddress);
        builder.SetCarry();
        builder.SubtractImmediate(1);

        builder.Label(storeLabel);
        builder.StoreAZeroPage(CameraTileRowAddress);
    }

    private void EmitPrepareRightStreamColumn(NesCameraConfig config)
    {
        const int lookaheadColumns = 32;
        builder.LoadAZeroPage(CameraTileColumnAddress);
        builder.ClearCarry();
        builder.AddImmediate(lookaheadColumns);
        builder.AndImmediate(0x3F);
        builder.StoreAZeroPage(CameraTargetColumnAddress);

        builder.LoadAZeroPage(CameraTileColumnAddress);
        builder.ClearCarry();
        builder.AddImmediate(lookaheadColumns);
        builder.StoreAZeroPage(CameraSourceColumnAddress);
        var sourceNoWrapLabel = builder.CreateLabel("nes_camera_source_no_wrap");
        builder.CompareImmediate(config.MapWidth);
        builder.BranchRelative(0x90, sourceNoWrapLabel); // BCC sourceNoWrapLabel
        builder.SetCarry();
        builder.SubtractImmediate(config.MapWidth);
        builder.StoreAZeroPage(CameraSourceColumnAddress);
        builder.Label(sourceNoWrapLabel);
    }

    private void EmitPrepareLeftStreamColumn()
    {
        builder.LoadAZeroPage(CameraTileColumnAddress);
        builder.AndImmediate(0x3F);
        builder.StoreAZeroPage(CameraTargetColumnAddress);

        builder.LoadAZeroPage(CameraTileColumnAddress);
        builder.StoreAZeroPage(CameraSourceColumnAddress);
    }

    private void EmitPrepareDownStreamRow(NesCameraConfig config)
    {
        EmitAddCameraRowWithWrap(30, 60, CameraTargetRowAddress);
        EmitAddCameraRowWithWrap(30, config.MapHeight, CameraSourceRowAddress);
        EmitPrepareRowColumns();
    }

    private void EmitPrepareUpStreamRow()
    {
        EmitCameraRowModulo(60, CameraTargetRowAddress);
        builder.LoadAZeroPage(CameraTileRowAddress);
        builder.StoreAZeroPage(CameraSourceRowAddress);
        EmitPrepareRowColumns();
    }

    private void EmitPrepareRowColumns()
    {
        builder.LoadAZeroPage(CameraTileColumnAddress);
        builder.AndImmediate(0x3F);
        builder.StoreAZeroPage(CameraTargetColumnAddress);

        builder.LoadAZeroPage(CameraTileColumnAddress);
        builder.StoreAZeroPage(CameraSourceColumnAddress);
    }

    private void EmitAddCameraRowWithWrap(int addend, int modulo, byte targetAddress)
    {
        var noWrapLabel = builder.CreateLabel("nes_camera_row_add_no_wrap");
        builder.LoadAZeroPage(CameraTileRowAddress);
        builder.ClearCarry();
        builder.AddImmediate(addend);
        builder.CompareImmediate(modulo);
        builder.BranchRelative(0x90, noWrapLabel); // BCC noWrapLabel
        builder.SetCarry();
        builder.SubtractImmediate(modulo);
        builder.Label(noWrapLabel);
        builder.StoreAZeroPage(targetAddress);
    }

    private void EmitCameraRowModulo(int modulo, byte targetAddress)
    {
        var loopLabel = builder.CreateLabel("nes_camera_row_mod_loop");
        var doneLabel = builder.CreateLabel("nes_camera_row_mod_done");

        builder.LoadAZeroPage(CameraTileRowAddress);
        builder.Label(loopLabel);
        builder.CompareImmediate(modulo);
        builder.BranchRelative(0x90, doneLabel); // BCC doneLabel
        builder.SetCarry();
        builder.SubtractImmediate(modulo);
        builder.JumpAbsolute(loopLabel);
        builder.Label(doneLabel);
        builder.StoreAZeroPage(targetAddress);
    }

    private void EmitRestoreCameraScroll(NesCameraConfig config)
    {
        if (config.UseFourScreenNametables)
        {
            EmitRestoreFourScreenCameraScroll();
        }
        else
        {
            EmitRestoreCameraScroll();
        }
    }

    private void EmitRestoreCameraScroll()
    {
        var rightNameTableLabel = builder.CreateLabel("nes_camera_apply_right_nt");
        var storeControlLabel = builder.CreateLabel("nes_camera_apply_store_ctrl");

        builder.LoadAAbsolute(0x2002);              // reset PPU scroll latch
        builder.LoadAZeroPage(CameraTileColumnAddress);
        builder.AndImmediate(0x20);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xD0, rightNameTableLabel); // BNE rightNameTableLabel
        builder.LoadAImmediate(0);
        builder.JumpAbsolute(storeControlLabel);

        builder.Label(rightNameTableLabel);
        builder.LoadAImmediate(1);

        builder.Label(storeControlLabel);
        builder.StoreAAbsolute(0x2000);
        builder.LoadAZeroPage(CameraXAddress);
        builder.StoreAAbsolute(0x2005);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(0x2005);
    }

    private void EmitRestoreFourScreenCameraScroll()
    {
        var noRightNameTableLabel = builder.CreateLabel("nes_camera_apply_no_right_nt");
        var noBottomNameTableLabel = builder.CreateLabel("nes_camera_apply_no_bottom_nt");
        var topRowLabel = builder.CreateLabel("nes_camera_apply_top_row");
        var storeYScrollLabel = builder.CreateLabel("nes_camera_apply_store_y_scroll");

        builder.LoadAAbsolute(0x2002);              // reset PPU scroll latch
        builder.LoadAImmediate(0);
        builder.StoreAZeroPage(ExpressionScratchAddress);
        EmitCameraRowModulo(60, RuntimeIndexScratchAddress);

        builder.LoadAZeroPage(CameraTileColumnAddress);
        builder.AndImmediate(0x20);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, noRightNameTableLabel); // BEQ noRightNameTableLabel
        builder.LoadAZeroPage(ExpressionScratchAddress);
        builder.OrImmediate(0x01);
        builder.StoreAZeroPage(ExpressionScratchAddress);
        builder.Label(noRightNameTableLabel);

        builder.LoadAZeroPage(RuntimeIndexScratchAddress);
        builder.CompareImmediate(30);
        builder.BranchRelative(0x90, noBottomNameTableLabel); // BCC noBottomNameTableLabel
        builder.LoadAZeroPage(ExpressionScratchAddress);
        builder.OrImmediate(0x02);
        builder.StoreAZeroPage(ExpressionScratchAddress);
        builder.Label(noBottomNameTableLabel);

        builder.LoadAZeroPage(ExpressionScratchAddress);
        builder.StoreAAbsolute(0x2000);
        builder.LoadAZeroPage(CameraXAddress);
        builder.StoreAAbsolute(0x2005);

        builder.LoadAZeroPage(RuntimeIndexScratchAddress);
        builder.CompareImmediate(30);
        builder.BranchRelative(0x90, topRowLabel); // BCC topRowLabel
        builder.SetCarry();
        builder.SubtractImmediate(30);
        builder.JumpAbsolute(storeYScrollLabel);
        builder.Label(topRowLabel);
        builder.LoadAZeroPage(RuntimeIndexScratchAddress);
        builder.Label(storeYScrollLabel);
        builder.ShiftLeftA();
        builder.ShiftLeftA();
        builder.ShiftLeftA();
        builder.StoreAZeroPage(ExpressionScratchAddress);
        builder.LoadAZeroPage(CameraYAddress);
        builder.AndImmediate(0x07);
        builder.ClearCarry();
        builder.AddZeroPage(ExpressionScratchAddress);
        builder.StoreAAbsolute(0x2005);
    }

    private void EmitQueuePendingCameraColumn()
    {
        builder.LoadAZeroPage(CameraTargetColumnAddress);
        builder.StoreAAbsolute(PendingCameraColumnTargetAddress);
        builder.LoadAZeroPage(CameraSourceColumnAddress);
        builder.StoreAAbsolute(PendingCameraColumnSourceAddress);
        builder.LoadAAbsolute(PendingCameraStreamFlagsAddress);
        builder.OrImmediate(PendingStreamColumn);
        builder.StoreAAbsolute(PendingCameraStreamFlagsAddress);
    }

    private void EmitQueuePendingCameraRow()
    {
        builder.LoadAZeroPage(CameraTargetRowAddress);
        builder.StoreAAbsolute(PendingCameraRowTargetAddress);
        builder.LoadAZeroPage(CameraSourceRowAddress);
        builder.StoreAAbsolute(PendingCameraRowSourceAddress);
        builder.LoadAZeroPage(CameraTargetColumnAddress);
        builder.StoreAAbsolute(PendingCameraRowTargetColumnAddress);
        builder.LoadAZeroPage(CameraSourceColumnAddress);
        builder.StoreAAbsolute(PendingCameraRowSourceColumnAddress);
        builder.LoadAAbsolute(PendingCameraStreamFlagsAddress);
        builder.OrImmediate(PendingStreamRow);
        builder.StoreAAbsolute(PendingCameraStreamFlagsAddress);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(PendingCameraRowPhaseAddress);
    }

    private void EmitCommitPendingCameraStream(NesCameraConfig config)
    {
        var canStreamColumns = ShouldStreamColumnsForCamera(config);
        var canStreamRows = ShouldStreamRowsForCamera(config);
        if (!canStreamColumns && !canStreamRows)
        {
            return;
        }

        if (canStreamColumns && canStreamRows)
        {
            EmitCommitStaggeredPendingCameraStream(config);
            return;
        }

        if (canStreamColumns)
        {
            EmitCommitSinglePendingCameraStream(PendingStreamColumn, config);
            return;
        }

        EmitCommitSinglePendingCameraStream(PendingStreamRow, config);
    }

    private void EmitCommitSinglePendingCameraStream(byte kind, NesCameraConfig config)
    {
        var commitLabel = builder.CreateLabel("nes_camera_commit_pending");
        var doneLabel = builder.CreateLabel("nes_camera_commit_done");

        builder.LoadAAbsolute(PendingCameraStreamFlagsAddress);
        builder.AndImmediate(kind);
        builder.CompareImmediate(PendingStreamNone);
        builder.BranchRelative(0xD0, commitLabel); // BNE commitLabel
        builder.JumpAbsolute(doneLabel);

        builder.Label(commitLabel);
        EmitCommitPendingCameraStreamKind(kind, config);

        builder.Label(doneLabel);
    }

    private void EmitCommitStaggeredPendingCameraStream(NesCameraConfig config)
    {
        var checkRowLabel = builder.CreateLabel("nes_camera_commit_check_row");
        var columnPendingLabel = builder.CreateLabel("nes_camera_commit_column_pending");
        var bothPendingLabel = builder.CreateLabel("nes_camera_commit_both_pending");
        var rowNextLabel = builder.CreateLabel("nes_camera_commit_row_next");
        var rowPendingLabel = builder.CreateLabel("nes_camera_commit_row_pending");
        var commitColumnLabel = builder.CreateLabel("nes_camera_commit_column");
        var commitRowLabel = builder.CreateLabel("nes_camera_commit_row");
        var doneLabel = builder.CreateLabel("nes_camera_commit_done");

        builder.LoadAAbsolute(PendingCameraStreamFlagsAddress);
        builder.AndImmediate(PendingStreamColumn);
        builder.CompareImmediate(PendingStreamNone);
        builder.BranchRelative(0xD0, columnPendingLabel); // BNE columnPendingLabel
        builder.JumpAbsolute(checkRowLabel);

        builder.Label(columnPendingLabel);
        builder.LoadAAbsolute(PendingCameraStreamFlagsAddress);
        builder.AndImmediate(PendingStreamRow);
        builder.CompareImmediate(PendingStreamNone);
        builder.BranchRelative(0xD0, bothPendingLabel); // BNE bothPendingLabel
        builder.JumpAbsolute(commitColumnLabel);

        builder.Label(bothPendingLabel);
        builder.LoadAAbsolute(PendingCameraNextStreamAddress);
        builder.CompareImmediate(PendingStreamRow);
        builder.BranchRelative(0xF0, rowNextLabel); // BEQ rowNextLabel
        builder.JumpAbsolute(commitColumnLabel);

        builder.Label(rowNextLabel);
        builder.JumpAbsolute(commitRowLabel);

        builder.Label(checkRowLabel);
        builder.LoadAAbsolute(PendingCameraStreamFlagsAddress);
        builder.AndImmediate(PendingStreamRow);
        builder.CompareImmediate(PendingStreamNone);
        builder.BranchRelative(0xD0, rowPendingLabel); // BNE rowPendingLabel
        builder.JumpAbsolute(doneLabel);

        builder.Label(rowPendingLabel);
        builder.JumpAbsolute(commitRowLabel);

        builder.Label(commitColumnLabel);
        EmitCommitPendingCameraStreamKind(PendingStreamColumn, config);
        builder.LoadAImmediate(PendingStreamRow);
        builder.StoreAAbsolute(PendingCameraNextStreamAddress);
        builder.JumpAbsolute(doneLabel);

        builder.Label(commitRowLabel);
        EmitCommitPendingCameraStreamKind(PendingStreamRow, config);
        builder.LoadAImmediate(PendingStreamColumn);
        builder.StoreAAbsolute(PendingCameraNextStreamAddress);

        builder.Label(doneLabel);
    }

    private void EmitCommitPendingCameraStreamKind(byte kind, NesCameraConfig config)
    {
        if (kind == PendingStreamColumn)
        {
            EmitLoadPendingCameraColumnToStreamAddresses();
            EmitStreamColumnFromAddresses(config);
            EmitClearPendingCameraStream(kind);
        }
        else
        {
            EmitCommitPendingCameraRowStream(config);
        }
    }

    private void EmitLoadPendingCameraColumnToStreamAddresses()
    {
        builder.LoadAAbsolute(PendingCameraColumnTargetAddress);
        builder.StoreAZeroPage(CameraTargetColumnAddress);
        builder.LoadAAbsolute(PendingCameraColumnSourceAddress);
        builder.StoreAZeroPage(CameraSourceColumnAddress);
    }

    private void EmitLoadPendingCameraRowToStreamAddresses()
    {
        builder.LoadAAbsolute(PendingCameraRowTargetAddress);
        builder.StoreAZeroPage(CameraTargetRowAddress);
        builder.LoadAAbsolute(PendingCameraRowSourceAddress);
        builder.StoreAZeroPage(CameraSourceRowAddress);
        builder.LoadAAbsolute(PendingCameraRowTargetColumnAddress);
        builder.StoreAZeroPage(CameraTargetColumnAddress);
        builder.LoadAAbsolute(PendingCameraRowSourceColumnAddress);
        builder.StoreAZeroPage(CameraSourceColumnAddress);
    }

    private void EmitClearPendingCameraStream(byte kind)
    {
        builder.LoadAAbsolute(PendingCameraStreamFlagsAddress);
        builder.AndImmediate(kind == PendingStreamColumn ? 0xFE : 0xFD);
        builder.StoreAAbsolute(PendingCameraStreamFlagsAddress);
    }

    private void EmitCommitPendingCameraRowStream(NesCameraConfig config)
    {
        const int tilesPerPhase = 8;
        const int attributePhase = 4;
        var tilesLabel = builder.CreateLabel("nes_camera_row_tiles");
        var attributesLabel = builder.CreateLabel("nes_camera_row_attrs");
        var doneLabel = builder.CreateLabel("nes_camera_row_done");

        builder.LoadAAbsolute(PendingCameraRowPhaseAddress);
        builder.CompareImmediate(attributePhase);
        builder.BranchRelative(0xD0, tilesLabel); // BNE tilesLabel
        builder.JumpAbsolute(attributesLabel);

        builder.Label(tilesLabel);
        EmitLoadPendingCameraRowToStreamAddresses();
        EmitAdvancePendingRowColumnsForPhase(config, tilesPerPhase);
        EmitStreamRowTilesFromAddresses(config, tilesPerPhase);
        builder.LoadAAbsolute(PendingCameraRowPhaseAddress);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.StoreAAbsolute(PendingCameraRowPhaseAddress);
        builder.JumpAbsolute(doneLabel);

        builder.Label(attributesLabel);
        EmitLoadPendingCameraRowToStreamAddresses();
        EmitPrepareRuntimeRowAttributeColumn();
        EmitStreamRuntimeRowAttributes();
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(PendingCameraRowPhaseAddress);
        EmitClearPendingCameraStream(PendingStreamRow);

        builder.Label(doneLabel);
    }

    private void EmitAdvancePendingRowColumnsForPhase(NesCameraConfig config, int tilesPerPhase)
    {
        var loopLabel = builder.CreateLabel("nes_camera_row_phase_loop");
        var doneLabel = builder.CreateLabel("nes_camera_row_phase_done");

        builder.LoadAAbsolute(PendingCameraRowPhaseAddress);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, doneLabel); // BEQ doneLabel
        builder.StoreAZeroPage(CameraStreamRemainingAddress);

        builder.Label(loopLabel);
        EmitAddImmediateModulo(CameraTargetColumnAddress, tilesPerPhase, 64, "nes_camera_row_target_phase");
        EmitAddImmediateModulo(CameraSourceColumnAddress, tilesPerPhase, config.MapWidth, "nes_camera_row_source_phase");
        builder.DecrementZeroPage(CameraStreamRemainingAddress);
        builder.BranchRelative(0xD0, loopLabel); // BNE loopLabel

        builder.Label(doneLabel);
    }

    private void EmitAddImmediateModulo(byte address, int addend, int modulo, string labelPrefix)
    {
        var loopLabel = builder.CreateLabel($"{labelPrefix}_loop");
        var doneLabel = builder.CreateLabel($"{labelPrefix}_done");

        builder.LoadAZeroPage(address);
        builder.ClearCarry();
        builder.AddImmediate(addend);
        builder.Label(loopLabel);
        builder.CompareImmediate(modulo);
        builder.BranchRelative(0x90, doneLabel); // BCC doneLabel
        builder.SetCarry();
        builder.SubtractImmediate(modulo);
        builder.JumpAbsolute(loopLabel);
        builder.Label(doneLabel);
        builder.StoreAZeroPage(address);
    }

    private void EmitStreamColumnFromAddresses(NesCameraConfig config)
    {
        Sdk2DOperationValidator.Validate(
            NesTarget.Capabilities,
            new Sdk2DOperation.StreamMapColumn(TargetColumn: 0, SourceColumn: 0, Y: config.StreamY, Height: config.StreamHeight));

        builder.LoadAAbsolute(0x2002);              // reset PPU address latch
        for (var row = 0; row < config.StreamHeight; row++)
        {
            EmitStreamColumnRow(row, config.StreamY + row);
        }
    }

    private void EmitStreamRowFromAddresses(NesCameraConfig config)
    {
        EmitStreamRowTilesFromAddresses(config, NesTarget.Capabilities.ScreenTiles.Width);
        EmitPrepareRuntimeRowAttributeColumn();
        EmitStreamRuntimeRowAttributes();
    }

    private void EmitStreamRowTilesFromAddresses(NesCameraConfig config, int width)
    {
        Sdk2DOperationValidator.Validate(
            NesTarget.Capabilities,
            new Sdk2DOperation.StreamMapRow(TargetRow: 0, SourceRow: 0, X: 0, Width: width));

        var segmentLabel = builder.CreateLabel("nes_stream_row_segment");
        var writeLabel = builder.CreateLabel("nes_stream_row_write");
        var sourceNoWrapLabel = builder.CreateLabel("nes_stream_row_source_no_wrap");
        var tilesDoneLabel = builder.CreateLabel("nes_stream_row_tiles_done");
        var resetAddressLabel = builder.CreateLabel("nes_stream_row_reset_address");

        builder.LoadAAbsolute(0x2002);              // reset PPU address latch
        builder.LoadXZeroPage(CameraSourceRowAddress);
        builder.LdaAbsoluteX(NesRomBuilder.WorldMapRowPointerLowLabel);
        builder.StoreAZeroPage(RuntimeIndexScratchAddress);
        builder.LdaAbsoluteX(NesRomBuilder.WorldMapRowPointerHighLabel);
        builder.StoreAZeroPage(ExpressionScratchAddress);
        builder.LoadAImmediate(width);
        builder.StoreAZeroPage(CameraStreamRemainingAddress);

        builder.Label(segmentLabel);
        EmitSetPpuAddressFromTargetRowAndColumn();

        builder.Label(writeLabel);
        builder.LoadYZeroPage(CameraSourceColumnAddress);
        builder.LoadAIndirectY(RuntimeIndexScratchAddress);
        builder.StoreAAbsolute(0x2007);

        builder.LoadAZeroPage(CameraSourceColumnAddress);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.CompareImmediate(config.MapWidth);
        builder.BranchRelative(0x90, sourceNoWrapLabel); // BCC sourceNoWrapLabel
        builder.LoadAImmediate(0);
        builder.Label(sourceNoWrapLabel);
        builder.StoreAZeroPage(CameraSourceColumnAddress);

        builder.LoadAZeroPage(CameraTargetColumnAddress);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.AndImmediate(0x3F);
        builder.StoreAZeroPage(CameraTargetColumnAddress);

        builder.DecrementZeroPage(CameraStreamRemainingAddress);
        builder.BranchRelative(0xF0, tilesDoneLabel); // BEQ tilesDoneLabel
        builder.LoadAZeroPage(CameraTargetColumnAddress);
        builder.AndImmediate(0x1F);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, resetAddressLabel); // BEQ resetAddressLabel
        builder.JumpAbsolute(writeLabel);

        builder.Label(resetAddressLabel);
        builder.JumpAbsolute(segmentLabel);

        builder.Label(tilesDoneLabel);
    }

    private void EmitPrepareRuntimeRowAttributeColumn()
    {
        builder.LoadAZeroPage(CameraTargetColumnAddress);
        builder.AndImmediate(0x3C);
        builder.StoreAZeroPage(CameraSourceRowAddress);
    }

    private void EmitStreamRuntimeRowAttributes()
    {
        var loopLabel = builder.CreateLabel("nes_stream_row_attr_loop");

        builder.LoadAImmediate(9);
        builder.StoreAZeroPage(CameraStreamRemainingAddress);

        builder.Label(loopLabel);
        EmitSetPpuAttributeAddressFromTargetRowAndColumn();
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(0x2007);

        builder.LoadAZeroPage(CameraSourceRowAddress);
        builder.ClearCarry();
        builder.AddImmediate(4);
        builder.AndImmediate(0x3F);
        builder.StoreAZeroPage(CameraSourceRowAddress);

        builder.DecrementZeroPage(CameraStreamRemainingAddress);
        builder.BranchRelative(0xD0, loopLabel); // BNE loopLabel
    }

    private void EmitSetPpuAttributeAddressFromTargetRowAndColumn()
    {
        var leftNameTableLabel = builder.CreateLabel("nes_stream_attr_left_nt");
        var storeColumnLabel = builder.CreateLabel("nes_stream_attr_store_col");

        builder.LoadXZeroPage(CameraTargetRowAddress);
        builder.LdaAbsoluteX(NesRomBuilder.PpuAttributeRowAddressHighLabel);
        builder.StoreAZeroPage(SpriteFrameScratchAddress);

        builder.LoadAZeroPage(CameraSourceRowAddress);
        builder.CompareImmediate(32);
        builder.BranchRelative(0x90, leftNameTableLabel); // BCC leftNameTableLabel
        builder.LoadAZeroPage(SpriteFrameScratchAddress);
        builder.ClearCarry();
        builder.AddImmediate(4);
        builder.StoreAZeroPage(SpriteFrameScratchAddress);
        builder.LoadAZeroPage(CameraSourceRowAddress);
        builder.SetCarry();
        builder.SubtractImmediate(32);
        builder.JumpAbsolute(storeColumnLabel);

        builder.Label(leftNameTableLabel);
        builder.LoadAZeroPage(CameraSourceRowAddress);

        builder.Label(storeColumnLabel);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.StoreAZeroPage(CollisionColumnScratchAddress);
        builder.LoadAZeroPage(SpriteFrameScratchAddress);
        builder.StoreAAbsolute(0x2006);
        builder.LoadXZeroPage(CameraTargetRowAddress);
        builder.LdaAbsoluteX(NesRomBuilder.PpuAttributeRowAddressLowLabel);
        builder.ClearCarry();
        builder.AddZeroPage(CollisionColumnScratchAddress);
        builder.StoreAAbsolute(0x2006);
    }

    private void EmitSetPpuAddressFromTargetRowAndColumn()
    {
        var leftNameTableLabel = builder.CreateLabel("nes_stream_row_left_nt");
        var storeColumnLabel = builder.CreateLabel("nes_stream_row_store_col");

        builder.LoadXZeroPage(CameraTargetRowAddress);
        builder.LdaAbsoluteX(NesRomBuilder.PpuRowAddressHighLabel);
        builder.StoreAZeroPage(SpriteFrameScratchAddress);

        builder.LoadAZeroPage(CameraTargetColumnAddress);
        builder.CompareImmediate(32);
        builder.BranchRelative(0x90, leftNameTableLabel); // BCC leftNameTableLabel
        builder.LoadAZeroPage(SpriteFrameScratchAddress);
        builder.ClearCarry();
        builder.AddImmediate(4);
        builder.StoreAZeroPage(SpriteFrameScratchAddress);
        builder.LoadAZeroPage(CameraTargetColumnAddress);
        builder.SetCarry();
        builder.SubtractImmediate(32);
        builder.JumpAbsolute(storeColumnLabel);

        builder.Label(leftNameTableLabel);
        builder.LoadAZeroPage(CameraTargetColumnAddress);

        builder.Label(storeColumnLabel);
        builder.StoreAZeroPage(CollisionColumnScratchAddress);
        builder.LoadAZeroPage(SpriteFrameScratchAddress);
        builder.StoreAAbsolute(0x2006);
        builder.LoadXZeroPage(CameraTargetRowAddress);
        builder.LdaAbsoluteX(NesRomBuilder.PpuRowAddressLowLabel);
        builder.ClearCarry();
        builder.AddZeroPage(CollisionColumnScratchAddress);
        builder.StoreAAbsolute(0x2006);
    }

    private void EmitStreamColumnRow(int sourceRow, int targetY)
    {
        var rightNameTableLabel = builder.CreateLabel("nes_stream_right_nt");
        var writeTileLabel = builder.CreateLabel("nes_stream_write_tile");
        var rowAddress = 0x2000 + targetY * 32;

        builder.LoadAZeroPage(CameraTargetColumnAddress);
        builder.CompareImmediate(32);
        builder.BranchRelative(0xB0, rightNameTableLabel); // BCS rightNameTableLabel

        builder.LoadAImmediate(rowAddress >> 8);
        builder.StoreAAbsolute(0x2006);
        builder.LoadAZeroPage(CameraTargetColumnAddress);
        builder.ClearCarry();
        builder.AddImmediate(rowAddress & 0xFF);
        builder.StoreAAbsolute(0x2006);
        builder.JumpAbsolute(writeTileLabel);

        builder.Label(rightNameTableLabel);
        builder.LoadAImmediate((rowAddress >> 8) + 4);
        builder.StoreAAbsolute(0x2006);
        builder.LoadAZeroPage(CameraTargetColumnAddress);
        builder.SetCarry();
        builder.SubtractImmediate(32);
        builder.ClearCarry();
        builder.AddImmediate(rowAddress & 0xFF);
        builder.StoreAAbsolute(0x2006);

        builder.Label(writeTileLabel);
        builder.LoadAZeroPage(CameraSourceColumnAddress);
        builder.TransferAToX();
        builder.LdaAbsoluteX(NesRomBuilder.WorldMapRowLabel(sourceRow));
        builder.StoreAAbsolute(0x2007);
    }

    private void EmitStreamMapRowSegment(int targetRow, int sourceRow, int x, int width)
    {
        var rowAddress = PpuNameTableTileAddress(x, targetRow);

        builder.LoadAImmediate(rowAddress >> 8);
        builder.StoreAAbsolute(0x2006);
        builder.LoadAImmediate(rowAddress & 0xFF);
        builder.StoreAAbsolute(0x2006);

        for (var offset = 0; offset < width; offset++)
        {
            builder.LoadXImmediate(x + offset);
            builder.LdaAbsoluteX(NesRomBuilder.WorldMapRowLabel(sourceRow));
            builder.StoreAAbsolute(0x2007);
        }
    }

    private static int PpuNameTableTileAddress(int x, int y)
    {
        var nameTableX = x / 32;
        var nameTableY = y / 30;
        return 0x2000
               + nameTableY * 0x800
               + nameTableX * 0x400
               + y % 30 * 32
               + x % 32;
    }

    private void EmitStreamMapRowAttributes(int targetRow, int x, int width)
    {
        var firstAttributeColumn = x / 4;
        var lastAttributeColumn = (x + width - 1) / 4;
        for (var attributeColumn = firstAttributeColumn; attributeColumn <= lastAttributeColumn; attributeColumn++)
        {
            var attributeX = attributeColumn * 4;
            var attributeAddress = PpuAttributeAddress(attributeX, targetRow);
            builder.LoadAImmediate(attributeAddress >> 8);
            builder.StoreAAbsolute(0x2006);
            builder.LoadAImmediate(attributeAddress & 0xFF);
            builder.StoreAAbsolute(0x2006);
            builder.LoadAImmediate(0);
            builder.StoreAAbsolute(0x2007);
        }
    }

    private static int PpuAttributeAddress(int x, int y)
    {
        var nameTableX = x / 32;
        var nameTableY = y / 30;
        return 0x23C0
               + nameTableY * 0x800
               + nameTableX * 0x400
               + (y % 30) / 4 * 8
               + (x % 32) / 4;
    }

    internal void EmitCameraAabbTiles(Sdk2DOperation.CameraAabbTiles operation)
    {
        if (operation.WorldId != "default")
        {
            throw new InvalidOperationException($"Unsupported NES world id '{operation.WorldId}'.");
        }

        var config = EnsureCameraConfigured("camera_aabb_tiles");
        _ = WorldMapForFlagQuery("camera_aabb_tiles");
        var width = CameraAabbWidth(operation.Width);
        var flags = (int)operation.Flags;
        if (width == 0 || operation.Height == 0 || flags == 0)
        {
            builder.LoadAImmediate(0);
            return;
        }

        ValidateConstantCameraAabbSpan(operation.ScreenX, width, NesTarget.Capabilities.ScreenPixels.Width, "camera_aabb_tiles");

        var foundLabel = builder.CreateLabel("camera_aabb_tiles_found");
        var endLabel = builder.CreateLabel("camera_aabb_tiles_end");
        foreach (var yOffset in AabbSampleOffsets(operation.Height))
        {
            foreach (var xOffset in AabbSampleOffsets(width))
            {
                var nextProbeLabel = builder.CreateLabel("camera_aabb_tiles_next");
                EmitCameraTileFlagsAt(operation.ScreenX, xOffset, operation.WorldY, operation.WorldYOffset + yOffset, config, "camera_aabb_tiles");
                builder.AndImmediate(flags);
                builder.CompareImmediate(0);
                builder.BranchRelative(0xF0, nextProbeLabel); // BEQ nextProbeLabel
                builder.JumpAbsolute(foundLabel);
                builder.Label(nextProbeLabel);
            }
        }

        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(foundLabel);
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
    }

    internal void EmitCameraAabbHitTop(Sdk2DOperation.CameraAabbHitTop operation)
    {
        if (operation.WorldId != "default")
        {
            throw new InvalidOperationException($"Unsupported NES world id '{operation.WorldId}'.");
        }

        var callName = "camera_aabb_hit_top";
        var config = EnsureCameraConfigured(callName);
        _ = WorldMapForFlagQuery(callName);
        var width = CameraAabbWidth(operation.Width);
        var flags = (int)operation.Flags;
        if (width == 0 || operation.Height == 0 || flags == 0)
        {
            builder.LoadAImmediate(255);
            return;
        }

        ValidateConstantCameraAabbSpan(operation.ScreenX, width, NesTarget.Capabilities.ScreenPixels.Width, "camera_aabb_hit_top");

        var endLabel = builder.CreateLabel("camera_aabb_hit_top_end");
        foreach (var yOffset in AabbSampleOffsets(operation.Height))
        {
            foreach (var xOffset in AabbSampleOffsets(width))
            {
                var nextProbeLabel = builder.CreateLabel("camera_aabb_hit_top_next");
                var hitTopOffset = operation.WorldYOffset + yOffset;
                EmitCameraTileFlagsAt(operation.ScreenX, xOffset, operation.WorldY, hitTopOffset, config, callName);
                builder.AndImmediate(flags);
                builder.CompareImmediate(0);
                builder.BranchRelative(0xF0, nextProbeLabel); // BEQ nextProbeLabel
                EmitWorldPixelTileTop(operation.WorldY, hitTopOffset);
                builder.JumpAbsolute(endLabel);
                builder.Label(nextProbeLabel);
            }
        }

        builder.LoadAImmediate(255);
        builder.Label(endLabel);
    }

    internal void EmitCameraScreenAabbTiles(Sdk2DOperation.CameraScreenAabbTiles operation)
    {
        if (operation.WorldId != "default")
        {
            throw new InvalidOperationException($"Unsupported NES world id '{operation.WorldId}'.");
        }

        var config = EnsureCameraConfigured("camera_screen_aabb_tiles");
        _ = WorldMapForFlagQuery("camera_screen_aabb_tiles");
        var width = CameraAabbWidth(operation.Width);
        var flags = (int)operation.Flags;
        if (width == 0 || operation.Height == 0 || flags == 0)
        {
            builder.LoadAImmediate(0);
            return;
        }

        ValidateConstantCameraAabbSpan(operation.ScreenX, width, NesTarget.Capabilities.ScreenPixels.Width, "camera_screen_aabb_tiles");

        var foundLabel = builder.CreateLabel("camera_screen_aabb_tiles_found");
        var endLabel = builder.CreateLabel("camera_screen_aabb_tiles_end");
        foreach (var yOffset in AabbSampleOffsets(operation.Height))
        {
            foreach (var xOffset in AabbSampleOffsets(width))
            {
                var nextProbeLabel = builder.CreateLabel("camera_screen_aabb_tiles_next");
                EmitCameraScreenTileFlagsAt(
                    operation.ScreenX,
                    xOffset,
                    operation.ScreenY,
                    operation.ScreenYOffset + yOffset,
                    config,
                    "camera_screen_aabb_tiles");
                builder.AndImmediate(flags);
                builder.CompareImmediate(0);
                builder.BranchRelative(0xF0, nextProbeLabel); // BEQ nextProbeLabel
                builder.JumpAbsolute(foundLabel);
                builder.Label(nextProbeLabel);
            }
        }

        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(foundLabel);
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
    }

    internal void EmitCameraScreenAabbHitTop(Sdk2DOperation.CameraScreenAabbHitTop operation)
    {
        if (operation.WorldId != "default")
        {
            throw new InvalidOperationException($"Unsupported NES world id '{operation.WorldId}'.");
        }

        var callName = "camera_screen_aabb_hit_top";
        var config = EnsureCameraConfigured(callName);
        _ = WorldMapForFlagQuery(callName);
        var width = CameraAabbWidth(operation.Width);
        var flags = (int)operation.Flags;
        if (width == 0 || operation.Height == 0 || flags == 0)
        {
            builder.LoadAImmediate(255);
            return;
        }

        ValidateConstantCameraAabbSpan(operation.ScreenX, width, NesTarget.Capabilities.ScreenPixels.Width, callName);

        var endLabel = builder.CreateLabel("camera_screen_aabb_hit_top_end");
        foreach (var yOffset in AabbSampleOffsets(operation.Height))
        {
            foreach (var xOffset in AabbSampleOffsets(width))
            {
                var nextProbeLabel = builder.CreateLabel("camera_screen_aabb_hit_top_next");
                var hitTopOffset = operation.ScreenYOffset + yOffset;
                EmitCameraScreenTileFlagsAt(operation.ScreenX, xOffset, operation.ScreenY, hitTopOffset, config, callName);
                builder.AndImmediate(flags);
                builder.CompareImmediate(0);
                builder.BranchRelative(0xF0, nextProbeLabel); // BEQ nextProbeLabel
                EmitScreenPixelTileTop(operation.ScreenY, hitTopOffset);
                builder.JumpAbsolute(endLabel);
                builder.Label(nextProbeLabel);
            }
        }

        builder.LoadAImmediate(255);
        builder.Label(endLabel);
    }

    private void EmitCameraTileFlagsAt(int screenPixelX, SdkByteExpression worldY, int worldYOffset, NesCameraConfig config, string callName)
    {
        var worldMap = WorldMapForFlagQuery(callName);
        var outOfBoundsLabel = builder.CreateLabel("camera_tile_flags_oob");
        var endLabel = builder.CreateLabel("camera_tile_flags_end");
        if (TrySdkConst(worldY, out var constantWorldY))
        {
            var row = (constantWorldY + worldYOffset) / 8;
            if (row < 0 || row >= worldMap.Height)
            {
                builder.LoadAImmediate(0);
                return;
            }

            EmitCameraPixelToSourceColumn(screenPixelX, config.MapWidth);
            EmitMapFlagsAtSourceColumnInA(row);
            return;
        }

        EmitCameraPixelToSourceColumn(screenPixelX, config.MapWidth);
        builder.StoreAZeroPage(CollisionColumnScratchAddress);

        EmitWorldPixelToTileCoordinate(worldY, worldYOffset);
        builder.CompareImmediate(worldMap.Height);
        var inBoundsLabel = builder.CreateLabel("camera_tile_flags_in_bounds");
        builder.BranchRelative(0x90, inBoundsLabel); // BCC inBoundsLabel
        builder.JumpAbsolute(outOfBoundsLabel);
        builder.Label(inBoundsLabel);
        builder.StoreAZeroPage(CollisionRowScratchAddress);
        EmitMapFlagsAtScratchColumnAndRow();
        builder.JumpAbsolute(endLabel);

        builder.Label(outOfBoundsLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private void EmitCameraTileFlagsAt(SdkByteExpression screenPixelX, int screenPixelXOffset, SdkByteExpression worldY, int worldYOffset, NesCameraConfig config, string callName)
    {
        if (TrySdkConst(screenPixelX, out var constantScreenX))
        {
            EmitCameraTileFlagsAt(constantScreenX + screenPixelXOffset, worldY, worldYOffset, config, callName);
            return;
        }

        var worldMap = WorldMapForFlagQuery(callName);
        var outOfBoundsLabel = builder.CreateLabel("camera_tile_flags_oob");
        var endLabel = builder.CreateLabel("camera_tile_flags_end");
        if (TrySdkConst(worldY, out var constantWorldY))
        {
            var row = (constantWorldY + worldYOffset) / 8;
            if (row < 0 || row >= worldMap.Height)
            {
                builder.LoadAImmediate(0);
                return;
            }

            EmitCameraPixelToSourceColumn(screenPixelX, screenPixelXOffset, config.MapWidth);
            EmitMapFlagsAtSourceColumnInA(row);
            return;
        }

        EmitCameraPixelToSourceColumn(screenPixelX, screenPixelXOffset, config.MapWidth);
        builder.StoreAZeroPage(CollisionColumnScratchAddress);

        EmitWorldPixelToTileCoordinate(worldY, worldYOffset);
        builder.CompareImmediate(worldMap.Height);
        var inBoundsLabel = builder.CreateLabel("camera_tile_flags_in_bounds");
        builder.BranchRelative(0x90, inBoundsLabel); // BCC inBoundsLabel
        builder.JumpAbsolute(outOfBoundsLabel);
        builder.Label(inBoundsLabel);
        builder.StoreAZeroPage(CollisionRowScratchAddress);
        EmitMapFlagsAtScratchColumnAndRow();
        builder.JumpAbsolute(endLabel);

        builder.Label(outOfBoundsLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private void EmitCameraScreenTileFlagsAt(
        SdkByteExpression screenPixelX,
        int screenPixelXOffset,
        SdkByteExpression screenPixelY,
        int screenPixelYOffset,
        NesCameraConfig config,
        string callName)
    {
        var worldMap = WorldMapForFlagQuery(callName);
        var endLabel = builder.CreateLabel("camera_screen_tile_flags_end");

        EmitCameraPixelToSourceColumn(screenPixelX, screenPixelXOffset, config.MapWidth);
        builder.StoreAZeroPage(CollisionColumnScratchAddress);

        EmitCameraPixelToSourceRow(screenPixelY, screenPixelYOffset, worldMap.Height);
        builder.StoreAZeroPage(CollisionRowScratchAddress);
        EmitMapFlagsAtScratchColumnAndRow();
        builder.Label(endLabel);
    }

    private void EmitMapFlagsAtSourceColumnInA(int row)
    {
        builder.TransferAToX();
        builder.LdaAbsoluteX(NesRomBuilder.WorldMapFlagRowLabel(row));
    }

    private void EmitMapFlagsAtScratchColumnAndRow()
    {
        builder.LoadXZeroPage(CollisionRowScratchAddress);
        builder.LdaAbsoluteX(NesRomBuilder.WorldMapFlagRowPointerLowLabel);
        builder.StoreAZeroPage(RuntimeIndexScratchAddress);
        builder.LdaAbsoluteX(NesRomBuilder.WorldMapFlagRowPointerHighLabel);
        builder.StoreAZeroPage(ExpressionScratchAddress);
        builder.LoadYZeroPage(CollisionColumnScratchAddress);
        builder.LoadAIndirectY(RuntimeIndexScratchAddress);
    }

    private void EmitCameraPixelToSourceColumn(int screenPixelX, int mapWidth)
    {
        var wrapLabel = builder.CreateLabel("camera_pixel_column_wrap");
        var endLabel = builder.CreateLabel("camera_pixel_column_end");

        builder.LoadAZeroPage(CameraXAddress);
        builder.AndImmediate(0x07);
        if (screenPixelX != 0)
        {
            builder.ClearCarry();
            builder.AddImmediate(screenPixelX);
        }

        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.ClearCarry();
        builder.AddZeroPage(CameraTileColumnAddress);

        builder.Label(wrapLabel);
        builder.CompareImmediate(mapWidth);
        builder.BranchRelative(0x90, endLabel); // BCC endLabel
        builder.SetCarry();
        builder.SubtractImmediate(mapWidth);
        builder.JumpAbsolute(wrapLabel);
        builder.Label(endLabel);
    }

    private void EmitCameraPixelToSourceColumn(SdkByteExpression screenPixelX, int screenPixelXOffset, int mapWidth)
    {
        if (TrySdkConst(screenPixelX, out var constantScreenX))
        {
            EmitCameraPixelToSourceColumn(constantScreenX + screenPixelXOffset, mapWidth);
            return;
        }

        var wrapLabel = builder.CreateLabel("camera_pixel_column_wrap");
        var endLabel = builder.CreateLabel("camera_pixel_column_end");

        EmitSdkByteExpressionToA(screenPixelX);
        if (screenPixelXOffset != 0)
        {
            builder.ClearCarry();
            builder.AddImmediate(screenPixelXOffset);
        }

        builder.StoreAZeroPage(CollisionColumnScratchAddress);
        builder.LoadAZeroPage(CameraXAddress);
        builder.AndImmediate(0x07);
        builder.ClearCarry();
        builder.AddZeroPage(CollisionColumnScratchAddress);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.ClearCarry();
        builder.AddZeroPage(CameraTileColumnAddress);

        builder.Label(wrapLabel);
        builder.CompareImmediate(mapWidth);
        builder.BranchRelative(0x90, endLabel); // BCC endLabel
        builder.SetCarry();
        builder.SubtractImmediate(mapWidth);
        builder.JumpAbsolute(wrapLabel);
        builder.Label(endLabel);
    }

    private static void ValidateConstantCameraAabbSpan(SdkByteExpression screenX, int width, int screenWidth, string callName)
    {
        if (screenX is SdkByteExpression.Constant constant && constant.Value + width > screenWidth)
        {
            throw new InvalidOperationException($"{callName} screen span must fit within the visible NES width.");
        }
    }

    private void EmitWorldPixelToTileCoordinate(SdkByteExpression expression, int offset)
    {
        EmitSdkByteExpressionToA(expression);
        EmitAddSignedImmediate(offset);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.ShiftRightA();
    }

    private void EmitWorldPixelTileTop(SdkByteExpression expression, int offset)
    {
        EmitSdkByteExpressionToA(expression);
        EmitAddSignedImmediate(offset);
        builder.AndImmediate(0xF8);
    }

    private void EmitCameraPixelToSourceRow(SdkByteExpression screenPixelY, int screenPixelYOffset, int mapHeight)
    {
        var wrapLabel = builder.CreateLabel("camera_pixel_row_wrap");
        var endLabel = builder.CreateLabel("camera_pixel_row_end");

        EmitSdkByteExpressionToA(screenPixelY);
        EmitAddSignedImmediate(screenPixelYOffset);
        builder.StoreAZeroPage(CollisionRowScratchAddress);
        builder.LoadAZeroPage(CameraYAddress);
        builder.AndImmediate(0x07);
        builder.ClearCarry();
        builder.AddZeroPage(CollisionRowScratchAddress);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.ClearCarry();
        builder.AddZeroPage(CameraTileRowAddress);

        builder.Label(wrapLabel);
        builder.CompareImmediate(mapHeight);
        builder.BranchRelative(0x90, endLabel); // BCC endLabel
        builder.SetCarry();
        builder.SubtractImmediate(mapHeight);
        builder.JumpAbsolute(wrapLabel);
        builder.Label(endLabel);
    }

    private void EmitScreenPixelTileTop(SdkByteExpression expression, int offset)
    {
        EmitSdkByteExpressionToA(expression);
        EmitAddSignedImmediate(offset);
        builder.AndImmediate(0xF8);
    }

    private WorldMap2D WorldMapForFlagQuery(string callName)
    {
        return program.WorldMap
               ?? throw new InvalidOperationException($"{callName} requires world_map collision flag data.");
    }

    private int CameraAabbWidth(SdkAabbExtent width)
    {
        return width switch
        {
            SdkAabbExtent.Constant constant => constant.Value,
            SdkAabbExtent.SpriteWidth spriteWidth => SpriteWidth(spriteWidth.SpriteId),
            _ => throw new InvalidOperationException($"Unsupported camera AABB width '{width.GetType().Name}'."),
        };
    }

    private int SpriteWidth(string assetName)
    {
        if (!program.SpriteAssets.TryGetValue(assetName, out var asset))
        {
            throw new InvalidOperationException($"Unknown NES sprite asset '{assetName}'. Declare it with sprite_asset(...).");
        }

        return asset.LogicalWidth;
    }

    private void EmitSpriteWidth(FunctionCall call)
    {
        builder.LoadAImmediate(SpriteWidth(call));
    }

    private int SpriteWidth(FunctionCall call)
    {
        NesVideoProgram.RequireArity(call, 1);
        var assetName = NesVideoProgram.IdentifierArg(call.Parameters.ElementAt(0), "sprite_width argument 1");
        return SpriteWidth(assetName);
    }

    private void EmitAnimationFrame(FunctionCall call)
    {
        NesVideoProgram.RequireArity(call, 2);
        var clip = AnimationClipArg(call);
        var tickExpression = call.Parameters.ElementAt(1);
        if (TryConst(tickExpression, out var tick))
        {
            builder.LoadAImmediate(clip.FrameAtTick(tick % clip.DurationTicks));
            return;
        }

        EmitExpressionToA(tickExpression);
        EmitAnimationFrameFromTickInA(clip);
    }

    private void EmitAnimationFrameFromTickInA(SpriteAnimationClip clip)
    {
        var moduloLabel = builder.CreateLabel("animation_frame_modulo");
        var afterModuloLabel = builder.CreateLabel("animation_frame_after_modulo");
        var endLabel = builder.CreateLabel("animation_frame_end");

        builder.Label(moduloLabel);
        builder.CompareImmediate(clip.DurationTicks);
        builder.BranchRelative(0x90, afterModuloLabel); // BCC afterModuloLabel
        builder.SetCarry();
        builder.SubtractImmediate(clip.DurationTicks);
        builder.JumpAbsolute(moduloLabel);

        builder.Label(afterModuloLabel);
        for (var i = 0; i < clip.FrameCount - 1; i++)
        {
            var nextFrameLabel = builder.CreateLabel("animation_frame_next");
            builder.CompareImmediate(clip.FrameStartTicks[i + 1]);
            builder.BranchRelative(0xB0, nextFrameLabel); // BCS nextFrameLabel
            builder.LoadAImmediate(clip.FrameIndices[i]);
            builder.JumpAbsolute(endLabel);
            builder.Label(nextFrameLabel);
        }

        builder.LoadAImmediate(clip.FrameIndices[^1]);
        builder.Label(endLabel);
    }

    private SpriteAnimationClip AnimationClipArg(FunctionCall call)
    {
        var clipName = NesVideoProgram.IdentifierArg(call.Parameters.ElementAt(0), "animation_frame argument 1");
        if (!program.AnimationClips.TryGetValue(clipName, out var clip))
        {
            throw new InvalidOperationException($"Unknown animation clip '{clipName}'. Declare it with animation_clip(...).");
        }

        return clip;
    }

    private static IReadOnlyList<int> AabbSampleOffsets(int size)
    {
        var offsets = new List<int>();
        for (var offset = 0; offset < size; offset += 8)
        {
            offsets.Add(offset);
        }

        var lastOffset = size - 1;
        if (!offsets.Contains(lastOffset))
        {
            offsets.Add(lastOffset);
        }

        return offsets;
    }

    private NesCameraConfig EnsureCameraConfigured(string callName)
    {
        if (cameraConfig is not { } config)
        {
            throw new InvalidOperationException($"{callName} requires camera_init(...) to be emitted first.");
        }

        return config;
    }

    internal void EmitDrawLogicalSprite(Sdk2DOperation.DrawLogicalSprite operation)
    {
        if (!program.SpriteAssets.TryGetValue(operation.SpriteId, out var asset))
        {
            throw new InvalidOperationException($"Unknown NES sprite asset '{operation.SpriteId}'. Declare it with sprite_asset(...).");
        }

        var firstHardwareSprite = nextHardwareSprite;
        if (firstHardwareSprite + asset.Pieces.Count > NesTarget.Capabilities.SpriteCount)
        {
            throw new InvalidOperationException($"NES sprite_draw calls exceed the {NesTarget.Capabilities.SpriteCount} hardware sprite OAM limit.");
        }

        nextHardwareSprite += asset.Pieces.Count;
        for (var pieceIndex = 0; pieceIndex < asset.Pieces.Count; pieceIndex++)
        {
            var piece = asset.Pieces[pieceIndex];
            var oamAddress = (ushort)(OamShadowAddress + (firstHardwareSprite + pieceIndex) * 4);

            EmitSpriteDrawY(operation.Y, piece.YOffset, oamAddress);

            EmitSpriteTile(operation.Frame, asset, piece.TileOffset);
            builder.StoreAAbsolute((ushort)(oamAddress + 1));

            EmitSpriteDrawAttributes(operation.FlipX, operation.PaletteSlot, (ushort)(oamAddress + 2));

            EmitSpriteDrawX(operation.X, operation.FlipX, asset, piece, (ushort)(oamAddress + 3));
        }

        EmitOamDma();
    }

    private void EmitSpriteDrawY(SdkByteExpression yExpression, int offset, ushort oamAddress)
    {
        EmitSdkByteExpressionToA(yExpression);
        EmitAddSignedImmediate(offset - 1);
        builder.StoreAAbsolute(oamAddress);
    }

    private void EmitSpriteTile(SdkByteExpression frameExpression, NesCompiledSpriteAsset asset, int pieceTileOffset)
    {
        if (frameExpression is SdkByteExpression.Constant constant)
        {
            if (constant.Value < 0 || constant.Value >= asset.FrameCount)
            {
                throw new InvalidOperationException($"sprite_draw argument 4 must be between 0 and {asset.FrameCount - 1}.");
            }

            builder.LoadAImmediate(asset.FirstTile + constant.Value * asset.TilesPerFrame + pieceTileOffset);
            return;
        }

        EmitSdkByteExpressionToA(frameExpression);
        EmitMultiplyAByConstant(asset.TilesPerFrame);
        EmitAddSignedImmediate(asset.FirstTile + pieceTileOffset);
    }

    private void EmitSpriteDrawX(
        SdkByteExpression xExpression,
        SdkByteExpression? flipXExpression,
        NesCompiledSpriteAsset asset,
        NesMetaspritePiece piece,
        ushort oamAddress)
    {
        var normalOffset = piece.XOffset;
        var flippedOffset = asset.LogicalWidth - 8 - piece.XOffset;
        if (flipXExpression is null || normalOffset == flippedOffset)
        {
            EmitSpriteDrawXAtOffset(xExpression, normalOffset, oamAddress);
            return;
        }

        var normalLabel = builder.CreateLabel("sprite_x_normal");
        var endLabel = builder.CreateLabel("sprite_x_end");

        EmitSdkByteExpressionToA(flipXExpression);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, normalLabel); // BEQ normalLabel

        EmitSpriteDrawXAtOffset(xExpression, flippedOffset, oamAddress);
        builder.JumpAbsolute(endLabel);

        builder.Label(normalLabel);
        EmitSpriteDrawXAtOffset(xExpression, normalOffset, oamAddress);
        builder.Label(endLabel);
    }

    private void EmitSpriteDrawXAtOffset(SdkByteExpression xExpression, int offset, ushort oamAddress)
    {
        EmitSdkByteExpressionToA(xExpression);
        EmitAddSignedImmediate(offset);
        builder.StoreAAbsolute(oamAddress);
    }

    private void EmitSpriteDrawAttributes(SdkByteExpression? flipXExpression, int paletteSlot, ushort oamAddress)
    {
        if (flipXExpression is null || (TrySdkConst(flipXExpression, out var constant) && constant == 0))
        {
            builder.LoadAImmediate(paletteSlot);
            builder.StoreAAbsolute(oamAddress);
            return;
        }

        if (TrySdkConst(flipXExpression, out _))
        {
            builder.LoadAImmediate(paletteSlot | 0x40);
            builder.StoreAAbsolute(oamAddress);
            return;
        }

        var noFlipLabel = builder.CreateLabel("sprite_flags_no_flip");
        var storeLabel = builder.CreateLabel("sprite_flags_store");

        EmitSdkByteExpressionToA(flipXExpression);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, noFlipLabel); // BEQ noFlipLabel

        builder.LoadAImmediate(paletteSlot | 0x40);
        builder.JumpAbsolute(storeLabel);

        builder.Label(noFlipLabel);
        builder.LoadAImmediate(paletteSlot);

        builder.Label(storeLabel);
        builder.StoreAAbsolute(oamAddress);
    }

    private void EmitMultiplyAByConstant(int factor)
    {
        if (factor == 1)
        {
            return;
        }

        builder.StoreAZeroPage(SpriteFrameScratchAddress);
        builder.LoadAImmediate(0);
        for (var i = 0; i < factor; i++)
        {
            builder.ClearCarry();
            builder.AddZeroPage(SpriteFrameScratchAddress);
        }
    }

    private static bool TrySdkConst(SdkByteExpression expression, out int value)
    {
        if (expression is SdkByteExpression.Constant constant)
        {
            value = constant.Value;
            return true;
        }

        value = 0;
        return false;
    }

    private void EmitAddSignedImmediate(int offset)
    {
        if (offset == 0)
        {
            return;
        }

        if (offset is < -255 or > 255)
        {
            throw new InvalidOperationException("NES sprite piece offset must fit in one byte for the current sprite spike.");
        }

        if (offset > 0)
        {
            builder.ClearCarry();
            builder.AddImmediate(offset);
            return;
        }

        builder.SetCarry();
        builder.SubtractImmediate(-offset);
    }

    private void EmitOamDma()
    {
        builder.LoadAImmediate((OamShadowAddress >> 8) & 0xFF);
        builder.StoreAAbsolute(OamDmaAddress);
    }

    private void EmitExpressionToA(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case ConstantSyntax:
                builder.LoadAImmediate(NesVideoProgram.ConstValue(expression, "constant"));
                break;
            case IdentifierSyntax { Identifier: "true" }:
                builder.LoadAImmediate(1);
                break;
            case IdentifierSyntax { Identifier: "false" }:
                builder.LoadAImmediate(0);
                break;
            case IdentifierSyntax identifier:
                builder.LoadAZeroPage(VariableAddress(identifier.Identifier));
                break;
            case MemberAccessSyntax memberAccess:
                if (TryRuntimeIndexedMemberAccess(memberAccess, out var indexedBase, out var fieldName))
                {
                    EmitRuntimeMemberIndexToX(indexedBase);
                    builder.LoadAZeroPageX(RuntimeIndexedMemberBaseAddress(indexedBase, fieldName));
                }
                else
                {
                    builder.LoadAZeroPage(VariableAddress(NesVideoProgram.MemberAccessName(memberAccess)));
                }

                break;
            case IndexExpressionSyntax indexExpression:
                if (TryConst(indexExpression.Index, out _))
                {
                    builder.LoadAZeroPage(VariableAddress(IndexedElementName(indexExpression.BaseIdentifier, indexExpression.Index, $"{indexExpression.BaseIdentifier} array index")));
                }
                else
                {
                    EmitRuntimeIndexToX(indexExpression.BaseIdentifier, indexExpression.Index);
                    builder.LoadAZeroPageX(ArrayBaseAddress(indexExpression.BaseIdentifier));
                }
                break;
            case FunctionCall call:
                EmitValueCallToA(call);
                break;
            case CastSyntax cast:
                RequireSupportedCastTarget(cast);
                EmitExpressionToA(cast.Expression);
                break;
            case ConditionalExpressionSyntax conditional:
                EmitConditionalExpressionToA(conditional);
                break;
            case UnaryExpressionSyntax unary when IsBooleanValueExpression(unary):
                EmitBooleanExpressionToA(unary);
                break;
            case BinaryExpressionSyntax binary when IsBooleanValueExpression(binary):
                EmitBooleanExpressionToA(binary);
                break;
            case BinaryExpressionSyntax binary:
                EmitBinaryExpressionToA(binary);
                break;
            default:
                throw new InvalidOperationException($"Unsupported NES expression '{expression.GetType().Name}'.");
        }
    }

    private void EmitConditionalExpressionToA(ConditionalExpressionSyntax conditional)
    {
        var falseLabel = builder.CreateLabel("conditional_false");
        var endLabel = builder.CreateLabel("conditional_end");

        EmitConditionFalseJump(conditional.Condition, falseLabel);
        EmitExpressionToA(conditional.WhenTrue);
        builder.JumpAbsolute(endLabel);
        builder.Label(falseLabel);
        EmitExpressionToA(conditional.WhenFalse);
        builder.Label(endLabel);
    }

    private static bool IsBooleanValueExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            UnaryExpressionSyntax { OperatorSymbol: "!" } => true,
            BinaryExpressionSyntax binary => binary.Operator.Symbol is "&&" or "||" or "==" or "!=" or "<" or "<=" or ">" or ">=",
            _ => false,
        };
    }

    private void EmitBooleanExpressionToA(ExpressionSyntax expression)
    {
        var falseLabel = builder.CreateLabel("bool_false");
        var endLabel = builder.CreateLabel("bool_end");

        EmitConditionFalseJump(expression, falseLabel);
        builder.LoadAImmediate(1);
        builder.JumpAbsolute(endLabel);
        builder.Label(falseLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private void EmitBinaryExpressionToA(BinaryExpressionSyntax binary)
    {
        switch (binary.Operator.Symbol)
        {
            case "+":
                if (TryConst(binary.Right, out var addRight))
                {
                    EmitExpressionToA(binary.Left);
                    builder.ClearCarry();
                    builder.AddImmediate(addRight);
                    return;
                }

                if (TryConst(binary.Left, out var addLeft))
                {
                    EmitExpressionToA(binary.Right);
                    builder.ClearCarry();
                    builder.AddImmediate(addLeft);
                    return;
                }

                if (TryAddress(binary.Left, out var addAddress))
                {
                    EmitExpressionToA(binary.Right);
                    builder.ClearCarry();
                    builder.AddZeroPage(addAddress);
                    return;
                }

                break;
            case "-":
                if (TryConst(binary.Right, out var subtractRight))
                {
                    EmitExpressionToA(binary.Left);
                    builder.SetCarry();
                    builder.SubtractImmediate(subtractRight);
                    return;
                }

                if (TryAddress(binary.Right, out var subtractAddress))
                {
                    EmitExpressionToA(binary.Left);
                    builder.SetCarry();
                    builder.SubtractZeroPage(subtractAddress);
                    return;
                }

                EmitVariableOperandsToAAndScratch(binary.Left, binary.Right);
                builder.SetCarry();
                builder.SubtractZeroPage(ExpressionScratchAddress);
                return;
            case "&":
            case "|":
            case "^":
                if (EmitBitwiseBinaryExpressionToA(binary))
                {
                    return;
                }

                break;
        }

        throw new InvalidOperationException($"Unsupported NES binary expression '{binary.Operator.Symbol}'.");
    }

    private bool EmitBitwiseBinaryExpressionToA(BinaryExpressionSyntax binary)
    {
        if (TryConst(binary.Right, out var rightConstant))
        {
            EmitExpressionToA(binary.Left);
            EmitBitwiseImmediate(binary.Operator.Symbol, rightConstant);
            return true;
        }

        if (TryConst(binary.Left, out var leftConstant))
        {
            EmitExpressionToA(binary.Right);
            EmitBitwiseImmediate(binary.Operator.Symbol, leftConstant);
            return true;
        }

        if (TryAddress(binary.Right, out var rightAddress))
        {
            EmitExpressionToA(binary.Left);
            EmitBitwiseZeroPage(binary.Operator.Symbol, rightAddress);
            return true;
        }

        if (TryAddress(binary.Left, out var leftAddress))
        {
            EmitExpressionToA(binary.Right);
            EmitBitwiseZeroPage(binary.Operator.Symbol, leftAddress);
            return true;
        }

        return false;
    }

    private void EmitBitwiseImmediate(string op, int value)
    {
        var mask = value & 0xFF;
        switch (op)
        {
            case "&":
                builder.AndImmediate(mask);
                return;
            case "|":
                builder.OrImmediate(mask);
                return;
            case "^":
                builder.XorImmediate(mask);
                return;
            default:
                throw new InvalidOperationException($"Unsupported NES bitwise operator '{op}'.");
        }
    }

    private void EmitBitwiseZeroPage(string op, byte address)
    {
        switch (op)
        {
            case "&":
                builder.AndZeroPage(address);
                return;
            case "|":
                builder.OrZeroPage(address);
                return;
            case "^":
                builder.XorZeroPage(address);
                return;
            default:
                throw new InvalidOperationException($"Unsupported NES bitwise operator '{op}'.");
        }
    }

    private bool TryAddress(ExpressionSyntax expression, out byte address)
    {
        return TryDirectAddress(expression, out address);
    }

    private bool TryDirectAddress(ExpressionSyntax expression, out byte address)
    {
        switch (expression)
        {
            case CastSyntax cast:
                RequireSupportedCastTarget(cast);
                return TryDirectAddress(cast.Expression, out address);
            case IdentifierSyntax identifier:
                address = VariableAddress(identifier.Identifier);
                return true;
            case MemberAccessSyntax memberAccess:
                if (TryRuntimeIndexedMemberAccess(memberAccess, out _, out _))
                {
                    address = 0;
                    return false;
                }

                address = VariableAddress(NesVideoProgram.MemberAccessName(memberAccess));
                return true;
            case IndexExpressionSyntax indexExpression when TryConst(indexExpression.Index, out _):
                address = VariableAddress(IndexedElementName(indexExpression.BaseIdentifier, indexExpression.Index, $"{indexExpression.BaseIdentifier} array index"));
                return true;
            default:
                address = 0;
                return false;
        }
    }

    private void EmitRuntimeIndexToX(string baseIdentifier, ExpressionSyntax index)
    {
        var elementSize = StorageSize(VariableStorageType(IndexedElementName(baseIdentifier, 0)));
        EmitExpressionToA(index);
        EmitMultiplyA(elementSize);
        builder.TransferAToX();
    }

    private void EmitRuntimeMemberIndexToX(IndexExpressionSyntax indexExpression)
    {
        var layout = StructArrayLayoutFor(indexExpression.BaseIdentifier);
        EmitExpressionToA(indexExpression.Index);
        EmitMultiplyA(layout.Stride);
        builder.TransferAToX();
    }

    private void EmitRuntimeMemberIndexToX(string baseIdentifier, SdkByteExpression index)
    {
        var layout = StructArrayLayoutFor(baseIdentifier);
        EmitSdkByteExpressionToA(index);
        EmitMultiplyA(layout.Stride);
        builder.TransferAToX();
    }

    private void EmitMultiplyA(int multiplier)
    {
        if (multiplier <= 1)
        {
            return;
        }

        builder.StoreAZeroPage(RuntimeIndexScratchAddress);
        for (var count = 1; count < multiplier; count++)
        {
            builder.ClearCarry();
            builder.AddZeroPage(RuntimeIndexScratchAddress);
        }
    }

    private byte ArrayBaseAddress(string baseIdentifier)
    {
        return VariableAddress(IndexedElementName(baseIdentifier, 0));
    }

    private byte RuntimeIndexedMemberBaseAddress(IndexExpressionSyntax indexExpression, string fieldName)
    {
        _ = StructArrayLayoutFor(indexExpression.BaseIdentifier).FieldOffsets[fieldName];
        return VariableAddress(IndexedMemberName(indexExpression.BaseIdentifier, 0, fieldName));
    }

    private byte RuntimeIndexedMemberBaseAddress(string baseIdentifier, string fieldName)
    {
        _ = StructArrayLayoutFor(baseIdentifier).FieldOffsets[fieldName];
        return VariableAddress(IndexedMemberName(baseIdentifier, 0, fieldName));
    }

    private bool TryRuntimeIndexedMemberAccess(MemberAccessSyntax memberAccess, out IndexExpressionSyntax indexExpression, out string fieldName)
    {
        if (memberAccess.Target is IndexExpressionSyntax candidate
            && !TryConst(candidate.Index, out _)
            && structArrays.ContainsKey(ScopedVariableName(candidate.BaseIdentifier))
            && StructArrayLayoutFor(candidate.BaseIdentifier).FieldOffsets.ContainsKey(memberAccess.Member))
        {
            indexExpression = candidate;
            fieldName = memberAccess.Member;
            return true;
        }

        indexExpression = null!;
        fieldName = string.Empty;
        return false;
    }

    private StructArrayLayout StructArrayLayoutFor(string baseIdentifier)
    {
        var scopedBaseIdentifier = ScopedVariableName(baseIdentifier);
        return structArrays.TryGetValue(scopedBaseIdentifier, out var layout)
            ? layout
            : throw new InvalidOperationException($"NES target has no struct array layout for '{baseIdentifier}'.");
    }

    private void RequireExpressionPreservesX(ExpressionSyntax expression, string context)
    {
        if (!PreservesX(expression))
        {
            throw new InvalidOperationException($"NES target cannot use expression '{expression.GetType().Name}' as the right side of a {context} yet because it also needs X for array indexing.");
        }
    }

    private bool PreservesX(ExpressionSyntax expression)
    {
        if (TryConst(expression, out _))
        {
            return true;
        }

        return expression switch
        {
            IdentifierSyntax => true,
            MemberAccessSyntax memberAccess => !TryRuntimeIndexedMemberAccess(memberAccess, out _, out _),
            IndexExpressionSyntax indexExpression => TryConst(indexExpression.Index, out _),
            CastSyntax cast => PreservesX(cast.Expression),
            ConditionalExpressionSyntax conditional => PreservesX(conditional.Condition) && PreservesX(conditional.WhenTrue) && PreservesX(conditional.WhenFalse),
            BinaryExpressionSyntax binary => PreservesX(binary.Left) && PreservesX(binary.Right),
            _ => false,
        };
    }

    private void EmitValueCallToA(FunctionCall call)
    {
        switch (call.Name)
        {
            case "button_down":
                EmitButtonDown(call);
                break;
            case "button_just_pressed":
                EmitButtonJustPressed(call);
                break;
            case "button_just_released":
                EmitButtonJustReleased(call);
                break;
            case "button_hold_ticks":
                EmitButtonHoldTicks(call);
                break;
            case "camera_aabb_tiles":
                EmitSdkOperation(Sdk2DOperationCollector.ReadCameraAabbTiles(call));
                break;
            case "camera_aabb_hit_top":
                EmitSdkOperation(Sdk2DOperationCollector.ReadCameraAabbHitTop(call));
                break;
            case "camera_screen_aabb_tiles":
                EmitSdkOperation(Sdk2DOperationCollector.ReadCameraScreenAabbTiles(call));
                break;
            case "camera_screen_aabb_hit_top":
                EmitSdkOperation(Sdk2DOperationCollector.ReadCameraScreenAabbHitTop(call));
                break;
            case "animation_frame":
                EmitAnimationFrame(call);
                break;
            case "sprite_width":
                EmitSpriteWidth(call);
                break;
            case "__rs_actor_camera_x_lo":
                NesVideoProgram.RequireArity(call, 0);
                builder.LoadAZeroPage(CameraXAddress);
                break;
            case "__rs_actor_camera_x_hi":
                NesVideoProgram.RequireArity(call, 0);
                builder.LoadAZeroPage(CameraTileColumnAddress);
                builder.ShiftRightA();
                builder.ShiftRightA();
                builder.ShiftRightA();
                builder.ShiftRightA();
                builder.ShiftRightA();
                break;
            case "__rs_actor_camera_y_lo":
                NesVideoProgram.RequireArity(call, 0);
                if (useFourScreenNametables)
                {
                    builder.LoadAZeroPage(CameraYAddress);
                }
                else
                {
                    builder.LoadAImmediate(0);
                }

                break;
            case "__rs_actor_camera_y_hi":
                NesVideoProgram.RequireArity(call, 0);
                if (useFourScreenNametables)
                {
                    builder.LoadAZeroPage(CameraTileRowAddress);
                    builder.ShiftRightA();
                    builder.ShiftRightA();
                    builder.ShiftRightA();
                    builder.ShiftRightA();
                    builder.ShiftRightA();
                }
                else
                {
                    builder.LoadAImmediate(0);
                }

                break;
            default:
                if (TryEmitTargetValueIntrinsic(call))
                {
                    break;
                }

                if (TryEmitUserValueFunction(call))
                {
                    break;
                }

                throw new InvalidOperationException($"Unsupported NES value API call '{call.Name}'.");
        }
    }

    private bool TryEmitTargetValueIntrinsic(FunctionCall call)
    {
        if (!program.Functions.TryGetValue(call.Name, out var function) || !function.IsExtern)
        {
            return false;
        }

        var intrinsic = TargetIntrinsicResolver.Resolve(function, NesTarget.Intrinsics);
        switch (intrinsic.Operation)
        {
            case TargetIntrinsicOperation.CameraAabbTiles:
                NesVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitSdkOperation(Sdk2DOperationCollector.ReadCameraAabbTiles(
                    TargetIntrinsicResolver.ResolveCall(function, call, NesTarget.Intrinsics),
                    program.Functions,
                    NesTarget.Intrinsics));
                return true;
            case TargetIntrinsicOperation.CameraAabbHitTop:
                NesVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitSdkOperation(Sdk2DOperationCollector.ReadCameraAabbHitTop(
                    TargetIntrinsicResolver.ResolveCall(function, call, NesTarget.Intrinsics),
                    program.Functions,
                    NesTarget.Intrinsics));
                return true;
            case TargetIntrinsicOperation.CameraScreenAabbTiles:
                NesVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitSdkOperation(Sdk2DOperationCollector.ReadCameraScreenAabbTiles(
                    TargetIntrinsicResolver.ResolveCall(function, call, NesTarget.Intrinsics),
                    program.Functions,
                    NesTarget.Intrinsics));
                return true;
            case TargetIntrinsicOperation.CameraScreenAabbHitTop:
                NesVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitSdkOperation(Sdk2DOperationCollector.ReadCameraScreenAabbHitTop(
                    TargetIntrinsicResolver.ResolveCall(function, call, NesTarget.Intrinsics),
                    program.Functions,
                    NesTarget.Intrinsics));
                return true;
            case TargetIntrinsicOperation.ButtonDown:
                NesVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitButtonDown(call);
                return true;
            case TargetIntrinsicOperation.ButtonJustPressed:
                NesVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitButtonJustPressed(call);
                return true;
            case TargetIntrinsicOperation.ButtonJustReleased:
                NesVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitButtonJustReleased(call);
                return true;
            case TargetIntrinsicOperation.ButtonHoldTicks:
                NesVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitButtonHoldTicks(call);
                return true;
            case TargetIntrinsicOperation.ReadSpriteWidth:
                NesVideoProgram.RequireArity(call, intrinsic.Arity);
                _ = TargetIntrinsicResolver.ResolveCall(function, call, NesTarget.Intrinsics);
                EmitSpriteWidth(call);
                return true;
            case TargetIntrinsicOperation.ReadAnimationFrame:
                NesVideoProgram.RequireArity(call, intrinsic.Arity);
                _ = TargetIntrinsicResolver.ResolveCall(function, call, NesTarget.Intrinsics);
                EmitAnimationFrame(call);
                return true;
            default:
                return false;
        }
    }

    private void EmitButtonDown(FunctionCall call)
    {
        NesVideoProgram.RequireArity(call, 1);
        EmitButtonMaskToBool(InputCurrentAddress, ButtonArg(call, "button_down argument 1"));
    }

    private void EmitButtonJustPressed(FunctionCall call)
    {
        NesVideoProgram.RequireArity(call, 1);
        var button = ButtonArg(call, "button_just_pressed argument 1");
        var falseLabel = builder.CreateLabel("button_just_pressed_false");
        var endLabel = builder.CreateLabel("button_just_pressed_end");

        builder.LoadAZeroPage(InputCurrentAddress);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, falseLabel);   // BEQ falseLabel

        builder.LoadAZeroPage(InputPreviousAddress);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xD0, falseLabel);   // BNE falseLabel

        builder.LoadAImmediate(1);
        builder.JumpAbsolute(endLabel);
        builder.Label(falseLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private void EmitButtonJustReleased(FunctionCall call)
    {
        NesVideoProgram.RequireArity(call, 1);
        var button = ButtonArg(call, "button_just_released argument 1");
        var falseLabel = builder.CreateLabel("button_just_released_false");
        var endLabel = builder.CreateLabel("button_just_released_end");

        builder.LoadAZeroPage(InputCurrentAddress);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xD0, falseLabel);   // BNE falseLabel

        builder.LoadAZeroPage(InputPreviousAddress);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, falseLabel);   // BEQ falseLabel

        builder.LoadAImmediate(1);
        builder.JumpAbsolute(endLabel);
        builder.Label(falseLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private void EmitButtonHoldTicks(FunctionCall call)
    {
        NesVideoProgram.RequireArity(call, 1);
        builder.LoadAZeroPage(ButtonArg(call, "button_hold_ticks argument 1").HoldTicksAddress);
    }

    private void EmitButtonMaskToBool(byte address, NesButton button)
    {
        var pressedLabel = builder.CreateLabel("button_down");
        var endLabel = builder.CreateLabel("button_down_end");

        builder.LoadAZeroPage(address);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xD0, pressedLabel); // BNE pressedLabel
        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(pressedLabel);
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
    }

    private NesButton ButtonArg(FunctionCall call, string context)
    {
        var argument = call.Parameters.ElementAt(0);

        // A `Button` enum member (e.g. Button.A) is constant-folded to its ordinal,
        // which matches the canonical Buttons order, so resolve it by index. The bare
        // lowercase identifier form (e.g. `a`) is kept as a transitional alias.
        if (argument is ConstantSyntax)
        {
            var ordinal = NesVideoProgram.ConstValue(argument, context);
            if (ordinal < 0 || ordinal >= Buttons.Length)
            {
                throw new InvalidOperationException($"Unsupported NES button ordinal '{ordinal}'.");
            }

            return Buttons[ordinal];
        }

        var name = argument is MemberAccessSyntax memberAccess
            ? memberAccess.Member.ToLowerInvariant()
            : NesVideoProgram.IdentifierArg(argument, context);
        foreach (var button in Buttons)
        {
            if (button.Name == name)
            {
                return button;
            }
        }

        throw new InvalidOperationException($"Unsupported NES button '{name}'.");
    }

    private bool TryConst(ExpressionSyntax expression, out int value)
    {
        if (expression is CastSyntax cast)
        {
            RequireSupportedCastTarget(cast);
            return TryConst(cast.Expression, out value);
        }

        if (expression is ConstantSyntax)
        {
            value = NesVideoProgram.ConstValue(expression, "constant");
            return true;
        }

        if (expression is IdentifierSyntax { Identifier: "true" })
        {
            value = 1;
            return true;
        }

        if (expression is IdentifierSyntax { Identifier: "false" })
        {
            value = 0;
            return true;
        }

        value = 0;
        return false;
    }

    private static string IndexedElementName(string baseIdentifier, int index)
    {
        return $"{baseIdentifier}[{index}]";
    }

    private static string IndexedMemberName(string baseIdentifier, int index, string fieldName)
    {
        return $"{IndexedElementName(baseIdentifier, index)}.{fieldName}";
    }

    private Dictionary<string, int> StructFieldOffsets(StructSyntax structSyntax)
    {
        var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
        var offset = 0;
        foreach (var field in structSyntax.Fields)
        {
            if (!IsScalarLocalType(field.Type))
            {
                throw new InvalidOperationException($"NES target struct array field type '{field.Type}' is not scalar.");
            }

            offsets.Add(field.Name, offset);
            offset += StorageSize(field.Type);
        }

        return offsets;
    }

    private int StructStride(StructSyntax structSyntax)
    {
        return structSyntax.Fields.Sum(field => StorageSize(field.Type));
    }

    private static string StorageKey(SdkStorageLocation location)
    {
        return location switch
        {
            SdkStorageLocation.Local local => local.Name,
            SdkStorageLocation.Field field => $"{StorageKey(field.Target)}.{field.FieldName}",
            SdkStorageLocation.IndexedElement indexed => IndexedElementName(indexed.BaseName, indexed.Index),
            SdkStorageLocation.RuntimeIndexedField => throw new InvalidOperationException("Runtime indexed SDK fields must be emitted directly."),
            _ => throw new InvalidOperationException($"Unsupported SDK storage location '{location.GetType().Name}'."),
        };
    }

    private static string IndexedElementName(string baseIdentifier, ExpressionSyntax index, string context)
    {
        var value = CheckedRange(NesVideoProgram.ConstValue(index, context), 0, 255, context);
        return IndexedElementName(baseIdentifier, value);
    }

    private static int CheckedRange(int value, int min, int max, string context)
    {
        if (value < min || value > max)
        {
            throw new InvalidOperationException($"{context} must be between {min} and {max}.");
        }

        return value;
    }

    private byte VariableAddress(string name)
    {
        var scopedName = ScopedVariableName(name);
        if (!variables.TryGetValue(scopedName, out var address))
        {
            throw new InvalidOperationException($"Use of undeclared variable '{name}'.");
        }

        return address;
    }

    private readonly record struct NesButton(string Name, byte SnapshotMask, byte HoldTicksAddress);

    private sealed class InlineVariableScope
    {
        public InlineVariableScope(string prefix)
        {
            Prefix = prefix;
        }

        public string Prefix { get; }
        public Dictionary<string, string> Names { get; } = new(StringComparer.Ordinal);
    }

    private readonly record struct NesCameraConfig(
        int MapWidth,
        int MapHeight,
        int StreamY,
        int StreamHeight,
        bool UseFourScreenNametables);

    private readonly record struct LoopTarget(string BreakLabel, string ContinueLabel);
}

internal sealed class PrgBuilder
{
    public const ushort BaseAddress = 0x8000;
    private readonly List<byte> bytes = [];
    private readonly Dictionary<string, int> labels = [];
    private readonly List<(int Offset, string Label, int Addend)> absoluteFixups = [];
    private readonly List<(int Offset, string Label, int Addend, bool High)> byteFixups = [];
    private readonly List<(int Offset, string Label)> relativeFixups = [];
    private int nextLabelId;

    public int CurrentAddress => BaseAddress + bytes.Count;

    public void Label(string name) => labels[name] = bytes.Count;

    public string CreateLabel(string prefix) => $"{prefix}_{nextLabelId++}";

    public void Emit(params byte[] values) => bytes.AddRange(values);

    public void PadToAddress(ushort address)
    {
        if (address < BaseAddress)
        {
            throw new InvalidOperationException($"NES PRG address ${address:X4} is below PRG ROM base ${BaseAddress:X4}.");
        }

        var targetOffset = address - BaseAddress;
        if (targetOffset < bytes.Count)
        {
            throw new InvalidOperationException($"NES PRG address ${address:X4} has already been emitted.");
        }

        while (bytes.Count < targetOffset)
        {
            bytes.Add(0);
        }
    }

    public void EmitLabelLowByte(string label, int addend = 0)
    {
        Emit(0x00);
        byteFixups.Add((bytes.Count - 1, label, addend, High: false));
    }

    public void EmitLabelHighByte(string label, int addend = 0)
    {
        Emit(0x00);
        byteFixups.Add((bytes.Count - 1, label, addend, High: true));
    }

    public void LoadAImmediate(int value) => Emit(0xA9, CheckedByte(value));

    public void LoadAImmediateLabelLowByte(string label, int addend = 0)
    {
        Emit(0xA9);
        EmitLabelLowByte(label, addend);
    }

    public void LoadAImmediateLabelHighByte(string label, int addend = 0)
    {
        Emit(0xA9);
        EmitLabelHighByte(label, addend);
    }

    public void LoadXImmediate(int value) => Emit(0xA2, CheckedByte(value));

    public void LoadYImmediate(int value) => Emit(0xA0, CheckedByte(value));

    public void LoadXZeroPage(byte address) => Emit(0xA6, address);

    public void LoadXAbsolute(ushort address) => Emit(0xAE, Low(address), High(address));

    public void LoadYZeroPage(byte address) => Emit(0xA4, address);

    public void LoadAZeroPage(byte address) => Emit(0xA5, address);

    public void LoadAZeroPageX(byte address) => Emit(0xB5, address);

    public void StoreAZeroPage(byte address) => Emit(0x85, address);

    public void StoreAZeroPageX(byte address) => Emit(0x95, address);

    public void StoreYZeroPage(byte address) => Emit(0x84, address);

    public void LoadAAbsolute(ushort address) => Emit(0xAD, Low(address), High(address));

    public void StoreAAbsolute(ushort address) => Emit(0x8D, Low(address), High(address));

    public void StoreAAbsoluteX(ushort address) => Emit(0x9D, Low(address), High(address));

    public void StoreAIndirectY(byte address) => Emit(0x91, address);

    public void StoreYAbsolute(ushort address) => Emit(0x8C, Low(address), High(address));

    public void LoadYAbsolute(ushort address) => Emit(0xAC, Low(address), High(address));

    public void AndImmediate(int value) => Emit(0x29, CheckedByte(value));

    public void AndZeroPage(byte address) => Emit(0x25, address);

    public void OrImmediate(int value) => Emit(0x09, CheckedByte(value));

    public void OrZeroPage(byte address) => Emit(0x05, address);

    public void XorImmediate(int value) => Emit(0x49, CheckedByte(value));

    public void XorZeroPage(byte address) => Emit(0x45, address);

    public void CompareImmediate(int value) => Emit(0xC9, CheckedByte(value));

    public void CompareZeroPage(byte address) => Emit(0xC5, address);

    public void ClearCarry() => Emit(0x18);

    public void SetCarry() => Emit(0x38);

    public void AddImmediate(int value) => Emit(0x69, CheckedByte(value));

    public void AddZeroPage(byte address) => Emit(0x65, address);

    public void AddZeroPageX(byte address) => Emit(0x75, address);

    public void SubtractImmediate(int value) => Emit(0xE9, CheckedByte(value));

    public void SubtractZeroPage(byte address) => Emit(0xE5, address);

    public void PushA() => Emit(0x48);

    public void PullA() => Emit(0x68);

    public void DecrementZeroPage(byte address) => Emit(0xC6, address);

    public void IncrementZeroPage(byte address) => Emit(0xE6, address);

    public void IncrementX() => Emit(0xE8);

    public void IncrementY() => Emit(0xC8);

    public void TransferAToX() => Emit(0xAA);

    public void TransferYToA() => Emit(0x98);

    public void CompareXImmediate(int value) => Emit(0xE0, CheckedByte(value));

    public void Return() => Emit(0x60);

    public void ShiftLeftA() => Emit(0x0A);

    public void ShiftRightA() => Emit(0x4A);

    public void LdaAbsoluteX(string label, int addend = 0)
    {
        Emit(0xBD, 0x00, 0x00);
        absoluteFixups.Add((bytes.Count - 2, label, addend));
    }

    public void LoadAIndirectY(byte address) => Emit(0xB1, address);

    public void JumpAbsolute(string label)
    {
        Emit(0x4C, 0x00, 0x00);
        absoluteFixups.Add((bytes.Count - 2, label, 0));
    }

    public void CallSubroutine(string label)
    {
        Emit(0x20, 0x00, 0x00);
        absoluteFixups.Add((bytes.Count - 2, label, 0));
    }

    public void BranchRelative(byte opcode, string label)
    {
        Emit(opcode, 0x00);
        relativeFixups.Add((bytes.Count - 1, label));
    }

    public byte[] Build()
    {
        foreach (var fixup in byteFixups)
        {
            var address = AddressOf(fixup.Label, fixup.Addend);
            bytes[fixup.Offset] = (byte)(fixup.High ? address >> 8 : address & 0xFF);
        }

        foreach (var fixup in absoluteFixups)
        {
            var address = AddressOf(fixup.Label, fixup.Addend);
            bytes[fixup.Offset] = (byte)(address & 0xFF);
            bytes[fixup.Offset + 1] = (byte)(address >> 8);
        }

        foreach (var fixup in relativeFixups)
        {
            var target = AddressOf(fixup.Label);
            var branchFrom = BaseAddress + fixup.Offset + 1;
            var delta = target - branchFrom;
            if (delta is < -128 or > 127)
            {
                throw new BranchOutOfRangeException(fixup.Label, delta);
            }

            bytes[fixup.Offset] = unchecked((byte)(sbyte)delta);
        }

        return bytes.ToArray();
    }

    private static byte CheckedByte(int value)
    {
        if (value is < -128 or > 255)
        {
            throw new InvalidOperationException($"NES byte immediate must be between -128 and 255, got {value}.");
        }

        return (byte)value;
    }

    private static byte Low(ushort value) => (byte)(value & 0xFF);

    private static byte High(ushort value) => (byte)(value >> 8);

    private int AddressOf(string label, int addend = 0)
    {
        if (!labels.TryGetValue(label, out var offset))
        {
            throw new InvalidOperationException($"Unknown NES PRG label '{label}'.");
        }

        return BaseAddress + offset + addend;
    }
}

internal sealed class BranchOutOfRangeException(string label, int delta)
    : InvalidOperationException($"Branch to '{label}' is out of range.")
{
    public string Label { get; } = label;

    public int Delta { get; } = delta;
}
