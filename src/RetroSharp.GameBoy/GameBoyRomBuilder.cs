using System.Globalization;
using System.Text;
using RetroSharp.Core;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;
using RetroSharp.Sdk;

namespace RetroSharp.GameBoy;

internal static class GameBoyRomBuilder
{
    private const int RomOnlySize = 32 * 1024;
    private const int BankSize = 16 * 1024;
    private const int FixedBankProgramStart = 0x0150;
    private const int BankedWindowStart = 0x4000;
    private const int BankedWindowEnd = 0x8000;
    private const int RomOnlyPayloadLimit = RomOnlySize - FixedBankProgramStart;
    private const int FixedBankPayloadLimit = BankSize - FixedBankProgramStart;
    internal const ushort RomBankSelectAddress = 0x2000;
    internal const ushort ProgramCurrentBankAddress = 0xC11C;
    private const byte CartridgeTypeRomOnly = 0x00;
    private const byte CartridgeTypeMbc1 = 0x01;
    private const string TileDataLabel = "tile_data";
    private const string TileMapLabel = "tilemap";
    private const string WindowTileMapLabel = "window_tilemap";
    internal const string MapDataLabel = "map_data";
    internal const string MapFlagDataLabel = "map_flags_data";
    private const string ProgramStartLabel = "program_start";
    private const string ProgramMainEndLabel = "program_main_end";
    internal const string ProgramBankContinuationLabel = "program_bank_continue";

    private static readonly byte[] NintendoLogo =
    [
        0xCE, 0xED, 0x66, 0x66, 0xCC, 0x0D, 0x00, 0x0B,
        0x03, 0x73, 0x00, 0x83, 0x00, 0x0C, 0x00, 0x0D,
        0x00, 0x08, 0x11, 0x1F, 0x88, 0x89, 0x00, 0x0E,
        0xDC, 0xCC, 0x6E, 0xE6, 0xDD, 0xDD, 0xD9, 0x99,
        0xBB, 0xBB, 0x67, 0x63, 0x6E, 0x0E, 0xEC, 0xCC,
        0xDD, 0xDC, 0x99, 0x9F, 0xBB, 0xB9, 0x33, 0x3E,
    ];

    public static byte[] Build(GameBoyVideoProgram program)
    {
        var readOnlyData = BuildReadOnlyData(program);
        var romOnlyLayout = GameBoyRomLayout.RomOnly;
        var builder = BuildProgram(program, romOnlyLayout, readOnlyData);
        var programBytes = builder.Build();
        if (programBytes.Length <= RomOnlyPayloadLimit)
        {
            var rom = new byte[RomOnlySize];
            WriteHeaderSkeleton(rom, CartridgeTypeRomOnly, romSizeCode: 0x00);
            programBytes.CopyTo(rom, FixedBankProgramStart);
            WriteHeaderChecksums(rom);
            return rom;
        }

        var bankedLayout = GameBoyRomLayout.CreateBankedMusicLayout(program, readOnlyData);
        builder = BuildProgram(program, bankedLayout, readOnlyData);
        programBytes = builder.Build();
        var programTailBanks = CalculateProgramTailBanks(programBytes.Length);
        if (programTailBanks > 1)
        {
            bankedLayout = GameBoyRomLayout.CreateBankedMusicLayout(program, readOnlyData, bankReadOnlyData: true);
            builder = BuildProgram(program, bankedLayout, readOnlyData);
            programBytes = builder.Build();
            programTailBanks = CalculateProgramTailBanks(programBytes.Length);
        }

        EnsureSupportedProgramTailBanks(programTailBanks, bankedLayout.UsesBankedReadOnlyData);
        if (programTailBanks != bankedLayout.ProgramTailBankCount)
        {
            bankedLayout = GameBoyRomLayout.CreateBankedMusicLayout(
                program,
                readOnlyData,
                programTailBanks,
                bankedLayout.UsesBankedReadOnlyData);
            builder = BuildProgram(program, bankedLayout, readOnlyData);
            programBytes = builder.Build();
            programTailBanks = CalculateProgramTailBanks(programBytes.Length);
            EnsureSupportedProgramTailBanks(programTailBanks, bankedLayout.UsesBankedReadOnlyData);
        }

        if (programBytes.Length > FixedBankPayloadLimit + (programTailBanks * BankSize))
        {
            throw new InvalidOperationException(
                $"Generated Game Boy banked program is {programBytes.Length} bytes, but the current contiguous MBC1 program layout fits {FixedBankPayloadLimit + (programTailBanks * BankSize)} bytes.");
        }

        ValidateBankedProgramShape(builder, programTailBanks);

        var bankedRom = new byte[bankedLayout.RomSize];
        WriteHeaderSkeleton(bankedRom, CartridgeTypeMbc1, bankedLayout.RomSizeCode);
        CopyProgramToBankedRom(programBytes, bankedRom, programTailBanks);

        foreach (var placement in bankedLayout.ReadOnlyDataPlacements.Values)
        {
            var bankOffset = placement.Bank * BankSize;
            var windowOffset = placement.Address - BankedWindowStart;
            placement.Data.CopyTo(bankedRom.AsSpan(bankOffset + windowOffset, placement.Data.Length));
        }

        foreach (var placement in bankedLayout.MusicPlacements.Values)
        {
            CopyBankedAudioData(bankedRom, placement);
        }

        foreach (var placement in bankedLayout.SoundEffectPlacements.Values)
        {
            CopyBankedAudioData(bankedRom, placement);
        }

        WriteHeaderChecksums(bankedRom);
        return bankedRom;
    }

    private static void CopyBankedAudioData(byte[] bankedRom, GameBoyBankedAudioPlacement placement)
    {
        var sourceOffset = 0;
        var bank = placement.Bank;
        while (sourceOffset < placement.Data.Length)
        {
            var chunkLength = Math.Min(BankSize, placement.Data.Length - sourceOffset);
            placement.Data.AsSpan(sourceOffset, chunkLength).CopyTo(bankedRom.AsSpan(bank * BankSize, chunkLength));
            sourceOffset += chunkLength;
            bank++;
        }
    }

    private static int CalculateProgramTailBanks(int programLength)
    {
        if (programLength <= FixedBankPayloadLimit)
        {
            return 0;
        }

        return (programLength - FixedBankPayloadLimit + BankSize - 1) / BankSize;
    }

    private static void EnsureSupportedProgramTailBanks(int programTailBanks, bool bankedReadOnlyData)
    {
        if (programTailBanks <= GameBoyRomLayout.MaxProgramTailBankCount)
        {
            return;
        }

        var kind = bankedReadOnlyData ? "code" : "code/data";
        throw new InvalidOperationException(
            $"Generated Game Boy program needs {programTailBanks} switchable {kind} banks, but the current MBC1 foundation supports up to {GameBoyRomLayout.MaxProgramTailBankCount} switchable program banks.");
    }

    private static void CopyProgramToBankedRom(byte[] programBytes, byte[] bankedRom, int programTailBanks)
    {
        var fixedLength = Math.Min(programBytes.Length, FixedBankPayloadLimit);
        programBytes.AsSpan(0, fixedLength).CopyTo(bankedRom.AsSpan(FixedBankProgramStart, fixedLength));

        var sourceOffset = fixedLength;
        for (var bank = 1; bank <= programTailBanks && sourceOffset < programBytes.Length; bank++)
        {
            var chunkLength = Math.Min(BankSize, programBytes.Length - sourceOffset);
            programBytes.AsSpan(sourceOffset, chunkLength).CopyTo(bankedRom.AsSpan(bank * BankSize, chunkLength));
            sourceOffset += chunkLength;
        }
    }

    private static void ValidateBankedProgramShape(GbBuilder builder, int programTailBanks)
    {
        if (programTailBanks == 0)
        {
            return;
        }

        if (builder.TryLabelOffset(ProgramStartLabel, out var programStartOffset) && programStartOffset > FixedBankPayloadLimit)
        {
            throw new InvalidOperationException(
                $"Generated Game Boy fixed-bank helpers are {programStartOffset} bytes, but only {FixedBankPayloadLimit} bytes fit in bank 0 before the switchable window.");
        }

        builder.LabelOffset(ProgramMainEndLabel);
    }

    private static GbBuilder BuildProgram(
        GameBoyVideoProgram program,
        GameBoyRomLayout layout,
        IReadOnlyList<GameBoyReadOnlyDataBlob> readOnlyData)
    {
        var builder = new GbBuilder();
        foreach (var placement in layout.ReadOnlyDataPlacements.Values)
        {
            builder.ExternalLabel(placement.Label, placement.Address);
        }

        if (layout.ProgramTailBankCount > 0)
        {
            builder.EnableBankedAddressing(FixedBankPayloadLimit, BankSize);
        }

        var readOnlyDataByLabel = readOnlyData.ToDictionary(data => data.Label);
        var tileData = readOnlyDataByLabel[TileDataLabel].Data;

        builder.Emit(0xF3);                         // DI
        builder.Emit(0x31, 0xFE, 0xFF);             // LD SP,$FFFE
        builder.Emit(0xAF);                         // XOR A
        builder.Emit(0xE0, 0xFF);                   // LDH ($FF),A

        EmitWaitVBlank(builder, "startup_wait_vblank");

        builder.Emit(0xAF);                         // XOR A
        builder.Emit(0xE0, 0x40);                   // LDH ($40),A
        builder.Emit(0x3E, program.BackgroundPalette);
        builder.Emit(0xE0, 0x47);                   // LDH ($47),A
        builder.Emit(0x3E, program.ObjectPalette);
        builder.Emit(0xE0, 0x48);                   // LDH ($48),A
        builder.Emit(0x3E, program.ObjectPalette1);
        builder.Emit(0xE0, 0x49);                   // LDH ($49),A
        builder.Emit(0xAF);                         // XOR A
        builder.Emit(0xE0, 0x42);                   // LDH ($42),A
        builder.Emit(0xE0, 0x43);                   // LDH ($43),A
        if (program.UsesWindowHud)
        {
            builder.LoadAImmediate(0);
            builder.StoreHighRamA(0x4A);            // WY=0
            builder.LoadAImmediate(7);
            builder.StoreHighRamA(0x4B);            // WX=7 maps to screen X=0
        }

        EmitStartupCopy(builder, layout, 0x8000, TileDataLabel, tileData.Length, "copy_tiles");
        EmitStartupCopy(builder, layout, 0x9800, TileMapLabel, 1024, "copy_tilemap");

        if (program.UsesWindowHud)
        {
            EmitStartupCopy(builder, layout, 0x9C00, WindowTileMapLabel, 1024, "copy_window_tilemap");
        }

        EmitClearOam(builder, "clear_oam");

        builder.Emit(0x3E, (byte)(program.UsesWindowHud ? 0xF7 : 0x97));
        builder.Emit(0xE0, 0x40);                   // LDH ($40),A

        var runtime = new GameBoyRuntimeCompiler(builder, program, layout);
        runtime.EmitProgramBankInitialization();
        var usesBankContinuationHelper = layout.ProgramTailBankCount > 0;
        if (usesBankContinuationHelper || runtime.UsesReadOnlyDataHelpers || runtime.UsesAudioHelpers || runtime.UsesSubroutineTrampolines)
        {
            builder.JumpAbsolute(ProgramStartLabel);
            if (usesBankContinuationHelper)
            {
                EmitProgramBankContinuationHelper(builder);
            }

            runtime.EmitReadOnlyDataHelpers();
            runtime.EmitAudioHelpers();
            runtime.EmitSubroutineTrampolines();
            builder.Label(ProgramStartLabel);
        }

        runtime.EmitMain(program.MainBlock);

        builder.Label("forever");
        builder.JumpRelative(0x18, "forever");     // JR forever
        builder.Label(ProgramMainEndLabel);
        runtime.EmitSubroutines();
        runtime.EnsureAllStreamsConsumed();
        builder.DisableBankContinuationStubs();

        if (!layout.UsesBankedReadOnlyData)
        {
            builder.Label(TileDataLabel);
            builder.Emit(tileData);
            builder.Label(TileMapLabel);
            builder.Emit(program.TileMap);
            if (program.UsesWindowHud)
            {
                builder.Label(WindowTileMapLabel);
                builder.Emit(program.WindowTileMap);
            }
        }

        EmitMapData(builder, program);
        if (!layout.UsesBankedMusic)
        {
            EmitAudioData(builder, program);
        }

        return builder;
    }

    private static void EmitProgramBankContinuationHelper(GbBuilder builder)
    {
        builder.Label(ProgramBankContinuationLabel);
        builder.StoreA(ProgramCurrentBankAddress);
        builder.StoreA(RomBankSelectAddress);
        builder.Emit(0xC3, (byte)(BankedWindowStart & 0xFF), (byte)(BankedWindowStart >> 8)); // JP $4000
    }

    private static IReadOnlyList<GameBoyReadOnlyDataBlob> BuildReadOnlyData(GameBoyVideoProgram program)
    {
        var data = new List<GameBoyReadOnlyDataBlob>
        {
            new(TileDataLabel, BuildTileData(program)),
            new(TileMapLabel, program.TileMap),
        };

        if (program.UsesWindowHud)
        {
            data.Add(new GameBoyReadOnlyDataBlob(WindowTileMapLabel, program.WindowTileMap));
        }

        AddMapReadOnlyData(data, program);
        return data;
    }

    private static void AddMapReadOnlyData(List<GameBoyReadOnlyDataBlob> data, GameBoyVideoProgram program)
    {
        if (program.MapColumnHeight == 0)
        {
            return;
        }

        var columnCount = program.MapColumns.Keys.Max() + 1;
        data.Add(new GameBoyReadOnlyDataBlob(
            MapDataLabel,
            BuildMapRows(program.MapColumns, columnCount, program.MapColumnHeight)));

        for (var row = 0; row < program.MapColumnHeight; row++)
        {
            data.Add(new GameBoyReadOnlyDataBlob(MapRowLabel(row), BuildMapRow(program.MapColumns, columnCount, row)));
        }

        for (var row = 0; row < program.BackgroundStreamHeight; row++)
        {
            data.Add(new GameBoyReadOnlyDataBlob(BackgroundRowLabel(row), BuildMapRow(program.BackgroundColumns, columnCount, row)));
        }

        if (program.MapFlagColumnHeight == 0)
        {
            return;
        }

        var flagColumnCount = program.MapFlagColumns.Keys.Max() + 1;
        data.Add(new GameBoyReadOnlyDataBlob(
            MapFlagDataLabel,
            BuildMapRows(program.MapFlagColumns, flagColumnCount, program.MapFlagColumnHeight)));
    }

    private static byte[] BuildMapRow(SortedDictionary<int, byte[]> columns, int columnCount, int row)
    {
        var result = new byte[columnCount];
        for (var column = 0; column < columnCount; column++)
        {
            result[column] = columns.TryGetValue(column, out var tiles) ? tiles[row] : (byte)0;
        }

        return result;
    }

    private static byte[] BuildMapRows(SortedDictionary<int, byte[]> columns, int columnCount, int rowCount)
    {
        var result = new byte[checked(columnCount * rowCount)];
        for (var row = 0; row < rowCount; row++)
        {
            for (var column = 0; column < columnCount; column++)
            {
                result[row * columnCount + column] = columns.TryGetValue(column, out var tiles) ? tiles[row] : (byte)0;
            }
        }

        return result;
    }

    private static void EmitStartupCopy(
        GbBuilder builder,
        GameBoyRomLayout layout,
        ushort destination,
        string sourceLabel,
        int length,
        string copyLabel)
    {
        if (length is < 0 or > ushort.MaxValue)
        {
            throw new InvalidOperationException($"Game Boy startup data '{sourceLabel}' is too large to copy in one block.");
        }

        if (layout.TryReadOnlyDataPlacement(sourceLabel, out var placement))
        {
            EmitSelectRomBank(builder, placement.Bank);
            builder.LoadDe(placement.Address);
        }
        else
        {
            builder.LoadDe(sourceLabel);
        }

        builder.LoadHl(destination);
        builder.LoadBc((ushort)length);
        EmitCopyLoop(builder, copyLabel);
    }

    private static void EmitSelectRomBank(GbBuilder builder, byte bank)
    {
        builder.LoadAImmediate(bank);
        builder.StoreA(RomBankSelectAddress);
    }

    private static void EmitMapData(GbBuilder builder, GameBoyVideoProgram program)
    {
        if (program.MapColumnHeight == 0)
        {
            return;
        }

        var columnCount = program.MapColumns.Keys.Max() + 1;
        builder.Label(MapDataLabel);
        for (var row = 0; row < program.MapColumnHeight; row++)
        {
            builder.Label(MapRowLabel(row));
            for (var column = 0; column < columnCount; column++)
            {
                var tile = program.MapColumns.TryGetValue(column, out var tiles) ? tiles[row] : (byte)0;
                builder.Emit(tile);
            }
        }

        EmitMapRowPointerTables(builder, program.MapColumnHeight);

        for (var row = 0; row < program.BackgroundStreamHeight; row++)
        {
            builder.Label(BackgroundRowLabel(row));
            for (var column = 0; column < columnCount; column++)
            {
                var tile = program.BackgroundColumns.TryGetValue(column, out var tiles) ? tiles[row] : (byte)0;
                builder.Emit(tile);
            }
        }

        if (program.MapFlagColumnHeight == 0)
        {
            return;
        }

        var flagColumnCount = program.MapFlagColumns.Keys.Max() + 1;
        builder.Label(MapFlagDataLabel);
        builder.Emit(BuildMapRows(program.MapFlagColumns, flagColumnCount, program.MapFlagColumnHeight));
    }

    private static void EmitMapRowPointerTables(GbBuilder builder, int rowCount)
    {
        builder.Label(MapRowPointerLowLabel);
        for (var row = 0; row < rowCount; row++)
        {
            builder.EmitLabelLowByte(MapRowLabel(row));
        }

        builder.Label(MapRowPointerHighLabel);
        for (var row = 0; row < rowCount; row++)
        {
            builder.EmitLabelHighByte(MapRowLabel(row));
        }
    }

    internal static string MapRowLabel(int row) => $"map_row_{row}";

    internal static string BackgroundRowLabel(int row) => $"background_stream_row_{row}";

    internal const string MapRowPointerLowLabel = "map_row_ptr_lo";

    internal const string MapRowPointerHighLabel = "map_row_ptr_hi";

    internal static string MusicLabel(string name) => $"music_{name}";

    internal static string SoundEffectLabel(string name) => $"sfx_{name}";

    private static void EmitAudioData(GbBuilder builder, GameBoyVideoProgram program)
    {
        foreach (var asset in program.MusicAssetsInLoadOrder)
        {
            builder.Label(MusicLabel(asset.Name));
            builder.Emit(asset.Data);
        }

        foreach (var asset in program.SoundEffectAssetsInLoadOrder)
        {
            builder.Label(SoundEffectLabel(asset.Name));
            builder.Emit(asset.Data);
        }
    }

    internal static void EmitWaitVBlank(GbBuilder builder, string label)
    {
        builder.Label($"{label}_wait_visible");
        builder.Emit(0xF0, 0x44);                   // LDH A,($44)
        builder.Emit(0xFE, 0x90);                   // CP $90
        builder.JumpRelative(0x30, $"{label}_wait_visible"); // JR NC,label

        builder.Label($"{label}_wait_vblank");
        builder.Emit(0xF0, 0x44);                   // LDH A,($44)
        builder.Emit(0xFE, 0x90);                   // CP $90
        builder.JumpRelative(0x38, $"{label}_wait_vblank"); // JR C,label
    }

    private static void EmitCopyLoop(GbBuilder builder, string label)
    {
        builder.Label(label);
        builder.Emit(0x1A);                         // LD A,(DE)
        builder.Emit(0x22);                         // LD (HL+),A
        builder.Emit(0x13);                         // INC DE
        builder.Emit(0x0B);                         // DEC BC
        builder.Emit(0x78);                         // LD A,B
        builder.Emit(0xB1);                         // OR C
        builder.JumpRelative(0x20, label);          // JR NZ,label
    }

    internal static void EmitClearOam(GbBuilder builder, string label)
    {
        builder.LoadHl(0xFE00);
        builder.LoadBc(160);
        builder.Label(label);
        builder.Emit(0x36, 0x00);                   // LD (HL),$00
        builder.Emit(0x23);                         // INC HL
        builder.Emit(0x0B);                         // DEC BC
        builder.Emit(0x78);                         // LD A,B
        builder.Emit(0xB1);                         // OR C
        builder.JumpRelative(0x20, label);          // JR NZ,label
    }

    private static byte[] BuildTileData(GameBoyVideoProgram program)
    {
        var tiles = new byte[(program.FirstSpriteTile + program.SpriteTileCount) * 16];
        WriteCloudTile(tiles, 1);
        WriteHillTile(tiles, 2);
        WriteSpikeTile(tiles, 3);
        WriteGroundTopTile(tiles, 4);
        WriteBrickTile(tiles, 5);
        program.GeneratedBackgroundTileData.CopyTo(tiles, GameBoyVideoProgram.FirstGeneratedBackgroundTile * 16);

        foreach (var asset in program.SpriteAssetsInLoadOrder)
        {
            asset.TileData.CopyTo(tiles, asset.FirstTile * 16);
        }

        return tiles;
    }

    private static void WriteSolidTile(byte[] tiles, int tile, int color)
    {
        for (var row = 0; row < 8; row++)
        {
            WriteTileRow(tiles, tile, row, color, color, color, color, color, color, color, color);
        }
    }

    private static void WriteCheckerTile(byte[] tiles, int tile, int colorA, int colorB)
    {
        for (var row = 0; row < 8; row++)
        {
            var a = (row & 1) == 0 ? colorA : colorB;
            var b = (row & 1) == 0 ? colorB : colorA;
            WriteTileRow(tiles, tile, row, a, b, a, b, a, b, a, b);
        }
    }

    private static void WriteFrameTile(byte[] tiles, int tile, int color)
    {
        for (var row = 0; row < 8; row++)
        {
            if (row is 0 or 7)
            {
                WriteTileRow(tiles, tile, row, color, color, color, color, color, color, color, color);
            }
            else
            {
                WriteTileRow(tiles, tile, row, color, 0, 0, 0, 0, 0, 0, color);
            }
        }
    }

    private static void WriteCloudTile(byte[] tiles, int tile)
    {
        WriteTileRow(tiles, tile, 0, 0, 0, 0, 0, 1, 1, 0, 0);
        WriteTileRow(tiles, tile, 1, 0, 0, 1, 1, 1, 1, 1, 0);
        WriteTileRow(tiles, tile, 2, 0, 1, 1, 1, 1, 1, 1, 1);
        WriteTileRow(tiles, tile, 3, 1, 1, 1, 1, 1, 1, 1, 1);
        WriteTileRow(tiles, tile, 4, 0, 1, 1, 1, 1, 1, 1, 0);
        WriteTileRow(tiles, tile, 5, 0, 0, 1, 1, 1, 1, 0, 0);
        WriteTileRow(tiles, tile, 6, 0, 0, 0, 0, 0, 0, 0, 0);
        WriteTileRow(tiles, tile, 7, 0, 0, 0, 0, 0, 0, 0, 0);
    }

    private static void WriteHillTile(byte[] tiles, int tile)
    {
        WriteTileRow(tiles, tile, 0, 0, 0, 0, 0, 2, 2, 0, 0);
        WriteTileRow(tiles, tile, 1, 0, 0, 0, 2, 2, 2, 2, 0);
        WriteTileRow(tiles, tile, 2, 0, 0, 2, 2, 1, 2, 2, 2);
        WriteTileRow(tiles, tile, 3, 0, 2, 2, 1, 2, 2, 1, 2);
        WriteTileRow(tiles, tile, 4, 2, 2, 2, 2, 2, 1, 2, 2);
        WriteTileRow(tiles, tile, 5, 2, 1, 2, 2, 2, 2, 2, 1);
        WriteTileRow(tiles, tile, 6, 2, 2, 2, 1, 2, 2, 2, 2);
        WriteTileRow(tiles, tile, 7, 2, 2, 2, 2, 2, 2, 2, 2);
    }

    private static void WriteSpikeTile(byte[] tiles, int tile)
    {
        WriteTileRow(tiles, tile, 0, 0, 0, 0, 3, 3, 0, 0, 0);
        WriteTileRow(tiles, tile, 1, 0, 0, 3, 3, 3, 3, 0, 0);
        WriteTileRow(tiles, tile, 2, 0, 3, 3, 3, 3, 3, 3, 0);
        WriteTileRow(tiles, tile, 3, 3, 3, 3, 3, 3, 3, 3, 3);
        WriteTileRow(tiles, tile, 4, 2, 2, 3, 2, 2, 3, 2, 2);
        WriteTileRow(tiles, tile, 5, 2, 3, 2, 2, 3, 2, 2, 3);
        WriteTileRow(tiles, tile, 6, 3, 2, 2, 3, 2, 2, 3, 2);
        WriteTileRow(tiles, tile, 7, 3, 3, 3, 3, 3, 3, 3, 3);
    }

    private static void WriteGroundTopTile(byte[] tiles, int tile)
    {
        WriteTileRow(tiles, tile, 0, 1, 1, 1, 1, 1, 1, 1, 1);
        WriteTileRow(tiles, tile, 1, 2, 1, 2, 1, 2, 1, 2, 1);
        WriteTileRow(tiles, tile, 2, 2, 2, 2, 2, 2, 2, 2, 2);
        WriteTileRow(tiles, tile, 3, 2, 3, 2, 2, 2, 3, 2, 2);
        WriteTileRow(tiles, tile, 4, 2, 2, 2, 3, 2, 2, 2, 3);
        WriteTileRow(tiles, tile, 5, 3, 2, 2, 2, 3, 2, 2, 2);
        WriteTileRow(tiles, tile, 6, 2, 2, 3, 2, 2, 2, 3, 2);
        WriteTileRow(tiles, tile, 7, 2, 2, 2, 2, 2, 2, 2, 2);
    }

    private static void WriteBrickTile(byte[] tiles, int tile)
    {
        WriteTileRow(tiles, tile, 0, 3, 3, 3, 3, 3, 3, 3, 3);
        WriteTileRow(tiles, tile, 1, 3, 2, 2, 2, 3, 2, 2, 2);
        WriteTileRow(tiles, tile, 2, 3, 2, 2, 2, 3, 2, 2, 2);
        WriteTileRow(tiles, tile, 3, 3, 3, 3, 3, 3, 3, 3, 3);
        WriteTileRow(tiles, tile, 4, 2, 2, 3, 2, 2, 2, 3, 2);
        WriteTileRow(tiles, tile, 5, 2, 2, 3, 2, 2, 2, 3, 2);
        WriteTileRow(tiles, tile, 6, 3, 3, 3, 3, 3, 3, 3, 3);
        WriteTileRow(tiles, tile, 7, 2, 2, 2, 3, 2, 2, 2, 3);
    }

    private static void WriteTileRow(byte[] tiles, int tile, int row, params int[] colors)
    {
        var plane0 = 0;
        var plane1 = 0;
        for (var col = 0; col < 8; col++)
        {
            var color = colors[col] & 0x03;
            var bit = 7 - col;
            if ((color & 1) != 0) plane0 |= 1 << bit;
            if ((color & 2) != 0) plane1 |= 1 << bit;
        }

        var offset = tile * 16 + row * 2;
        tiles[offset] = (byte)plane0;
        tiles[offset + 1] = (byte)plane1;
    }

    private static void WriteHeaderSkeleton(byte[] rom, byte cartridgeType, byte romSizeCode)
    {
        rom[0x0100] = 0x00;                         // NOP
        rom[0x0101] = 0xC3;                         // JP $0150
        rom[0x0102] = 0x50;
        rom[0x0103] = 0x01;
        NintendoLogo.CopyTo(rom, 0x0104);

        var title = Encoding.ASCII.GetBytes("RETROSHARPGB");
        title.CopyTo(rom, 0x0134);

        rom[0x0147] = cartridgeType;
        rom[0x0148] = romSizeCode;
        rom[0x0149] = 0x00;                         // No cartridge RAM
        rom[0x014A] = 0x01;                         // Non-Japanese
        rom[0x014B] = 0x00;                         // No old licensee
        rom[0x014C] = 0x00;                         // Version
    }

    private static void WriteHeaderChecksums(byte[] rom)
    {
        var headerChecksum = 0;
        for (var i = 0x0134; i <= 0x014C; i++)
        {
            headerChecksum = headerChecksum - rom[i] - 1;
        }

        rom[0x014D] = (byte)headerChecksum;

        var globalChecksum = 0;
        for (var i = 0; i < rom.Length; i++)
        {
            if (i is 0x014E or 0x014F)
            {
                continue;
            }

            globalChecksum = (globalChecksum + rom[i]) & 0xFFFF;
        }

        rom[0x014E] = (byte)(globalChecksum >> 8);
        rom[0x014F] = (byte)(globalChecksum & 0xFF);
    }
}

internal sealed class GameBoyRomLayout
{
    private const int BankSize = 16 * 1024;
    private const int BankedWindowStart = 0x4000;
    private const int BankedWindowEnd = 0x8000;
    private const int MaxSimpleMbc1BankCount = 32;
    public const int MaxProgramTailBankCount = MaxSimpleMbc1BankCount - 1;

    private GameBoyRomLayout(
        bool usesBankedMusic,
        int programTailBankCount,
        int romSize,
        byte romSizeCode,
        IReadOnlyDictionary<string, GameBoyReadOnlyDataPlacement> readOnlyDataPlacements,
        IReadOnlyDictionary<string, GameBoyBankedAudioPlacement> musicPlacements,
        IReadOnlyDictionary<string, GameBoyBankedAudioPlacement> soundEffectPlacements)
    {
        UsesBankedMusic = usesBankedMusic;
        ProgramTailBankCount = programTailBankCount;
        RomSize = romSize;
        RomSizeCode = romSizeCode;
        ReadOnlyDataPlacements = readOnlyDataPlacements;
        MusicPlacements = musicPlacements;
        SoundEffectPlacements = soundEffectPlacements;
    }

    public static GameBoyRomLayout RomOnly { get; } = new(
        usesBankedMusic: false,
        programTailBankCount: 0,
        romSize: 32 * 1024,
        romSizeCode: 0x00,
        readOnlyDataPlacements: new Dictionary<string, GameBoyReadOnlyDataPlacement>(),
        musicPlacements: new Dictionary<string, GameBoyBankedAudioPlacement>(),
        soundEffectPlacements: new Dictionary<string, GameBoyBankedAudioPlacement>());

    public bool UsesBankedMusic { get; }

    public int ProgramTailBankCount { get; }

    public int RomSize { get; }

    public byte RomSizeCode { get; }

    public bool UsesBankedReadOnlyData => ReadOnlyDataPlacements.Count != 0;

    public IReadOnlyDictionary<string, GameBoyReadOnlyDataPlacement> ReadOnlyDataPlacements { get; }

    public IReadOnlyDictionary<string, GameBoyBankedAudioPlacement> MusicPlacements { get; }

    public IReadOnlyDictionary<string, GameBoyBankedAudioPlacement> SoundEffectPlacements { get; }

    public bool TryReadOnlyDataPlacement(string label, out GameBoyReadOnlyDataPlacement placement)
    {
        return ReadOnlyDataPlacements.TryGetValue(label, out placement!);
    }

    public GameBoyBankedAudioPlacement MusicPlacement(string name)
    {
        return MusicPlacements.TryGetValue(name, out var placement)
            ? placement
            : throw new InvalidOperationException($"Unknown banked Game Boy music asset '{name}'.");
    }

    public GameBoyBankedAudioPlacement SoundEffectPlacement(string name)
    {
        return SoundEffectPlacements.TryGetValue(name, out var placement)
            ? placement
            : throw new InvalidOperationException($"Unknown banked Game Boy SFX asset '{name}'.");
    }

    public static GameBoyRomLayout CreateBankedMusicLayout(
        GameBoyVideoProgram program,
        IReadOnlyList<GameBoyReadOnlyDataBlob> readOnlyData,
        int programTailBankCount = 0,
        bool bankReadOnlyData = false)
    {
        var readOnlyPlacements = new Dictionary<string, GameBoyReadOnlyDataPlacement>();
        var placements = new Dictionary<string, GameBoyBankedAudioPlacement>();
        var soundEffectPlacements = new Dictionary<string, GameBoyBankedAudioPlacement>();
        var nextBank = 1 + programTailBankCount;
        var nextAddress = BankedWindowStart;
        if (bankReadOnlyData)
        {
            foreach (var data in readOnlyData)
            {
                if (data.Data.Length > BankSize)
                {
                    throw new InvalidOperationException(
                        $"Game Boy read-only data block '{data.Label}' is {data.Data.Length} bytes, which exceeds one 16 KiB MBC1 bank.");
                }

                if (nextAddress + data.Data.Length > BankedWindowEnd)
                {
                    nextBank++;
                    nextAddress = BankedWindowStart;
                }

                readOnlyPlacements[data.Label] = new GameBoyReadOnlyDataPlacement(
                    data.Label,
                    (byte)nextBank,
                    (ushort)nextAddress,
                    data.Data);
                nextAddress += data.Data.Length;
            }

            if (readOnlyPlacements.Count != 0)
            {
                nextBank++;
            }
        }

        foreach (var asset in program.MusicAssetsInLoadOrder)
        {
            placements[asset.Name] = new GameBoyBankedAudioPlacement(asset.Name, (byte)nextBank, 0x4000, asset.Data);
            nextBank += Math.Max(1, (asset.Data.Length + BankSize - 1) / BankSize);
        }

        foreach (var asset in program.SoundEffectAssetsInLoadOrder)
        {
            soundEffectPlacements[asset.Name] = new GameBoyBankedAudioPlacement(asset.Name, (byte)nextBank, 0x4000, asset.Data);
            nextBank += Math.Max(1, (asset.Data.Length + BankSize - 1) / BankSize);
        }

        var bankCount = RoundBankCount(nextBank);
        if (bankCount > MaxSimpleMbc1BankCount)
        {
            throw new InvalidOperationException(
                $"Generated Game Boy ROM needs {bankCount} banks, but the current transparent MBC1 lowering supports up to {MaxSimpleMbc1BankCount} banks.");
        }

        return new GameBoyRomLayout(
            usesBankedMusic: placements.Count != 0 || soundEffectPlacements.Count != 0,
            programTailBankCount: programTailBankCount,
            romSize: bankCount * BankSize,
            romSizeCode: ToRomSizeCode(bankCount),
            readOnlyDataPlacements: readOnlyPlacements,
            musicPlacements: placements,
            soundEffectPlacements: soundEffectPlacements);
    }

    private static int RoundBankCount(int requiredBanks)
    {
        var banks = 2;
        while (banks < requiredBanks)
        {
            banks *= 2;
        }

        return banks;
    }

    private static byte ToRomSizeCode(int bankCount) => bankCount switch
    {
        2 => 0x00,
        4 => 0x01,
        8 => 0x02,
        16 => 0x03,
        32 => 0x04,
        _ => throw new InvalidOperationException($"Unsupported Game Boy ROM bank count '{bankCount}'."),
    };
}

internal sealed record GameBoyReadOnlyDataBlob(string Label, byte[] Data);

internal sealed record GameBoyReadOnlyDataPlacement(string Label, byte Bank, ushort Address, byte[] Data);

internal sealed record GameBoyBankedAudioPlacement(string Name, byte Bank, ushort Address, byte[] Data);

internal sealed class GameBoyRuntimeCompiler
{
    private sealed record StructArrayLayout(int Stride, IReadOnlyDictionary<string, int> FieldOffsets);

    private const ushort FirstVariableAddress = 0xC000;
    private const ushort RuntimeReservedStateAddress = 0xC0E0;
    private const ushort CameraXLowAddress = 0xC0E0;
    private const ushort CameraXHighAddress = 0xC0E1;
    private const ushort CameraFineXAddress = 0xC0E2;
    private const ushort CameraScreenLeftColumnAddress = 0xC0E3;
    private const ushort CameraRightBackgroundColumnAddress = 0xC0E4;
    private const ushort CameraLeftBackgroundColumnAddress = 0xC0E5;
    private const ushort CameraRightSourceColumnAddress = 0xC0E6;
    private const ushort CameraLeftSourceColumnAddress = 0xC0E7;
    private const ushort CameraYLowAddress = 0xC0E8;
    private const ushort CameraYHighAddress = 0xC0E9;
    private const ushort CameraFineYAddress = 0xC0EA;
    private const ushort CameraTopBackgroundRowAddress = 0xC0EB;
    private const ushort CameraBottomBackgroundRowAddress = 0xC0EC;
    private const ushort CameraTopSourceRowAddress = 0xC0ED;
    private const ushort CameraBottomSourceRowAddress = 0xC0EE;
    private const ushort InputCurrentAddress = 0xC0F0;
    private const ushort InputPreviousAddress = 0xC0F1;
    private const ushort InputHoldTicksStartAddress = 0xC0F2;
    private const ushort MusicActiveAddress = 0xC0FA;
    private const ushort MusicRowAddress = 0xC0FB;
    private const ushort MusicTickAddress = 0xC0FC;
    private const ushort MusicDataPointerLowAddress = 0xC0FD;
    private const ushort MusicDataPointerHighAddress = 0xC0FE;
    private const ushort MusicCurrentPointerLowAddress = 0xC0FF;
    private const ushort MusicCurrentPointerHighAddress = 0xC100;
    private const ushort MusicScratchPointerLowAddress = 0xC101;
    private const ushort MusicScratchPointerHighAddress = 0xC102;
    private const ushort MusicTicksPerRowAddress = 0xC103;
    private const ushort MusicRowHighAddress = 0xC104;
    private const ushort MusicRowMaskAddress = 0xC105;
    private const ushort MusicRowCacheStartAddress = 0xC106;
    private const ushort MusicDataBankAddress = 0xC115;
    private const ushort MusicCurrentBankAddress = 0xC116;
    private const ushort MusicScratchBankAddress = 0xC117;
    private const ushort MusicDataCursorBankAddress = 0xC118;
    // Deferred camera streaming: camera movement queues at most one column/row crossing per
    // frame here; the camera apply operation drains it to VRAM during the top-of-frame VBlank.
    // This keeps each main-loop iteration to a single VBlank so audio update stays frame-locked.
    private const ushort PendingStreamKindAddress = 0xC119;   // 0=none, 1=column, 2=row
    private const ushort PendingStreamTargetAddress = 0xC11A; // background column or row index
    private const ushort PendingStreamSourceAddress = 0xC11B; // source-map column or row index
    private const ushort PendingStreamRowDataLowAddress = 0xC11D;
    private const ushort PendingStreamRowDataHighAddress = 0xC11E;
    private const ushort PendingDiagonalColumnKindAddress = 0xC11F;   // 0=none, 1=queued
    private const ushort PendingDiagonalColumnTargetAddress = 0xC120; // background column index
    private const ushort PendingDiagonalColumnSourceAddress = 0xC121; // source-map column index
    private const ushort PendingDiagonalRowKindAddress = 0xC122;      // 0=none, 1=queued
    private const ushort PendingDiagonalRowTargetAddress = 0xC123;    // background row index
    private const ushort PendingDiagonalRowSourceAddress = 0xC124;    // source-map row index
    private const ushort CameraScreenTileFlagsColumnAddress = 0xC125;
    private const ushort PendingDiagonalNextStreamKindAddress = 0xC127;
    private const ushort CameraStreamSourceRowScratchAddress = 0xC128;
    private const ushort CameraStreamSourceColumnScratchAddress = 0xC12A;
    private const ushort CameraStreamTargetColumnScratchAddress = 0xC12B;
    private const ushort CameraStreamColumnsRemainingAddress = 0xC12C;
    private const ushort CameraSetPositionTargetAddress = 0xC12D;
    private const ushort CameraSetPositionStepsRemainingAddress = 0xC12E;
    private const ushort WordScratchLowAddress = 0xC12F;
    private const ushort WordScratchHighAddress = 0xC130;
    private const ushort SfxActiveAddress = 0xC131;
    private const ushort SfxTickAddress = 0xC132;
    private const ushort SfxDataPointerLowAddress = 0xC133;
    private const ushort SfxDataPointerHighAddress = 0xC134;
    private const ushort SfxCurrentPointerLowAddress = 0xC135;
    private const ushort SfxCurrentPointerHighAddress = 0xC136;
    private const ushort SfxDataBankAddress = 0xC137;
    private const ushort SfxCurrentBankAddress = 0xC138;
    private const ushort SfxDataCursorBankAddress = 0xC139;
    private const ushort PendingStreamCountAddress = 0xC13A;
    private const ushort PendingStreamSecondTargetAddress = 0xC13B;
    private const ushort PendingStreamSecondSourceAddress = 0xC13C;
    private const ushort PendingDiagonalColumnCountAddress = 0xC13D;
    private const ushort PendingDiagonalColumnSecondTargetAddress = 0xC13E;
    private const ushort PendingDiagonalColumnSecondSourceAddress = 0xC13F;
    private const ushort PendingDiagonalRowCountAddress = 0xC140;
    private const ushort PendingDiagonalRowSecondTargetAddress = 0xC141;
    private const ushort PendingDiagonalRowSecondSourceAddress = 0xC142;
    // Shadow of the BGM's intended channel 1 state (NR10-NR14, $FF10-$FF14). The music updates it on
    // every channel 1 write even while the channel is muted by an active SFX, so when the effect
    // releases the channel it can be restored to the BGM's full channel 1 state instead of the effect's
    // residue (the BGM rewrites NR13/NR14 every note but rarely NR10/NR11/NR12, so otherwise its notes
    // would inherit the SFX sweep/duty/envelope). The shadow is page-aligned so that its address equals
    // $C200 + register offset (C = $10..$14), letting the music write path index it with `ld l,c`.
    private const byte Channel1ShadowPageHigh = 0xC2;
    private const ushort Channel1ShadowBaseAddress = 0xC210; // NR10 shadow; NR11-NR14 at +1..+4
    // Camera movement walks the camera toward the requested position one pixel at a time. The pending
    // stream slots below preserve both exposed edges when that walk crosses two tile boundaries.
    private const byte CameraSetPositionMaxStepsPerFrame = 16;
    private const byte PendingStreamNone = 0;
    private const byte PendingStreamColumn = 1;
    private const byte PendingStreamRow = 2;
    private const byte PendingStreamQueued = 1;
    private const byte PendingStreamSingle = 1;
    private const byte PendingStreamDouble = 2;
    private const byte MusicActiveUgeRows = 1;
    private const byte MusicActiveApuTrace = 2;
    private const int MusicHeaderLength = 3;
    private const int MusicWaveTableBytes = 16 * 16;
    private const int MusicRowCacheLength = 15;
    private const byte ApuTraceWaveRamBlockCommand = 0xFF;
    private const int VisibleScreenTileWidth = 20;
    private const int VisibleScreenTileWidthWithPartial = VisibleScreenTileWidth + 1;
    private const int VisibleScreenTileHeight = 18;
    private const int BackgroundTileMapWidth = 32;
    private const int BackgroundTileMapHeight = 32;
    private const byte JoypadDeselect = 0x30;
    private const int JoypadSettleReadCount = 4;

    private static readonly GameBoyButton[] Buttons =
    [
        new("a", 0x10, 0x01, 0x01, InputHoldTicksStartAddress),
        new("b", 0x10, 0x02, 0x02, InputHoldTicksStartAddress + 1),
        new("select", 0x10, 0x04, 0x04, InputHoldTicksStartAddress + 2),
        new("start", 0x10, 0x08, 0x08, InputHoldTicksStartAddress + 3),
        new("right", 0x20, 0x01, 0x10, InputHoldTicksStartAddress + 4),
        new("left", 0x20, 0x02, 0x20, InputHoldTicksStartAddress + 5),
        new("up", 0x20, 0x04, 0x40, InputHoldTicksStartAddress + 6),
        new("down", 0x20, 0x08, 0x80, InputHoldTicksStartAddress + 7),
    ];

    private readonly GbBuilder builder;
    private readonly GameBoyVideoProgram program;
    private readonly GameBoyRomLayout romLayout;
    private readonly Sdk2DStreamReader sdkOperations;
    private readonly SdkAudioStreamReader sdkAudioOperations;
    private readonly Dictionary<string, ushort> variables = [];
    private readonly Dictionary<string, string> variableTypes = [];
    private readonly Dictionary<string, StructArrayLayout> structArrays = [];
    private readonly HashSet<string> declaredVariables = [];
    private readonly HashSet<string> signedByteLocations = [];
    private readonly HashSet<string> immutableVariables = [];
    private readonly HashSet<string> userFunctionCallStack = [];
    private readonly Stack<InlineVariableScope> inlineVariableScopes = [];
    private readonly Stack<LoopTarget> loopTargets = [];
    private int nextHardwareSprite;
    private int nextInlineVariableScopeId;
    private ushort nextVariableAddress = FirstVariableAddress;
    private int? cameraMapWidth;
    private int? cameraStreamY;
    private int? cameraStreamHeight;

    public GameBoyRuntimeCompiler(GbBuilder builder, GameBoyVideoProgram program, GameBoyRomLayout? romLayout = null)
    {
        this.builder = builder;
        this.program = program;
        this.romLayout = romLayout ?? GameBoyRomLayout.RomOnly;
        sdkOperations = Sdk2DStreamReader.ForProgram(program);
        sdkAudioOperations = SdkAudioStreamReader.ForProgram(program);
    }

    public void Emit(BlockSyntax block)
    {
        EmitMain(block);
        EmitSubroutines();
        EnsureAllStreamsConsumed();
    }

    public void EmitMain(BlockSyntax block)
    {
        EmitInputStateInitialization();
        EmitBlock(block);
    }

    public bool UsesSubroutineTrampolines => romLayout.ProgramTailBankCount > 0 && program.SubroutineNames.Count != 0;

    public bool UsesReadOnlyDataHelpers => romLayout.ReadOnlyDataPlacements.Keys.Any(IsRuntimeReadOnlyDataLabel);

    public bool UsesAudioUpdateHelper =>
        romLayout.ProgramTailBankCount > 0
        && romLayout.UsesBankedMusic
        && program.SdkAudioOperations.Any(operation => operation is SdkAudioOperation.UpdateAudio);

    public bool UsesMusicPlayHelpers =>
        romLayout.ProgramTailBankCount > 0
        && romLayout.UsesBankedMusic
        && program.SdkAudioOperations.Any(operation => operation is SdkAudioOperation.PlayMusic);

    public bool UsesSoundEffectPlayHelpers =>
        romLayout.ProgramTailBankCount > 0
        && romLayout.UsesBankedMusic
        && program.SdkAudioOperations.Any(operation => operation is SdkAudioOperation.PlaySoundEffect);

    public bool UsesAudioHelpers => UsesAudioUpdateHelper || UsesMusicPlayHelpers || UsesSoundEffectPlayHelpers;

    public void EmitProgramBankInitialization()
    {
        if (romLayout.ProgramTailBankCount == 0)
        {
            return;
        }

        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyRomBuilder.ProgramCurrentBankAddress);
        EmitSelectRomBankFromA();
    }

    public void EmitReadOnlyDataHelpers()
    {
        if (!UsesReadOnlyDataHelpers)
        {
            return;
        }

        foreach (var placement in romLayout.ReadOnlyDataPlacements.Values)
        {
            if (!IsRuntimeReadOnlyDataLabel(placement.Label))
            {
                continue;
            }

            builder.Label(ReadOnlyDataByteReaderLabel(placement.Label));
            builder.LoadEFromA();
            builder.LoadDImmediate(0);
            builder.LoadAImmediate(placement.Bank);
            EmitSelectRomBankFromA();
            builder.LoadHl(placement.Address);
            builder.AddHlDe();
            builder.LoadAFromHl();
            if (romLayout.ProgramTailBankCount > 0)
            {
                builder.LoadBFromA();
                builder.LoadA(GameBoyRomBuilder.ProgramCurrentBankAddress);
                EmitSelectRomBankFromA();
                builder.LoadAFromB();
            }

            builder.Emit(0xC9); // RET
        }
    }

    public void EmitAudioHelpers()
    {
        EmitMusicPlayHelpers();
        EmitSoundEffectPlayHelpers();
        EmitAudioUpdateHelper();
    }

    private void EmitMusicPlayHelpers()
    {
        if (!UsesMusicPlayHelpers)
        {
            return;
        }

        foreach (var themeId in program.SdkAudioOperations.OfType<SdkAudioOperation.PlayMusic>().Select(play => play.ThemeId).Distinct())
        {
            builder.Label(MusicPlayHelperLabel(themeId));
            EmitPlayMusic(new SdkAudioOperation.PlayMusic(themeId));
            EmitRestoreProgramTailBank();
            builder.Emit(0xC9); // RET
        }
    }

    private void EmitSoundEffectPlayHelpers()
    {
        if (!UsesSoundEffectPlayHelpers)
        {
            return;
        }

        foreach (var soundId in program.SdkAudioOperations.OfType<SdkAudioOperation.PlaySoundEffect>().Select(play => play.SoundId).Distinct())
        {
            builder.Label(SoundEffectPlayHelperLabel(soundId));
            EmitPlaySoundEffect(new SdkAudioOperation.PlaySoundEffect(soundId));
            EmitRestoreProgramTailBank();
            builder.Emit(0xC9); // RET
        }
    }

    private void EmitAudioUpdateHelper()
    {
        if (!UsesAudioUpdateHelper)
        {
            return;
        }

        builder.Label(AudioUpdateHelperLabel);
        EmitUpdateAudio();
        EmitRestoreProgramTailBank();
        builder.Emit(0xC9); // RET
    }

    public void EmitSubroutineTrampolines()
    {
        if (!UsesSubroutineTrampolines)
        {
            return;
        }

        foreach (var name in program.SubroutineNames)
        {
            builder.Label(SubroutineTrampolineLabel(name));
            builder.LoadA(GameBoyRomBuilder.ProgramCurrentBankAddress);
            builder.Emit(0xF5); // PUSH AF
            builder.LoadAImmediateBankOf(SubroutineLabel(name));
            builder.StoreA(GameBoyRomBuilder.ProgramCurrentBankAddress);
            EmitSelectRomBankFromA();
            builder.JumpAbsolute(0xCD, SubroutineLabel(name)); // CALL nn
            builder.Emit(0xF1); // POP AF
            builder.StoreA(GameBoyRomBuilder.ProgramCurrentBankAddress);
            EmitSelectRomBankFromA();
            builder.Emit(0xC9); // RET
        }
    }

    public void EmitSubroutines()
    {
        foreach (var name in program.SubroutineNames)
        {
            if (!program.Functions.TryGetValue(name, out var function))
            {
                continue;
            }

            builder.Label(SubroutineLabel(name));
            sdkOperations.EnterSubroutine(name);
            sdkAudioOperations.EnterSubroutine(name);
            var aliases = PushSubroutineParameterAliases(function);
            try
            {
                EmitBlock(SubroutineBody(function));
                sdkOperations.LeaveSubroutine(name);
                sdkAudioOperations.LeaveSubroutine(name);
                builder.Emit(0xC9); // RET
            }
            finally
            {
                PopSubroutineParameterAliases(aliases);
            }
        }
    }

    public void EnsureAllStreamsConsumed()
    {
        EnsureAllSdkOperationsConsumed();
        EnsureAllSdkAudioOperationsConsumed();
    }

    private void EmitInputStateInitialization()
    {
        builder.LoadAImmediate(0);
        builder.StoreA(InputCurrentAddress);
        builder.StoreA(InputPreviousAddress);
        foreach (var button in Buttons)
        {
            builder.StoreA(button.HoldTicksAddress);
        }
    }

    private void EmitBlock(BlockSyntax block)
    {
        foreach (var statement in block.Statements)
        {
            EmitStatement(statement);
        }
    }

    private void EmitStatement(StatementSyntax statement)
    {
        switch (statement)
        {
            case DeclarationSyntax declaration:
                EmitDeclaration(declaration);
                break;
            case ExpressionStatementSyntax expressionStatement:
                EmitExpressionStatement(expressionStatement);
                break;
            case WhileSyntax whileSyntax:
                EmitWhile(whileSyntax);
                break;
            case DoWhileSyntax doWhileSyntax:
                EmitDoWhile(doWhileSyntax);
                break;
            case RangeForSyntax rangeForSyntax:
                EmitFor(RangeForLowerer.Lower(rangeForSyntax));
                break;
            case ForSyntax forSyntax:
                EmitFor(forSyntax);
                break;
            case IfElseSyntax ifElseSyntax:
                EmitIf(ifElseSyntax);
                break;
            case BreakSyntax:
                EmitBreak();
                break;
            case ContinueSyntax:
                EmitContinue();
                break;
            case ReturnSyntax:
                break;
            default:
                throw new InvalidOperationException($"Unsupported Game Boy statement '{statement.GetType().Name}'.");
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

        throw new InvalidOperationException($"Game Boy target does not support local type '{declaration.Type}' yet.");
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
            throw new InvalidOperationException($"Game Boy target only supports scalar fixed-size arrays; '{declaration.Type}' is not supported yet.");
        }

        var declarationName = DeclareScopedVariableName(declaration.Name);
        if (!declaredVariables.Add(declarationName))
        {
            throw new InvalidOperationException($"Variable '{declaration.Name}' is already declared.");
        }

        TrackImmutable(declaration);

        var length = CheckedRange(GameBoyVideoProgram.ConstValue(declaration.ArrayLength.Value, $"{declaration.Name} array length"), 1, 255, $"{declaration.Name} array length");
        var elementAddresses = new List<ushort>();
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

        var length = CheckedRange(GameBoyVideoProgram.ConstValue(declaration.ArrayLength.Value, $"{declaration.Name} array length"), 1, 255, $"{declaration.Name} array length");
        var fieldOffsets = StructFieldOffsets(structSyntax);
        var stride = StructStride(structSyntax);
        if (length * stride > 255)
        {
            throw new InvalidOperationException($"Game Boy target struct array '{declaration.Name}' uses {length * stride} byte slot(s), but runtime indexed struct arrays are limited to 255 byte slots.");
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
            throw new InvalidOperationException($"Game Boy target requires an array initializer for local struct array '{declaration.Name}'.");
        }

        if (arrayInitializer.Elements.Count > length)
        {
            throw new InvalidOperationException($"Game Boy target struct array initializer for '{declaration.Name}' has {arrayInitializer.Elements.Count} element(s), but the array length is {length}.");
        }

        var knownFields = fieldNames.ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < arrayInitializer.Elements.Count; index++)
        {
            if (arrayInitializer.Elements[index] is not StructInitializerSyntax structInitializer)
            {
                throw new InvalidOperationException($"Game Boy target requires struct initializers for elements of struct array '{declaration.Name}'.");
            }

            var initializedFields = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
            foreach (var field in structInitializer.Fields)
            {
                if (!initializedFields.TryAdd(field.Name, field.Expression))
                {
                    throw new InvalidOperationException($"Game Boy target struct array initializer for '{declaration.Name}' element {index} supplies field '{field.Name}' more than once.");
                }

                if (!knownFields.Contains(field.Name))
                {
                    throw new InvalidOperationException($"Game Boy target struct array initializer for '{declaration.Name}' has no field named '{field.Name}'.");
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

    private void EmitArrayInitializer(DeclarationSyntax declaration, ExpressionSyntax initialization, int length, IReadOnlyList<ushort> elementAddresses)
    {
        if (initialization is not ArrayInitializerSyntax arrayInitializer)
        {
            throw new InvalidOperationException($"Game Boy target requires an array initializer for local array '{declaration.Name}'.");
        }

        if (arrayInitializer.Elements.Count > length)
        {
            throw new InvalidOperationException($"Game Boy target array initializer for '{declaration.Name}' has {arrayInitializer.Elements.Count} element(s), but the array length is {length}.");
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

        var fieldAddresses = new Dictionary<string, ushort>(StringComparer.Ordinal);
        var fieldNames = new List<string>();
        foreach (var field in structSyntax.Fields)
        {
            if (!IsScalarLocalType(field.Type))
            {
                throw new InvalidOperationException($"Game Boy target does not support struct field type '{field.Type}' yet.");
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
        IReadOnlyDictionary<string, ushort> fieldAddresses)
    {
        if (initialization is not StructInitializerSyntax structInitializer)
        {
            throw new InvalidOperationException($"Game Boy target requires a struct initializer for local struct '{declaration.Name}'.");
        }

        var initializedFields = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
        foreach (var field in structInitializer.Fields)
        {
            if (!initializedFields.TryAdd(field.Name, field.Expression))
            {
                throw new InvalidOperationException($"Game Boy target struct initializer for '{declaration.Name}' supplies field '{field.Name}' more than once.");
            }

            if (!fieldAddresses.ContainsKey(field.Name))
            {
                throw new InvalidOperationException($"Game Boy target struct initializer for '{declaration.Name}' has no field named '{field.Name}'.");
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

    private ushort DeclareVariable(string name, string type)
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
            throw new InvalidOperationException("Game Boy target local variables exceed the current prototype WRAM allocation.");
        }

        var address = nextVariableAddress;
        nextVariableAddress += (ushort)size;
        variables.Add(name, address);
        variableTypes.Add(name, type);
        return address;
    }

    private void EmitZeroToStorage(ushort address, string type)
    {
        builder.LoadAImmediate(0);
        builder.StoreA(address);
        if (IsWordBackedType(type))
        {
            builder.StoreA(HighAddress(address));
        }
    }

    private void EmitExpressionToStorage(ExpressionSyntax expression, ushort address, string type)
    {
        if (IsWordBackedType(type))
        {
            EmitWordExpressionToStorage(expression, address, type);
            return;
        }

        EmitExpressionToA(expression);
        builder.StoreA(address);
    }

    private void EmitWordExpressionToStorage(ExpressionSyntax expression, ushort address, string targetType)
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
                builder.StoreA(address);
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
                EmitRuntimeIndexedMemberAddressToHl(indexedBase, fieldName);
                EmitRuntimeStorageFromHlToWordStorage(address, VariableStorageType(IndexedMemberName(indexedBase.BaseIdentifier, 0, fieldName)));
                return;
            case IndexExpressionSyntax indexExpression:
                EmitRuntimeIndexedAddressToHl(indexExpression.BaseIdentifier, indexExpression.Index);
                EmitRuntimeStorageFromHlToWordStorage(address, VariableStorageType(IndexedElementName(indexExpression.BaseIdentifier, 0)));
                return;
            case FunctionCall call:
                EmitValueCallToA(call);
                builder.StoreA(address);
                builder.LoadAImmediate(0);
                builder.StoreA(HighAddress(address));
                return;
            case ConditionalExpressionSyntax conditional:
                EmitWordConditionalExpressionToStorage(conditional, address, targetType);
                return;
            case UnaryExpressionSyntax unary when IsBooleanValueExpression(unary):
                EmitBooleanExpressionToA(unary);
                builder.StoreA(address);
                builder.LoadAImmediate(0);
                builder.StoreA(HighAddress(address));
                return;
            case BinaryExpressionSyntax binary when IsBooleanValueExpression(binary):
                EmitBooleanExpressionToA(binary);
                builder.StoreA(address);
                builder.LoadAImmediate(0);
                builder.StoreA(HighAddress(address));
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
                builder.StoreA(address);
                builder.LoadAImmediate(0);
                builder.StoreA(HighAddress(address));
                return;
        }
    }

    private void EmitRuntimeStorageFromHlToWordStorage(ushort address, string sourceType)
    {
        builder.LoadAFromHl();
        builder.StoreA(address);
        if (IsWordBackedType(sourceType))
        {
            builder.IncrementHl();
            builder.LoadAFromHl();
        }
        else if (sourceType == "i8")
        {
            EmitSignExtensionFromA();
        }
        else
        {
            builder.LoadAImmediate(0);
        }

        builder.StoreA(HighAddress(address));
    }

    private void EmitWordConditionalExpressionToStorage(ConditionalExpressionSyntax conditional, ushort address, string targetType)
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

    private void EmitStoreWordImmediate(ushort address, int value)
    {
        builder.LoadAImmediate(value & 0xFF);
        builder.StoreA(address);
        builder.LoadAImmediate((value >> 8) & 0xFF);
        builder.StoreA(HighAddress(address));
    }

    private void EmitCopyToWordStorage(ushort sourceAddress, string sourceType, ushort targetAddress)
    {
        builder.LoadA(sourceAddress);
        builder.StoreA(targetAddress);

        if (IsWordBackedType(sourceType))
        {
            builder.LoadA(HighAddress(sourceAddress));
        }
        else if (sourceType == "i8")
        {
            builder.LoadA(sourceAddress);
            EmitSignExtensionFromA();
        }
        else
        {
            builder.LoadAImmediate(0);
        }

        builder.StoreA(HighAddress(targetAddress));
    }

    private void EmitAddWordIntoStorage(ushort address, ExpressionSyntax right)
    {
        if (TryConst(right, out var constant))
        {
            builder.LoadA(address);
            builder.AddAImmediate(constant & 0xFF);
            builder.StoreA(address);
            builder.LoadA(HighAddress(address));
            builder.AdcAImmediate((constant >> 8) & 0xFF);
            builder.StoreA(HighAddress(address));
            return;
        }

        if (!TryDirectStorageExpression(right, out var rightAddress, out var rightType))
        {
            EmitWordExpressionToStorage(right, WordScratchLowAddress, WordExpressionType(right));
            rightAddress = WordScratchLowAddress;
            rightType = "u16";
        }

        // Sign-extending an i8 addend clobbers the carry flag, so its high byte must be materialized
        // to scratch before the low-byte add. Wider operands load the high byte with a carry-safe LD
        // after the low-byte add, leaving their emission unchanged.
        var hoistI8HighByte = rightType == "i8";
        if (hoistI8HighByte)
        {
            EmitStoreHighByteToScratch(rightAddress, rightType, WordScratchHighAddress);
        }

        builder.LoadA(rightAddress);
        builder.LoadBFromA();
        builder.LoadA(address);
        builder.AddAFromB();
        builder.StoreA(address);

        if (hoistI8HighByte)
        {
            builder.LoadA(WordScratchHighAddress);
            builder.LoadBFromA();
        }
        else
        {
            EmitLoadHighByteToB(rightAddress, rightType);
        }

        builder.LoadA(HighAddress(address));
        builder.AdcAFromB();
        builder.StoreA(HighAddress(address));
    }

    private void EmitSubtractWordFromStorage(ushort address, ExpressionSyntax right)
    {
        if (TryConst(right, out var constant))
        {
            builder.LoadA(address);
            builder.SubtractAImmediate(constant & 0xFF);
            builder.StoreA(address);
            builder.LoadA(HighAddress(address));
            builder.SbcAImmediate((constant >> 8) & 0xFF);
            builder.StoreA(HighAddress(address));
            return;
        }

        if (!TryDirectStorageExpression(right, out var rightAddress, out var rightType))
        {
            EmitWordExpressionToStorage(right, WordScratchLowAddress, WordExpressionType(right));
            rightAddress = WordScratchLowAddress;
            rightType = "u16";
        }

        // Sign-extending an i8 operand clobbers the carry/borrow flag, so its high byte must be
        // materialized to scratch before the low-byte subtract. Wider operands load the high byte
        // with a carry-safe LD after the low-byte subtract, leaving their emission unchanged.
        var hoistI8HighByte = rightType == "i8";
        if (hoistI8HighByte)
        {
            EmitStoreHighByteToScratch(rightAddress, rightType, WordScratchHighAddress);
        }

        builder.LoadA(rightAddress);
        builder.LoadBFromA();
        builder.LoadA(address);
        builder.SubtractB();
        builder.StoreA(address);

        if (hoistI8HighByte)
        {
            builder.LoadA(WordScratchHighAddress);
            builder.LoadBFromA();
        }
        else
        {
            EmitLoadHighByteToB(rightAddress, rightType);
        }

        builder.LoadA(HighAddress(address));
        builder.SbcB();
        builder.StoreA(HighAddress(address));
    }

    private void EmitLoadHighByteToB(ushort address, string type)
    {
        if (IsWordBackedType(type))
        {
            builder.LoadA(HighAddress(address));
        }
        else if (type == "i8")
        {
            builder.LoadA(address);
            EmitSignExtensionFromA();
        }
        else
        {
            builder.LoadAImmediate(0);
        }

        builder.LoadBFromA();
    }

    private void EmitStoreHighByteToScratch(ushort address, string type, ushort scratchAddress)
    {
        if (IsWordBackedType(type))
        {
            builder.LoadA(HighAddress(address));
        }
        else if (type == "i8")
        {
            builder.LoadA(address);
            EmitSignExtensionFromA();
        }
        else
        {
            builder.LoadAImmediate(0);
        }

        builder.StoreA(scratchAddress);
    }

    private void EmitHighByteFromLowAToStorage(ushort highAddress, string sourceType)
    {
        if (sourceType == "i8")
        {
            EmitSignExtensionFromA();
        }
        else
        {
            builder.LoadAImmediate(0);
        }

        builder.StoreA(highAddress);
    }

    private void EmitSignExtensionFromA()
    {
        var negativeLabel = builder.CreateLabel("sign_extend_negative");
        var endLabel = builder.CreateLabel("sign_extend_end");

        builder.AndImmediate(0x80);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, negativeLabel); // JP NZ,negativeLabel
        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(negativeLabel);
        builder.LoadAImmediate(0xFF);
        builder.Label(endLabel);
    }

    private static ushort HighAddress(ushort lowAddress) => (ushort)(lowAddress + 1);

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
                signedByteLocations.Contains(ScopedVariableName(GameBoyVideoProgram.MemberAccessName(memberAccess))),
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

    private bool TryDirectStorageExpression(ExpressionSyntax expression, out ushort address, out string type)
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

                var memberName = GameBoyVideoProgram.MemberAccessName(memberAccess);
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
                type = VariableStorageType(GameBoyVideoProgram.MemberAccessName(memberAccess));
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
            throw new InvalidOperationException($"Game Boy target only supports explicit casts to byte-backed local types; '{cast.Type}' is not supported yet.");
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
                throw new InvalidOperationException($"Unsupported Game Boy expression statement '{expressionStatement.Expression.GetType().Name}'.");
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
        builder.StoreA(address);
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
        EmitRuntimeIndexedAddressToHl(indexLValue.BaseIdentifier, indexLValue.Index);
        var elementType = VariableStorageType(IndexedElementName(indexLValue.BaseIdentifier, 0));
        if (IsWordBackedType(elementType))
        {
            EmitRuntimeIndexedWordAssignment(assignment, elementType, "runtime indexed assignment");
            return;
        }

        switch (assignment.OperatorSymbol)
        {
            case "=":
                RequireExpressionPreservesHl(assignment.Right, "runtime indexed assignment");
                EmitExpressionToA(assignment.Right);
                builder.StoreHlA();
                return;
            case "+=":
                builder.LoadAFromHl();
                if (TryConst(assignment.Right, out var addRight))
                {
                    builder.AddAImmediate(addRight);
                    builder.StoreHlA();
                    return;
                }

                RequireExpressionPreservesHl(assignment.Right, "runtime indexed compound assignment");
                builder.LoadCFromA();
                EmitExpressionToA(assignment.Right);
                builder.AddAFromC();
                builder.StoreHlA();
                return;
            case "-=":
                builder.LoadAFromHl();
                if (TryConst(assignment.Right, out var subtractRight))
                {
                    builder.SubtractAImmediate(subtractRight);
                    builder.StoreHlA();
                    return;
                }

                RequireExpressionPreservesHl(assignment.Right, "runtime indexed compound assignment");
                builder.LoadCFromA();
                EmitExpressionToA(assignment.Right);
                builder.LoadBFromA();
                builder.LoadAFromC();
                builder.SubtractB();
                builder.StoreHlA();
                return;
            case "&=":
                EmitRuntimeIndexedBitwiseCompoundAssignment(assignment.Right, "&", "runtime indexed &= assignment");
                return;
            case "|=":
                EmitRuntimeIndexedBitwiseCompoundAssignment(assignment.Right, "|", "runtime indexed |= assignment");
                return;
            case "^=":
                EmitRuntimeIndexedBitwiseCompoundAssignment(assignment.Right, "^", "runtime indexed ^= assignment");
                return;
            default:
                throw new InvalidOperationException($"Unsupported Game Boy assignment operator '{assignment.OperatorSymbol}'.");
        }
    }

    private void EmitRuntimeIndexedBitwiseCompoundAssignment(ExpressionSyntax right, string op, string context)
    {
        builder.LoadAFromHl();
        if (TryConst(right, out var constant))
        {
            EmitBitwiseImmediate(op, constant);
            builder.StoreHlA();
            return;
        }

        RequireExpressionPreservesHl(right, context);
        builder.LoadCFromA();
        EmitExpressionToA(right);
        EmitBitwiseAFromC(op);
        builder.StoreHlA();
    }

    private void EmitRuntimeIndexedMemberAssignment(IndexExpressionSyntax indexExpression, string fieldName, AssignmentSyntax assignment)
    {
        EmitRuntimeIndexedMemberAddressToHl(indexExpression, fieldName);
        var fieldType = VariableStorageType(IndexedMemberName(indexExpression.BaseIdentifier, 0, fieldName));
        if (IsWordBackedType(fieldType))
        {
            EmitRuntimeIndexedWordAssignment(assignment, fieldType, "runtime indexed struct field assignment");
            return;
        }

        switch (assignment.OperatorSymbol)
        {
            case "=":
                RequireExpressionPreservesHl(assignment.Right, "runtime indexed struct field assignment");
                EmitExpressionToA(assignment.Right);
                builder.StoreHlA();
                return;
            case "+=":
                builder.LoadAFromHl();
                if (TryConst(assignment.Right, out var addRight))
                {
                    builder.AddAImmediate(addRight);
                    builder.StoreHlA();
                    return;
                }

                RequireExpressionPreservesHl(assignment.Right, "runtime indexed struct field compound assignment");
                builder.LoadCFromA();
                EmitExpressionToA(assignment.Right);
                builder.AddAFromC();
                builder.StoreHlA();
                return;
            case "-=":
                builder.LoadAFromHl();
                if (TryConst(assignment.Right, out var subtractRight))
                {
                    builder.SubtractAImmediate(subtractRight);
                    builder.StoreHlA();
                    return;
                }

                RequireExpressionPreservesHl(assignment.Right, "runtime indexed struct field compound assignment");
                builder.LoadCFromA();
                EmitExpressionToA(assignment.Right);
                builder.LoadBFromA();
                builder.LoadAFromC();
                builder.SubtractB();
                builder.StoreHlA();
                return;
            case "&=":
                EmitRuntimeIndexedBitwiseCompoundAssignment(assignment.Right, "&", "runtime indexed struct field &= assignment");
                return;
            case "|=":
                EmitRuntimeIndexedBitwiseCompoundAssignment(assignment.Right, "|", "runtime indexed struct field |= assignment");
                return;
            case "^=":
                EmitRuntimeIndexedBitwiseCompoundAssignment(assignment.Right, "^", "runtime indexed struct field ^= assignment");
                return;
            default:
                throw new InvalidOperationException($"Unsupported Game Boy assignment operator '{assignment.OperatorSymbol}'.");
        }
    }

    private void EmitRuntimeIndexedWordAssignment(AssignmentSyntax assignment, string targetType, string context)
    {
        if (assignment.OperatorSymbol != "=")
        {
            throw new InvalidOperationException($"Game Boy target only supports direct '=' for 16-bit {context}.");
        }

        RequireExpressionPreservesHl(assignment.Right, context);
        EmitWordExpressionToHl(assignment.Right, targetType);
    }

    private void EmitWordExpressionToHl(ExpressionSyntax expression, string targetType)
    {
        if (TryConst(expression, out var constant))
        {
            builder.LoadAImmediate(constant & 0xFF);
            builder.StoreHlA();
            builder.IncrementHl();
            builder.LoadAImmediate((constant >> 8) & 0xFF);
            builder.StoreHlA();
            return;
        }

        if (TryDirectStorageExpression(expression, out var sourceAddress, out var sourceType))
        {
            builder.LoadA(sourceAddress);
            builder.StoreHlA();
            builder.IncrementHl();
            if (IsWordBackedType(sourceType))
            {
                builder.LoadA(HighAddress(sourceAddress));
            }
            else if (sourceType == "i8")
            {
                builder.LoadA(sourceAddress);
                EmitSignExtensionFromA();
            }
            else
            {
                builder.LoadAImmediate(0);
            }

            builder.StoreHlA();
            return;
        }

        EmitWordExpressionToStorage(expression, WordScratchLowAddress, targetType);
        builder.LoadA(WordScratchLowAddress);
        builder.StoreHlA();
        builder.IncrementHl();
        builder.LoadA(WordScratchHighAddress);
        builder.StoreHlA();
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
                throw new InvalidOperationException($"Unsupported Game Boy assignment operator '{assignment.OperatorSymbol}'.");
        }
    }

    private void EmitWordAssignment(AssignmentSyntax assignment, ushort address, string targetType)
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
                throw new InvalidOperationException($"Game Boy target does not support 16-bit assignment operator '{assignment.OperatorSymbol}'.");
        }
    }

    private ushort LValueAddress(LValue lValue)
    {
        return lValue switch
        {
            IdentifierLValue identifier => VariableAddress(identifier.Identifier),
            MemberAccessLValue memberAccess => VariableAddress(GameBoyVideoProgram.MemberAccessName(memberAccess.MemberAccess)),
            IndexLValue index => VariableAddress(IndexedElementName(index.BaseIdentifier, index.Index, $"{index.BaseIdentifier} array index")),
            _ => throw new InvalidOperationException("Game Boy target only supports assignments to local variables, struct fields, or constant array indices."),
        };
    }

    private string LValueStorageType(LValue lValue)
    {
        return lValue switch
        {
            IdentifierLValue identifier => VariableStorageType(identifier.Identifier),
            MemberAccessLValue memberAccess => VariableStorageType(GameBoyVideoProgram.MemberAccessName(memberAccess.MemberAccess)),
            IndexLValue index => VariableStorageType(IndexedElementName(index.BaseIdentifier, index.Index, $"{index.BaseIdentifier} array index")),
            _ => throw new InvalidOperationException("Game Boy target only supports assignments to local variables, struct fields, or constant array indices."),
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
            case "hud_set_tile":
                _ = ConsumeSdkOperation<Sdk2DOperation.SetHudTile>(call.Name);
                break;
            case "tilemap_fill_column":
                EmitTilemapFillColumn(call);
                break;
            case "map_stream_column":
                EmitNextSdkOperation<Sdk2DOperation.StreamMapColumn>(call.Name);
                break;
            case "camera_move_right":
                EmitCameraMoveRight(call);
                break;
            case "camera_move_left":
                EmitCameraMoveLeft(call);
                break;
            case "sprite_set":
                EmitSpriteSet(call);
                break;
            case "scroll_set":
                EmitScrollSet(call);
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

                throw new InvalidOperationException($"Unsupported Game Boy video API call '{call.Name}'.");
        }
    }

    private bool IsResourceDeclarationCall(FunctionCall call)
    {
        return program.Functions.TryGetValue(call.Name, out var function)
               && SdkResourceDeclarationResolver.TryResolve(function, out _);
    }

    private void EmitSdkOperation(Sdk2DOperation operation)
    {
        GameBoySdkOperationLowerer.Emit(this, operation);
    }

    private void EmitSdkAudioOperation(SdkAudioOperation operation)
    {
        GameBoySdkAudioOperationLowerer.Emit(this, operation);
    }

    private void EmitNextSdkOperation<T>(string callName)
        where T : Sdk2DOperation
    {
        EmitSdkOperation(ConsumeSdkOperation<T>(callName));
    }

    private T ConsumeSdkOperation<T>(string callName)
        where T : Sdk2DOperation
    {
        var operation = sdkOperations.ConsumeOperation(callName);
        if (operation is T typed)
        {
            return typed;
        }

        throw new InvalidOperationException(
            $"Game Boy SDK call '{callName}' expected collected operation {typeof(T).Name}, got {operation.GetType().Name}.");
    }

    private void EmitNextSdkAudioOperation<T>(string callName)
        where T : SdkAudioOperation
    {
        var operation = ConsumeSdkAudioOperation<T>(callName);
        if (operation is SdkAudioOperation.PlayMusic play && UsesMusicPlayHelpers)
        {
            builder.JumpAbsolute(0xCD, MusicPlayHelperLabel(play.ThemeId)); // CALL nn
            return;
        }

        if (operation is SdkAudioOperation.PlaySoundEffect sfx && UsesSoundEffectPlayHelpers)
        {
            builder.JumpAbsolute(0xCD, SoundEffectPlayHelperLabel(sfx.SoundId)); // CALL nn
            return;
        }

        if (operation is SdkAudioOperation.UpdateAudio && UsesAudioUpdateHelper)
        {
            builder.JumpAbsolute(0xCD, AudioUpdateHelperLabel); // CALL nn
            return;
        }

        EmitSdkAudioOperation(operation);
        EmitRestoreProgramTailBank();
    }

    private T ConsumeSdkAudioOperation<T>(string callName)
        where T : SdkAudioOperation
    {
        var operation = sdkAudioOperations.ConsumeOperation(callName);
        if (operation is T typed)
        {
            return typed;
        }

        throw new InvalidOperationException(
            $"Game Boy SDK audio call '{callName}' expected collected operation {typeof(T).Name}, got {operation.GetType().Name}.");
    }

    private void EnsureAllSdkOperationsConsumed()
    {
        sdkOperations.EnsureAllConsumed("Game Boy runtime");
    }

    private void EnsureAllSdkAudioOperationsConsumed()
    {
        sdkAudioOperations.EnsureAllConsumed("Game Boy runtime");
    }

    private void EmitRestoreProgramTailBank()
    {
        if (romLayout.ProgramTailBankCount == 0)
        {
            return;
        }

        builder.LoadA(GameBoyRomBuilder.ProgramCurrentBankAddress);
        EmitSelectRomBankFromA();
    }

    internal void EmitWaitFrame()
    {
        GameBoyRomBuilder.EmitWaitVBlank(builder, builder.CreateLabel("wait_vblank"));
    }

    internal void EmitInitializeAudio()
    {
        builder.LoadAImmediate(0x80);
        builder.StoreHighRamA(0x26);                // NR52: enable APU
        builder.LoadAImmediate(0xFF);
        builder.StoreHighRamA(0x25);                // NR51: route channels
        builder.LoadAImmediate(0x77);
        builder.StoreHighRamA(0x24);                // NR50: balanced master volume

        builder.LoadAImmediate(0);
        builder.StoreA(MusicActiveAddress);
        builder.StoreA(MusicRowAddress);
        builder.StoreA(MusicTickAddress);
        builder.StoreA(MusicDataPointerLowAddress);
        builder.StoreA(MusicDataPointerHighAddress);
        builder.StoreA(MusicCurrentPointerLowAddress);
        builder.StoreA(MusicCurrentPointerHighAddress);
        builder.StoreA(MusicScratchPointerLowAddress);
        builder.StoreA(MusicScratchPointerHighAddress);
        builder.StoreA(MusicTicksPerRowAddress);
        builder.StoreA(MusicRowHighAddress);
        builder.StoreA(MusicDataBankAddress);
        builder.StoreA(MusicCurrentBankAddress);
        builder.StoreA(MusicScratchBankAddress);
        builder.StoreA(SfxActiveAddress);
        builder.StoreA(SfxTickAddress);
        builder.StoreA(SfxDataPointerLowAddress);
        builder.StoreA(SfxDataPointerHighAddress);
        builder.StoreA(SfxCurrentPointerLowAddress);
        builder.StoreA(SfxCurrentPointerHighAddress);
        builder.StoreA(SfxDataBankAddress);
        builder.StoreA(SfxCurrentBankAddress);
        builder.StoreA((ushort)(Channel1ShadowBaseAddress + 0)); // NR10 shadow
        builder.StoreA((ushort)(Channel1ShadowBaseAddress + 1)); // NR11 shadow
        builder.StoreA((ushort)(Channel1ShadowBaseAddress + 2)); // NR12 shadow
        builder.StoreA((ushort)(Channel1ShadowBaseAddress + 3)); // NR13 shadow
        builder.StoreA((ushort)(Channel1ShadowBaseAddress + 4)); // NR14 shadow
        if (romLayout.UsesBankedMusic)
        {
            builder.StoreA(MusicDataCursorBankAddress);
            builder.StoreA(SfxDataCursorBankAddress);
        }

        EmitClearMusicRowCache();
    }

    internal void EmitPlayMusic(SdkAudioOperation.PlayMusic operation)
    {
        if (!program.MusicAssets.TryGetValue(operation.ThemeId, out var asset))
        {
            throw new InvalidOperationException($"Unknown Game Boy music asset '{operation.ThemeId}'. Declare it before playback.");
        }

        if (romLayout.UsesBankedMusic)
        {
            var placement = romLayout.MusicPlacement(operation.ThemeId);
            builder.LoadHl(placement.Address);
            builder.Emit(0x7D);                         // LD A,L
            builder.StoreA(MusicDataPointerLowAddress);
            builder.Emit(0x7C);                         // LD A,H
            builder.StoreA(MusicDataPointerHighAddress);
            builder.LoadAImmediate(placement.Bank);
            builder.StoreA(MusicDataBankAddress);
            builder.StoreA(MusicCurrentBankAddress);
            EmitSelectRomBankFromA();
        }
        else
        {
            builder.LoadHl(GameBoyRomBuilder.MusicLabel(operation.ThemeId));
            builder.Emit(0x7D);                         // LD A,L
            builder.StoreA(MusicDataPointerLowAddress);
            builder.Emit(0x7C);                         // LD A,H
            builder.StoreA(MusicDataPointerHighAddress);
        }

        builder.LoadAImmediate(asset.Kind == GameBoyMusicAssetKind.ApuTrace ? MusicActiveApuTrace : MusicActiveUgeRows);
        builder.StoreA(MusicActiveAddress);
        builder.LoadAImmediate(0);
        builder.StoreA(MusicRowAddress);
        builder.StoreA(MusicRowHighAddress);
        builder.StoreA(MusicTickAddress);
        if (asset.Kind == GameBoyMusicAssetKind.ApuTrace)
        {
            EmitResetApuTracePointerToStart();
        }
        else
        {
            EmitResetMusicRowPointer();
        }
    }

    internal void EmitPlaySoundEffect(SdkAudioOperation.PlaySoundEffect operation)
    {
        if (!program.SoundEffectAssets.TryGetValue(operation.SoundId, out _))
        {
            throw new InvalidOperationException($"Unknown Game Boy SFX asset '{operation.SoundId}'. Declare it before playback.");
        }

        if (romLayout.UsesBankedMusic)
        {
            var placement = romLayout.SoundEffectPlacement(operation.SoundId);
            builder.LoadHl(placement.Address);
            builder.Emit(0x7D);                         // LD A,L
            builder.StoreA(SfxDataPointerLowAddress);
            builder.Emit(0x7C);                         // LD A,H
            builder.StoreA(SfxDataPointerHighAddress);
            builder.LoadAImmediate(placement.Bank);
            builder.StoreA(SfxDataBankAddress);
            builder.StoreA(SfxCurrentBankAddress);
            EmitSelectRomBankFromA();
        }
        else
        {
            builder.LoadHl(GameBoyRomBuilder.SoundEffectLabel(operation.SoundId));
            builder.Emit(0x7D);                         // LD A,L
            builder.StoreA(SfxDataPointerLowAddress);
            builder.Emit(0x7C);                         // LD A,H
            builder.StoreA(SfxDataPointerHighAddress);
        }

        builder.LoadAImmediate(1);
        builder.StoreA(SfxActiveAddress);
        builder.LoadAImmediate(0);
        builder.StoreA(SfxTickAddress);
        EmitResetSfxApuTracePointerToStart();
    }

    internal void EmitStopMusic()
    {
        builder.LoadAImmediate(0);
        builder.StoreA(MusicActiveAddress);
        builder.StoreHighRamA(0x12);                // NR12: silence CH1 envelope
        builder.StoreHighRamA(0x17);                // NR22: silence CH2 envelope
        builder.StoreHighRamA(0x1A);                // NR30: disable CH3
        builder.StoreHighRamA(0x21);                // NR42: silence CH4 envelope
    }

    internal void EmitUpdateAudio()
    {
        var endLabel = builder.CreateLabel("audio_update_end");
        var sfxLabel = builder.CreateLabel("audio_update_sfx");
        var apuTraceLabel = builder.CreateLabel("audio_update_apu_trace");
        var loadRowLabel = builder.CreateLabel("audio_update_load_row");
        var rowReadyLabel = builder.CreateLabel("audio_update_row_ready");
        var rowHighMatchesLabel = builder.CreateLabel("audio_update_row_high_matches");
        var resetRowLabel = builder.CreateLabel("audio_update_reset_row");
        var rowIncrementDoneLabel = builder.CreateLabel("audio_update_row_increment_done");

        builder.LoadA(MusicActiveAddress);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, sfxLabel);       // JP Z,sfxLabel
        builder.CompareImmediate(MusicActiveApuTrace);
        builder.JumpAbsolute(0xCA, apuTraceLabel);  // JP Z,apuTraceLabel

        builder.LoadA(MusicTickAddress);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, loadRowLabel);   // JP Z,loadRowLabel
        builder.SubtractAImmediate(1);
        builder.StoreA(MusicTickAddress);
        builder.JumpAbsolute(sfxLabel);

        builder.Label(loadRowLabel);
        EmitLoadMusicPointerToHl(MusicDataCursorBankAddress);
        builder.LoadAFromHl();                      // ticks per row
        builder.StoreA(MusicTicksPerRowAddress);
        EmitAdvanceMusicHl(MusicDataCursorBankAddress);
        builder.LoadAFromHl();                      // row count low
        builder.LoadCFromA();
        EmitAdvanceMusicHl(MusicDataCursorBankAddress);
        builder.LoadAFromHl();                      // row count high
        builder.LoadBFromA();

        builder.LoadA(MusicRowHighAddress);
        builder.CompareB();
        builder.JumpAbsolute(0xDA, rowReadyLabel);  // JP C,rowReadyLabel
        builder.JumpAbsolute(0xCA, rowHighMatchesLabel); // JP Z,rowHighMatchesLabel
        builder.JumpAbsolute(resetRowLabel);

        builder.Label(rowHighMatchesLabel);
        builder.LoadA(MusicRowAddress);
        builder.Emit(0xB9);                         // CP C
        builder.JumpAbsolute(0xDA, rowReadyLabel);  // JP C,rowReadyLabel
        builder.Label(resetRowLabel);
        builder.LoadAImmediate(0);
        builder.StoreA(MusicRowAddress);
        builder.StoreA(MusicRowHighAddress);
        EmitResetMusicRowPointer();

        builder.Label(rowReadyLabel);
        EmitLoadMusicRowEventsToCache();
        builder.LoadHl(MusicRowCacheStartAddress);

        EmitWriteMusicRegister(0x11);
        EmitWriteMusicRegister(0x12);
        EmitWriteMusicRegister(0x13);
        EmitWriteMusicRegister(0x14);
        EmitWriteMusicRegister(0x16);
        EmitWriteMusicRegister(0x17);
        EmitWriteMusicRegister(0x18);
        EmitWriteMusicRegister(0x19);
        EmitWriteWaveChannelFromRow();
        EmitWriteMusicRegister(0x21);
        EmitWriteMusicRegister(0x22);
        EmitWriteMusicRegister(0x23);

        EmitClearMusicRowTriggerBits();

        builder.LoadA(MusicRowAddress);
        builder.AddAImmediate(1);
        builder.StoreA(MusicRowAddress);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, rowIncrementDoneLabel); // JP NZ,rowIncrementDoneLabel
        builder.LoadA(MusicRowHighAddress);
        builder.AddAImmediate(1);
        builder.StoreA(MusicRowHighAddress);
        builder.Label(rowIncrementDoneLabel);

        builder.LoadA(MusicTicksPerRowAddress);
        builder.SubtractAImmediate(1);
        builder.StoreA(MusicTickAddress);
        builder.JumpAbsolute(sfxLabel);

        builder.Label(apuTraceLabel);
        EmitUpdateApuTrace(sfxLabel);

        builder.Label(sfxLabel);
        EmitUpdateSoundEffectApuTrace(endLabel);
        builder.Label(endLabel);
    }

    private void EmitUpdateApuTrace(string endLabel)
    {
        var processLabel = builder.CreateLabel("audio_update_apu_process");
        var commandLoopLabel = builder.CreateLabel("audio_update_apu_command_loop");
        var waveBlockLabel = builder.CreateLabel("audio_update_apu_wave_block");
        var commandDoneLabel = builder.CreateLabel("audio_update_apu_command_done");
        var loopLabel = builder.CreateLabel("audio_update_apu_loop");

        builder.LoadA(MusicTickAddress);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, processLabel);   // JP Z,processLabel
        builder.SubtractAImmediate(1);
        builder.StoreA(MusicTickAddress);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, endLabel);       // JP NZ,endLabel

        // Read the next order entry: { bodyOffset (u16 from data start), waitAfter }.
        builder.Label(processLabel);
        EmitLoadMusicCurrentPointerToHl();          // HL -> order entry
        builder.LoadAFromHl();                      // body offset low
        builder.LoadEFromA();
        EmitAdvanceMusicHl();
        builder.LoadAFromHl();                      // body offset high
        builder.LoadDFromA();
        builder.Emit(0xB3);                         // OR E: body offset zero is the loop sentinel
        builder.JumpAbsolute(0xCA, loopLabel);      // JP Z,loopLabel
        EmitAdvanceMusicHl();                       // HL -> waitAfter
        builder.LoadAFromHl();
        builder.StoreA(MusicTickAddress);
        EmitAdvanceMusicHl();                       // HL -> next order entry
        EmitStoreHlToMusicCurrentPointer();

        // Resolve the pooled group body via a transient cursor bank, leaving the order-stream bank
        // (MusicCurrentBankAddress) untouched so the next entry is still read from the right bank.
        EmitLoadMusicDataOffsetToHl(MusicDataCursorBankAddress);
        builder.LoadAFromHl();                      // command count
        builder.LoadBFromA();
        EmitAdvanceMusicHl(MusicDataCursorBankAddress); // HL -> first command

        builder.Label(commandLoopLabel);
        builder.LoadAFromHl();                      // register offset or wave RAM block command
        builder.CompareImmediate(ApuTraceWaveRamBlockCommand);
        builder.JumpAbsolute(0xCA, waveBlockLabel); // JP Z,waveBlockLabel
        builder.LoadCFromA();
        EmitAdvanceMusicHl(MusicDataCursorBankAddress);
        builder.LoadAFromHl();                      // value
        EmitMusicApuRegisterWrite();                // LDH (C),A, muting/shadowing channel 1 for SFX
        EmitAdvanceMusicHl(MusicDataCursorBankAddress);
        builder.JumpAbsolute(commandDoneLabel);

        builder.Label(waveBlockLabel);
        EmitAdvanceMusicHl(MusicDataCursorBankAddress);
        for (var i = 0; i < 16; i++)
        {
            builder.LoadAFromHl();
            builder.StoreHighRamA((byte)(0x30 + i));
            EmitAdvanceMusicHl(MusicDataCursorBankAddress);
        }

        builder.Label(commandDoneLabel);
        builder.Emit(0x05);                         // DEC B
        builder.LoadAFromB();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, commandLoopLabel); // JP NZ,commandLoopLabel

        builder.LoadA(MusicTickAddress);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, processLabel);   // JP Z,processLabel: drain zero-wait order entries
        builder.JumpAbsolute(endLabel);

        builder.Label(loopLabel);
        EmitResetApuTracePointerToLoop();
        builder.JumpAbsolute(endLabel);
    }

    // Writes the music's APU register value (A) to register offset (C). Without SFX assets this is a
    // plain LDH (C),A. With SFX, channel 1 ($FF10-$FF14) gets priority for effects: the music always
    // shadows its full channel 1 state (so the channel can be restored when an effect ends) and, while
    // an effect owns channel 1 (SfxActive != 0), the music's channel 1 writes are suppressed so the
    // effect note is not stomped. Every other register (channels 2-4 and the globals NR50/NR51/NR52) is
    // written normally. Uses D as a scratch for the value; D is free inside the music command loop
    // (only B/C/HL are live there), and HL (the data cursor) is preserved across the shadow store.
    private void EmitMusicApuRegisterWrite()
    {
        if (program.SoundEffectAssetsInLoadOrder.Count == 0)
        {
            builder.StoreHighRamCFromA();           // LDH (C),A
            return;
        }

        var writeHwLabel = builder.CreateLabel("music_apu_write_hw");
        var skipLabel = builder.CreateLabel("music_apu_skip");

        builder.LoadDFromA();                       // D = value (A/flags get clobbered)
        builder.LoadAFromC();                       // A = register offset
        builder.SubtractAImmediate(0x10);           // A = offset - $10
        builder.CompareImmediate(0x05);             // carry set if A < 5 (channel 1: offsets $10..$14)
        builder.JumpRelative(0x30, writeHwLabel);   // JR NC -> not channel 1, write hardware

        // Channel 1: shadow the value at $C200 + offset (== $C200 + C). The shadow page low byte equals
        // the register offset, so the whole NR10-NR14 state is captured with no per-register branching.
        builder.PushHl();                           // preserve the music data cursor
        builder.Emit(0x26, Channel1ShadowPageHigh); // LD H, $C2
        builder.Emit(0x69);                         // LD L, C
        builder.Emit(0x72);                         // LD (HL), D  -> shadow[$C200+offset] = value
        builder.PopHl();

        builder.LoadA(SfxActiveAddress);
        builder.CompareImmediate(0x00);
        builder.JumpRelative(0x20, skipLabel);      // JR NZ -> SFX owns channel 1, skip hardware write

        builder.Label(writeHwLabel);
        builder.LoadAFromD();                       // A = value
        builder.StoreHighRamCFromA();               // LDH (C),A

        builder.Label(skipLabel);
    }

    private void EmitUpdateSoundEffectApuTrace(string endLabel)
    {
        var processLabel = builder.CreateLabel("sfx_update_apu_process");
        var commandLoopLabel = builder.CreateLabel("sfx_update_apu_command_loop");
        var waveBlockLabel = builder.CreateLabel("sfx_update_apu_wave_block");
        var commandDoneLabel = builder.CreateLabel("sfx_update_apu_command_done");
        var stopLabel = builder.CreateLabel("sfx_update_stop");

        builder.LoadA(SfxActiveAddress);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, endLabel);       // JP Z,endLabel

        builder.LoadA(SfxTickAddress);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, processLabel);   // JP Z,processLabel
        builder.SubtractAImmediate(1);
        builder.StoreA(SfxTickAddress);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, endLabel);       // JP NZ,endLabel

        builder.Label(processLabel);
        EmitLoadSfxCurrentPointerToHl();            // HL -> order entry
        builder.LoadAFromHl();                      // body offset low
        builder.LoadEFromA();
        EmitAdvanceSfxHl();
        builder.LoadAFromHl();                      // body offset high
        builder.LoadDFromA();
        builder.Emit(0xB3);                         // OR E: body offset zero is the one-shot sentinel
        builder.JumpAbsolute(0xCA, stopLabel);      // JP Z,stopLabel
        EmitAdvanceSfxHl();                         // HL -> waitAfter
        builder.LoadAFromHl();
        builder.StoreA(SfxTickAddress);
        EmitAdvanceSfxHl();                         // HL -> next order entry
        EmitStoreHlToSfxCurrentPointer();

        EmitLoadSfxDataOffsetToHl(SfxDataCursorBankAddress);
        builder.LoadAFromHl();                      // command count
        builder.LoadBFromA();
        EmitAdvanceSfxHl(SfxDataCursorBankAddress); // HL -> first command

        builder.Label(commandLoopLabel);
        builder.LoadAFromHl();                      // register offset or wave RAM block command
        builder.CompareImmediate(ApuTraceWaveRamBlockCommand);
        builder.JumpAbsolute(0xCA, waveBlockLabel); // JP Z,waveBlockLabel
        builder.LoadCFromA();
        EmitAdvanceSfxHl(SfxDataCursorBankAddress);
        builder.LoadAFromHl();                      // value
        builder.StoreHighRamCFromA();               // LDH (C),A
        EmitAdvanceSfxHl(SfxDataCursorBankAddress);
        builder.JumpAbsolute(commandDoneLabel);

        builder.Label(waveBlockLabel);
        EmitAdvanceSfxHl(SfxDataCursorBankAddress);
        for (var i = 0; i < 16; i++)
        {
            builder.LoadAFromHl();
            builder.StoreHighRamA((byte)(0x30 + i));
            EmitAdvanceSfxHl(SfxDataCursorBankAddress);
        }

        builder.Label(commandDoneLabel);
        builder.Emit(0x05);                         // DEC B
        builder.LoadAFromB();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, commandLoopLabel); // JP NZ,commandLoopLabel

        builder.LoadA(SfxTickAddress);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, processLabel);   // JP Z,processLabel: drain zero-wait order entries
        builder.JumpAbsolute(endLabel);

        builder.Label(stopLabel);
        builder.LoadAImmediate(0);
        builder.StoreA(SfxActiveAddress);
        builder.StoreA(SfxTickAddress);
        builder.StoreA(SfxCurrentPointerLowAddress);
        builder.StoreA(SfxCurrentPointerHighAddress);
        builder.StoreA(SfxCurrentBankAddress);
        // Release channel 1 back to the BGM: restore its full shadowed state (NR10-NR14) so the melody
        // is not left carrying the effect's sweep/duty/envelope. NR14 is restored with the shadowed
        // trigger bit, which reloads NR12's envelope, fully re-establishing the BGM's channel 1; the
        // BGM's next note re-writes them anyway.
        builder.LoadA((ushort)(Channel1ShadowBaseAddress + 0));
        builder.StoreHighRamA(0x10);                // NR10 (sweep)
        builder.LoadA((ushort)(Channel1ShadowBaseAddress + 1));
        builder.StoreHighRamA(0x11);                // NR11 (duty + length)
        builder.LoadA((ushort)(Channel1ShadowBaseAddress + 2));
        builder.StoreHighRamA(0x12);                // NR12 (envelope)
        builder.LoadA((ushort)(Channel1ShadowBaseAddress + 3));
        builder.StoreHighRamA(0x13);                // NR13 (frequency low)
        builder.LoadA((ushort)(Channel1ShadowBaseAddress + 4));
        builder.StoreHighRamA(0x14);                // NR14 (frequency high + trigger)
        builder.JumpAbsolute(endLabel);
    }

    private void EmitLoadMusicRowEventsToCache()
    {
        EmitLoadMusicCurrentPointerToHl();
        builder.LoadAFromHl();                      // row event mask
        builder.StoreA(MusicRowMaskAddress);
        EmitAdvanceMusicHl();

        EmitCopyMusicChannelEventToCache(0x01, MusicRowCacheStartAddress, 4);
        EmitCopyMusicChannelEventToCache(0x02, (ushort)(MusicRowCacheStartAddress + 4), 4);
        EmitCopyMusicChannelEventToCache(0x04, (ushort)(MusicRowCacheStartAddress + 8), 4);
        EmitCopyMusicChannelEventToCache(0x08, (ushort)(MusicRowCacheStartAddress + 12), 3);

        EmitStoreHlToMusicCurrentPointer();
    }

    private void EmitCopyMusicChannelEventToCache(byte mask, ushort cacheAddress, int byteCount)
    {
        var skipLabel = builder.CreateLabel("audio_row_event_skip");
        builder.LoadA(MusicRowMaskAddress);
        builder.AndImmediate(mask);
        builder.JumpAbsolute(0xCA, skipLabel);      // JP Z,skipLabel
        for (var i = 0; i < byteCount; i++)
        {
            builder.LoadAFromHl();
            builder.StoreA((ushort)(cacheAddress + i));
            EmitAdvanceMusicHl();
        }

        builder.Label(skipLabel);
    }

    private void EmitClearMusicRowTriggerBits()
    {
        EmitClearMusicTriggerBit((ushort)(MusicRowCacheStartAddress + 3));
        EmitClearMusicTriggerBit((ushort)(MusicRowCacheStartAddress + 7));
        EmitClearMusicTriggerBit((ushort)(MusicRowCacheStartAddress + 11));
        EmitClearMusicTriggerBit((ushort)(MusicRowCacheStartAddress + 14));
    }

    private void EmitClearMusicTriggerBit(ushort address)
    {
        builder.LoadA(address);
        builder.AndImmediate(0x7F);
        builder.StoreA(address);
    }

    private void EmitClearMusicRowCache()
    {
        builder.LoadAImmediate(0);
        builder.StoreA(MusicRowMaskAddress);
        for (var i = 0; i < MusicRowCacheLength; i++)
        {
            builder.StoreA((ushort)(MusicRowCacheStartAddress + i));
        }
    }

    private void EmitLoadMusicPointerToHl(ushort cursorBankAddress)
    {
        if (romLayout.UsesBankedMusic)
        {
            builder.LoadA(MusicDataBankAddress);
            builder.StoreA(cursorBankAddress);
            EmitSelectRomBankFromA();
        }

        builder.LoadA(MusicDataPointerLowAddress);
        builder.LoadLFromA();
        builder.LoadA(MusicDataPointerHighAddress);
        builder.Emit(0x67);                         // LD H,A
    }

    private void EmitLoadMusicCurrentPointerToHl()
    {
        if (romLayout.UsesBankedMusic)
        {
            builder.LoadA(MusicCurrentBankAddress);
            EmitSelectRomBankFromA();
        }

        builder.LoadA(MusicCurrentPointerLowAddress);
        builder.LoadLFromA();
        builder.LoadA(MusicCurrentPointerHighAddress);
        builder.Emit(0x67);                         // LD H,A
    }

    private void EmitResetMusicRowPointer()
    {
        builder.LoadDe((ushort)(MusicHeaderLength + MusicWaveTableBytes));
        EmitLoadMusicDataOffsetToHl(MusicCurrentBankAddress);
        EmitStoreHlToMusicCurrentPointer();
        EmitClearMusicRowCache();
    }

    private void EmitResetApuTracePointerToStart()
    {
        // Order stream pointer = dataPointer + orderStartOffset (header bytes 1..2).
        EmitLoadMusicPointerToHl(MusicDataCursorBankAddress);
        EmitAdvanceMusicHl(MusicDataCursorBankAddress); // HL -> orderStart low
        builder.LoadAFromHl();
        builder.LoadEFromA();
        EmitAdvanceMusicHl(MusicDataCursorBankAddress); // HL -> orderStart high
        builder.LoadAFromHl();
        builder.LoadDFromA();

        EmitLoadMusicDataOffsetToHl(MusicCurrentBankAddress);
        EmitStoreHlToMusicCurrentPointer();
        EmitClearMusicRowCache();
    }

    private void EmitResetSfxApuTracePointerToStart()
    {
        // Order stream pointer = dataPointer + orderStartOffset (header bytes 1..2).
        EmitLoadSfxPointerToHl(SfxDataCursorBankAddress);
        EmitAdvanceSfxHl(SfxDataCursorBankAddress); // HL -> orderStart low
        builder.LoadAFromHl();
        builder.LoadEFromA();
        EmitAdvanceSfxHl(SfxDataCursorBankAddress); // HL -> orderStart high
        builder.LoadAFromHl();
        builder.LoadDFromA();

        EmitLoadSfxDataOffsetToHl(SfxCurrentBankAddress);
        EmitStoreHlToSfxCurrentPointer();
    }

    private void EmitResetApuTracePointerToLoop()
    {
        // Order stream pointer = dataPointer + loopOrderOffset (header bytes 3..4).
        EmitLoadMusicPointerToHl(MusicDataCursorBankAddress);
        EmitAdvanceMusicHl(MusicDataCursorBankAddress); // HL -> byte 1
        EmitAdvanceMusicHl(MusicDataCursorBankAddress); // HL -> byte 2
        EmitAdvanceMusicHl(MusicDataCursorBankAddress); // HL -> loopOrder low (byte 3)
        builder.LoadAFromHl();
        builder.LoadEFromA();
        EmitAdvanceMusicHl(MusicDataCursorBankAddress); // HL -> loopOrder high (byte 4)
        builder.LoadAFromHl();
        builder.LoadDFromA();

        EmitLoadMusicDataOffsetToHl(MusicCurrentBankAddress);
        EmitStoreHlToMusicCurrentPointer();
    }

    private void EmitStoreHlToMusicCurrentPointer()
    {
        builder.Emit(0x7D);                         // LD A,L
        builder.StoreA(MusicCurrentPointerLowAddress);
        builder.Emit(0x7C);                         // LD A,H
        builder.StoreA(MusicCurrentPointerHighAddress);
    }

    private void EmitStoreHlToMusicScratchPointer()
    {
        builder.Emit(0x7D);                         // LD A,L
        builder.StoreA(MusicScratchPointerLowAddress);
        builder.Emit(0x7C);                         // LD A,H
        builder.StoreA(MusicScratchPointerHighAddress);
        if (romLayout.UsesBankedMusic)
        {
            builder.LoadA(MusicCurrentBankAddress);
            builder.StoreA(MusicScratchBankAddress);
        }
    }

    private void EmitLoadMusicScratchPointerToHl()
    {
        if (romLayout.UsesBankedMusic)
        {
            builder.LoadA(MusicScratchBankAddress);
            EmitSelectRomBankFromA();
        }

        builder.LoadA(MusicScratchPointerLowAddress);
        builder.LoadLFromA();
        builder.LoadA(MusicScratchPointerHighAddress);
        builder.Emit(0x67);                         // LD H,A
    }

    private void EmitLoadSfxPointerToHl(ushort cursorBankAddress)
    {
        if (romLayout.UsesBankedMusic)
        {
            builder.LoadA(SfxDataBankAddress);
            builder.StoreA(cursorBankAddress);
            EmitSelectRomBankFromA();
        }

        builder.LoadA(SfxDataPointerLowAddress);
        builder.LoadLFromA();
        builder.LoadA(SfxDataPointerHighAddress);
        builder.Emit(0x67);                         // LD H,A
    }

    private void EmitLoadSfxCurrentPointerToHl()
    {
        if (romLayout.UsesBankedMusic)
        {
            builder.LoadA(SfxCurrentBankAddress);
            EmitSelectRomBankFromA();
        }

        builder.LoadA(SfxCurrentPointerLowAddress);
        builder.LoadLFromA();
        builder.LoadA(SfxCurrentPointerHighAddress);
        builder.Emit(0x67);                         // LD H,A
    }

    private void EmitStoreHlToSfxCurrentPointer()
    {
        builder.Emit(0x7D);                         // LD A,L
        builder.StoreA(SfxCurrentPointerLowAddress);
        builder.Emit(0x7C);                         // LD A,H
        builder.StoreA(SfxCurrentPointerHighAddress);
    }

    private void EmitLoadMusicDataOffsetToHl(ushort targetBankAddress)
    {
        if (!romLayout.UsesBankedMusic)
        {
            EmitLoadMusicPointerToHl(targetBankAddress);
            builder.AddHlDe();
            return;
        }

        // Resolve a base-relative offset (DE) into the banked window: the absolute ROM bank is
        // dataBank + (DE >> 14) and the in-window address is 0x4000 | (DE & 0x3FFF). The result bank
        // is written to targetBankAddress so the caller's own cursor bank is never disturbed.
        builder.LoadA(MusicDataBankAddress);
        builder.LoadCFromA();
        builder.LoadAFromD();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.AddAFromC();
        builder.StoreA(targetBankAddress);
        EmitSelectRomBankFromA();
        builder.LoadAFromD();
        builder.AndImmediate(0x3F);
        builder.OrImmediate(0x40);
        builder.Emit(0x67);                         // LD H,A
        builder.LoadLFromE();
    }

    private void EmitLoadSfxDataOffsetToHl(ushort targetBankAddress)
    {
        if (!romLayout.UsesBankedMusic)
        {
            EmitLoadSfxPointerToHl(targetBankAddress);
            builder.AddHlDe();
            return;
        }

        builder.LoadA(SfxDataBankAddress);
        builder.LoadCFromA();
        builder.LoadAFromD();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.AddAFromC();
        builder.StoreA(targetBankAddress);
        EmitSelectRomBankFromA();
        builder.LoadAFromD();
        builder.AndImmediate(0x3F);
        builder.OrImmediate(0x40);
        builder.Emit(0x67);                         // LD H,A
        builder.LoadLFromE();
    }

    // Advances the persistent current/order/row cursor (HL) and its bank (MusicCurrentBankAddress).
    private void EmitAdvanceMusicHl() => EmitAdvanceMusicHl(MusicCurrentBankAddress);

    private void EmitAdvanceMusicHl(ushort cursorBankAddress)
    {
        builder.Emit(0x23);                         // INC HL
        if (!romLayout.UsesBankedMusic)
        {
            return;
        }

        // Crossing 0x8000 means the cursor walked past its 16 KiB window: rewind HL to 0x4000 and
        // advance the cursor's own bank so sequential reads continue transparently.
        var endLabel = builder.CreateLabel("music_bank_advance_end");
        builder.Emit(0x7C);                         // LD A,H
        builder.CompareImmediate(0x80);
        builder.JumpAbsolute(0xC2, endLabel);       // JP NZ,endLabel
        builder.LoadHImmediate(0x40);
        builder.LoadA(cursorBankAddress);
        builder.AddAImmediate(1);
        builder.StoreA(cursorBankAddress);
        EmitSelectRomBankFromA();
        builder.Label(endLabel);
    }

    private void EmitAdvanceSfxHl() => EmitAdvanceSfxHl(SfxCurrentBankAddress);

    private void EmitAdvanceSfxHl(ushort cursorBankAddress)
    {
        builder.Emit(0x23);                         // INC HL
        if (!romLayout.UsesBankedMusic)
        {
            return;
        }

        var endLabel = builder.CreateLabel("sfx_bank_advance_end");
        builder.Emit(0x7C);                         // LD A,H
        builder.CompareImmediate(0x80);
        builder.JumpAbsolute(0xC2, endLabel);       // JP NZ,endLabel
        builder.LoadHImmediate(0x40);
        builder.LoadA(cursorBankAddress);
        builder.AddAImmediate(1);
        builder.StoreA(cursorBankAddress);
        EmitSelectRomBankFromA();
        builder.Label(endLabel);
    }

    private void EmitSelectRomBankFromA()
    {
        builder.StoreA(GameBoyRomBuilder.RomBankSelectAddress);
    }

    private void EmitWriteMusicRegister(byte register)
    {
        builder.LoadAFromHl();
        builder.StoreHighRamA(register);
        builder.Emit(0x23);                         // INC HL
    }

    private void EmitWriteWaveChannelFromRow()
    {
        builder.LoadAFromHl();                      // wave index
        builder.LoadBFromA();
        builder.Emit(0x23);                         // INC HL
        EmitStoreHlToMusicScratchPointer();

        builder.LoadAImmediate(0);
        builder.StoreHighRamA(0x1A);                // NR30: disable CH3 before wave RAM writes
        builder.LoadAFromB();
        builder.SwapA();
        builder.AndImmediate(0xF0);
        builder.AddAImmediate(MusicHeaderLength);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        EmitLoadMusicDataOffsetToHl(MusicDataCursorBankAddress);
        for (var i = 0; i < 16; i++)
        {
            builder.LoadAFromHl();
            builder.StoreHighRamA((byte)(0x30 + i));
            EmitAdvanceMusicHl(MusicDataCursorBankAddress);
        }

        builder.LoadAImmediate(0x80);
        builder.StoreHighRamA(0x1A);                // NR30: enable CH3
        builder.LoadAImmediate(0);
        builder.StoreHighRamA(0x1B);                // NR31: full length

        EmitLoadMusicScratchPointerToHl();
        EmitWriteMusicRegister(0x1C);
        EmitWriteMusicRegister(0x1D);
        EmitWriteMusicRegister(0x1E);
    }

    private bool TryEmitTargetIntrinsic(FunctionCall call)
    {
        if (!program.Functions.TryGetValue(call.Name, out var function) || !function.IsExtern)
        {
            return false;
        }

        var intrinsic = TargetIntrinsicResolver.Resolve(function, GameBoyTarget.Intrinsics);
        GameBoyVideoProgram.RequireArity(call, intrinsic.Arity);
        switch (intrinsic.Operation)
        {
            case TargetIntrinsicOperation.InitializeVideo:
            case TargetIntrinsicOperation.PresentVideo:
                return true;
            case TargetIntrinsicOperation.WaitFrame:
                EmitNextSdkOperation<Sdk2DOperation.WaitFrame>(call.Name);
                return true;
            case TargetIntrinsicOperation.PollInput:
                EmitNextSdkOperation<Sdk2DOperation.PollInput>(call.Name);
                return true;
            case TargetIntrinsicOperation.UpdateAudio:
                EmitNextSdkAudioOperation<SdkAudioOperation.UpdateAudio>(call.Name);
                return true;
            case TargetIntrinsicOperation.InitializeAudio:
                EmitNextSdkAudioOperation<SdkAudioOperation.InitializeAudio>(call.Name);
                return true;
            case TargetIntrinsicOperation.PlayMusic:
                EmitNextSdkAudioOperation<SdkAudioOperation.PlayMusic>(call.Name);
                return true;
            case TargetIntrinsicOperation.PlaySoundEffect:
                EmitNextSdkAudioOperation<SdkAudioOperation.PlaySoundEffect>(call.Name);
                return true;
            case TargetIntrinsicOperation.StopMusic:
                EmitNextSdkAudioOperation<SdkAudioOperation.StopMusic>(call.Name);
                return true;
            case TargetIntrinsicOperation.InitializeCamera:
                EmitCameraInit(call);
                return true;
            case TargetIntrinsicOperation.SetCameraPosition:
                EmitNextSdkOperation<Sdk2DOperation.SetCameraPosition>(call.Name);
                return true;
            case TargetIntrinsicOperation.ApplyCamera:
                EmitNextSdkOperation<Sdk2DOperation.ApplyCamera>(call.Name);
                return true;
            case TargetIntrinsicOperation.DrawLogicalSprite:
                EmitNextSdkOperation<Sdk2DOperation.DrawLogicalSprite>(call.Name);
                return true;
            default:
                throw new NotSupportedException($"Game Boy intrinsic lowering does not support {intrinsic.Operation} yet.");
        }
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

        if (program.SubroutineNames.Contains(function.Name))
        {
            EmitUserSubroutineCall(call, function);
            return true;
        }

        if (!userFunctionCallStack.Add(function.Name))
        {
            throw new InvalidOperationException($"Recursive Game Boy user function call '{function.Name}' is not supported.");
        }

        try
        {
            PushInlineVariableScope();
            EmitBlock(ParameterSubstitution.Substitute(function, call, "Game Boy"));
        }
        finally
        {
            PopInlineVariableScope();
            userFunctionCallStack.Remove(function.Name);
        }

        return true;
    }

    private void EmitUserSubroutineCall(FunctionCall call, FunctionSyntax function)
    {
        var arguments = BindCallArguments(function, call);
        foreach (var parameter in function.Parameters)
        {
            if (parameter.IsReceiver)
            {
                throw new InvalidOperationException($"Game Boy subroutine '{function.Name}' cannot use receiver parameter '{parameter.Name}' yet.");
            }

            var slot = EnsureSubroutineParameterSlot(function, parameter);
            EmitExpressionToStorage(arguments[parameter.Name], slot, parameter.Type);
        }

        sdkOperations.ConsumeSubroutineCall(function.Name);
        sdkAudioOperations.ConsumeSubroutineCall(function.Name);
        builder.JumpAbsolute(0xCD, SubroutineEntryLabel(function.Name)); // CALL nn
    }

    private string SubroutineEntryLabel(string functionName)
    {
        return UsesSubroutineTrampolines
            ? SubroutineTrampolineLabel(functionName)
            : SubroutineLabel(functionName);
    }

    private static string SubroutineLabel(string functionName) => $"user_fn_{functionName}";

    private static string SubroutineTrampolineLabel(string functionName) => $"user_fn_{functionName}_trampoline";

    private static bool IsRuntimeReadOnlyDataLabel(string label)
    {
        return label.StartsWith("map_row_", StringComparison.Ordinal)
               || label.StartsWith("background_stream_row_", StringComparison.Ordinal);
    }

    private static string ReadOnlyDataByteReaderLabel(string label) => $"read_data_{label}";

    private static string MusicPlayHelperLabel(string themeId) => $"music_play_fixed_{themeId}";

    private static string SoundEffectPlayHelperLabel(string soundId) => $"sfx_play_fixed_{soundId}";

    private const string AudioUpdateHelperLabel = "audio_update_fixed_helper";

    private BlockSyntax SubroutineBody(FunctionSyntax function)
    {
        var slotArguments = function.Parameters
            .Select(parameter => (ExpressionSyntax)new IdentifierSyntax(SubroutineParameterSlotName(function, parameter)))
            .ToArray();
        return ParameterSubstitution.Substitute(function, new FunctionCall(function.Name, slotArguments), "Game Boy");
    }

    private ushort EnsureSubroutineParameterSlot(FunctionSyntax function, ParameterSyntax parameter)
    {
        if (!IsByteBackedLocalType(parameter.Type))
        {
            throw new InvalidOperationException($"Game Boy subroutine '{function.Name}' parameter '{parameter.Name}' has unsupported type '{parameter.Type}'.");
        }

        var name = SubroutineParameterSlotName(function, parameter);
        return variables.TryGetValue(name, out var address)
            ? address
            : DeclareVariable(name, parameter.Type);
    }

    private IReadOnlyList<(string Name, bool HadPrevious, ushort PreviousAddress, bool HadPreviousType, string PreviousType)> PushSubroutineParameterAliases(FunctionSyntax function)
    {
        var aliases = new List<(string Name, bool HadPrevious, ushort PreviousAddress, bool HadPreviousType, string PreviousType)>();
        foreach (var parameter in function.Parameters)
        {
            if (parameter.IsReceiver)
            {
                continue;
            }

            var hadPrevious = variables.TryGetValue(parameter.Name, out var previousAddress);
            var hadPreviousType = variableTypes.TryGetValue(parameter.Name, out var previousType);
            aliases.Add((parameter.Name, hadPrevious, previousAddress, hadPreviousType, previousType ?? string.Empty));
            variables[parameter.Name] = EnsureSubroutineParameterSlot(function, parameter);
            variableTypes[parameter.Name] = parameter.Type;
        }

        return aliases;
    }

    private void PopSubroutineParameterAliases(IReadOnlyList<(string Name, bool HadPrevious, ushort PreviousAddress, bool HadPreviousType, string PreviousType)> aliases)
    {
        for (var index = aliases.Count - 1; index >= 0; index--)
        {
            var alias = aliases[index];
            if (alias.HadPrevious)
            {
                variables[alias.Name] = alias.PreviousAddress;
            }
            else
            {
                variables.Remove(alias.Name);
            }

            if (alias.HadPreviousType)
            {
                variableTypes[alias.Name] = alias.PreviousType;
            }
            else
            {
                variableTypes.Remove(alias.Name);
            }
        }
    }

    private static string SubroutineParameterSlotName(FunctionSyntax function, ParameterSyntax parameter)
    {
        return $"{function.Name}__p_{parameter.Name}";
    }

    private static IReadOnlyDictionary<string, ExpressionSyntax> BindCallArguments(FunctionSyntax function, FunctionCall call)
    {
        var parameters = function.Parameters.ToList();
        var parameterNames = parameters.Select(parameter => parameter.Name).ToHashSet(StringComparer.Ordinal);
        var arguments = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
        var positionalIndex = 0;
        var namedArgumentSeen = false;

        foreach (var argument in call.Parameters)
        {
            if (argument is NamedArgumentSyntax namedArgument)
            {
                namedArgumentSeen = true;
                if (!parameterNames.Contains(namedArgument.Name))
                {
                    throw new InvalidOperationException($"Game Boy target call '{call.Name}' has no parameter named '{namedArgument.Name}'.");
                }

                if (!arguments.TryAdd(namedArgument.Name, namedArgument.Expression))
                {
                    throw new InvalidOperationException($"Game Boy target call '{call.Name}' supplies parameter '{namedArgument.Name}' more than once.");
                }

                continue;
            }

            if (namedArgumentSeen)
            {
                throw new InvalidOperationException($"Game Boy target call '{call.Name}' cannot use positional arguments after named arguments.");
            }

            if (positionalIndex >= parameters.Count)
            {
                throw new InvalidOperationException($"Game Boy target expected at most {parameters.Count} argument(s) for '{call.Name}', but got {call.Parameters.Count()}.");
            }

            arguments.Add(parameters[positionalIndex].Name, argument);
            positionalIndex++;
        }

        var substitutions = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
        foreach (var parameter in parameters)
        {
            if (arguments.TryGetValue(parameter.Name, out var argument))
            {
                substitutions.Add(parameter.Name, argument);
                continue;
            }

            if (!parameter.DefaultValue.HasValue)
            {
                throw new InvalidOperationException($"Game Boy target expected argument for '{call.Name}' because parameter '{parameter.Name}' has no default value.");
            }

            var defaultValue = ConstantFolder.FoldConstants(SubstituteDefaultValue(parameter.DefaultValue.Value, substitutions));
            substitutions.Add(parameter.Name, defaultValue);
        }

        return substitutions;
    }

    private static ExpressionSyntax SubstituteDefaultValue(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, ExpressionSyntax> substitutions)
    {
        return expression switch
        {
            IdentifierSyntax identifier when substitutions.TryGetValue(identifier.Identifier, out var value) => value,
            BinaryExpressionSyntax binary => new BinaryExpressionSyntax(
                SubstituteDefaultValue(binary.Left, substitutions),
                SubstituteDefaultValue(binary.Right, substitutions),
                binary.Operator),
            CastSyntax cast => new CastSyntax(cast.Type, SubstituteDefaultValue(cast.Expression, substitutions)),
            NamedArgumentSyntax namedArgument => new NamedArgumentSyntax(namedArgument.Name, SubstituteDefaultValue(namedArgument.Expression, substitutions)),
            _ => expression,
        };
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
            throw new InvalidOperationException($"Recursive Game Boy user function call '{function.Name}' is not supported.");
        }

        try
        {
            EmitExpressionToA(ParameterSubstitution.SubstituteReturnExpression(function, call, "Game Boy"));
        }
        finally
        {
            userFunctionCallStack.Remove(function.Name);
        }

        return true;
    }

    internal void EmitPollInput()
    {
        builder.LoadA(InputCurrentAddress);
        builder.StoreA(InputPreviousAddress);

        EmitReadJoypadNibble(0x10);
        builder.LoadBFromA();

        EmitReadJoypadNibble(0x20);
        builder.SwapA();
        builder.OrAFromB();
        builder.StoreA(InputCurrentAddress);
        EmitDeselectJoypad();

        foreach (var button in Buttons)
        {
            EmitUpdateButtonHoldTicks(button);
        }
    }

    private void EmitReadJoypadNibble(byte selector)
    {
        builder.LoadAImmediate(selector);
        builder.StoreHighRamA(0x00);
        for (var i = 0; i < JoypadSettleReadCount; i++)
        {
            builder.LoadHighRamA(0x00);
        }

        builder.ComplementA();
        builder.AndImmediate(0x0F);
    }

    private void EmitDeselectJoypad()
    {
        builder.LoadAImmediate(JoypadDeselect);
        builder.StoreHighRamA(0x00);
    }

    private void EmitUpdateButtonHoldTicks(GameBoyButton button)
    {
        var resetLabel = builder.CreateLabel("button_hold_reset");
        var endLabel = builder.CreateLabel("button_hold_end");

        builder.LoadA(InputCurrentAddress);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, resetLabel); // JP Z,resetLabel

        builder.LoadA(button.HoldTicksAddress);
        builder.CompareImmediate(0xFF);
        builder.JumpAbsolute(0xCA, endLabel);   // JP Z,endLabel
        builder.AddAImmediate(1);
        builder.StoreA(button.HoldTicksAddress);
        builder.JumpAbsolute(endLabel);

        builder.Label(resetLabel);
        builder.LoadAImmediate(0);
        builder.StoreA(button.HoldTicksAddress);
        builder.Label(endLabel);
    }

    internal void EmitDrawLogicalSprite(Sdk2DOperation.DrawLogicalSprite operation)
    {
        if (!program.SpriteAssets.TryGetValue(operation.SpriteId, out var asset))
        {
            throw new InvalidOperationException($"Unknown Game Boy sprite asset '{operation.SpriteId}'. Declare it with sprite_asset(...).");
        }

        var firstHardwareSprite = nextHardwareSprite;
        if (firstHardwareSprite + asset.Pieces.Count > 40)
        {
            throw new InvalidOperationException("Game Boy sprite_draw calls exceed the 40 hardware sprite OAM limit.");
        }

        nextHardwareSprite += asset.Pieces.Count;
        for (var pieceIndex = 0; pieceIndex < asset.Pieces.Count; pieceIndex++)
        {
            var piece = asset.Pieces[pieceIndex];
            var oamAddress = (ushort)(0xFE00 + (firstHardwareSprite + pieceIndex) * 4);

            EmitSdkByteExpressionToA(operation.Y);
            builder.AddAImmediate(16 + piece.YOffset);
            builder.StoreA(oamAddress);

            EmitSpriteDrawX(operation.X, operation.FlipX, asset, piece, (ushort)(oamAddress + 1));

            EmitSdkByteExpressionToA(operation.Frame);
            EmitMultiplyAByConstant(asset.TilesPerFrame);
            builder.AddAImmediate(asset.FirstTile + piece.TileOffset);
            builder.StoreA((ushort)(oamAddress + 2));

            EmitSpriteDrawAttributes(operation.FlipX, operation.PaletteSlot, (ushort)(oamAddress + 3));
        }
    }

    private void EmitSpriteDrawX(
        SdkByteExpression xExpression,
        SdkByteExpression? flipXExpression,
        GameBoyCompiledSpriteAsset asset,
        GameBoyMetaspritePiece piece,
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
        builder.JumpAbsolute(0xCA, normalLabel); // JP Z,normalLabel

        EmitSpriteDrawXAtOffset(xExpression, flippedOffset, oamAddress);
        builder.JumpAbsolute(endLabel);

        builder.Label(normalLabel);
        EmitSpriteDrawXAtOffset(xExpression, normalOffset, oamAddress);
        builder.Label(endLabel);
    }

    private void EmitSpriteDrawAttributes(SdkByteExpression? flipXExpression, int paletteSlot, ushort oamAddress)
    {
        var paletteAttribute = SpritePaletteAttribute(paletteSlot);
        if (flipXExpression is null || (TrySdkConst(flipXExpression, out var constant) && constant == 0))
        {
            builder.LoadAImmediate(paletteAttribute);
            builder.StoreA(oamAddress);
            return;
        }

        if (TrySdkConst(flipXExpression, out _))
        {
            builder.LoadAImmediate(paletteAttribute | 0x20);
            builder.StoreA(oamAddress);
            return;
        }

        var noFlipLabel = builder.CreateLabel("sprite_flags_no_flip");
        var storeLabel = builder.CreateLabel("sprite_flags_store");

        EmitSdkByteExpressionToA(flipXExpression);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, noFlipLabel); // JP Z,noFlipLabel

        builder.LoadAImmediate(paletteAttribute | 0x20);
        builder.JumpAbsolute(storeLabel);

        builder.Label(noFlipLabel);
        builder.LoadAImmediate(paletteAttribute);

        builder.Label(storeLabel);
        builder.StoreA(oamAddress);
    }

    private static int SpritePaletteAttribute(int paletteSlot)
    {
        return (paletteSlot & 0x01) << 4;
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

    private void EmitSpriteDrawXAtOffset(SdkByteExpression xExpression, int offset, ushort oamAddress)
    {
        EmitSdkByteExpressionToA(xExpression);
        builder.AddAImmediate(8 + offset);
        builder.StoreA(oamAddress);
    }

    private void EmitMultiplyAByConstant(int factor)
    {
        if (factor == 1)
        {
            return;
        }

        builder.LoadBFromA();
        builder.XorA();
        for (var i = 0; i < factor; i++)
        {
            builder.AddAFromB();
        }
    }

    internal void EmitStreamMapColumn(Sdk2DOperation.StreamMapColumn operation)
    {
        if (program.MapColumnHeight == 0)
        {
            throw new InvalidOperationException("map_stream_column requires at least one map_column declaration.");
        }

        var y = CheckedRange(operation.Y, 0, 31, "map_stream_column argument 3");
        var height = CheckedRange(operation.Height, 1, program.MapColumnHeight, "map_stream_column argument 4");

        for (var row = 0; row < height; row++)
        {
            EmitSdkByteExpressionToA(operation.SourceColumn);
            EmitReadOnlyMapByteAtSourceColumnInA(GameBoyRomBuilder.MapRowLabel(row));
            builder.LoadBFromA();

            var rowAddress = 0x9800 + (y + row) * 32;
            EmitSdkByteExpressionToA(operation.TargetColumn);
            builder.AddAImmediate(rowAddress & 0xFF);
            builder.LoadLFromA();
            builder.LoadHImmediate(rowAddress >> 8);
            builder.LoadAFromB();
            builder.StoreHlA();
        }
    }

    private void EmitCameraInit(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 3);
        if (program.MapColumnHeight == 0)
        {
            throw new InvalidOperationException("camera_init requires at least one map_column declaration.");
        }

        var args = call.Parameters.ToList();
        var mapWidth = CheckedRange(GameBoyVideoProgram.ConstValue(args[0], "camera_init argument 1"), 1, 255, "camera_init argument 1");
        var mapDataColumnCount = program.MapColumns.Keys.Max() + 1;
        if (mapWidth > mapDataColumnCount)
        {
            throw new InvalidOperationException($"camera_init argument 1 must not exceed the declared map column count ({mapDataColumnCount}).");
        }

        var y = CheckedRange(GameBoyVideoProgram.ConstValue(args[1], "camera_init argument 2"), 0, 31, "camera_init argument 2");
        var requestedHeight = CheckedRange(GameBoyVideoProgram.ConstValue(args[2], "camera_init argument 3"), 1, program.MapColumnHeight, "camera_init argument 3");
        var maxBufferedHeight = 32 - y;
        var height = requestedHeight;
        if (requestedHeight > maxBufferedHeight)
        {
            if (!ProgramQueuesRowStreaming())
            {
                throw new InvalidOperationException("camera_init stream area exceeds the Game Boy background tilemap height.");
            }

            height = maxBufferedHeight;
        }

        cameraMapWidth = mapWidth;
        cameraStreamY = y;
        cameraStreamHeight = height;

        builder.LoadAImmediate(0);
        builder.StoreA(CameraXLowAddress);
        builder.StoreA(CameraXHighAddress);
        builder.StoreA(CameraFineXAddress);
        builder.StoreA(CameraScreenLeftColumnAddress);

        builder.LoadAImmediate(VisibleScreenTileWidthWithPartial);
        builder.StoreA(CameraRightBackgroundColumnAddress);
        builder.LoadAImmediate(31);
        builder.StoreA(CameraLeftBackgroundColumnAddress);
        builder.LoadAImmediate(VisibleScreenTileWidthWithPartial % mapWidth);
        builder.StoreA(CameraRightSourceColumnAddress);
        builder.LoadAImmediate(mapWidth - 1);
        builder.StoreA(CameraLeftSourceColumnAddress);

        builder.LoadAImmediate(0);
        builder.StoreA(CameraYLowAddress);
        builder.StoreA(CameraYHighAddress);
        builder.StoreA(CameraFineYAddress);
        if (ProgramQueuesDiagonalStreaming())
        {
            builder.StoreA(PendingDiagonalColumnKindAddress);
            builder.StoreA(PendingDiagonalColumnCountAddress);
            builder.StoreA(PendingDiagonalRowKindAddress);
            builder.StoreA(PendingDiagonalRowCountAddress);
            builder.LoadAImmediate(PendingStreamColumn);
            builder.StoreA(PendingDiagonalNextStreamKindAddress);
        }
        else
        {
            builder.StoreA(PendingStreamKindAddress);
            builder.StoreA(PendingStreamCountAddress);
        }

        builder.LoadAImmediate(y);
        builder.StoreA(CameraTopBackgroundRowAddress);
        // The bottom edge tracks the row just below the visible window, not the full buffered/stream
        // height: for a tall map the streamed height is clamped to the 32-row background buffer, and
        // (y + 32) % 32 == y would collapse the bottom edge onto the top so every downward row crossing
        // streamed into the top band and left the real bottom rows permanently stale. Offsetting by the
        // visible tile height keeps the bottom edge a screen-height below the top.
        var bottomRowOffset = Math.Min(height, VisibleScreenTileHeight);
        builder.LoadAImmediate((y + bottomRowOffset) % 32);
        builder.StoreA(CameraBottomBackgroundRowAddress);
        builder.LoadAImmediate(0);
        builder.StoreA(CameraTopSourceRowAddress);
        builder.LoadAImmediate(bottomRowOffset % program.MapColumnHeight);
        builder.StoreA(CameraBottomSourceRowAddress);
    }

    internal void EmitSetCameraPosition(Sdk2DOperation.SetCameraPosition operation)
    {
        var config = EnsureCameraConfigured("camera_set_position");

        if (operation.Axes.HasFlag(ScrollAxes.Horizontal))
        {
            EmitCameraSetAxisPosition(
                () => EmitSdkByteExpressionToA(operation.X),
                CameraXLowAddress,
                () => EmitCameraMoveLeftStep(config),
                () => EmitCameraMoveRightStep(config),
                "camera_set_position_right",
                "camera_set_position_x_end");
        }

        if (operation.Axes.HasFlag(ScrollAxes.Vertical))
        {
            EmitCameraSetAxisPosition(
                () => EmitSdkByteExpressionToA(operation.Y),
                CameraYLowAddress,
                () => EmitCameraMoveUpStep(config),
                () => EmitCameraMoveDownStep(config),
                "camera_set_position_down",
                "camera_set_position_y_end");
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
                EmitRuntimeIndexedMemberAddressToHl(runtimeIndexed.BaseName, runtimeIndexed.Index, runtimeIndexed.FieldName);
                builder.LoadAFromHl();
                break;
            default:
                builder.LoadA(VariableAddress(StorageKey(location)));
                break;
        }
    }

    internal void EmitApplyCamera(Sdk2DOperation.ApplyCamera operation)
    {
        var config = EnsureCameraConfigured("camera_apply");

        // Drain any column/row queued by last frame's camera move into VRAM now, while we are at the
        // top of the frame inside VBlank. This replaces the per-crossing extra WaitVBlank, so a
        // scrolling frame no longer costs two VBlanks for a single audio update.
        EmitCommitPendingStream(config);

        builder.LoadA(CameraXLowAddress);
        builder.StoreHighRamA(0x43);
        builder.LoadA(CameraYLowAddress);
        builder.StoreHighRamA(0x42);
    }

    private void EmitCommitPendingStream(CameraConfig config)
    {
        if (ProgramQueuesDiagonalStreaming())
        {
            EmitCommitStaggeredPendingStream(config);
            return;
        }

        // Rows are only ever queued by camera movement with a non-zero Y (the up/down move steps
        // are unreachable otherwise), so only emit the large row streamer when the program can
        // actually scroll vertically. The column streamer is small and always emitted.
        var emitRowCommit = ProgramQueuesRowStreaming();

        var rowLabel = builder.CreateLabel("camera_commit_row");
        var clearLabel = builder.CreateLabel("camera_commit_clear");
        var doneLabel = builder.CreateLabel("camera_commit_done");

        builder.LoadA(PendingStreamKindAddress);
        builder.CompareImmediate(PendingStreamNone);
        builder.JumpAbsolute(0xCA, doneLabel);      // JP Z,doneLabel: nothing queued
        if (emitRowCommit)
        {
            builder.CompareImmediate(PendingStreamRow);
            builder.JumpAbsolute(0xCA, rowLabel);   // JP Z,rowLabel
        }

        // Column crossing: stream the queued background and visible-world column(s).
        EmitCommitPendingColumnSlots(
            PendingStreamCountAddress,
            PendingStreamTargetAddress,
            PendingStreamSourceAddress,
            PendingStreamSecondTargetAddress,
            PendingStreamSecondSourceAddress,
            config);

        if (emitRowCommit)
        {
            builder.JumpAbsolute(clearLabel);
            builder.Label(rowLabel);
            EmitCommitPendingRowSlots(
                PendingStreamCountAddress,
                PendingStreamTargetAddress,
                PendingStreamSourceAddress,
                PendingStreamSecondTargetAddress,
                PendingStreamSecondSourceAddress,
                config);
            builder.Label(clearLabel);
        }

        builder.LoadAImmediate(PendingStreamNone);
        builder.StoreA(PendingStreamKindAddress);
        builder.StoreA(PendingStreamCountAddress);

        builder.Label(doneLabel);
    }

    private void EmitCommitStaggeredPendingStream(CameraConfig config)
    {
        var checkRowLabel = builder.CreateLabel("camera_commit_check_row");
        var commitColumnLabel = builder.CreateLabel("camera_commit_column");
        var commitRowLabel = builder.CreateLabel("camera_commit_row");
        var doneLabel = builder.CreateLabel("camera_commit_done");

        builder.LoadA(PendingDiagonalColumnKindAddress);
        builder.CompareImmediate(PendingStreamNone);
        builder.JumpAbsolute(0xCA, checkRowLabel); // JP Z,checkRowLabel
        builder.LoadA(PendingDiagonalRowKindAddress);
        builder.CompareImmediate(PendingStreamNone);
        builder.JumpAbsolute(0xCA, commitColumnLabel); // JP Z,commitColumnLabel
        builder.LoadA(PendingDiagonalNextStreamKindAddress);
        builder.CompareImmediate(PendingStreamRow);
        builder.JumpAbsolute(0xCA, commitRowLabel); // JP Z,commitRowLabel

        builder.Label(commitColumnLabel);
        EmitCommitPendingColumnSlots(
            PendingDiagonalColumnCountAddress,
            PendingDiagonalColumnTargetAddress,
            PendingDiagonalColumnSourceAddress,
            PendingDiagonalColumnSecondTargetAddress,
            PendingDiagonalColumnSecondSourceAddress,
            config);
        builder.LoadAImmediate(PendingStreamNone);
        builder.StoreA(PendingDiagonalColumnKindAddress);
        builder.StoreA(PendingDiagonalColumnCountAddress);
        builder.LoadAImmediate(PendingStreamRow);
        builder.StoreA(PendingDiagonalNextStreamKindAddress);
        builder.JumpAbsolute(doneLabel);

        builder.Label(checkRowLabel);
        builder.LoadA(PendingDiagonalRowKindAddress);
        builder.CompareImmediate(PendingStreamNone);
        builder.JumpAbsolute(0xCA, doneLabel); // JP Z,doneLabel

        builder.Label(commitRowLabel);
        EmitCommitPendingRowSlots(
            PendingDiagonalRowCountAddress,
            PendingDiagonalRowTargetAddress,
            PendingDiagonalRowSourceAddress,
            PendingDiagonalRowSecondTargetAddress,
            PendingDiagonalRowSecondSourceAddress,
            config);
        builder.LoadAImmediate(PendingStreamNone);
        builder.StoreA(PendingDiagonalRowKindAddress);
        builder.StoreA(PendingDiagonalRowCountAddress);
        builder.LoadAImmediate(PendingStreamColumn);
        builder.StoreA(PendingDiagonalNextStreamKindAddress);

        builder.Label(doneLabel);
    }

    private void EmitCommitPendingColumnSlots(
        ushort pendingCountAddress,
        ushort pendingTargetAddress,
        ushort pendingSourceAddress,
        ushort pendingSecondTargetAddress,
        ushort pendingSecondSourceAddress,
        CameraConfig config)
    {
        EmitVisibleWorldStreamColumnFromAddresses(pendingTargetAddress, pendingSourceAddress, config);
        EmitBackgroundStreamColumnFromAddresses(pendingTargetAddress, pendingSourceAddress, config);

        var doneLabel = builder.CreateLabel("camera_commit_second_column_done");
        builder.LoadA(pendingCountAddress);
        builder.CompareImmediate(PendingStreamDouble);
        builder.JumpAbsolute(0xC2, doneLabel); // JP NZ,doneLabel

        EmitVisibleWorldStreamColumnFromAddresses(pendingSecondTargetAddress, pendingSecondSourceAddress, config);
        EmitBackgroundStreamColumnFromAddresses(pendingSecondTargetAddress, pendingSecondSourceAddress, config);

        builder.Label(doneLabel);
    }

    private void EmitCommitPendingRowSlots(
        ushort pendingCountAddress,
        ushort pendingTargetAddress,
        ushort pendingSourceAddress,
        ushort pendingSecondTargetAddress,
        ushort pendingSecondSourceAddress,
        CameraConfig config)
    {
        EmitMapStreamRowFromSourceRowAddress(pendingTargetAddress, pendingSourceAddress, config);

        var doneLabel = builder.CreateLabel("camera_commit_second_row_done");
        builder.LoadA(pendingCountAddress);
        builder.CompareImmediate(PendingStreamDouble);
        builder.JumpAbsolute(0xC2, doneLabel); // JP NZ,doneLabel

        EmitMapStreamRowFromSourceRowAddress(pendingSecondTargetAddress, pendingSecondSourceAddress, config);

        builder.Label(doneLabel);
    }

    private bool ProgramQueuesRowStreaming()
    {
        foreach (var operation in program.SdkOperations)
        {
            if (operation is Sdk2DOperation.SetCameraPosition position
                && position.Axes.HasFlag(ScrollAxes.Vertical))
            {
                return true;
            }
        }

        return false;
    }

    private bool ProgramQueuesDiagonalStreaming()
    {
        foreach (var operation in program.SdkOperations)
        {
            if (operation is Sdk2DOperation.SetCameraPosition position
                && position.Axes.HasFlag(ScrollAxes.Horizontal)
                && position.Axes.HasFlag(ScrollAxes.Vertical))
            {
                return true;
            }
        }

        return false;
    }

    private void EmitQueuePendingColumn(ushort backgroundColumnAddress, ushort sourceColumnAddress)
    {
        if (ProgramQueuesDiagonalStreaming())
        {
            EmitQueuePendingStaggeredStream(
                PendingDiagonalColumnKindAddress,
                PendingDiagonalColumnCountAddress,
                PendingDiagonalColumnTargetAddress,
                PendingDiagonalColumnSourceAddress,
                PendingDiagonalColumnSecondTargetAddress,
                PendingDiagonalColumnSecondSourceAddress,
                backgroundColumnAddress,
                sourceColumnAddress);
            return;
        }

        EmitQueuePendingStream(PendingStreamColumn, backgroundColumnAddress, sourceColumnAddress);
    }

    private void EmitQueuePendingRow(ushort backgroundRowAddress, ushort sourceRowAddress)
    {
        if (ProgramQueuesDiagonalStreaming())
        {
            EmitQueuePendingStaggeredStream(
                PendingDiagonalRowKindAddress,
                PendingDiagonalRowCountAddress,
                PendingDiagonalRowTargetAddress,
                PendingDiagonalRowSourceAddress,
                PendingDiagonalRowSecondTargetAddress,
                PendingDiagonalRowSecondSourceAddress,
                backgroundRowAddress,
                sourceRowAddress);
            return;
        }

        EmitQueuePendingStream(PendingStreamRow, backgroundRowAddress, sourceRowAddress);
    }

    private void EmitQueuePendingStream(byte kind, ushort targetAddress, ushort sourceAddress)
    {
        var storeFirstLabel = builder.CreateLabel("camera_queue_store_first");
        var storeSecondLabel = builder.CreateLabel("camera_queue_store_second");
        var doneLabel = builder.CreateLabel("camera_queue_done");

        builder.LoadA(PendingStreamKindAddress);
        builder.CompareImmediate(PendingStreamNone);
        builder.JumpAbsolute(0xCA, storeFirstLabel); // JP Z,storeFirstLabel
        builder.CompareImmediate(kind);
        builder.JumpAbsolute(0xC2, storeFirstLabel); // JP NZ,storeFirstLabel

        builder.LoadA(PendingStreamCountAddress);
        builder.CompareImmediate(PendingStreamSingle);
        builder.JumpAbsolute(0xCA, storeSecondLabel); // JP Z,storeSecondLabel
        builder.JumpAbsolute(doneLabel);

        builder.Label(storeFirstLabel);
        builder.LoadA(targetAddress);
        builder.StoreA(PendingStreamTargetAddress);
        builder.LoadA(sourceAddress);
        builder.StoreA(PendingStreamSourceAddress);
        builder.LoadAImmediate(kind);
        builder.StoreA(PendingStreamKindAddress);
        builder.LoadAImmediate(PendingStreamSingle);
        builder.StoreA(PendingStreamCountAddress);
        builder.JumpAbsolute(doneLabel);

        builder.Label(storeSecondLabel);
        builder.LoadA(targetAddress);
        builder.StoreA(PendingStreamSecondTargetAddress);
        builder.LoadA(sourceAddress);
        builder.StoreA(PendingStreamSecondSourceAddress);
        builder.LoadAImmediate(PendingStreamDouble);
        builder.StoreA(PendingStreamCountAddress);

        builder.Label(doneLabel);
    }

    private void EmitQueuePendingStaggeredStream(
        ushort pendingKindAddress,
        ushort pendingCountAddress,
        ushort pendingTargetAddress,
        ushort pendingSourceAddress,
        ushort pendingSecondTargetAddress,
        ushort pendingSecondSourceAddress,
        ushort targetAddress,
        ushort sourceAddress)
    {
        var storeFirstLabel = builder.CreateLabel("camera_queue_staggered_store_first");
        var storeSecondLabel = builder.CreateLabel("camera_queue_staggered_store_second");
        var doneLabel = builder.CreateLabel("camera_queue_staggered_done");

        builder.LoadA(pendingKindAddress);
        builder.CompareImmediate(PendingStreamNone);
        builder.JumpAbsolute(0xCA, storeFirstLabel); // JP Z,storeFirstLabel
        builder.LoadA(pendingCountAddress);
        builder.CompareImmediate(PendingStreamSingle);
        builder.JumpAbsolute(0xCA, storeSecondLabel); // JP Z,storeSecondLabel
        builder.JumpAbsolute(doneLabel);

        builder.Label(storeFirstLabel);
        builder.LoadA(targetAddress);
        builder.StoreA(pendingTargetAddress);
        builder.LoadA(sourceAddress);
        builder.StoreA(pendingSourceAddress);
        builder.LoadAImmediate(PendingStreamQueued);
        builder.StoreA(pendingKindAddress);
        builder.LoadAImmediate(PendingStreamSingle);
        builder.StoreA(pendingCountAddress);
        builder.JumpAbsolute(doneLabel);

        builder.Label(storeSecondLabel);
        builder.LoadA(targetAddress);
        builder.StoreA(pendingSecondTargetAddress);
        builder.LoadA(sourceAddress);
        builder.StoreA(pendingSecondSourceAddress);
        builder.LoadAImmediate(PendingStreamDouble);
        builder.StoreA(pendingCountAddress);

        builder.Label(doneLabel);
    }

    private void EmitCameraSetAxisPosition(
        Action emitRequestedPositionToA,
        ushort currentLowAddress,
        Action moveNegative,
        Action movePositive,
        string positiveLabelName,
        string endLabelName)
    {
        var loopLabel = builder.CreateLabel(endLabelName + "_step");
        var movePositiveLabel = builder.CreateLabel(positiveLabelName);
        var endLabel = builder.CreateLabel(endLabelName);

        // Cache the requested absolute position and walk the camera toward it, one pixel per step, up
        // to the per-frame streaming budget. Reaching the target early exits the loop, so callers set
        // the desired position once per frame instead of stepping the camera manually.
        emitRequestedPositionToA();
        builder.StoreA(CameraSetPositionTargetAddress);
        builder.LoadAImmediate(CameraSetPositionMaxStepsPerFrame);
        builder.StoreA(CameraSetPositionStepsRemainingAddress);

        builder.Label(loopLabel);

        builder.LoadA(CameraSetPositionStepsRemainingAddress);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, endLabel); // JP Z,endLabel: streaming budget spent
        builder.SubtractAImmediate(1);
        builder.StoreA(CameraSetPositionStepsRemainingAddress);

        builder.LoadA(CameraSetPositionTargetAddress);
        builder.LoadBFromA();
        builder.LoadA(currentLowAddress);
        builder.LoadCFromA();
        builder.LoadAFromB();
        builder.SubtractAFromC();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, endLabel); // JP Z,endLabel: reached target
        builder.CompareImmediate(128);
        builder.JumpAbsolute(0xDA, movePositiveLabel); // JP C,movePositiveLabel

        moveNegative();
        builder.JumpAbsolute(loopLabel);

        builder.Label(movePositiveLabel);
        movePositive();
        builder.JumpAbsolute(loopLabel);

        builder.Label(endLabel);
    }

    private void EmitCameraMoveRight(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 0);
        var config = EnsureCameraConfigured(call.Name);
        EmitCameraMoveRightStep(config);
    }

    private void EmitCameraMoveRightStep(CameraConfig config)
    {
        var endLabel = builder.CreateLabel("camera_move_right_end");

        EmitIncrement16(CameraXLowAddress, CameraXHighAddress);
        builder.LoadA(CameraFineXAddress);
        builder.AddAImmediate(1);
        builder.StoreA(CameraFineXAddress);
        builder.CompareImmediate(8);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel

        builder.LoadAImmediate(0);
        builder.StoreA(CameraFineXAddress);
        EmitQueuePendingColumn(CameraRightBackgroundColumnAddress, CameraRightSourceColumnAddress);
        EmitIncrementAddressModulo(CameraRightBackgroundColumnAddress, 32);
        EmitIncrementAddressModulo(CameraLeftBackgroundColumnAddress, 32);
        EmitIncrementAddressModulo(CameraScreenLeftColumnAddress, config.MapWidth);
        EmitIncrementAddressModulo(CameraRightSourceColumnAddress, config.MapWidth);
        EmitIncrementAddressModulo(CameraLeftSourceColumnAddress, config.MapWidth);

        builder.Label(endLabel);
    }

    private void EmitCameraMoveLeft(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 0);
        var config = EnsureCameraConfigured(call.Name);
        EmitCameraMoveLeftStep(config);
    }

    private void EmitCameraMoveLeftStep(CameraConfig config)
    {
        var endLabel = builder.CreateLabel("camera_move_left_end");

        EmitDecrement16(CameraXLowAddress, CameraXHighAddress);
        builder.LoadA(CameraFineXAddress);
        builder.SubtractAImmediate(1);
        builder.StoreA(CameraFineXAddress);
        builder.CompareImmediate(255);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel

        builder.LoadAImmediate(7);
        builder.StoreA(CameraFineXAddress);
        EmitQueuePendingColumn(CameraLeftBackgroundColumnAddress, CameraLeftSourceColumnAddress);
        EmitDecrementAddressModulo(CameraRightBackgroundColumnAddress, 32);
        EmitDecrementAddressModulo(CameraLeftBackgroundColumnAddress, 32);
        EmitDecrementAddressModulo(CameraScreenLeftColumnAddress, config.MapWidth);
        EmitDecrementAddressModulo(CameraRightSourceColumnAddress, config.MapWidth);
        EmitDecrementAddressModulo(CameraLeftSourceColumnAddress, config.MapWidth);

        builder.Label(endLabel);
    }

    private void EmitCameraMoveDownStep(CameraConfig config)
    {
        var endLabel = builder.CreateLabel("camera_move_down_end");

        EmitIncrement16(CameraYLowAddress, CameraYHighAddress);
        builder.LoadA(CameraFineYAddress);
        builder.AddAImmediate(1);
        builder.StoreA(CameraFineYAddress);
        builder.CompareImmediate(8);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel

        builder.LoadAImmediate(0);
        builder.StoreA(CameraFineYAddress);
        EmitIncrementAddressModulo(CameraTopBackgroundRowAddress, 32);
        EmitIncrementAddressModulo(CameraBottomBackgroundRowAddress, 32);
        EmitIncrementAddressModulo(CameraTopSourceRowAddress, config.SourceHeight);
        EmitIncrementAddressModulo(CameraBottomSourceRowAddress, config.SourceHeight);
        // After a downward tile crossing, the fine-scroll-exposed bottom row is the advanced row.
        EmitQueuePendingRow(CameraBottomBackgroundRowAddress, CameraBottomSourceRowAddress);

        builder.Label(endLabel);
    }

    private void EmitCameraMoveUpStep(CameraConfig config)
    {
        var endLabel = builder.CreateLabel("camera_move_up_end");

        EmitDecrement16(CameraYLowAddress, CameraYHighAddress);
        builder.LoadA(CameraFineYAddress);
        builder.SubtractAImmediate(1);
        builder.StoreA(CameraFineYAddress);
        builder.CompareImmediate(255);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel

        builder.LoadAImmediate(7);
        builder.StoreA(CameraFineYAddress);
        EmitDecrementAddressModulo(CameraTopBackgroundRowAddress, 32);
        EmitDecrementAddressModulo(CameraBottomBackgroundRowAddress, 32);
        EmitDecrementAddressModulo(CameraTopSourceRowAddress, config.SourceHeight);
        EmitDecrementAddressModulo(CameraBottomSourceRowAddress, config.SourceHeight);
        EmitQueuePendingRow(CameraTopBackgroundRowAddress, CameraTopSourceRowAddress);

        builder.Label(endLabel);
    }

    private void EmitMapStreamColumnFromAddresses(ushort targetColumnAddress, ushort sourceColumnAddress, int y, int height)
    {
        EmitMapStreamColumnFromAddresses(targetColumnAddress, sourceColumnAddress, y, height, GameBoyRomBuilder.MapRowLabel);
    }

    private void EmitMapStreamColumnFromAddresses(ushort targetColumnAddress, ushort sourceColumnAddress, int y, int height, Func<int, string> rowLabel)
    {
        for (var row = 0; row < height; row++)
        {
            builder.LoadA(sourceColumnAddress);
            EmitReadOnlyMapByteAtSourceColumnInA(rowLabel(row));
            builder.LoadBFromA();

            var rowAddress = 0x9800 + (y + row) * 32;
            builder.LoadA(targetColumnAddress);
            builder.AddAImmediate(rowAddress & 0xFF);
            builder.LoadLFromA();
            builder.LoadHImmediate(rowAddress >> 8);
            builder.LoadAFromB();
            builder.StoreHlA();
        }
    }

    private void EmitBackgroundStreamColumnFromAddresses(ushort targetColumnAddress, ushort sourceColumnAddress, CameraConfig config)
    {
        var visibleBackgroundRows = Math.Min(program.BackgroundStreamHeight, VisibleScreenTileHeight);
        if (visibleBackgroundRows <= 0)
        {
            return;
        }

        var loopLabel = builder.CreateLabel("background_stream_column_loop");
        var sourceNoCarryLabel = builder.CreateLabel("background_stream_column_source_no_carry");
        var targetNoCarryLabel = builder.CreateLabel("background_stream_column_target_no_carry");

        builder.LoadHl(GameBoyRomBuilder.BackgroundRowLabel(0));
        builder.LoadA(sourceColumnAddress);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.AddHlDe();
        EmitLoadDeFromHl();

        builder.LoadA(targetColumnAddress);
        builder.LoadLFromA();
        builder.LoadHImmediate(0x98);
        builder.Emit(0x0E, (byte)visibleBackgroundRows); // LD C,visibleBackgroundRows

        builder.Label(loopLabel);
        builder.Emit(0x1A); // LD A,(DE)
        builder.StoreHlA();

        builder.LoadAFromE();
        builder.AddAImmediate(config.MapWidth);
        builder.LoadEFromA();
        builder.JumpAbsolute(0xD2, sourceNoCarryLabel); // JP NC,sourceNoCarryLabel
        builder.Emit(0x14); // INC D
        builder.Label(sourceNoCarryLabel);

        builder.LoadAFromL();
        builder.AddAImmediate(BackgroundTileMapWidth);
        builder.LoadLFromA();
        builder.JumpAbsolute(0xD2, targetNoCarryLabel); // JP NC,targetNoCarryLabel
        builder.Emit(0x24); // INC H
        builder.Label(targetNoCarryLabel);

        builder.Emit(0x0D); // DEC C
        builder.JumpRelative(0x20, loopLabel); // JR NZ,loopLabel
    }

    private void EmitVisibleWorldStreamColumnFromAddresses(ushort targetColumnAddress, ushort sourceColumnAddress, CameraConfig config)
    {
        var streamRows = Math.Max(0, Math.Min(config.StreamHeight, VisibleScreenTileHeight + 1));
        if (streamRows == 0)
        {
            return;
        }

        Sdk2DOperationValidator.Validate(
            GameBoyTarget.Capabilities,
            new Sdk2DOperation.StreamMapColumn(TargetColumn: 0, SourceColumn: 0, Y: config.StreamY, Height: streamRows));

        var loopLabel = builder.CreateLabel("map_stream_column_loop");
        var sourceNoCarryLabel = builder.CreateLabel("map_stream_column_source_no_carry");
        var targetNoCarryLabel = builder.CreateLabel("map_stream_column_target_no_carry");
        var targetNoWrapLabel = builder.CreateLabel("map_stream_column_target_no_wrap");
        var endLabel = builder.CreateLabel("map_stream_column_end");

        builder.LoadA(CameraTopSourceRowAddress);
        builder.StoreA(CameraStreamSourceRowScratchAddress);
        EmitLoadMapRowPointerToHl(CameraStreamSourceRowScratchAddress);
        builder.LoadA(sourceColumnAddress);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.AddHlDe();
        EmitLoadDeFromHl();
        builder.LoadA(targetColumnAddress);
        builder.LoadCFromA();
        EmitBackgroundTileAddressToHl(CameraTopBackgroundRowAddress);
        builder.Emit(0x0E, (byte)streamRows); // LD C,streamRows

        builder.Label(loopLabel);
        builder.Emit(0x1A); // LD A,(DE)
        builder.StoreHlA();

        builder.LoadAFromE();
        builder.AddAImmediate(config.MapWidth);
        builder.LoadEFromA();
        builder.JumpAbsolute(0xD2, sourceNoCarryLabel); // JP NC,sourceNoCarryLabel
        builder.Emit(0x14); // INC D
        builder.Label(sourceNoCarryLabel);

        builder.LoadAFromL();
        builder.AddAImmediate(BackgroundTileMapWidth);
        builder.LoadLFromA();
        builder.JumpAbsolute(0xD2, targetNoCarryLabel); // JP NC,targetNoCarryLabel
        builder.Emit(0x24); // INC H
        builder.LoadAFromH();
        builder.CompareImmediate(0x9C);
        builder.JumpAbsolute(0xC2, targetNoWrapLabel); // JP NZ,targetNoWrapLabel
        builder.LoadHImmediate(0x98);
        builder.Label(targetNoWrapLabel);
        builder.Label(targetNoCarryLabel);

        builder.Emit(0x0D); // DEC C
        builder.JumpAbsolute(0xC2, loopLabel); // JP NZ,loopLabel
        builder.Label(endLabel);
    }

    private void EmitMapStreamRowFromSourceRowAddress(ushort targetRowAddress, ushort sourceRowAddress, CameraConfig config)
    {
        Sdk2DOperationValidator.Validate(
            GameBoyTarget.Capabilities,
            new Sdk2DOperation.StreamMapRow(TargetRow: 0, SourceRow: 0, X: 0, Width: VisibleScreenTileWidth + 1));

        EmitLoadMapRowPointerToHl(sourceRowAddress);
        builder.LoadAFromL();
        builder.StoreA(PendingStreamRowDataLowAddress);
        builder.LoadAFromH();
        builder.StoreA(PendingStreamRowDataHighAddress);
        EmitMapStreamRowFromSelectedSourceRow(targetRowAddress, config.MapWidth);
    }

    private void EmitMapStreamRowFromSelectedSourceRow(ushort targetRowAddress, int mapWidth)
    {
        var targetInitialReadyLabel = builder.CreateLabel("map_stream_row_initial_target_ready");
        var firstCountReadyLabel = builder.CreateLabel("map_stream_row_first_count_ready");
        var secondSegmentLabel = builder.CreateLabel("map_stream_row_second_segment");
        var endLabel = builder.CreateLabel("map_stream_row_copy_end");

        builder.LoadA(CameraScreenLeftColumnAddress);
        builder.LoadBFromA();
        builder.LoadA(CameraLeftBackgroundColumnAddress);
        builder.AddAImmediate(1);
        builder.CompareImmediate(BackgroundTileMapWidth);
        builder.JumpAbsolute(0xDA, targetInitialReadyLabel); // JP C,targetInitialReadyLabel
        builder.SubtractAImmediate(BackgroundTileMapWidth);
        builder.Label(targetInitialReadyLabel);
        builder.StoreA(CameraStreamTargetColumnScratchAddress);
        builder.LoadBFromA();
        builder.LoadAImmediate(BackgroundTileMapWidth);
        builder.SubtractB();
        builder.CompareImmediate(VisibleScreenTileWidth + 2);
        builder.JumpAbsolute(0xDA, firstCountReadyLabel); // JP C,firstCountReadyLabel
        builder.LoadAImmediate(VisibleScreenTileWidth + 1);
        builder.Label(firstCountReadyLabel);
        builder.StoreA(CameraStreamColumnsRemainingAddress);
        builder.LoadBFromA();
        builder.LoadAImmediate(VisibleScreenTileWidth + 1);
        builder.SubtractB();
        builder.StoreA(CameraStreamSourceColumnScratchAddress);
        builder.LoadA(CameraScreenLeftColumnAddress);
        builder.LoadBFromA();

        EmitLoadSourceRowPointerToDe();
        EmitLoadHlFromDe();
        builder.LoadAFromB();
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.AddHlDe();
        EmitLoadDeFromHl();

        builder.LoadA(CameraStreamTargetColumnScratchAddress);
        builder.LoadCFromA();
        EmitBackgroundTileAddressToHl(targetRowAddress);
        builder.LoadA(CameraStreamColumnsRemainingAddress);
        builder.LoadCFromA();
        EmitMapStreamRowCopyVisibleSegment(mapWidth);

        builder.LoadA(CameraStreamSourceColumnScratchAddress);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, endLabel); // JP Z,endLabel
        builder.Label(secondSegmentLabel);
        builder.Emit(0x0E, 0x00); // LD C,0
        EmitBackgroundTileAddressToHl(targetRowAddress);
        builder.LoadA(CameraStreamSourceColumnScratchAddress);
        builder.LoadCFromA();
        EmitMapStreamRowCopyVisibleSegment(mapWidth);
        builder.Label(endLabel);
    }

    private void EmitMapStreamRowCopyVisibleSegment(int mapWidth)
    {
        var loopLabel = builder.CreateLabel("map_stream_row_segment_loop");
        var sourceReadyLabel = builder.CreateLabel("map_stream_row_segment_source_ready");

        builder.Label(loopLabel);
        builder.Emit(0x1A); // LD A,(DE)
        builder.Emit(0x22); // LD (HL+),A
        builder.Emit(0x13); // INC DE
        builder.Emit(0x04); // INC B
        builder.LoadAFromB();
        builder.CompareImmediate(mapWidth);
        builder.JumpAbsolute(0xC2, sourceReadyLabel); // JP NZ,sourceReadyLabel
        builder.Emit(0x06, 0x00); // LD B,0
        EmitLoadSourceRowPointerToDe();
        builder.Label(sourceReadyLabel);
        builder.Emit(0x0D); // DEC C
        builder.JumpRelative(0x20, loopLabel); // JR NZ,loopLabel
    }

    private void EmitLoadSourceRowPointerToDe()
    {
        builder.LoadA(PendingStreamRowDataLowAddress);
        builder.LoadEFromA();
        builder.LoadA(PendingStreamRowDataHighAddress);
        builder.LoadDFromA();
    }

    private void EmitLoadMapRowPointerToHl(ushort sourceRowAddress)
    {
        builder.LoadA(sourceRowAddress);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.LoadHl(GameBoyRomBuilder.MapRowPointerLowLabel);
        builder.AddHlDe();
        builder.LoadAFromHl();
        builder.LoadCFromA();

        builder.LoadA(sourceRowAddress);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.LoadHl(GameBoyRomBuilder.MapRowPointerHighLabel);
        builder.AddHlDe();
        builder.LoadAFromHl();
        builder.LoadHFromA();
        builder.LoadAFromC();
        builder.LoadLFromA();
    }

    private void EmitLoadDeFromHl()
    {
        builder.Emit(0x54); // LD D,H
        builder.Emit(0x5D); // LD E,L
    }

    private void EmitLoadHlFromDe()
    {
        builder.Emit(0x62); // LD H,D
        builder.LoadLFromE();
    }

    private void EmitVisibleBackgroundColumnToC(int screenColumn)
    {
        var endLabel = builder.CreateLabel("camera_row_target_column_end");

        builder.LoadA(CameraLeftBackgroundColumnAddress);
        builder.AddAImmediate(1 + screenColumn);
        builder.CompareImmediate(32);
        builder.JumpAbsolute(0xDA, endLabel); // JP C,endLabel
        builder.SubtractAImmediate(32);
        builder.Label(endLabel);
        builder.LoadCFromA();
    }

    private void EmitBackgroundTileAddressToHl(ushort rowAddress)
    {
        EmitBackgroundTileAddressHighToH(rowAddress);

        builder.LoadA(rowAddress);
        for (var i = 0; i < 5; i++)
        {
            builder.AddAFromA();
        }

        builder.AddAFromC();
        builder.LoadLFromA();
    }

    private void EmitBackgroundTileAddressHighToH(ushort rowAddress)
    {
        var high98Label = builder.CreateLabel("camera_row_high_98");
        var high99Label = builder.CreateLabel("camera_row_high_99");
        var high9ALabel = builder.CreateLabel("camera_row_high_9a");
        var endLabel = builder.CreateLabel("camera_row_high_end");

        builder.LoadA(rowAddress);
        builder.CompareImmediate(8);
        builder.JumpAbsolute(0xDA, high98Label); // JP C,high98Label
        builder.CompareImmediate(16);
        builder.JumpAbsolute(0xDA, high99Label); // JP C,high99Label
        builder.CompareImmediate(24);
        builder.JumpAbsolute(0xDA, high9ALabel); // JP C,high9ALabel

        builder.LoadHImmediate(0x9B);
        builder.JumpAbsolute(endLabel);
        builder.Label(high98Label);
        builder.LoadHImmediate(0x98);
        builder.JumpAbsolute(endLabel);
        builder.Label(high99Label);
        builder.LoadHImmediate(0x99);
        builder.JumpAbsolute(endLabel);
        builder.Label(high9ALabel);
        builder.LoadHImmediate(0x9A);
        builder.Label(endLabel);
    }

    private void EmitIncrement16(ushort lowAddress, ushort highAddress)
    {
        var endLabel = builder.CreateLabel("increment16_end");

        builder.LoadA(lowAddress);
        builder.AddAImmediate(1);
        builder.StoreA(lowAddress);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel

        builder.LoadA(highAddress);
        builder.AddAImmediate(1);
        builder.StoreA(highAddress);
        builder.Label(endLabel);
    }

    private void EmitDecrement16(ushort lowAddress, ushort highAddress)
    {
        var noBorrowLabel = builder.CreateLabel("decrement16_no_borrow");

        builder.LoadA(lowAddress);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, noBorrowLabel); // JP NZ,noBorrowLabel

        builder.LoadA(highAddress);
        builder.SubtractAImmediate(1);
        builder.StoreA(highAddress);

        builder.Label(noBorrowLabel);
        builder.LoadA(lowAddress);
        builder.SubtractAImmediate(1);
        builder.StoreA(lowAddress);
    }

    private void EmitIncrementAddressModulo(ushort address, int modulo)
    {
        var endLabel = builder.CreateLabel("increment_modulo_end");

        builder.LoadA(address);
        builder.AddAImmediate(1);
        builder.StoreA(address);
        builder.CompareImmediate(modulo);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel
        builder.LoadAImmediate(0);
        builder.StoreA(address);
        builder.Label(endLabel);
    }

    private void EmitDecrementAddressModulo(ushort address, int modulo)
    {
        var endLabel = builder.CreateLabel("decrement_modulo_end");

        builder.LoadA(address);
        builder.SubtractAImmediate(1);
        builder.StoreA(address);
        builder.CompareImmediate(255);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel
        builder.LoadAImmediate(modulo - 1);
        builder.StoreA(address);
        builder.Label(endLabel);
    }

    private void EmitTilemapFillColumn(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 4);
        var args = call.Parameters.ToList();
        var y = CheckedRange(GameBoyVideoProgram.ConstValue(args[1], "tilemap_fill_column argument 2"), 0, 31, "tilemap_fill_column argument 2");
        var height = CheckedRange(GameBoyVideoProgram.ConstValue(args[2], "tilemap_fill_column argument 3"), 1, 32 - y, "tilemap_fill_column argument 3");

        EmitExpressionToA(args[3]);
        builder.LoadBFromA();

        for (var row = y; row < y + height; row++)
        {
            var rowAddress = 0x9800 + row * 32;
            EmitExpressionToA(args[0]);
            builder.AddAImmediate(rowAddress & 0xFF);
            builder.LoadLFromA();
            builder.LoadHImmediate(rowAddress >> 8);
            builder.LoadAFromB();
            builder.StoreHlA();
        }
    }

    private void EmitScrollSet(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 2);
        var args = call.Parameters.ToList();

        EmitExpressionToA(args[0]);
        builder.StoreHighRamA(0x43);

        EmitExpressionToA(args[1]);
        builder.StoreHighRamA(0x42);
    }

    private void EmitSpriteSet(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 5);
        var args = call.Parameters.ToList();
        var spriteId = CheckedRange(GameBoyVideoProgram.ConstValue(args[0], "sprite_set argument 1"), 0, 39, "sprite_set argument 1");
        var oamAddress = (ushort)(0xFE00 + spriteId * 4);

        EmitExpressionToA(args[2]);
        builder.AddAImmediate(16);
        builder.StoreA(oamAddress);

        EmitExpressionToA(args[1]);
        builder.AddAImmediate(8);
        builder.StoreA((ushort)(oamAddress + 1));

        EmitExpressionToA(args[3]);
        builder.StoreA((ushort)(oamAddress + 2));

        EmitExpressionToA(args[4]);
        builder.StoreA((ushort)(oamAddress + 3));
    }

    private void EmitWhile(WhileSyntax whileSyntax)
    {
        var startLabel = builder.CreateLabel("while_start");
        var endLabel = builder.CreateLabel("while_end");

        builder.Label(startLabel);
        EmitConditionFalseJump(whileSyntax.Condition, endLabel);
        loopTargets.Push(new LoopTarget(endLabel, startLabel));
        try
        {
            EmitBlock(whileSyntax.Body);
        }
        finally
        {
            loopTargets.Pop();
        }

        builder.JumpAbsolute(startLabel);
        builder.Label(endLabel);
    }

    private void EmitDoWhile(DoWhileSyntax doWhileSyntax)
    {
        var startLabel = builder.CreateLabel("do_start");
        var continueLabel = builder.CreateLabel("do_continue");
        var endLabel = builder.CreateLabel("do_end");

        builder.Label(startLabel);
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
        builder.JumpAbsolute(startLabel);
        builder.Label(endLabel);
    }

    private void EmitFor(ForSyntax forSyntax)
    {
        if (forSyntax.Initializer.HasValue)
        {
            EmitStatement(forSyntax.Initializer.Value);
        }

        var startLabel = builder.CreateLabel("for_start");
        var continueLabel = builder.CreateLabel("for_continue");
        var endLabel = builder.CreateLabel("for_end");

        builder.Label(startLabel);
        if (forSyntax.Condition.HasValue)
        {
            EmitConditionFalseJump(forSyntax.Condition.Value, endLabel);
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
                throw new InvalidOperationException($"Unsupported Game Boy for increment '{forSyntax.Increment.Value.GetType().Name}'.");
            }

            EmitAssignment(increment);
        }

        builder.JumpAbsolute(startLabel);
        builder.Label(endLabel);
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

    private void EmitIf(IfElseSyntax ifElseSyntax)
    {
        var falseLabel = builder.CreateLabel("if_false");
        var endLabel = builder.CreateLabel("if_end");

        EmitConditionFalseJump(ifElseSyntax.Condition, falseLabel);
        EmitBlock(ifElseSyntax.ThenBlock);
        if (ifElseSyntax.ElseBlock.HasValue)
        {
            builder.JumpAbsolute(endLabel);
            builder.Label(falseLabel);
            EmitBlock(ifElseSyntax.ElseBlock.Value);
            builder.Label(endLabel);
        }
        else
        {
            builder.Label(falseLabel);
        }
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
                    builder.JumpAbsolute(0xC2, falseLabel); // JP NZ,falseLabel
                    return;
                case "!=":
                    if (IsWordComparison(binary))
                    {
                        EmitWordInequalityFalseJump(binary.Left, binary.Right, falseLabel);
                        return;
                    }

                    EmitCompare(binary.Left, binary.Right);
                    builder.JumpAbsolute(0xCA, falseLabel); // JP Z,falseLabel
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
        builder.Emit(0xFE, 0x00);                   // CP $00
        builder.JumpAbsolute(0xCA, falseLabel);     // JP Z,falseLabel
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
                    builder.JumpAbsolute(0xCA, trueLabel); // JP Z,trueLabel
                    return;
                case "!=":
                    if (IsWordComparison(binary))
                    {
                        EmitWordInequalityTrueJump(binary.Left, binary.Right, trueLabel);
                        return;
                    }

                    EmitCompare(binary.Left, binary.Right);
                    builder.JumpAbsolute(0xC2, trueLabel); // JP NZ,trueLabel
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
        builder.JumpAbsolute(0xC2, trueLabel);       // JP NZ,trueLabel
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

        EmitVariableOperandsToAAndB(left, right);
        builder.CompareB();
    }

    private void EmitVariableOperandsToAAndB(ExpressionSyntax left, ExpressionSyntax right)
    {
        EmitExpressionToA(left);
        builder.PushAf();
        EmitExpressionToA(right);
        builder.LoadBFromA();
        builder.PopAf();
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
        builder.JumpAbsolute(0xC2, falseLabel); // JP NZ,falseLabel
        EmitCompareWordByte(left, right, highByte: false, signedHighByte: false);
        builder.JumpAbsolute(0xC2, falseLabel); // JP NZ,falseLabel
    }

    private void EmitWordInequalityFalseJump(ExpressionSyntax left, ExpressionSyntax right, string falseLabel)
    {
        var trueLabel = builder.CreateLabel("word_neq_true");
        EmitCompareWordByte(left, right, highByte: true, signedHighByte: false);
        builder.JumpAbsolute(0xC2, trueLabel); // JP NZ,trueLabel
        EmitCompareWordByte(left, right, highByte: false, signedHighByte: false);
        builder.JumpAbsolute(0xC2, trueLabel); // JP NZ,trueLabel
        builder.JumpAbsolute(falseLabel);
        builder.Label(trueLabel);
    }

    private void EmitWordEqualityTrueJump(ExpressionSyntax left, ExpressionSyntax right, string trueLabel)
    {
        var endLabel = builder.CreateLabel("word_eq_end");
        EmitCompareWordByte(left, right, highByte: true, signedHighByte: false);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel
        EmitCompareWordByte(left, right, highByte: false, signedHighByte: false);
        builder.JumpAbsolute(0xCA, trueLabel); // JP Z,trueLabel
        builder.Label(endLabel);
    }

    private void EmitWordInequalityTrueJump(ExpressionSyntax left, ExpressionSyntax right, string trueLabel)
    {
        EmitCompareWordByte(left, right, highByte: true, signedHighByte: false);
        builder.JumpAbsolute(0xC2, trueLabel); // JP NZ,trueLabel
        EmitCompareWordByte(left, right, highByte: false, signedHighByte: false);
        builder.JumpAbsolute(0xC2, trueLabel); // JP NZ,trueLabel
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
                builder.JumpAbsolute(0xDA, trueLabel);  // JP C,trueLabel
                builder.JumpAbsolute(0xC2, falseLabel); // JP NZ,falseLabel
                EmitCompareWordByte(binary.Left, binary.Right, highByte: false, signedHighByte: false);
                builder.JumpAbsolute(0xDA, trueLabel);  // JP C,trueLabel
                builder.JumpAbsolute(falseLabel);
                return;
            case "<=":
                builder.JumpAbsolute(0xDA, trueLabel);  // JP C,trueLabel
                builder.JumpAbsolute(0xC2, falseLabel); // JP NZ,falseLabel
                EmitCompareWordByte(binary.Left, binary.Right, highByte: false, signedHighByte: false);
                builder.JumpAbsolute(0xDA, trueLabel);  // JP C,trueLabel
                builder.JumpAbsolute(0xCA, trueLabel);  // JP Z,trueLabel
                builder.JumpAbsolute(falseLabel);
                return;
            case ">":
                builder.JumpAbsolute(0xDA, falseLabel); // JP C,falseLabel
                builder.JumpAbsolute(0xC2, trueLabel);  // JP NZ,trueLabel
                EmitCompareWordByte(binary.Left, binary.Right, highByte: false, signedHighByte: false);
                builder.JumpAbsolute(0xDA, falseLabel); // JP C,falseLabel
                builder.JumpAbsolute(0xCA, falseLabel); // JP Z,falseLabel
                builder.JumpAbsolute(trueLabel);
                return;
            case ">=":
                builder.JumpAbsolute(0xDA, falseLabel); // JP C,falseLabel
                builder.JumpAbsolute(0xC2, trueLabel);  // JP NZ,trueLabel
                EmitCompareWordByte(binary.Left, binary.Right, highByte: false, signedHighByte: false);
                builder.JumpAbsolute(0xDA, falseLabel); // JP C,falseLabel
                builder.JumpAbsolute(trueLabel);
                return;
            default:
                throw new InvalidOperationException($"Unsupported Game Boy relational operator '{binary.Operator.Symbol}'.");
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

        EmitLoadWordByteToB(right, highByte, signedHighByte);
        builder.CompareB();
    }

    private void EmitLoadWordByteToB(ExpressionSyntax expression, bool highByte, bool signedHighByte)
    {
        EmitLoadWordByteToA(expression, highByte, signedHighByte);
        builder.LoadBFromA();
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
                builder.LoadA(address);
            }
            else if (IsWordBackedType(type))
            {
                builder.LoadA(HighAddress(address));
            }
            else if (type == "i8")
            {
                builder.LoadA(address);
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

        EmitWordExpressionToStorage(expression, WordScratchLowAddress, WordExpressionType(expression));
        builder.LoadA(highByte ? WordScratchHighAddress : WordScratchLowAddress);
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

    // Signed relational comparison reuses the unsigned CP path after flipping both operands' sign bit
    // (XOR 0x80), which maps signed [-128,127] onto unsigned [0,255] while preserving order.
    private void EmitRelationalCompare(ExpressionSyntax left, ExpressionSyntax right, bool signed)
    {
        if (!signed)
        {
            EmitCompare(left, right);
            return;
        }

        EmitExpressionToA(left);
        builder.XorImmediate(0x80);
        builder.PushAf();
        EmitExpressionToA(right);
        builder.XorImmediate(0x80);
        builder.LoadBFromA();
        builder.PopAf();
        builder.CompareB();
    }

    private void EmitRelationalFalseJump(string op, string falseLabel)
    {
        switch (op)
        {
            case "<":
                builder.JumpAbsolute(0xD2, falseLabel); // JP NC,falseLabel
                return;
            case "<=":
                EmitGreaterThanFalseJump(falseLabel);
                return;
            case ">":
                builder.JumpAbsolute(0xDA, falseLabel); // JP C,falseLabel
                builder.JumpAbsolute(0xCA, falseLabel); // JP Z,falseLabel
                return;
            case ">=":
                builder.JumpAbsolute(0xDA, falseLabel); // JP C,falseLabel
                return;
            default:
                throw new InvalidOperationException($"Unsupported Game Boy relational operator '{op}'.");
        }
    }

    private void EmitGreaterThanFalseJump(string falseLabel)
    {
        var trueLabel = builder.CreateLabel("rel_true");
        builder.JumpAbsolute(0xDA, trueLabel);      // JP C,trueLabel
        builder.JumpAbsolute(0xCA, trueLabel);      // JP Z,trueLabel
        builder.JumpAbsolute(falseLabel);
        builder.Label(trueLabel);
    }

    private static string FlipRelationalOperator(string op)
    {
        return op switch
        {
            "<" => ">",
            "<=" => ">=",
            ">" => "<",
            ">=" => "<=",
            _ => throw new InvalidOperationException($"Unsupported Game Boy relational operator '{op}'."),
        };
    }

    private void EmitExpressionToA(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case ConstantSyntax:
                builder.LoadAImmediate(GameBoyVideoProgram.ConstValue(expression, "constant"));
                break;
            case IdentifierSyntax { Identifier: "true" }:
                builder.LoadAImmediate(1);
                break;
            case IdentifierSyntax { Identifier: "false" }:
                builder.LoadAImmediate(0);
                break;
            case IdentifierSyntax identifier:
                builder.LoadA(VariableAddress(identifier.Identifier));
                break;
            case MemberAccessSyntax memberAccess:
                if (TryRuntimeIndexedMemberAccess(memberAccess, out var indexedBase, out var fieldName))
                {
                    EmitRuntimeIndexedMemberAddressToHl(indexedBase, fieldName);
                    builder.LoadAFromHl();
                }
                else
                {
                    builder.LoadA(VariableAddress(GameBoyVideoProgram.MemberAccessName(memberAccess)));
                }

                break;
            case IndexExpressionSyntax indexExpression:
                if (TryConst(indexExpression.Index, out _))
                {
                    builder.LoadA(VariableAddress(IndexedElementName(indexExpression.BaseIdentifier, indexExpression.Index, $"{indexExpression.BaseIdentifier} array index")));
                }
                else
                {
                    EmitRuntimeIndexedAddressToHl(indexExpression.BaseIdentifier, indexExpression.Index);
                    builder.LoadAFromHl();
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
                throw new InvalidOperationException($"Unsupported Game Boy expression '{expression.GetType().Name}'.");
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

    private void EmitValueCallToA(FunctionCall call)
    {
        switch (call.Name)
        {
            case "map_tile_at":
                EmitMapTileAt(call);
                break;
            case "map_flags_at":
                EmitMapFlagsAt(call);
                break;
            case "collision_aabb_tiles":
                EmitCollisionAabbTiles(call);
                break;
            case "__rs_actor_camera_x_lo":
                GameBoyVideoProgram.RequireArity(call, 0);
                builder.LoadA(CameraXLowAddress);
                break;
            case "__rs_actor_camera_x_hi":
                GameBoyVideoProgram.RequireArity(call, 0);
                builder.LoadA(CameraXHighAddress);
                break;
            case "__rs_actor_camera_y_lo":
                GameBoyVideoProgram.RequireArity(call, 0);
                builder.LoadA(CameraYLowAddress);
                break;
            case "__rs_actor_camera_y_hi":
                GameBoyVideoProgram.RequireArity(call, 0);
                builder.LoadA(CameraYHighAddress);
                break;
            case "camera_tile_column_at":
                EmitCameraTileColumnAt(call);
                break;
            case "camera_span_tile_at":
                EmitCameraSpanTileAt(call);
                break;
            case "camera_span_has_tile":
                EmitCameraSpanHasTile(call);
                break;
            case "camera_span_has_flags":
                EmitCameraSpanHasFlags(call);
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

                throw new InvalidOperationException($"Unsupported Game Boy value API call '{call.Name}'.");
        }
    }

    private bool TryEmitTargetValueIntrinsic(FunctionCall call)
    {
        if (!program.Functions.TryGetValue(call.Name, out var function) || !function.IsExtern)
        {
            return false;
        }

        var intrinsic = TargetIntrinsicResolver.Resolve(function, GameBoyTarget.Intrinsics);
        switch (intrinsic.Operation)
        {
            case TargetIntrinsicOperation.ReadWorldTileFlags:
                GameBoyVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitReadWorldTileFlags(ConsumeSdkOperation<Sdk2DOperation.ReadWorldTileFlags>(call.Name));
                return true;
            case TargetIntrinsicOperation.CameraAabbTiles:
                GameBoyVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitCameraAabbTiles(ConsumeSdkOperation<Sdk2DOperation.CameraAabbTiles>(call.Name));
                return true;
            case TargetIntrinsicOperation.CameraAabbHitTop:
                GameBoyVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitCameraAabbHitTop(ConsumeSdkOperation<Sdk2DOperation.CameraAabbHitTop>(call.Name));
                return true;
            case TargetIntrinsicOperation.CameraScreenAabbTiles:
                GameBoyVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitCameraScreenAabbTiles(ConsumeSdkOperation<Sdk2DOperation.CameraScreenAabbTiles>(call.Name));
                return true;
            case TargetIntrinsicOperation.CameraScreenAabbHitTop:
                GameBoyVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitCameraScreenAabbHitTop(ConsumeSdkOperation<Sdk2DOperation.CameraScreenAabbHitTop>(call.Name));
                return true;
            case TargetIntrinsicOperation.ButtonDown:
                GameBoyVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitButtonDown(call);
                return true;
            case TargetIntrinsicOperation.ButtonJustPressed:
                GameBoyVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitButtonJustPressed(call);
                return true;
            case TargetIntrinsicOperation.ButtonJustReleased:
                GameBoyVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitButtonJustReleased(call);
                return true;
            case TargetIntrinsicOperation.ButtonHoldTicks:
                GameBoyVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitButtonHoldTicks(call);
                return true;
            case TargetIntrinsicOperation.ReadSpriteWidth:
                GameBoyVideoProgram.RequireArity(call, intrinsic.Arity);
                _ = TargetIntrinsicResolver.ResolveCall(function, call, GameBoyTarget.Intrinsics);
                EmitSpriteWidth(call);
                return true;
            case TargetIntrinsicOperation.ReadAnimationFrame:
                GameBoyVideoProgram.RequireArity(call, intrinsic.Arity);
                _ = TargetIntrinsicResolver.ResolveCall(function, call, GameBoyTarget.Intrinsics);
                EmitAnimationFrame(call);
                return true;
            default:
                return false;
        }
    }

    private void EmitMapTileAt(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 2);
        if (program.MapColumnHeight == 0)
        {
            throw new InvalidOperationException("map_tile_at requires at least one map_column declaration.");
        }

        var args = call.Parameters.ToList();
        var row = CheckedRange(GameBoyVideoProgram.ConstValue(args[1], "map_tile_at argument 2"), 0, program.MapColumnHeight - 1, "map_tile_at argument 2");

        EmitExpressionToA(args[0]);
        EmitReadOnlyMapByteAtSourceColumnInA(GameBoyRomBuilder.MapRowLabel(row));
    }

    private void EmitMapFlagsAt(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 2);
        if (program.MapFlagColumnHeight == 0)
        {
            throw new InvalidOperationException("map_flags_at requires world_map collision flag data.");
        }

        var args = call.Parameters.ToList();
        var row = CheckedRange(GameBoyVideoProgram.ConstValue(args[1], "map_flags_at argument 2"), 0, program.MapFlagColumnHeight - 1, "map_flags_at argument 2");

        EmitExpressionToA(args[0]);
        EmitMapFlagsAtSourceColumnInA(row);
    }

    private void EmitWorldTileFlagsAt(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 2);
        var args = call.Parameters.ToList();
        EmitWorldTileFlagsAt(args[0], 0, args[1], 0, call.Name);
    }

    private void EmitReadWorldTileFlags(Sdk2DOperation.ReadWorldTileFlags operation)
    {
        if (operation.WorldId != "default")
        {
            throw new InvalidOperationException($"Unsupported Game Boy world id '{operation.WorldId}'.");
        }

        EmitWorldTileFlagsAt(operation.WorldX, 0, operation.WorldY, 0, "world_tile_flags_at");
    }

    private void EmitWorldTileFlagsAt(ExpressionSyntax worldX, int worldXOffset, ExpressionSyntax worldY, int worldYOffset, string callName)
    {
        var worldMap = WorldMapForFlagQuery(callName);
        var outOfBoundsLabel = builder.CreateLabel("world_tile_flags_oob");
        var endLabel = builder.CreateLabel("world_tile_flags_end");
        if (TryConst(worldY, out var constantWorldY))
        {
            var row = (constantWorldY + worldYOffset) / 8;
            if (row < 0 || row >= worldMap.Height)
            {
                builder.LoadAImmediate(0);
                return;
            }

            EmitWorldPixelToTileCoordinate(worldX, worldXOffset);
            builder.CompareImmediate(worldMap.Width);
            builder.JumpAbsolute(0xD2, outOfBoundsLabel); // JP NC,outOfBoundsLabel
            EmitMapFlagsAtSourceColumnInA(row);
            builder.JumpAbsolute(endLabel);
            builder.Label(outOfBoundsLabel);
            builder.LoadAImmediate(0);
            builder.Label(endLabel);
            return;
        }

        EmitWorldPixelToTileCoordinate(worldX, worldXOffset);
        builder.CompareImmediate(worldMap.Width);
        builder.JumpAbsolute(0xD2, outOfBoundsLabel); // JP NC,outOfBoundsLabel
        builder.LoadBFromA();

        EmitWorldPixelToTileCoordinate(worldY, worldYOffset);
        builder.CompareImmediate(worldMap.Height);
        builder.JumpAbsolute(0xD2, outOfBoundsLabel); // JP NC,outOfBoundsLabel
        builder.LoadCFromA();
        EmitMapFlagsAtSourceColumnInBAndRowInC();
        builder.JumpAbsolute(endLabel);

        builder.Label(outOfBoundsLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private void EmitWorldTileFlagsAt(SdkByteExpression worldX, int worldXOffset, SdkByteExpression worldY, int worldYOffset, string callName)
    {
        var worldMap = WorldMapForFlagQuery(callName);
        var outOfBoundsLabel = builder.CreateLabel("world_tile_flags_oob");
        var endLabel = builder.CreateLabel("world_tile_flags_end");
        if (TrySdkConst(worldY, out var constantWorldY))
        {
            var row = (constantWorldY + worldYOffset) / 8;
            if (row < 0 || row >= worldMap.Height)
            {
                builder.LoadAImmediate(0);
                return;
            }

            EmitWorldPixelToTileCoordinate(worldX, worldXOffset);
            builder.CompareImmediate(worldMap.Width);
            builder.JumpAbsolute(0xD2, outOfBoundsLabel); // JP NC,outOfBoundsLabel
            EmitMapFlagsAtSourceColumnInA(row);
            builder.JumpAbsolute(endLabel);
            builder.Label(outOfBoundsLabel);
            builder.LoadAImmediate(0);
            builder.Label(endLabel);
            return;
        }

        EmitWorldPixelToTileCoordinate(worldX, worldXOffset);
        builder.CompareImmediate(worldMap.Width);
        builder.JumpAbsolute(0xD2, outOfBoundsLabel); // JP NC,outOfBoundsLabel
        builder.LoadBFromA();

        EmitWorldPixelToTileCoordinate(worldY, worldYOffset);
        builder.CompareImmediate(worldMap.Height);
        builder.JumpAbsolute(0xD2, outOfBoundsLabel); // JP NC,outOfBoundsLabel
        builder.LoadCFromA();
        EmitMapFlagsAtSourceColumnInBAndRowInC();
        builder.JumpAbsolute(endLabel);

        builder.Label(outOfBoundsLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private WorldMap2D WorldMapForFlagQuery(string callName)
    {
        return program.WorldMap
               ?? throw new InvalidOperationException($"{callName} requires world_map collision flag data.");
    }

    private void EmitWorldPixelToTileCoordinate(ExpressionSyntax expression, int offset)
    {
        EmitExpressionToA(expression);
        if (offset != 0)
        {
            builder.AddAImmediate(offset);
        }

        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
    }

    private void EmitWorldPixelToTileCoordinate(SdkByteExpression expression, int offset)
    {
        EmitSdkByteExpressionToA(expression);
        if (offset != 0)
        {
            builder.AddAImmediate(offset);
        }

        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
    }

    private void EmitWorldPixelTileTop(SdkByteExpression expression, int offset)
    {
        EmitSdkByteExpressionToA(expression);
        if (offset != 0)
        {
            builder.AddAImmediate(offset);
        }

        builder.AndImmediate(0xF8);
    }

    private void EmitCollisionAabbTiles(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 5);
        _ = WorldMapForFlagQuery(call.Name);
        var args = call.Parameters.ToList();
        var width = CheckedRange(ConstRuntimeValue(args[2], "collision_aabb_tiles argument 3"), 0, 255, "collision_aabb_tiles argument 3");
        var height = CheckedRange(ConstRuntimeValue(args[3], "collision_aabb_tiles argument 4"), 0, 255, "collision_aabb_tiles argument 4");
        var allowedFlags = (int)(WorldTileFlags.Solid | WorldTileFlags.Hazard | WorldTileFlags.Platform);
        var flags = CheckedRange(GameBoyVideoProgram.ConstValue(args[4], "collision_aabb_tiles argument 5"), 0, allowedFlags, "collision_aabb_tiles argument 5");
        if (width == 0 || height == 0 || flags == 0)
        {
            builder.LoadAImmediate(0);
            return;
        }

        var foundLabel = builder.CreateLabel("collision_aabb_tiles_found");
        var endLabel = builder.CreateLabel("collision_aabb_tiles_end");
        foreach (var yOffset in AabbSampleOffsets(height))
        {
            foreach (var xOffset in AabbSampleOffsets(width))
            {
                EmitWorldTileFlagsAt(args[0], xOffset, args[1], yOffset, call.Name);
                builder.AndImmediate(flags);
                builder.CompareImmediate(0);
                builder.JumpAbsolute(0xC2, foundLabel); // JP NZ,foundLabel
            }
        }

        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(foundLabel);
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
    }

    private void EmitCameraAabbTiles(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 5);
        var config = EnsureCameraConfigured(call.Name);
        _ = WorldMapForFlagQuery(call.Name);
        var args = call.Parameters.ToList();
        var screenX = CheckedRange(ConstRuntimeValue(args[0], "camera_aabb_tiles argument 1"), 0, 159, "camera_aabb_tiles argument 1");
        var width = CheckedRange(ConstRuntimeValue(args[2], "camera_aabb_tiles argument 3"), 0, 160, "camera_aabb_tiles argument 3");
        var height = CheckedRange(ConstRuntimeValue(args[3], "camera_aabb_tiles argument 4"), 0, 255, "camera_aabb_tiles argument 4");
        var allowedFlags = (int)(WorldTileFlags.Solid | WorldTileFlags.Hazard | WorldTileFlags.Platform);
        var flags = CheckedRange(GameBoyVideoProgram.ConstValue(args[4], "camera_aabb_tiles argument 5"), 0, allowedFlags, "camera_aabb_tiles argument 5");
        if (width == 0 || height == 0 || flags == 0)
        {
            builder.LoadAImmediate(0);
            return;
        }

        if (screenX + width > 160)
        {
            throw new InvalidOperationException("camera_aabb_tiles screen span must fit within the visible Game Boy width.");
        }

        var foundLabel = builder.CreateLabel("camera_aabb_tiles_found");
        var endLabel = builder.CreateLabel("camera_aabb_tiles_end");
        foreach (var yOffset in AabbSampleOffsets(height))
        {
            foreach (var xOffset in AabbSampleOffsets(width))
            {
                EmitCameraTileFlagsAt(screenX + xOffset, args[1], yOffset, config, call.Name);
                builder.AndImmediate(flags);
                builder.CompareImmediate(0);
                builder.JumpAbsolute(0xC2, foundLabel); // JP NZ,foundLabel
            }
        }

        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(foundLabel);
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
    }

    internal void EmitCameraAabbTiles(Sdk2DOperation.CameraAabbTiles operation)
    {
        if (operation.WorldId != "default")
        {
            throw new InvalidOperationException($"Unsupported Game Boy world id '{operation.WorldId}'.");
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

        ValidateConstantCameraAabbSpan(operation.ScreenX, width, 160, "camera_aabb_tiles");

        var foundLabel = builder.CreateLabel("camera_aabb_tiles_found");
        var endLabel = builder.CreateLabel("camera_aabb_tiles_end");
        foreach (var yOffset in AabbSampleOffsets(operation.Height))
        {
            foreach (var xOffset in AabbSampleOffsets(width))
            {
                EmitCameraTileFlagsAt(operation.ScreenX, xOffset, operation.WorldY, operation.WorldYOffset + yOffset, config, "camera_aabb_tiles");
                builder.AndImmediate(flags);
                builder.CompareImmediate(0);
                builder.JumpAbsolute(0xC2, foundLabel); // JP NZ,foundLabel
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
            throw new InvalidOperationException($"Unsupported Game Boy world id '{operation.WorldId}'.");
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

        ValidateConstantCameraAabbSpan(operation.ScreenX, width, 160, "camera_aabb_hit_top");

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
                builder.JumpAbsolute(0xCA, nextProbeLabel); // JP Z,nextProbeLabel
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
            throw new InvalidOperationException($"Unsupported Game Boy world id '{operation.WorldId}'.");
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

        ValidateConstantCameraAabbSpan(operation.ScreenX, width, 160, "camera_screen_aabb_tiles");

        var foundLabel = builder.CreateLabel("camera_screen_aabb_tiles_found");
        var endLabel = builder.CreateLabel("camera_screen_aabb_tiles_end");
        foreach (var yOffset in AabbSampleOffsets(operation.Height))
        {
            foreach (var xOffset in AabbSampleOffsets(width))
            {
                EmitCameraScreenTileFlagsAt(
                    operation.ScreenX,
                    xOffset,
                    operation.ScreenY,
                    operation.ScreenYOffset + yOffset,
                    config,
                    "camera_screen_aabb_tiles");
                builder.AndImmediate(flags);
                builder.CompareImmediate(0);
                builder.JumpAbsolute(0xC2, foundLabel); // JP NZ,foundLabel
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
            throw new InvalidOperationException($"Unsupported Game Boy world id '{operation.WorldId}'.");
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

        ValidateConstantCameraAabbSpan(operation.ScreenX, width, 160, callName);

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
                builder.JumpAbsolute(0xCA, nextProbeLabel); // JP Z,nextProbeLabel
                EmitScreenPixelTileTop(operation.ScreenY, hitTopOffset);
                builder.JumpAbsolute(endLabel);
                builder.Label(nextProbeLabel);
            }
        }

        builder.LoadAImmediate(255);
        builder.Label(endLabel);
    }

    private void EmitCameraTileFlagsAt(int screenPixelX, ExpressionSyntax worldY, int worldYOffset, CameraConfig config, string callName)
    {
        var worldMap = WorldMapForFlagQuery(callName);
        var outOfBoundsLabel = builder.CreateLabel("camera_tile_flags_oob");
        var endLabel = builder.CreateLabel("camera_tile_flags_end");
        if (TryConst(worldY, out var constantWorldY))
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

        EmitWorldPixelToTileCoordinate(worldY, worldYOffset);
        builder.CompareImmediate(worldMap.Height);
        builder.JumpAbsolute(0xD2, outOfBoundsLabel); // JP NC,outOfBoundsLabel
        builder.LoadCFromA();

        EmitCameraPixelToSourceColumn(screenPixelX, config.MapWidth);
        builder.LoadBFromA();
        EmitMapFlagsAtSourceColumnInBAndRowInC();
        builder.JumpAbsolute(endLabel);

        builder.Label(outOfBoundsLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private void EmitCameraTileFlagsAt(int screenPixelX, SdkByteExpression worldY, int worldYOffset, CameraConfig config, string callName)
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
        builder.LoadBFromA();

        EmitWorldPixelToTileCoordinate(worldY, worldYOffset);
        builder.CompareImmediate(worldMap.Height);
        builder.JumpAbsolute(0xD2, outOfBoundsLabel); // JP NC,outOfBoundsLabel
        builder.LoadCFromA();
        EmitMapFlagsAtSourceColumnInBAndRowInC();
        builder.JumpAbsolute(endLabel);

        builder.Label(outOfBoundsLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private void EmitCameraTileFlagsAt(SdkByteExpression screenPixelX, int screenPixelXOffset, SdkByteExpression worldY, int worldYOffset, CameraConfig config, string callName)
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
        builder.LoadBFromA();

        EmitWorldPixelToTileCoordinate(worldY, worldYOffset);
        builder.CompareImmediate(worldMap.Height);
        builder.JumpAbsolute(0xD2, outOfBoundsLabel); // JP NC,outOfBoundsLabel
        builder.LoadCFromA();
        EmitMapFlagsAtSourceColumnInBAndRowInC();
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
        CameraConfig config,
        string callName)
    {
        var worldMap = WorldMapForFlagQuery(callName);
        var endLabel = builder.CreateLabel("camera_screen_tile_flags_end");

        EmitCameraPixelToSourceColumn(screenPixelX, screenPixelXOffset, config.MapWidth);
        builder.StoreA(CameraScreenTileFlagsColumnAddress);

        EmitCameraPixelToSourceRow(screenPixelY, screenPixelYOffset, worldMap.Height);
        builder.LoadCFromA();
        builder.LoadA(CameraScreenTileFlagsColumnAddress);
        builder.LoadBFromA();
        EmitMapFlagsAtSourceColumnInBAndRowInC();
        builder.Label(endLabel);
    }

    private void EmitCameraPixelToSourceColumn(int screenPixelX, int mapWidth)
    {
        var wrapLabel = builder.CreateLabel("camera_pixel_column_wrap");
        var endLabel = builder.CreateLabel("camera_pixel_column_end");

        builder.LoadA(CameraFineXAddress);
        if (screenPixelX != 0)
        {
            builder.AddAImmediate(screenPixelX);
        }

        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.LoadBFromA();
        builder.LoadA(CameraScreenLeftColumnAddress);
        builder.AddAFromB();

        builder.Label(wrapLabel);
        builder.CompareImmediate(mapWidth);
        builder.JumpAbsolute(0xDA, endLabel); // JP C,endLabel
        builder.SubtractAImmediate(mapWidth);
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
            builder.AddAImmediate(screenPixelXOffset);
        }

        builder.LoadBFromA();
        builder.LoadA(CameraFineXAddress);
        builder.AddAFromB();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.LoadBFromA();
        builder.LoadA(CameraScreenLeftColumnAddress);
        builder.AddAFromB();

        builder.Label(wrapLabel);
        builder.CompareImmediate(mapWidth);
        builder.JumpAbsolute(0xDA, endLabel); // JP C,endLabel
        builder.SubtractAImmediate(mapWidth);
        builder.JumpAbsolute(wrapLabel);
        builder.Label(endLabel);
    }

    private void EmitCameraPixelToSourceRow(SdkByteExpression screenPixelY, int screenPixelYOffset, int mapHeight)
    {
        var wrapLabel = builder.CreateLabel("camera_pixel_row_wrap");
        var endLabel = builder.CreateLabel("camera_pixel_row_end");

        EmitSdkByteExpressionToA(screenPixelY);
        EmitAddSignedImmediateToA(screenPixelYOffset);
        builder.LoadBFromA();
        builder.LoadA(CameraFineYAddress);
        builder.AddAFromB();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.LoadBFromA();
        builder.LoadA(CameraTopSourceRowAddress);
        builder.AddAFromB();

        builder.Label(wrapLabel);
        builder.CompareImmediate(mapHeight);
        builder.JumpAbsolute(0xDA, endLabel); // JP C,endLabel
        builder.SubtractAImmediate(mapHeight);
        builder.JumpAbsolute(wrapLabel);
        builder.Label(endLabel);
    }

    private void EmitScreenPixelTileTop(SdkByteExpression expression, int offset)
    {
        EmitSdkByteExpressionToA(expression);
        EmitAddSignedImmediateToA(offset);
        builder.AndImmediate(0xF8);
    }

    private void EmitAddSignedImmediateToA(int offset)
    {
        if (offset > 0)
        {
            builder.AddAImmediate(offset);
        }
        else if (offset < 0)
        {
            builder.SubtractAImmediate(-offset);
        }
    }

    private static void ValidateConstantCameraAabbSpan(SdkByteExpression screenX, int width, int screenWidth, string callName)
    {
        if (screenX is SdkByteExpression.Constant constant && constant.Value + width > screenWidth)
        {
            throw new InvalidOperationException($"{callName} screen span must fit within the visible Game Boy width.");
        }
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

    private void EmitSpriteWidth(FunctionCall call)
    {
        builder.LoadAImmediate(SpriteWidth(call));
    }

    private void EmitAnimationFrame(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 2);
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
        var frameLabels = Enumerable
            .Range(0, Math.Max(clip.FrameCount - 1, 0))
            .Select(_ => builder.CreateLabel("animation_frame_match"))
            .ToArray();

        builder.Label(moduloLabel);
        builder.CompareImmediate(clip.DurationTicks);
        builder.JumpAbsolute(0xDA, afterModuloLabel); // JP C,afterModuloLabel
        builder.SubtractAImmediate(clip.DurationTicks);
        builder.JumpAbsolute(moduloLabel);

        builder.Label(afterModuloLabel);
        for (var i = 0; i < clip.FrameCount - 1; i++)
        {
            builder.CompareImmediate(clip.FrameStartTicks[i + 1]);
            builder.JumpAbsolute(0xDA, frameLabels[i]); // JP C,frameLabel
        }

        builder.LoadAImmediate(clip.FrameIndices[^1]);
        builder.JumpAbsolute(endLabel);

        for (var i = 0; i < frameLabels.Length; i++)
        {
            builder.Label(frameLabels[i]);
            builder.LoadAImmediate(clip.FrameIndices[i]);
            builder.JumpAbsolute(endLabel);
        }

        builder.Label(endLabel);
    }

    private SpriteAnimationClip AnimationClipArg(FunctionCall call)
    {
        var clipName = GameBoyVideoProgram.IdentifierArg(call.Parameters.ElementAt(0), "animation_frame argument 1");
        if (!program.AnimationClips.TryGetValue(clipName, out var clip))
        {
            throw new InvalidOperationException($"Unknown animation clip '{clipName}'. Declare it with animation_clip(...).");
        }

        return clip;
    }

    private void EmitCameraTileColumnAt(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 1);
        var config = EnsureCameraConfigured(call.Name);
        EmitCameraTileColumnAt(call.Parameters.ElementAt(0), config.MapWidth);
    }

    private void EmitCameraTileColumnAt(ExpressionSyntax screenColumnExpression, int mapWidth)
    {
        var wrapLabel = builder.CreateLabel("camera_tile_column_wrap");
        var endLabel = builder.CreateLabel("camera_tile_column_end");

        EmitExpressionToA(screenColumnExpression);
        builder.LoadBFromA();
        builder.LoadA(CameraScreenLeftColumnAddress);
        builder.AddAFromB();

        builder.Label(wrapLabel);
        builder.CompareImmediate(mapWidth);
        builder.JumpAbsolute(0xDA, endLabel); // JP C,endLabel
        builder.SubtractAImmediate(mapWidth);
        builder.JumpAbsolute(wrapLabel);
        builder.Label(endLabel);
    }

    private void EmitCameraSpanTileAt(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 3);
        var config = EnsureCameraConfigured(call.Name);
        var span = BuildCameraSpan(call, config.MapWidth, "camera_span_tile_at");
        var foundLabel = builder.CreateLabel("camera_span_tile_found");
        var endLabel = builder.CreateLabel("camera_span_tile_end");

        for (var screenColumn = span.FirstScreenColumn; screenColumn <= span.LastScreenColumn; screenColumn++)
        {
            EmitCameraTileColumnAt(new ConstantSyntax(screenColumn.ToString(CultureInfo.InvariantCulture)), config.MapWidth);
            EmitMapTileAtSourceColumnInA(span.Row);
            builder.CompareImmediate(0);
            builder.JumpAbsolute(0xC2, foundLabel); // JP NZ,foundLabel
        }

        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(foundLabel);
        builder.Label(endLabel);
    }

    private void EmitCameraSpanHasTile(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 4);
        var config = EnsureCameraConfigured(call.Name);
        var span = BuildCameraSpan(call, config.MapWidth, "camera_span_has_tile");
        var tile = CheckedRange(ConstRuntimeValue(call.Parameters.ElementAt(3), "camera_span_has_tile argument 4"), 0, 255, "camera_span_has_tile argument 4");
        var foundLabel = builder.CreateLabel("camera_span_has_tile_found");
        var endLabel = builder.CreateLabel("camera_span_has_tile_end");

        for (var screenColumn = span.FirstScreenColumn; screenColumn <= span.LastScreenColumn; screenColumn++)
        {
            EmitCameraTileColumnAt(new ConstantSyntax(screenColumn.ToString(CultureInfo.InvariantCulture)), config.MapWidth);
            EmitMapTileAtSourceColumnInA(span.Row);
            builder.CompareImmediate(tile);
            builder.JumpAbsolute(0xCA, foundLabel); // JP Z,foundLabel
        }

        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(foundLabel);
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
    }

    private void EmitCameraSpanHasFlags(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 4);
        if (program.MapFlagColumnHeight == 0)
        {
            throw new InvalidOperationException("camera_span_has_flags requires world_map collision flag data.");
        }

        var config = EnsureCameraConfigured(call.Name);
        var span = BuildCameraSpan(call, config.MapWidth, "camera_span_has_flags");
        var allowedFlags = (int)(WorldTileFlags.Solid | WorldTileFlags.Hazard | WorldTileFlags.Platform);
        var flags = CheckedRange(ConstRuntimeValue(call.Parameters.ElementAt(3), "camera_span_has_flags argument 4"), 1, allowedFlags, "camera_span_has_flags argument 4");
        var foundLabel = builder.CreateLabel("camera_span_has_flags_found");
        var endLabel = builder.CreateLabel("camera_span_has_flags_end");

        for (var screenColumn = span.FirstScreenColumn; screenColumn <= span.LastScreenColumn; screenColumn++)
        {
            EmitCameraTileColumnAt(new ConstantSyntax(screenColumn.ToString(CultureInfo.InvariantCulture)), config.MapWidth);
            EmitMapFlagsAtSourceColumnInA(span.Row);
            builder.AndImmediate(flags);
            builder.CompareImmediate(0);
            builder.JumpAbsolute(0xC2, foundLabel); // JP NZ,foundLabel
        }

        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(foundLabel);
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
    }

    private CameraSpanInfo BuildCameraSpan(FunctionCall call, int mapWidth, string context)
    {
        if (program.MapColumnHeight == 0)
        {
            throw new InvalidOperationException($"{context} requires at least one map_column declaration.");
        }

        var args = call.Parameters.ToList();
        var screenX = CheckedRange(ConstRuntimeValue(args[0], $"{context} argument 1"), 0, 255, $"{context} argument 1");
        var width = CheckedRange(ConstRuntimeValue(args[1], $"{context} argument 2"), 1, 255, $"{context} argument 2");
        var row = CheckedRange(ConstRuntimeValue(args[2], $"{context} argument 3"), 0, program.MapColumnHeight - 1, $"{context} argument 3");
        var firstScreenColumn = screenX / 8;
        var lastScreenColumn = (screenX + width - 1) / 8;
        if (lastScreenColumn > 31)
        {
            throw new InvalidOperationException($"{context} span must fit within the Game Boy background tilemap width.");
        }

        if (lastScreenColumn - firstScreenColumn + 1 > mapWidth)
        {
            throw new InvalidOperationException($"{context} span must not cover more columns than the configured camera map width.");
        }

        return new CameraSpanInfo(firstScreenColumn, lastScreenColumn, row);
    }

    private void EmitMapTileAtSourceColumnInA(int row)
    {
        EmitReadOnlyMapByteAtSourceColumnInA(GameBoyRomBuilder.MapRowLabel(row));
    }

    private void EmitMapFlagsAtSourceColumnInA(int row)
    {
        EmitReadOnlyMapFlagsByteAtSourceColumnInA(checked(row * MapFlagColumnCount()));
    }

    private int MapColumnCount()
    {
        return program.MapColumns.Keys.Max() + 1;
    }

    private int MapFlagColumnCount()
    {
        return program.MapFlagColumns.Keys.Max() + 1;
    }

    private void EmitReadOnlyMapFlagsByteAtSourceColumnInA(int rowOffset)
    {
        if (romLayout.TryReadOnlyDataPlacement(GameBoyRomBuilder.MapFlagDataLabel, out var placement))
        {
            builder.LoadAImmediate(placement.Bank);
            EmitSelectRomBankFromA();
            EmitMapDataByteAtSourceColumnInA(placement.Address, rowOffset);
            RestoreProgramBankAfterReadOnlyDataRead();
            return;
        }

        EmitMapDataByteAtSourceColumnInA(GameBoyRomBuilder.MapFlagDataLabel, rowOffset);
    }

    private void EmitMapFlagsAtSourceColumnInBAndRowInC()
    {
        if (romLayout.TryReadOnlyDataPlacement(GameBoyRomBuilder.MapFlagDataLabel, out var placement))
        {
            builder.LoadAImmediate(placement.Bank);
            EmitSelectRomBankFromA();
            EmitMapDataByteAtSourceColumnInBAndRowInC(placement.Address, MapFlagColumnCount());
            RestoreProgramBankAfterReadOnlyDataRead();
            return;
        }

        EmitMapDataByteAtSourceColumnInBAndRowInC(GameBoyRomBuilder.MapFlagDataLabel, MapFlagColumnCount());
    }

    private void EmitMapTileAtSourceColumnInBAndRowInC()
    {
        if (romLayout.TryReadOnlyDataPlacement(GameBoyRomBuilder.MapDataLabel, out var placement))
        {
            builder.LoadAImmediate(placement.Bank);
            EmitSelectRomBankFromA();
            EmitMapDataByteAtSourceColumnInBAndRowInC(placement.Address, MapColumnCount());
            RestoreProgramBankAfterReadOnlyDataRead();
            return;
        }

        EmitMapDataByteAtSourceColumnInBAndRowInC(GameBoyRomBuilder.MapDataLabel, MapColumnCount());
    }

    private void EmitMapDataByteAtSourceColumnInA(ushort baseAddress, int rowOffset)
    {
        builder.LoadHl(baseAddress);
        EmitAddConstantToHl(rowOffset);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.AddHlDe();
        builder.LoadAFromHl();
    }

    private void EmitMapDataByteAtSourceColumnInA(string baseLabel, int rowOffset)
    {
        builder.LoadHl(baseLabel);
        EmitAddConstantToHl(rowOffset);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.AddHlDe();
        builder.LoadAFromHl();
    }

    private void EmitMapDataByteAtSourceColumnInBAndRowInC(ushort baseAddress, int rowWidth)
    {
        builder.LoadHl(baseAddress);
        EmitAddRuntimeRowOffsetToHl(rowWidth);
        builder.LoadAFromB();
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.AddHlDe();
        builder.LoadAFromHl();
    }

    private void EmitMapDataByteAtSourceColumnInBAndRowInC(string baseLabel, int rowWidth)
    {
        builder.LoadHl(baseLabel);
        EmitAddRuntimeRowOffsetToHl(rowWidth);
        builder.LoadAFromB();
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.AddHlDe();
        builder.LoadAFromHl();
    }

    private void EmitAddRuntimeRowOffsetToHl(int rowWidth)
    {
        var doneLabel = builder.CreateLabel("map_flags_row_offset_done");
        var loopLabel = builder.CreateLabel("map_flags_row_offset_loop");

        builder.LoadAFromC();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, doneLabel); // JP Z,doneLabel
        builder.LoadDe((ushort)rowWidth);
        builder.Label(loopLabel);
        builder.AddHlDe();
        builder.DecrementA();
        builder.JumpAbsolute(0xC2, loopLabel); // JP NZ,loopLabel
        builder.Label(doneLabel);
    }

    private void EmitAddressWithOffsetModuloToA(ushort address, int offset, int modulo)
    {
        var endLabel = builder.CreateLabel("address_offset_modulo_end");

        builder.LoadA(address);
        if (offset != 0)
        {
            builder.AddAImmediate(offset);
        }

        builder.CompareImmediate(modulo);
        builder.JumpAbsolute(0xDA, endLabel); // JP C,endLabel
        builder.SubtractAImmediate(modulo);
        builder.Label(endLabel);
    }

    private void EmitAddConstantToHl(int offset)
    {
        if (offset == 0)
        {
            return;
        }

        builder.LoadDe((ushort)offset);
        builder.AddHlDe();
    }

    private void RestoreProgramBankAfterReadOnlyDataRead()
    {
        if (romLayout.ProgramTailBankCount == 0)
        {
            return;
        }

        builder.LoadBFromA();
        builder.LoadA(GameBoyRomBuilder.ProgramCurrentBankAddress);
        EmitSelectRomBankFromA();
        builder.LoadAFromB();
    }

    private void EmitReadOnlyMapByteAtSourceColumnInA(string rowLabel)
    {
        if (romLayout.TryReadOnlyDataPlacement(rowLabel, out _))
        {
            builder.JumpAbsolute(0xCD, ReadOnlyDataByteReaderLabel(rowLabel)); // CALL nn
            return;
        }

        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.LoadHl(rowLabel);
        builder.AddHlDe();
        builder.LoadAFromHl();
    }

    private void EmitButtonPressed(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 1);
        var button = ButtonArg(call, "button_pressed argument 1");
        var pressedLabel = builder.CreateLabel("button_pressed");
        var endLabel = builder.CreateLabel("button_end");

        EmitReadJoypadNibble(button.Selector);
        builder.AndImmediate(button.Mask);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, pressedLabel); // JP NZ,pressedLabel
        EmitDeselectJoypad();
        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(pressedLabel);
        EmitDeselectJoypad();
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
    }

    private void EmitButtonDown(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 1);
        EmitButtonMaskToBool(InputCurrentAddress, ButtonArg(call, "button_down argument 1"));
    }

    private void EmitButtonJustPressed(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 1);
        var button = ButtonArg(call, "button_just_pressed argument 1");
        var falseLabel = builder.CreateLabel("button_just_pressed_false");
        var endLabel = builder.CreateLabel("button_just_pressed_end");

        builder.LoadA(InputCurrentAddress);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, falseLabel); // JP Z,falseLabel

        builder.LoadA(InputPreviousAddress);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, falseLabel); // JP NZ,falseLabel

        builder.LoadAImmediate(1);
        builder.JumpAbsolute(endLabel);
        builder.Label(falseLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private void EmitButtonJustReleased(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 1);
        var button = ButtonArg(call, "button_just_released argument 1");
        var falseLabel = builder.CreateLabel("button_just_released_false");
        var endLabel = builder.CreateLabel("button_just_released_end");

        builder.LoadA(InputCurrentAddress);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, falseLabel); // JP NZ,falseLabel

        builder.LoadA(InputPreviousAddress);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, falseLabel); // JP Z,falseLabel

        builder.LoadAImmediate(1);
        builder.JumpAbsolute(endLabel);
        builder.Label(falseLabel);
        builder.LoadAImmediate(0);
        builder.Label(endLabel);
    }

    private void EmitButtonHoldTicks(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 1);
        builder.LoadA(ButtonArg(call, "button_hold_ticks argument 1").HoldTicksAddress);
    }

    private void EmitButtonMaskToBool(ushort address, GameBoyButton button)
    {
        var pressedLabel = builder.CreateLabel("button_down");
        var endLabel = builder.CreateLabel("button_down_end");

        builder.LoadA(address);
        builder.AndImmediate(button.SnapshotMask);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, pressedLabel); // JP NZ,pressedLabel
        builder.LoadAImmediate(0);
        builder.JumpAbsolute(endLabel);
        builder.Label(pressedLabel);
        builder.LoadAImmediate(1);
        builder.Label(endLabel);
    }

    private static GameBoyButton ButtonArg(FunctionCall call, string context)
    {
        var argument = call.Parameters.ElementAt(0);

        // A `Button` enum member (e.g. Button.A) is constant-folded to its ordinal,
        // which matches the canonical Buttons order, so resolve it by index.
        if (argument is ConstantSyntax)
        {
            var ordinal = GameBoyVideoProgram.ConstValue(argument, context);
            if (ordinal < 0 || ordinal >= Buttons.Length)
            {
                throw new InvalidOperationException($"Unsupported Game Boy button ordinal '{ordinal}'.");
            }

            return Buttons[ordinal];
        }

        throw new InvalidOperationException($"{context} must be a Button enum member.");
    }

    private void EmitBinaryExpressionToA(BinaryExpressionSyntax binary)
    {
        switch (binary.Operator.Symbol)
        {
            case "+":
                if (TryConst(binary.Right, out var addRight))
                {
                    EmitExpressionToA(binary.Left);
                    builder.AddAImmediate(addRight);
                    return;
                }

                if (TryConst(binary.Left, out var addLeft))
                {
                    EmitExpressionToA(binary.Right);
                    builder.AddAImmediate(addLeft);
                    return;
                }

                EmitExpressionToA(binary.Right);
                builder.LoadBFromA();
                EmitExpressionToA(binary.Left);
                builder.AddAFromB();
                return;
            case "-":
                if (TryConst(binary.Right, out var subtractRight))
                {
                    EmitExpressionToA(binary.Left);
                    builder.SubtractAImmediate(subtractRight);
                    return;
                }

                EmitVariableOperandsToAAndB(binary.Left, binary.Right);
                builder.SubtractB();
                return;
            case "&":
            case "|":
            case "^":
                EmitBitwiseBinaryExpressionToA(binary);
                return;
        }

        throw new InvalidOperationException($"Unsupported Game Boy binary expression '{binary.Operator.Symbol}'.");
    }

    private void EmitBitwiseBinaryExpressionToA(BinaryExpressionSyntax binary)
    {
        if (TryConst(binary.Right, out var rightConstant))
        {
            EmitExpressionToA(binary.Left);
            EmitBitwiseImmediate(binary.Operator.Symbol, rightConstant);
            return;
        }

        if (TryConst(binary.Left, out var leftConstant))
        {
            EmitExpressionToA(binary.Right);
            EmitBitwiseImmediate(binary.Operator.Symbol, leftConstant);
            return;
        }

        EmitExpressionToA(binary.Right);
        builder.LoadBFromA();
        EmitExpressionToA(binary.Left);
        EmitBitwiseAFromB(binary.Operator.Symbol);
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
                throw new InvalidOperationException($"Unsupported Game Boy bitwise operator '{op}'.");
        }
    }

    private void EmitBitwiseAFromB(string op)
    {
        switch (op)
        {
            case "&":
                builder.AndAFromB();
                return;
            case "|":
                builder.OrAFromB();
                return;
            case "^":
                builder.XorAFromB();
                return;
            default:
                throw new InvalidOperationException($"Unsupported Game Boy bitwise operator '{op}'.");
        }
    }

    private void EmitBitwiseAFromC(string op)
    {
        switch (op)
        {
            case "&":
                builder.AndAFromC();
                return;
            case "|":
                builder.OrAFromC();
                return;
            case "^":
                builder.XorAFromC();
                return;
            default:
                throw new InvalidOperationException($"Unsupported Game Boy bitwise operator '{op}'.");
        }
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
            value = GameBoyVideoProgram.ConstValue(expression, "constant");
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

    private void EmitRuntimeIndexedAddressToHl(string baseIdentifier, ExpressionSyntax index)
    {
        var baseAddress = VariableAddress(IndexedElementName(baseIdentifier, 0));
        var elementSize = StorageSize(VariableStorageType(IndexedElementName(baseIdentifier, 0)));
        EmitExpressionToA(index);
        EmitMultiplyA(elementSize);
        builder.LoadHl(baseAddress);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.AddHlDe();
    }

    private void EmitRuntimeIndexedMemberAddressToHl(IndexExpressionSyntax indexExpression, string fieldName)
    {
        var layout = StructArrayLayoutFor(indexExpression.BaseIdentifier);
        _ = layout.FieldOffsets[fieldName];
        var baseAddress = VariableAddress(IndexedMemberName(indexExpression.BaseIdentifier, 0, fieldName));
        EmitExpressionToA(indexExpression.Index);
        EmitMultiplyA(layout.Stride);
        builder.LoadHl(baseAddress);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.AddHlDe();
    }

    private void EmitRuntimeIndexedMemberAddressToHl(string baseIdentifier, SdkByteExpression index, string fieldName)
    {
        var layout = StructArrayLayoutFor(baseIdentifier);
        _ = layout.FieldOffsets[fieldName];
        var baseAddress = VariableAddress(IndexedMemberName(baseIdentifier, 0, fieldName));
        EmitSdkByteExpressionToA(index);
        EmitMultiplyA(layout.Stride);
        builder.LoadHl(baseAddress);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.AddHlDe();
    }

    private void EmitMultiplyA(int multiplier)
    {
        if (multiplier <= 1)
        {
            return;
        }

        builder.LoadBFromA();
        for (var count = 1; count < multiplier; count++)
        {
            builder.AddAFromB();
        }
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
            : throw new InvalidOperationException($"Game Boy target has no struct array layout for '{baseIdentifier}'.");
    }

    private void RequireExpressionPreservesHl(ExpressionSyntax expression, string context)
    {
        if (!PreservesHl(expression))
        {
            throw new InvalidOperationException($"Game Boy target cannot use expression '{expression.GetType().Name}' as the right side of a {context} yet because it also needs HL for array addressing.");
        }
    }

    private bool PreservesHl(ExpressionSyntax expression)
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
            CastSyntax cast => PreservesHl(cast.Expression),
            ConditionalExpressionSyntax conditional => PreservesHl(conditional.Condition) && PreservesHl(conditional.WhenTrue) && PreservesHl(conditional.WhenFalse),
            BinaryExpressionSyntax binary => PreservesHl(binary.Left) && PreservesHl(binary.Right),
            _ => false,
        };
    }

    private int ConstRuntimeValue(ExpressionSyntax expression, string context)
    {
        if (expression is CastSyntax cast)
        {
            RequireSupportedCastTarget(cast);
            return ConstRuntimeValue(cast.Expression, context);
        }

        if (TrySpriteWidth(expression, out var spriteWidth))
        {
            return spriteWidth;
        }

        return GameBoyVideoProgram.ConstValue(expression, context);
    }

    private bool TrySpriteWidth(ExpressionSyntax expression, out int width)
    {
        width = 0;
        if (expression is CastSyntax cast)
        {
            return TrySpriteWidth(cast.Expression, out width);
        }

        if (expression is not FunctionCall call)
        {
            return false;
        }

        if (!program.Functions.TryGetValue(call.Name, out var function))
        {
            return false;
        }

        if (function.IsExtern)
        {
            if (TargetAttributeReader.StringArgument(function, "intrinsic") is null)
            {
                return false;
            }

            var intrinsic = TargetIntrinsicResolver.Resolve(function, GameBoyTarget.Intrinsics);
            if (intrinsic.Operation != TargetIntrinsicOperation.ReadSpriteWidth)
            {
                return false;
            }

            width = SpriteWidth(call);
            return true;
        }

        if (function.Block.Statements is not [ReturnSyntax { Expression.HasValue: true }])
        {
            return false;
        }

        var returned = ParameterSubstitution.SubstituteReturnExpression(function, call, "Game Boy");
        return TrySpriteWidth(returned, out width);
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
                throw new InvalidOperationException($"Game Boy target struct array field type '{field.Type}' is not scalar.");
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
        var value = CheckedRange(GameBoyVideoProgram.ConstValue(index, context), 0, 255, context);
        return IndexedElementName(baseIdentifier, value);
    }

    private int SpriteWidth(FunctionCall call)
    {
        GameBoyVideoProgram.RequireArity(call, 1);
        var assetName = GameBoyVideoProgram.IdentifierArg(call.Parameters.ElementAt(0), "sprite_width argument 1");
        return SpriteWidth(assetName);
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
            throw new InvalidOperationException($"Unknown Game Boy sprite asset '{assetName}'. Declare it with sprite_asset(...).");
        }

        return asset.LogicalWidth;
    }

    private readonly record struct GameBoyButton(string Name, byte Selector, byte Mask, byte SnapshotMask, ushort HoldTicksAddress);

    private sealed class InlineVariableScope
    {
        public InlineVariableScope(string prefix)
        {
            Prefix = prefix;
        }

        public string Prefix { get; }
        public Dictionary<string, string> Names { get; } = new(StringComparer.Ordinal);
    }

    private ushort VariableAddress(string name)
    {
        var scopedName = ScopedVariableName(name);
        if (!variables.TryGetValue(scopedName, out var address))
        {
            throw new InvalidOperationException($"Use of undeclared variable '{name}'.");
        }

        return address;
    }

    private static int CheckedRange(int value, int min, int max, string context)
    {
        if (value < min || value > max)
        {
            throw new InvalidOperationException($"{context} must be between {min} and {max}.");
        }

        return value;
    }

    private CameraConfig EnsureCameraConfigured(string callName)
    {
        if (cameraMapWidth is not { } mapWidth || cameraStreamY is not { } streamY || cameraStreamHeight is not { } streamHeight)
        {
            throw new InvalidOperationException($"{callName} requires camera_init(...) to be emitted first.");
        }

        return new CameraConfig(mapWidth, streamY, streamHeight, program.MapColumnHeight);
    }

    private readonly record struct CameraConfig(int MapWidth, int StreamY, int StreamHeight, int SourceHeight);

    private readonly record struct CameraSpanInfo(int FirstScreenColumn, int LastScreenColumn, int Row);

    private readonly record struct LoopTarget(string BreakLabel, string ContinueLabel);

    private sealed class Sdk2DStreamReader(
        IReadOnlyList<Sdk2DStreamItem> main,
        IReadOnlyDictionary<string, IReadOnlyList<Sdk2DStreamItem>> subroutines)
    {
        private readonly Stack<StreamFrame> stack = [];
        private StreamFrame current = new("main", main);

        public static Sdk2DStreamReader ForProgram(GameBoyVideoProgram program)
        {
            if (program.SubroutineNames.Count == 0)
            {
                return new Sdk2DStreamReader(
                    program.SdkOperations.Select(operation => (Sdk2DStreamItem)new Sdk2DStreamItem.Op(operation)).ToArray(),
                    new Dictionary<string, IReadOnlyList<Sdk2DStreamItem>>());
            }

            return new Sdk2DStreamReader(program.SdkProgram.Main, program.SdkProgram.Subroutines);
        }

        public Sdk2DOperation ConsumeOperation(string callName)
        {
            if (current.Cursor >= current.Items.Count)
            {
                throw new InvalidOperationException($"Game Boy SDK call '{callName}' has no collected SDK operation in stream '{current.Name}'.");
            }

            var item = current.Items[current.Cursor++];
            return item is Sdk2DStreamItem.Op op
                ? op.Operation
                : throw new InvalidOperationException($"Game Boy SDK call '{callName}' expected a collected SDK operation in stream '{current.Name}', got {item.GetType().Name}.");
        }

        public void ConsumeSubroutineCall(string name)
        {
            if (current.Cursor >= current.Items.Count)
            {
                return;
            }

            if (current.Items[current.Cursor] is not Sdk2DStreamItem.CallSubroutine marker)
            {
                return;
            }

            if (!string.Equals(marker.Name, name, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Game Boy SDK stream expected subroutine call '{marker.Name}', got '{name}'.");
            }

            current.Cursor++;
        }

        public void EnterSubroutine(string name)
        {
            stack.Push(current);
            current = new StreamFrame(
                name,
                subroutines.TryGetValue(name, out var stream)
                    ? stream
                    : []);
        }

        public void LeaveSubroutine(string name)
        {
            EnsureCurrentConsumed($"Game Boy SDK subroutine '{name}'");
            current = stack.Pop();
        }

        public void EnsureAllConsumed(string context)
        {
            if (stack.Count != 0)
            {
                throw new InvalidOperationException($"{context} finished while SDK stream '{current.Name}' was still active.");
            }

            EnsureCurrentConsumed(context);
        }

        private void EnsureCurrentConsumed(string context)
        {
            if (current.Cursor == current.Items.Count)
            {
                return;
            }

            var item = current.Items[current.Cursor];
            var description = item is Sdk2DStreamItem.Op op
                ? op.Operation.GetType().Name
                : item.GetType().Name;
            throw new InvalidOperationException(
                $"{context} consumed {current.Cursor} of {current.Items.Count} SDK stream item(s) in '{current.Name}'; next item is {description}.");
        }

        private sealed class StreamFrame(string name, IReadOnlyList<Sdk2DStreamItem> items)
        {
            public string Name { get; } = name;

            public IReadOnlyList<Sdk2DStreamItem> Items { get; } = items;

            public int Cursor { get; set; }
        }
    }

    private sealed class SdkAudioStreamReader(
        IReadOnlyList<SdkAudioStreamItem> main,
        IReadOnlyDictionary<string, IReadOnlyList<SdkAudioStreamItem>> subroutines)
    {
        private readonly Stack<StreamFrame> stack = [];
        private StreamFrame current = new("main", main);

        public static SdkAudioStreamReader ForProgram(GameBoyVideoProgram program)
        {
            if (program.SubroutineNames.Count == 0)
            {
                return new SdkAudioStreamReader(
                    program.SdkAudioOperations.Select(operation => (SdkAudioStreamItem)new SdkAudioStreamItem.Op(operation)).ToArray(),
                    new Dictionary<string, IReadOnlyList<SdkAudioStreamItem>>());
            }

            return new SdkAudioStreamReader(program.SdkAudioProgram.Main, program.SdkAudioProgram.Subroutines);
        }

        public SdkAudioOperation ConsumeOperation(string callName)
        {
            if (current.Cursor >= current.Items.Count)
            {
                throw new InvalidOperationException($"Game Boy SDK audio call '{callName}' has no collected SDK audio operation in stream '{current.Name}'.");
            }

            var item = current.Items[current.Cursor++];
            return item is SdkAudioStreamItem.Op op
                ? op.Operation
                : throw new InvalidOperationException($"Game Boy SDK audio call '{callName}' expected a collected SDK audio operation in stream '{current.Name}', got {item.GetType().Name}.");
        }

        public void ConsumeSubroutineCall(string name)
        {
            if (current.Cursor >= current.Items.Count)
            {
                return;
            }

            if (current.Items[current.Cursor] is not SdkAudioStreamItem.CallSubroutine marker)
            {
                return;
            }

            if (!string.Equals(marker.Name, name, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Game Boy SDK audio stream expected subroutine call '{marker.Name}', got '{name}'.");
            }

            current.Cursor++;
        }

        public void EnterSubroutine(string name)
        {
            stack.Push(current);
            current = new StreamFrame(
                name,
                subroutines.TryGetValue(name, out var stream)
                    ? stream
                    : []);
        }

        public void LeaveSubroutine(string name)
        {
            EnsureCurrentConsumed($"Game Boy SDK audio subroutine '{name}'");
            current = stack.Pop();
        }

        public void EnsureAllConsumed(string context)
        {
            if (stack.Count != 0)
            {
                throw new InvalidOperationException($"{context} finished while SDK audio stream '{current.Name}' was still active.");
            }

            EnsureCurrentConsumed(context);
        }

        private void EnsureCurrentConsumed(string context)
        {
            if (current.Cursor == current.Items.Count)
            {
                return;
            }

            var item = current.Items[current.Cursor];
            var description = item is SdkAudioStreamItem.Op op
                ? op.Operation.GetType().Name
                : item.GetType().Name;
            throw new InvalidOperationException(
                $"{context} consumed {current.Cursor} of {current.Items.Count} SDK audio stream item(s) in '{current.Name}'; next item is {description}.");
        }

        private sealed class StreamFrame(string name, IReadOnlyList<SdkAudioStreamItem> items)
        {
            public string Name { get; } = name;

            public IReadOnlyList<SdkAudioStreamItem> Items { get; } = items;

            public int Cursor { get; set; }
        }
    }
}

internal sealed class GbBuilder
{
    private const int BaseAddress = 0x0150;
    private const int BankContinuationStubLength = 5;
    private readonly List<byte> bytes = [];
    private readonly Dictionary<string, int> labels = [];
    private readonly Dictionary<string, ushort> externalLabels = [];
    private readonly List<AbsoluteFixup> absoluteFixups = [];
    private readonly List<LabelByteFixup> labelByteFixups = [];
    private readonly List<(int Offset, string Label)> bankFixups = [];
    private readonly List<(int Offset, string Label)> relativeFixups = [];
    private int nextLabelId;
    private int? fixedPayloadLimit;
    private int? bankSize;
    private bool emitBankContinuationStubs;

    public string CreateLabel(string prefix) => $"{prefix}_{nextLabelId++}";

    public void Label(string name) => labels[name] = bytes.Count;

    public void ExternalLabel(string name, ushort address) => externalLabels[name] = address;

    public void EnableBankedAddressing(int fixedPayloadLimit, int bankSize)
    {
        this.fixedPayloadLimit = fixedPayloadLimit;
        this.bankSize = bankSize;
        emitBankContinuationStubs = true;
    }

    public void DisableBankContinuationStubs()
    {
        emitBankContinuationStubs = false;
    }

    public int LabelOffset(string label)
    {
        if (!labels.TryGetValue(label, out var offset))
        {
            throw new InvalidOperationException($"Unknown Game Boy ROM label '{label}'.");
        }

        return offset;
    }

    public bool TryLabelOffset(string label, out int offset)
    {
        return labels.TryGetValue(label, out offset);
    }

    public void Emit(params byte[] values)
    {
        if (values.Length == 0)
        {
            return;
        }

        InsertBankContinuationStubsBefore(values.Length);
        bytes.AddRange(values);
    }

    public void EmitLabelLowByte(string label)
    {
        Emit(0x00);
        labelByteFixups.Add(new LabelByteFixup(bytes.Count - 1, label, HighByte: false));
    }

    public void EmitLabelHighByte(string label)
    {
        Emit(0x00);
        labelByteFixups.Add(new LabelByteFixup(bytes.Count - 1, label, HighByte: true));
    }

    private void InsertBankContinuationStubsBefore(int byteCount)
    {
        if (!emitBankContinuationStubs || fixedPayloadLimit is null || bankSize is null)
        {
            return;
        }

        if (byteCount > bankSize.Value - BankContinuationStubLength)
        {
            return;
        }

        while (ShouldInsertBankContinuationStub(byteCount))
        {
            EmitBankContinuationStub();
        }
    }

    private bool ShouldInsertBankContinuationStub(int byteCount)
    {
        if (fixedPayloadLimit is null || bankSize is null || bytes.Count < fixedPayloadLimit.Value)
        {
            return false;
        }

        var bankOffset = (bytes.Count - fixedPayloadLimit.Value) % bankSize.Value;
        var payloadLimit = bankSize.Value - BankContinuationStubLength;
        return bankOffset >= payloadLimit || bankOffset + byteCount > payloadLimit;
    }

    private void EmitBankContinuationStub()
    {
        if (fixedPayloadLimit is null || bankSize is null)
        {
            return;
        }

        var tailOffset = bytes.Count - fixedPayloadLimit.Value;
        var bankOffset = tailOffset % bankSize.Value;
        var payloadLimit = bankSize.Value - BankContinuationStubLength;
        while (bankOffset < payloadLimit)
        {
            bytes.Add(0x00); // NOP padding up to the fixed-size continuation stub.
            bankOffset++;
        }

        var currentBank = 1 + (tailOffset / bankSize.Value);
        var nextBank = checked((byte)(currentBank + 1));
        bytes.Add(0x3E); // LD A,n
        bytes.Add(nextBank);
        bytes.Add(0xC3); // JP program_bank_continue
        bytes.Add(0x00);
        bytes.Add(0x00);
        absoluteFixups.Add(new AbsoluteFixup(bytes.Count - 2, GameBoyRomBuilder.ProgramBankContinuationLabel, IsControlFlow: true));
    }

    public void LoadAImmediate(int value)
    {
        Emit(0x3E, (byte)value);
    }

    public void LoadAImmediateBankOf(string label)
    {
        Emit(0x3E, 0x00);
        bankFixups.Add((bytes.Count - 1, label));
    }

    public void XorA()
    {
        Emit(0xAF);
    }

    public void LoadA(ushort address)
    {
        Emit(0xFA, (byte)(address & 0xFF), (byte)(address >> 8));
    }

    public void StoreA(ushort address)
    {
        Emit(0xEA, (byte)(address & 0xFF), (byte)(address >> 8));
    }

    public void StoreHighRamA(byte offset)
    {
        Emit(0xE0, offset);
    }

    public void StoreHighRamCFromA()
    {
        Emit(0xE2);
    }

    public void LoadHighRamA(byte offset)
    {
        Emit(0xF0, offset);
    }

    public void ComplementA()
    {
        Emit(0x2F);
    }

    public void AndImmediate(int value)
    {
        Emit(0xE6, (byte)value);
    }

    public void OrImmediate(int value)
    {
        Emit(0xF6, (byte)value);
    }

    public void XorImmediate(int value)
    {
        Emit(0xEE, (byte)value);
    }

    public void SwapA()
    {
        Emit(0xCB, 0x37);
    }

    public void ShiftRightLogicalA()
    {
        Emit(0xCB, 0x3F);
    }

    public void LoadBFromA()
    {
        Emit(0x47);
    }

    public void LoadDFromA()
    {
        Emit(0x57);
    }

    public void LoadCFromA()
    {
        Emit(0x4F);
    }

    public void LoadAFromB()
    {
        Emit(0x78);
    }

    public void PushAf()
    {
        Emit(0xF5);
    }

    public void PopAf()
    {
        Emit(0xF1);
    }

    public void PushHl()
    {
        Emit(0xE5);
    }

    public void PopHl()
    {
        Emit(0xE1);
    }

    public void LoadAFromC()
    {
        Emit(0x79);
    }

    public void LoadAFromD()
    {
        Emit(0x7A);
    }

    public void LoadAFromE()
    {
        Emit(0x7B);
    }

    public void LoadAFromH()
    {
        Emit(0x7C);
    }

    public void LoadAFromL()
    {
        Emit(0x7D);
    }

    public void AddAFromB()
    {
        Emit(0x80);
    }

    public void AdcAFromB()
    {
        Emit(0x88);
    }

    public void AddAFromC()
    {
        Emit(0x81);
    }

    public void AddAFromA()
    {
        Emit(0x87);
    }

    public void OrAFromB()
    {
        Emit(0xB0);
    }

    public void AndAFromB()
    {
        Emit(0xA0);
    }

    public void AndAFromC()
    {
        Emit(0xA1);
    }

    public void OrAFromC()
    {
        Emit(0xB1);
    }

    public void XorAFromB()
    {
        Emit(0xA8);
    }

    public void XorAFromC()
    {
        Emit(0xA9);
    }

    public void LoadLFromA()
    {
        Emit(0x6F);
    }

    public void LoadHFromA()
    {
        Emit(0x67);
    }

    public void LoadLFromE()
    {
        Emit(0x6B);
    }

    public void LoadHImmediate(int value)
    {
        Emit(0x26, (byte)value);
    }

    public void StoreHlA()
    {
        Emit(0x77);
    }

    public void AddAImmediate(int value)
    {
        Emit(0xC6, (byte)value);
    }

    public void AdcAImmediate(int value)
    {
        Emit(0xCE, (byte)value);
    }

    public void DecrementA()
    {
        Emit(0x3D);
    }

    public void SubtractAImmediate(int value)
    {
        Emit(0xD6, (byte)value);
    }

    public void SbcAImmediate(int value)
    {
        Emit(0xDE, (byte)value);
    }

    public void SubtractAFromC()
    {
        Emit(0x91);
    }

    public void SubtractB()
    {
        Emit(0x90);
    }

    public void SbcB()
    {
        Emit(0x98);
    }

    public void CompareImmediate(int value)
    {
        Emit(0xFE, (byte)value);
    }

    public void CompareB()
    {
        Emit(0xB8);
    }

    public void LoadHl(ushort value)
    {
        Emit(0x21, (byte)(value & 0xFF), (byte)(value >> 8));
    }

    public void IncrementHl()
    {
        Emit(0x23);
    }

    public void LoadHl(string label)
    {
        Emit(0x21, 0x00, 0x00);
        absoluteFixups.Add(new AbsoluteFixup(bytes.Count - 2, label, IsControlFlow: false));
    }

    public void LoadDImmediate(int value)
    {
        Emit(0x16, (byte)value);
    }

    public void LoadEFromA()
    {
        Emit(0x5F);
    }

    public void AddHlDe()
    {
        Emit(0x19);
    }

    public void AddHlBc()
    {
        Emit(0x09);
    }

    public void LoadAFromHl()
    {
        Emit(0x7E);
    }

    public void LoadDe(string label)
    {
        Emit(0x11, 0x00, 0x00);
        absoluteFixups.Add(new AbsoluteFixup(bytes.Count - 2, label, IsControlFlow: false));
    }

    public void LoadDe(ushort value)
    {
        Emit(0x11, (byte)(value & 0xFF), (byte)(value >> 8));
    }

    public void LoadBc(ushort value)
    {
        Emit(0x01, (byte)(value & 0xFF), (byte)(value >> 8));
    }

    public void JumpRelative(byte opcode, string label)
    {
        Emit(opcode, 0x00);
        relativeFixups.Add((bytes.Count - 1, label));
    }

    public void JumpAbsolute(string label)
    {
        JumpAbsolute(0xC3, label);
    }

    public void JumpAbsolute(byte opcode, string label)
    {
        Emit(opcode, 0x00, 0x00);
        absoluteFixups.Add(new AbsoluteFixup(bytes.Count - 2, label, IsControlFlow: true));
    }

    public byte[] Build()
    {
        foreach (var fixup in bankFixups)
        {
            bytes[fixup.Offset] = (byte)BankOf(fixup.Label);
        }

        foreach (var fixup in absoluteFixups)
        {
            if (fixup.IsControlFlow)
            {
                ValidateDirectControlFlow(fixup.Offset - 1, fixup.Label);
            }

            var address = AddressOf(fixup.Label);
            bytes[fixup.Offset] = (byte)(address & 0xFF);
            bytes[fixup.Offset + 1] = (byte)(address >> 8);
        }

        foreach (var fixup in labelByteFixups)
        {
            var address = AddressOf(fixup.Label);
            bytes[fixup.Offset] = fixup.HighByte
                ? (byte)(address >> 8)
                : (byte)(address & 0xFF);
        }

        foreach (var fixup in relativeFixups)
        {
            ValidateDirectControlFlow(fixup.Offset - 1, fixup.Label);
            var target = AddressOf(fixup.Label);
            var branchFrom = AddressOfOffset(fixup.Offset + 1);
            var delta = target - branchFrom;
            if (delta is < -128 or > 127)
            {
                throw new InvalidOperationException($"Relative jump to '{fixup.Label}' is out of range.");
            }

            bytes[fixup.Offset] = unchecked((byte)(sbyte)delta);
        }

        return bytes.ToArray();
    }

    private void ValidateDirectControlFlow(int sourceOffset, string targetLabel)
    {
        if (!labels.TryGetValue(targetLabel, out var targetOffset))
        {
            return;
        }

        var sourceBank = BankOfOffset(sourceOffset);
        var targetBank = BankOfOffset(targetOffset);
        if (sourceBank == targetBank || sourceBank == 0 || targetBank == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Direct Game Boy control flow from switchable program bank {sourceBank} to bank {targetBank} for label '{targetLabel}' is not supported; route it through a fixed-bank trampoline or helper.");
    }

    private int AddressOfOffset(int offset)
    {
        if (fixedPayloadLimit is null || bankSize is null || offset < fixedPayloadLimit.Value)
        {
            return BaseAddress + offset;
        }

        var bankedOffset = offset - fixedPayloadLimit.Value;
        return 0x4000 + (bankedOffset % bankSize.Value);
    }

    private int AddressOf(string label)
    {
        if (externalLabels.TryGetValue(label, out var externalAddress))
        {
            return externalAddress;
        }

        if (!labels.TryGetValue(label, out var offset))
        {
            throw new InvalidOperationException($"Unknown Game Boy ROM label '{label}'.");
        }

        return AddressOfOffset(offset);
    }

    private int BankOf(string label)
    {
        if (!labels.TryGetValue(label, out var offset))
        {
            throw new InvalidOperationException($"Unknown Game Boy ROM label '{label}'.");
        }

        return BankOfOffset(offset);
    }

    private int BankOfOffset(int offset)
    {
        if (fixedPayloadLimit is null || bankSize is null || offset < fixedPayloadLimit.Value)
        {
            return 0;
        }

        return 1 + ((offset - fixedPayloadLimit.Value) / bankSize.Value);
    }

    private readonly record struct AbsoluteFixup(int Offset, string Label, bool IsControlFlow);

    private readonly record struct LabelByteFixup(int Offset, string Label, bool HighByte);
}
