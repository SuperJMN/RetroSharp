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
    internal const string Mbc1FarReadByteLabel = "mbc1_far_read_byte";
    internal const string WorldPackLabel = "worldpack_default";
    internal const string WorldPackValidateLabel = "worldpack_validate";
    internal const string WorldPackVisualDecodeLabel = "worldpack_decode_visual";
    internal const string WorldPackVisualLookupLabel = "worldpack_visual_lookup";
    internal const string WorldPackVisualEdgeLookupLabel = "worldpack_visual_edge_lookup";
    internal const string WorldPackVisualEdgeContinueLabel = "worldpack_visual_edge_continue";
    internal const string WorldPackVisualEdgeExpansionLabel = "worldpack_visual_edge_expansion";
    internal const string WorldPackCollisionDecodeLabel = "worldpack_decode_collision";
    internal const string WorldPackCollisionLookupLabel = "worldpack_collision_lookup";
    internal const string WorldPackPrepareEdgeLabel = "worldpack_prepare_edge";
    internal const string WorldPackWaitOutsideVBlankLabel = "worldpack_wait_outside_vblank";
    internal const string WorldPackWaitIfInVBlankLabel = "worldpack_wait_if_in_vblank";
    internal const string WorldPackWaitAudioTickLabel = "worldpack_wait_audio_tick";
    internal const string WorldPackObserveFrameWrapLabel = "worldpack_observe_frame_wrap";

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
        return BuildWithReport(program).Rom;
    }

    internal static GameBoyRomBuildResult BuildWithReport(GameBoyVideoProgram program, byte[]? packedWorldOverride = null)
    {
        var packedWorldBytes = packedWorldOverride ?? program.PackedWorld?.SerializedBytes;
        if (packedWorldOverride is null
            && program.PackedWorld is not null
            && ProgramUsesCameraStreaming(program)
            && !RequiresPackedCamera(program.PackedWorld.Pack.Descriptor))
        {
            // Keep camera programs whose map still fits the legacy byte/32-row limits on the raw
            // compatibility route. Wide or tall worlds bypass this branch and select packed staging.
            return BuildWithReportCore(program, packedWorldBytes: null);
        }

        try
        {
            return BuildWithReportCore(program, packedWorldBytes);
        }
        catch (InvalidOperationException exception) when (
            packedWorldOverride is null &&
            program.PackedWorld is not null &&
            IsMissingLegacyWorldLabel(exception.Message))
        {
            // Packed reader-only builds can suppress legacy visual rows that a remaining raw
            // operation still references. Preserve the established raw compatibility route.
            return BuildWithReportCore(program, packedWorldBytes: null);
        }
    }

    private static bool ProgramUsesCameraStreaming(GameBoyVideoProgram program) =>
        program.SdkOperations.Any(operation => operation is Sdk2DOperation.SetCameraPosition or Sdk2DOperation.ApplyCamera);

    private static bool ProgramUsesDiagonalCameraStreaming(GameBoyVideoProgram program) =>
        program.SdkOperations.Any(operation =>
            operation is Sdk2DOperation.SetCameraPosition position
            && position.Axes.HasFlag(ScrollAxes.Horizontal)
            && position.Axes.HasFlag(ScrollAxes.Vertical));

    private static bool ProgramUsesAudioUpdate(GameBoyVideoProgram program) =>
        program.SdkAudioOperations.Any(operation => operation is SdkAudioOperation.UpdateAudio);

    private static bool RequiresPackedCamera(WorldPackDescriptor descriptor) =>
        descriptor.HardwareWidth > byte.MaxValue || descriptor.HardwareHeight > 32;

    private static GameBoyRomBuildResult BuildWithReportCore(GameBoyVideoProgram program, byte[]? packedWorldBytes)
    {
        var readOnlyData = BuildReadOnlyData(program, packedWorldBytes is not null);
        GbBuilder builder;
        byte[] programBytes;
        if (packedWorldBytes is null || packedWorldBytes.Length <= ushort.MaxValue)
        {
            var romOnlyLayout = GameBoyRomLayout.RomOnly;
            builder = BuildProgram(program, romOnlyLayout, readOnlyData, packedWorldBytes, out var userVariables);
            programBytes = builder.Build();
            if (programBytes.Length <= RomOnlyPayloadLimit)
            {
                var rom = new byte[RomOnlySize];
                WriteHeaderSkeleton(rom, CartridgeTypeRomOnly, romSizeCode: 0x00);
                programBytes.CopyTo(rom, FixedBankProgramStart);
                WriteHeaderChecksums(rom);
                return new GameBoyRomBuildResult(
                    rom,
                    BuildRomOnlyReport(builder, program, programBytes.Length, packedWorldBytes, userVariables));
            }
        }

        var bankedLayout = GameBoyRomLayout.CreateBankedMusicLayout(program, readOnlyData, packedWorldBytes);
        builder = BuildProgram(program, bankedLayout, readOnlyData, packedWorldBytes, out var bankedUserVariables);
        programBytes = builder.Build();
        var programTailBanks = CalculateProgramTailBanks(programBytes.Length);
        if (programTailBanks > 1)
        {
            bankedLayout = GameBoyRomLayout.CreateBankedMusicLayout(program, readOnlyData, packedWorldBytes, bankReadOnlyData: true);
            builder = BuildProgram(program, bankedLayout, readOnlyData, packedWorldBytes, out bankedUserVariables);
            programBytes = builder.Build();
            programTailBanks = CalculateProgramTailBanks(programBytes.Length);
        }

        EnsureSupportedProgramTailBanks(programTailBanks, bankedLayout.UsesBankedReadOnlyData);
        if (programTailBanks != bankedLayout.ProgramTailBankCount)
        {
            bankedLayout = GameBoyRomLayout.CreateBankedMusicLayout(
                program,
                readOnlyData,
                packedWorldBytes,
                programTailBanks,
                bankedLayout.UsesBankedReadOnlyData);
            builder = BuildProgram(program, bankedLayout, readOnlyData, packedWorldBytes, out bankedUserVariables);
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

        if (bankedLayout.WorldPackPlacement is { } worldPackPlacement)
        {
            foreach (var segment in worldPackPlacement.Segments)
            {
                worldPackPlacement.SerializedBytes.AsSpan(segment.RelativeOffset, segment.Length).CopyTo(
                    bankedRom.AsSpan(segment.Bank * BankSize + segment.Address - BankedWindowStart, segment.Length));
            }
        }

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
        return new GameBoyRomBuildResult(
            bankedRom,
            BuildBankedReport(builder, program, bankedLayout, programBytes.Length, packedWorldBytes, bankedUserVariables));
    }

    private static bool IsMissingLegacyWorldLabel(string message)
    {
        if (!message.StartsWith("Unknown Game Boy ROM label '", StringComparison.Ordinal))
        {
            return false;
        }

        return message.Contains("map_row_", StringComparison.Ordinal)
               || message.Contains(MapDataLabel, StringComparison.Ordinal)
               || message.Contains(MapFlagDataLabel, StringComparison.Ordinal)
               || message.Contains("background_stream_row_", StringComparison.Ordinal);
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

    private static GameBoyRomBuildReport BuildRomOnlyReport(
        GbBuilder builder,
        GameBoyVideoProgram program,
        int programLength,
        byte[]? inlineWorldPack,
        IReadOnlyList<GameBoyRuntimeUserVariable> userVariables)
    {
        var segments = BuildInlineProgramSegments(builder, program, programLength, inlineWorldPack);
        return CreateBuildReport("gb-rom-only-current", RomOnlySize, segments, BuildFixedSymbols(builder), userVariables);
    }

    private static GameBoyRomBuildReport BuildBankedReport(
        GbBuilder builder,
        GameBoyVideoProgram program,
        GameBoyRomLayout layout,
        int programLength,
        byte[]? packedWorldBytes,
        IReadOnlyList<GameBoyRuntimeUserVariable> userVariables)
    {
        var segments = BuildInlineProgramSegments(
            builder,
            program,
            programLength,
            layout.WorldPackPlacement is null ? packedWorldBytes : null).ToList();
        if (layout.WorldPackPlacement is { } worldPackPlacement)
        {
            foreach (var segment in worldPackPlacement.Segments)
            {
                AddPhysicalSegments(
                    segments,
                    "worldpack:default",
                    segment.Bank * BankSize + segment.Address - BankedWindowStart,
                    segment.Length);
            }
        }
        foreach (var placement in layout.ReadOnlyDataPlacements.Values.OrderBy(item => item.Bank).ThenBy(item => item.Address).ThenBy(item => item.Label, StringComparer.Ordinal))
        {
            AddPhysicalSegments(segments, $"read-only:{placement.Label}", placement.Bank * BankSize + placement.Address - BankedWindowStart, placement.Data.Length);
        }

        foreach (var placement in layout.MusicPlacements.Values.OrderBy(item => item.Bank).ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            AddPhysicalSegments(segments, $"bgm:{placement.Name}", placement.Bank * BankSize, placement.Data.Length);
        }

        foreach (var placement in layout.SoundEffectPlacements.Values.OrderBy(item => item.Bank).ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            AddPhysicalSegments(segments, $"sfx:{placement.Name}", placement.Bank * BankSize, placement.Data.Length);
        }

        return CreateBuildReport("gb-simple-mbc1-current", layout.RomSize, segments, BuildFixedSymbols(builder), userVariables);
    }

    private static IReadOnlyList<GameBoyRomBuildSegment> BuildInlineProgramSegments(
        GbBuilder builder,
        GameBoyVideoProgram program,
        int programLength,
        byte[]? inlineWorldPack)
    {
        var known = new List<(int Start, int Length, string Owner)>();
        AddKnownInlineRange(known, builder, TileDataLabel, BuildTileData(program).Length, "read-only:tile-data");
        AddKnownInlineRange(known, builder, TileMapLabel, program.TileMap.Length, "read-only:tilemap");
        if (program.UsesWindowHud)
        {
            AddKnownInlineRange(known, builder, WindowTileMapLabel, program.WindowTileMap.Length, "read-only:window-tilemap");
        }

        if (inlineWorldPack is not null)
        {
            AddKnownInlineRange(known, builder, WorldPackLabel, inlineWorldPack.Length, "worldpack:default");
        }
        else if (program.MapColumnHeight != 0 && builder.TryLabelOffset(MapDataLabel, out var mapStart))
        {
            var nextAsset = program.MusicAssetsInLoadOrder
                .Select(asset => builder.TryLabelOffset(MusicLabel(asset.Name), out var offset) ? offset : int.MaxValue)
                .Concat(program.SoundEffectAssetsInLoadOrder.Select(asset => builder.TryLabelOffset(SoundEffectLabel(asset.Name), out var offset) ? offset : int.MaxValue))
                .Append(programLength)
                .Min();
            known.Add((mapStart, nextAsset - mapStart, "legacy-world-data:default"));
        }

        foreach (var asset in program.MusicAssetsInLoadOrder)
        {
            AddKnownInlineRange(known, builder, MusicLabel(asset.Name), asset.Data.Length, $"bgm:{asset.Name}");
        }

        foreach (var asset in program.SoundEffectAssetsInLoadOrder)
        {
            AddKnownInlineRange(known, builder, SoundEffectLabel(asset.Name), asset.Data.Length, $"sfx:{asset.Name}");
        }

        var segments = new List<GameBoyRomBuildSegment>();
        AddPhysicalSegments(segments, "fixed-bank/header", 0, FixedBankProgramStart);
        var cursor = 0;
        foreach (var range in known.OrderBy(item => item.Start).ThenBy(item => item.Owner, StringComparer.Ordinal))
        {
            if (range.Start > cursor)
            {
                AddPhysicalSegments(segments, "program", FixedBankProgramStart + cursor, range.Start - cursor);
            }

            AddPhysicalSegments(segments, range.Owner, FixedBankProgramStart + range.Start, range.Length);
            cursor = checked(range.Start + range.Length);
        }

        if (cursor < programLength)
        {
            AddPhysicalSegments(segments, "program", FixedBankProgramStart + cursor, programLength - cursor);
        }

        return segments;
    }

    private static void AddKnownInlineRange(
        List<(int Start, int Length, string Owner)> ranges,
        GbBuilder builder,
        string label,
        int length,
        string owner)
    {
        if (builder.TryLabelOffset(label, out var offset))
        {
            ranges.Add((offset, length, owner));
        }
    }

    private static void AddPhysicalSegments(
        ICollection<GameBoyRomBuildSegment> segments,
        string owner,
        int physicalStart,
        int length)
    {
        var remaining = length;
        var current = physicalStart;
        while (remaining > 0)
        {
            var bank = current / BankSize;
            var bankOffset = current % BankSize;
            var partLength = Math.Min(remaining, BankSize - bankOffset);
            var cpuAddress = checked((ushort)(bank == 0 ? bankOffset : BankedWindowStart + bankOffset));
            segments.Add(new GameBoyRomBuildSegment(owner, current, partLength, bank, cpuAddress));
            current += partLength;
            remaining -= partLength;
        }
    }

    private static GameBoyRomBuildReport CreateBuildReport(
        string selectedProfile,
        int romSize,
        IEnumerable<GameBoyRomBuildSegment> segments,
        IReadOnlyDictionary<string, ushort> fixedSymbols,
        IReadOnlyList<GameBoyRuntimeUserVariable> userVariables)
    {
        var ordered = segments
            .OrderBy(segment => segment.PhysicalStart)
            .ThenBy(segment => segment.Owner, StringComparer.Ordinal)
            .ToArray();
        var occupiedBanks = ordered.Select(segment => segment.Bank).Distinct().Order().ToArray();
        return new GameBoyRomBuildReport(selectedProfile, romSize, ordered, occupiedBanks, fixedSymbols, userVariables);
    }

    private static IReadOnlyDictionary<string, ushort> BuildFixedSymbols(GbBuilder builder)
    {
        var result = new Dictionary<string, ushort>(StringComparer.Ordinal);
        foreach (var label in new[]
                 {
                     Mbc1FarReadByteLabel,
                     WorldPackValidateLabel,
                     WorldPackVisualDecodeLabel,
                     WorldPackVisualLookupLabel,
                     WorldPackCollisionDecodeLabel,
                     WorldPackCollisionLookupLabel,
                 })
        {
            if (builder.TryLabelAddress(label, out var address) && address < BankedWindowStart)
            {
                result[label] = address;
            }
        }

        return result;
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
        IReadOnlyList<GameBoyReadOnlyDataBlob> readOnlyData,
        byte[]? packedWorldBytes,
        out IReadOnlyList<GameBoyRuntimeUserVariable> userVariables)
    {
        var enableDiagonalVisualCache = ProgramUsesDiagonalCameraStreaming(program);
        var enablePackedCameraCache = ProgramUsesCameraStreaming(program);
        var worldPackRuntime = packedWorldBytes is null
            ? null
            : GameBoyWorldPackRuntimePlan.Create(
                packedWorldBytes,
                enablePackedCameraCache,
                enableDiagonalVisualCache);
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
        var usesShadowOam = program.SdkOperations.Any(operation => operation is Sdk2DOperation.DrawLogicalSprite);
        if (usesShadowOam)
        {
            EmitInstallOamDmaRoutine(builder);
        }
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
        if (usesShadowOam)
        {
            EmitClearOamShadow(builder, "clear_oam_shadow");
        }

        var lcdControl = (byte)(program.UsesWindowHud ? 0xF7 : 0x97);
        if (worldPackRuntime is null)
        {
            builder.Emit(0x3E, lcdControl);
            builder.Emit(0xE0, 0x40);               // LDH ($40),A
        }

        var usesPackedCameraRuntime = worldPackRuntime is not null && ProgramUsesCameraStreaming(program);
        var runtime = new GameBoyRuntimeCompiler(
            builder,
            program,
            layout,
            worldPackRuntime?.Layout,
            usesPackedCameraRuntime);
        var usesMbc1Foundation = layout.ProgramTailBankCount > 0 || layout.UsesBankedReadOnlyData || layout.UsesBankedMusic || layout.UsesBankedWorldPack;
        runtime.EmitProgramBankInitialization(usesMbc1Foundation);
        if (worldPackRuntime is not null)
        {
            GameBoyWorldPackRuntimeEmitter.EmitInitializeState(builder, worldPackRuntime);
            builder.StoreA(GameBoyRuntimeMemoryLayout.WorldPack.ValidationState);
        }

        var usesBankContinuationHelper = layout.ProgramTailBankCount > 0;
        if (usesMbc1Foundation || worldPackRuntime is not null || runtime.UsesReadOnlyDataHelpers || runtime.UsesAudioHelpers || runtime.UsesSubroutineTrampolines)
        {
            builder.JumpAbsolute(ProgramStartLabel);
            if (usesMbc1Foundation)
            {
                EmitMbc1FarReadByteHelper(builder);
            }

            if (worldPackRuntime is not null)
            {
                GameBoyWorldPackRuntimeEmitter.Emit(
                    builder,
                    worldPackRuntime,
                    layout,
                    usesPackedCameraRuntime,
                    enableDiagonalVisualCache,
                    ProgramUsesAudioUpdate(program));
            }

            if (usesBankContinuationHelper)
            {
                EmitProgramBankContinuationHelper(builder);
            }

            runtime.EmitReadOnlyDataHelpers();
            runtime.EmitAudioHelpers();
            runtime.EmitSubroutineTrampolines();
            builder.Label(ProgramStartLabel);
        }

        if (worldPackRuntime is not null)
        {
            var validated = builder.CreateLabel("worldpack_startup_validated");
            var invalid = builder.CreateLabel("worldpack_startup_invalid");
            builder.JumpAbsolute(0xCD, WorldPackValidateLabel);
            builder.LoadAFromB();
            builder.CompareImmediate((byte)GameBoyWorldPackResult.Success);
            builder.JumpAbsolute(0xCA, validated);
            builder.Label(invalid);
            builder.JumpRelative(0x18, invalid);
            builder.Label(validated);
            builder.Emit(0x3E, lcdControl);
            builder.Emit(0xE0, 0x40);               // LDH ($40),A after packed validation.
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

        if (packedWorldBytes is not null)
        {
            if (layout.WorldPackPlacement is null)
            {
                builder.Label(WorldPackLabel);
                builder.Emit(packedWorldBytes);
            }
        }
        else
        {
            EmitMapData(builder, program);
        }
        if (!layout.UsesBankedMusic)
        {
            EmitAudioData(builder, program);
        }

        userVariables = runtime.UserVariables;
        return builder;
    }

    private static void EmitProgramBankContinuationHelper(GbBuilder builder)
    {
        builder.Label(ProgramBankContinuationLabel);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Banking.ProgramCurrentBank);
        EmitSelectRomBankFromA(builder);
        builder.Emit(0xC3, (byte)(BankedWindowStart & 0xFF), (byte)(BankedWindowStart >> 8)); // JP $4000
    }

    private static void EmitMbc1FarReadByteHelper(GbBuilder builder)
    {
        var missLabel = builder.CreateLabel("mbc1_far_read_miss");
        var errorLabel = builder.CreateLabel("mbc1_far_read_error");
        var restoreLabel = builder.CreateLabel("mbc1_far_read_restore");

        // Private ABI: input A=bank and HL=$4000-$7FFF. Output A=data, B=status
        // (0 success, 1 miss for bank zero, 2 malformed bank/window), Z=miss/error,
        // and C=error. The actual entry bank is stacked so nested/re-entrant reads restore LIFO.
        builder.Label(Mbc1FarReadByteLabel);
        builder.LoadEFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank);
        builder.LoadCFromA();
        builder.Emit(0xC5);                         // PUSH BC (C=actual entry bank)
        builder.LoadAFromE();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, missLabel);      // JP Z,miss
        builder.CompareImmediate(32);
        builder.JumpAbsolute(0xD2, errorLabel);     // JP NC,error
        builder.LoadAFromH();
        builder.CompareImmediate(0x40);
        builder.JumpAbsolute(0xDA, errorLabel);     // JP C,error
        builder.CompareImmediate(0x80);
        builder.JumpAbsolute(0xD2, errorLabel);     // JP NC,error

        builder.LoadAFromE();
        EmitSelectRomBankFromA(builder);
        builder.LoadAFromHl();
        builder.LoadDFromA();
        builder.Emit(0x1E, 0x00);                  // LD E,0 (status=success)
        builder.LoadAImmediate(1);
        builder.CompareImmediate(0);                // Z=0,C=0 independent of data
        builder.JumpAbsolute(restoreLabel);

        builder.Label(missLabel);
        builder.LoadDImmediate(0);
        builder.Emit(0x1E, 0x01);                  // LD E,1 (status=miss)
        builder.XorA();                             // Z=1,C=0
        builder.JumpAbsolute(restoreLabel);

        builder.Label(errorLabel);
        builder.LoadDImmediate(0);
        builder.Emit(0x1E, 0x02);                  // LD E,2 (status=error)
        builder.XorA();                             // Z=1
        builder.Emit(0x37);                         // SCF => C=1

        builder.Label(restoreLabel);
        builder.Emit(0xC1);                         // POP BC (C=actual entry bank)
        builder.LoadAFromC();
        EmitSelectRomBankFromA(builder);
        builder.Emit(0x43);                         // LD B,E (returned status)
        builder.LoadAFromD();
        builder.Emit(0xC9);                         // RET
    }

    private static IReadOnlyList<GameBoyReadOnlyDataBlob> BuildReadOnlyData(GameBoyVideoProgram program, bool usesPackedWorld)
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

        if (!usesPackedWorld)
        {
            AddMapReadOnlyData(data, program);
        }
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
        EmitSelectRomBankFromA(builder);
    }

    internal static void EmitSelectRomBankFromA(GbBuilder builder, bool instrumentPackedCameraCritical = false)
    {
        if (instrumentPackedCameraCritical)
        {
            builder.Emit(0xF5); // PUSH AF; LY polling must not replace the requested bank.
            GameBoyPackedCameraRuntimeEmitter.EmitGuardCriticalWork(
                builder,
                GameBoyRuntimeMemoryLayout.PackedCamera.BankWorkInCommit);
            builder.Emit(0xF1); // POP AF
        }

        builder.StoreA(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank);
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

    internal static void EmitPublishShadowOam(GbBuilder builder)
    {
        builder.Emit(
            0xCD,
            (byte)(GameBoyRuntimeMemoryLayout.Sprites.DmaRoutineAddress & 0xFF),
            (byte)(GameBoyRuntimeMemoryLayout.Sprites.DmaRoutineAddress >> 8));
    }

    private static void EmitInstallOamDmaRoutine(GbBuilder builder)
    {
        byte[] routine =
        [
            0x3E, GameBoyRuntimeMemoryLayout.Sprites.OamShadowPage, // LD A,$C6
            0xE0, 0x46,                                          // LDH ($46),A
            0x3E, 0x28,                                          // LD A,40
            0x3D,                                                // wait: DEC A
            0x20, 0xFD,                                          // JR NZ,wait
            0xC9,                                                // RET
        ];
        for (var index = 0; index < routine.Length; index++)
        {
            builder.LoadAImmediate(routine[index]);
            builder.StoreA(checked((ushort)(GameBoyRuntimeMemoryLayout.Sprites.DmaRoutineAddress + index)));
        }
    }

    internal static void EmitEnterVBlank(GbBuilder builder)
    {
        const byte lastSafeStartingScanline = 148;
        var waitForVisible = builder.CreateLabel("packed_camera_wait_for_visible");
        var waitForVBlank = builder.CreateLabel("packed_camera_wait_for_vblank");
        var safe = builder.CreateLabel("packed_camera_vblank_safe");
        builder.Emit(0xF0, 0x44);                   // LDH A,($44)
        builder.Emit(0xFE, 0x90);                   // CP $90
        builder.JumpAbsolute(0xDA, waitForVBlank);  // JP C,waitForVBlank
        builder.Emit(0xFE, lastSafeStartingScanline);
        builder.JumpAbsolute(0xDA, safe);           // JP C,safe

        builder.Label(waitForVisible);
        builder.Emit(0xF0, 0x44);                   // LDH A,($44)
        builder.Emit(0xFE, 0x90);                   // CP $90
        builder.JumpAbsolute(0xD2, waitForVisible); // JP NC,waitForVisible

        builder.Label(waitForVBlank);
        builder.Emit(0xF0, 0x44);                   // LDH A,($44)
        builder.Emit(0xFE, 0x90);                   // CP $90
        builder.JumpAbsolute(0xDA, waitForVBlank);  // JP C,waitForVBlank
        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.CommitEnteredVBlank);
        builder.Label(safe);
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
        EmitClearMemory(builder, 0xFE00, 160, label);
    }

    private static void EmitClearOamShadow(GbBuilder builder, string label)
    {
        EmitClearMemory(builder, GameBoyRuntimeMemoryLayout.Sprites.OamShadowStart, 160, label);
    }

    private static void EmitClearMemory(GbBuilder builder, ushort start, ushort length, string label)
    {
        builder.LoadHl(start);
        builder.LoadBc(length);
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
