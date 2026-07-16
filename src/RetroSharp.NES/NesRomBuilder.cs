using System.Globalization;
using RetroSharp.Core;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;
using RetroSharp.Sdk;

namespace RetroSharp.NES;

internal static class NesRomBuilder
{
    private const ushort Mmc3BootPaletteAddress = 0xA000;
    private const ushort Mmc3BootNameTableAddress = 0xA020;
    internal const string WorldPackLabel = "worldpack_default";
    internal const string WorldPackValidateLabel = "worldpack_validate";
    internal const string WorldPackVisualDecodeLabel = "worldpack_decode_visual";
    internal const string WorldPackVisualLookupLabel = "worldpack_visual_lookup";
    internal const string WorldPackVisualLookupPreparedLabel = "worldpack_visual_lookup_prepared";
    internal const string WorldPackInitializeLabel = "worldpack_initialize";
    internal const string WorldPackCollisionDecodeLabel = "worldpack_decode_collision";
    internal const string WorldPackCollisionLookupLabel = "worldpack_collision_lookup";
    internal const string WorldPackReadByteLabel = "worldpack_read_byte";
    internal const string WorldPackProbeLabel = "worldpack_probe";
    internal const string WorldPackPrepareEdgeLabel = "worldpack_prepare_edge";
    internal const string WorldPackCommitEdgeLabel = "worldpack_commit_edge";
    internal const string WorldPackReleaseReversedEdgeLabel = "worldpack_release_reversed_edge";
    internal const string WorldPackAttributesLabel = "worldpack_attributes";
    private const string FrameSignalNmiHandlerLabel = "nes_frame_signal_nmi_handler";
    private const string Mmc3IrqHandlerLabel = "mmc3_irq_handler";

    public static byte[] Build(
        NesVideoProgram program,
        bool useFourScreenNametables,
        NesCartridgeProfile cartridgeProfile = NesCartridgeProfile.Mapper0)
    {
        return BuildForProfile(
            program,
            useFourScreenNametables,
            cartridgeProfile,
            packedWorldBytes: null,
            worldPackProbe: null).Rom;
    }

    internal static NesRomBuildResult BuildWithReport(
        NesVideoProgram program,
        bool useFourScreenNametables,
        NesCartridgeProfile? forcedCartridgeProfile,
        byte[]? packedWorldOverride,
        NesWorldPackProbe? worldPackProbe)
    {
        var discoveredWorldPack = program.PackedWorld?.SerializedBytes;
        var selectedWorldPack = packedWorldOverride ?? discoveredWorldPack;
        var requiresPackedCamera = program.UsesCameraRuntime &&
                                   program.PackedWorld?.LoweredWorld.Height >
                                   NesTarget.Capabilities.MaxBackgroundTileWritesPerFrame;
        if (worldPackProbe is not null && selectedWorldPack is null)
        {
            throw new InvalidOperationException("NES WorldPack probe requires a packed world override.");
        }
        if (selectedWorldPack is not null)
        {
            var pack = WorldPackSerializer.Deserialize(selectedWorldPack);
            if (pack.Descriptor.TargetCellStride != 2)
            {
                throw new InvalidOperationException(
                    $"NES WorldPack v1 requires targetCellStride 2; received {pack.Descriptor.TargetCellStride}.");
            }
        }

        if (forcedCartridgeProfile is { } forced)
        {
            return BuildForProfile(program, useFourScreenNametables, forced, selectedWorldPack, worldPackProbe);
        }

        if (packedWorldOverride is null && discoveredWorldPack is not null && !requiresPackedCamera)
        {
            try
            {
                // Preserve the exact historical mapper-0 image when it genuinely fits. The
                // discovered canonical pack becomes a physical section only after that real
                // final link proves a banked address/capacity need.
                return BuildForProfile(program, useFourScreenNametables, NesCartridgeProfile.Mapper0, packedWorldBytes: null, worldPackProbe: null);
            }
            catch (InvalidOperationException exception) when (IsMapper0CapacityConstraint(exception))
            {
                return BuildForProfile(program, useFourScreenNametables, NesCartridgeProfile.Mmc3Tvrom, discoveredWorldPack, worldPackProbe);
            }
        }

        // A tall discovered world can exceed the raw one-VBlank column shape while
        // remaining valid through the bounded packed camera scheduler. In that case,
        // attempt mapper 0 with the canonical pack first; mapper selection still
        // escalates only if that real packed final link exceeds mapper-0 capacity.

        try
        {
            return BuildForProfile(program, useFourScreenNametables, NesCartridgeProfile.Mapper0, selectedWorldPack, worldPackProbe);
        }
        catch (InvalidOperationException exception) when (
            selectedWorldPack is not null &&
            IsMapper0CapacityConstraint(exception))
        {
            return BuildForProfile(program, useFourScreenNametables, NesCartridgeProfile.Mmc3Tvrom, selectedWorldPack, worldPackProbe);
        }
    }

    private static bool IsMapper0CapacityConstraint(InvalidOperationException exception) =>
        exception.Data[nameof(NesLinkConstraint)] is NesLinkConstraint.Mapper0Prg or NesLinkConstraint.Mapper0Dpcm;

    private static InvalidOperationException LinkConstraint(NesLinkConstraint constraint, string message)
    {
        var exception = new InvalidOperationException(message);
        exception.Data[nameof(NesLinkConstraint)] = constraint;
        return exception;
    }

    private static NesRomBuildResult BuildForProfile(
        NesVideoProgram program,
        bool useFourScreenNametables,
        NesCartridgeProfile cartridgeProfile,
        byte[]? packedWorldBytes,
        NesWorldPackProbe? worldPackProbe)
    {
        var layout = NesCartridgeLayout.Create(cartridgeProfile, useFourScreenNametables);
        var worldPackRuntime = packedWorldBytes is null
            ? null
            : NesWorldPackRuntimePlan.Create(packedWorldBytes);
        var worldPackPlacement = layout.EmitMmc3Foundation && packedWorldBytes is not null
            ? NesWorldPackPlacement.Create(
                packedWorldBytes,
                layout.PrgSections.Where(section => section.Kind is NesPrgSectionKind.WorldR6).ToArray())
            : null;
        var includeLegacyWorldData = !layout.EmitMmc3Foundation ||
                                     packedWorldBytes is null ||
                                     program.RequiresLegacyWorldData;
        var prgBuild = BuildPrgRom(
            program,
            layout,
            worldPackRuntime,
            worldPackPlacement,
            worldPackProbe,
            worldPackPlacement is null ? packedWorldBytes : null,
            includeLegacyWorldData);
        var prg = prgBuild.Bytes;
        if (layout.EmitMmc3Foundation)
        {
            var bootSection = layout.PrgSections.Single(section => section.Kind is NesPrgSectionKind.BootR7);
            program.Palette.CopyTo(prg, bootSection.PhysicalOffset);
            PadToLength(program.NameTable, 4 * 1_024).CopyTo(prg, bootSection.PhysicalOffset + program.Palette.Length);
            var pinnedSection = layout.PrgSections.Single(section => section.Kind is NesPrgSectionKind.PinnedR7);
            prgBuild.PinnedDataBytes.CopyTo(prg, pinnedSection.PhysicalOffset);
        }

        if (worldPackPlacement is not null)
        {
            foreach (var segment in worldPackPlacement.Segments)
            {
                worldPackPlacement.SerializedBytes.AsSpan(segment.RelativeOffset, segment.Length).CopyTo(
                    prg.AsSpan(segment.PhysicalOffset, segment.Length));
            }
        }

        var chr = BuildChrRom(program, layout.ChrRomSize);

        var rom = new byte[16 + layout.PrgRomSize + layout.ChrRomSize];
        rom[0] = (byte)'N';
        rom[1] = (byte)'E';
        rom[2] = (byte)'S';
        rom[3] = 0x1A;
        rom[4] = (byte)(layout.PrgRomSize / (16 * 1024));
        rom[5] = (byte)(layout.ChrRomSize / (8 * 1024));
        rom[6] = layout.HeaderFlags6;

        prg.CopyTo(rom, 16);
        chr.CopyTo(rom, 16 + layout.PrgRomSize);
        return new NesRomBuildResult(
            rom,
            BuildReport(
                program,
                layout,
                prgBuild,
                packedWorldBytes,
                worldPackPlacement,
                worldPackRuntime,
                includeLegacyWorldData));
    }

    private static NesPrgBuild BuildPrgRom(
        NesVideoProgram program,
        NesCartridgeLayout layout,
        NesWorldPackRuntimePlan? worldPackRuntime,
        NesWorldPackPlacement? worldPackPlacement,
        NesWorldPackProbe? worldPackProbe,
        byte[]? inlineWorldPack,
        bool includeLegacyWorldData)
    {
        var longForLoopIds = new HashSet<int>();
        var longWhileLoopIds = new HashSet<int>();
        while (true)
        {
            try
            {
                return BuildPrgRom(
                    program,
                    longForLoopIds,
                    longWhileLoopIds,
                    layout,
                    worldPackRuntime,
                    worldPackPlacement,
                    worldPackProbe,
                    inlineWorldPack,
                    includeLegacyWorldData);
            }
            catch (BranchOutOfRangeException ex)
            {
                if (TryForEndLabelId(ex.Label, out var forId) && longForLoopIds.Add(forId))
                {
                    continue;
                }

                if (TryWhileEndLabelId(ex.Label, out var whileId) && longWhileLoopIds.Add(whileId))
                {
                    continue;
                }

                throw;
            }
        }
    }

    private static NesPrgBuild BuildPrgRom(
        NesVideoProgram program,
        IReadOnlySet<int> longForLoopIds,
        IReadOnlySet<int> longWhileLoopIds,
        NesCartridgeLayout layout,
        NesWorldPackRuntimePlan? worldPackRuntime,
        NesWorldPackPlacement? worldPackPlacement,
        NesWorldPackProbe? worldPackProbe,
        byte[]? inlineWorldPack,
        bool includeLegacyWorldData)
    {
        var builder = new PrgBuilder(layout.FixedRuntimeCpuBaseAddress);
        var usePackedCamera = worldPackRuntime is not null && program.UsesCameraRuntime;
        var nameTableUploadByteCount = layout.UseFourScreenNametables ? 4096 : 2048;
        if (layout.EmitMmc3Foundation)
        {
            var pinnedDataStart = 0xA000;
            if (worldPackRuntime is not null)
            {
                pinnedDataStart = NesWorldPackRuntimeEmitter.DefinePinnedLookupLabels(
                    builder,
                    worldPackRuntime,
                    pinnedDataStart,
                    usePackedCamera);
            }

            DefinePinnedDataLabels(builder, program, pinnedDataStart, includeLegacyWorldData);
        }

        builder.Label("fixed_runtime_entry");
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
        EmitPaletteUpload(builder, layout.EmitMmc3Foundation ? Mmc3BootPaletteAddress : null);
        EmitNameTableUpload(builder, nameTableUploadByteCount, layout.EmitMmc3Foundation ? Mmc3BootNameTableAddress : null);
        if (layout.EmitMmc3Foundation)
        {
            EmitMmc3RegisterWrite(builder, register: 7, value: 1);
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.Banking.Mmc3R7Shadow);
        }

        var runtimeCompiler = new NesRuntimeCompiler(
            builder,
            program,
            longForLoopIds,
            longWhileLoopIds,
            layout.UseFourScreenNametables,
            usePackedCamera,
            useDirectOamWrites: layout.EmitMmc3Foundation && usePackedCamera);
        runtimeCompiler.EmitInitialization();
        if (worldPackRuntime is not null)
        {
            builder.CallSubroutine(WorldPackValidateLabel);
            builder.CallSubroutine(WorldPackInitializeLabel);
            if (worldPackProbe is not null)
            {
                builder.CallSubroutine(WorldPackProbeLabel);
            }
        }

        builder.Emit(0xA9, usePackedCamera ? (byte)0x80 : (byte)0x00);
        builder.Emit(0x8D, 0x05, 0x20);             // STA $2005
        builder.Emit(0x8D, 0x05, 0x20);             // STA $2005
        builder.Emit(0x8D, 0x00, 0x20);             // STA $2000
        builder.Emit(0xA9, 0x1E);                   // LDA #$1E
        builder.Emit(0x8D, 0x01, 0x20);             // STA $2001

        runtimeCompiler.Emit(program.MainBlock);

        builder.Label("forever");
        builder.JumpAbsolute("forever");

        runtimeCompiler.EmitReferencedSubroutines();
        if (worldPackRuntime is not null)
        {
            NesWorldPackRuntimeEmitter.Emit(
                builder,
                worldPackRuntime,
                worldPackPlacement,
                worldPackProbe,
                program.UsesCameraRuntime);
        }
        if (layout.EmitMmc3Foundation)
        {
            EmitMmc3BankHelpers(builder);
        }
        if (layout.EmitMmc3Foundation || usePackedCamera)
        {
            EmitFrameSignalNmiHandler(builder);
        }
        if (layout.EmitMmc3Foundation)
        {
            EmitMmc3IrqHandler(builder);
        }

        if (!layout.EmitMmc3Foundation)
        {
            builder.Label("palette");
            builder.Emit(program.Palette);
            builder.Label("nametable");
            builder.Emit(PadToLength(program.NameTable, nameTableUploadByteCount));
        }
        if (usePackedCamera)
        {
            EmitPpuRowAddressTables(builder, program.WorldMap, force: true);
        }

        if (includeLegacyWorldData)
        {
            EmitWorldMapRows(builder, program.WorldMap, program.WorldTileGrid);
            EmitWorldMapRowPointerTables(builder, program.WorldMap);
            if (!usePackedCamera)
            {
                EmitPpuRowAddressTables(builder, program.WorldMap);
            }
            EmitWorldMapFlagRows(builder, program.WorldMap);
            if (!layout.EmitMmc3Foundation)
            {
                EmitWorldMapFlagRowPointerTables(builder, program.WorldMap);
                EmitWorldColumnAttributes(builder, program.WorldColumnAttributes);
            }
        }
        if (inlineWorldPack is not null)
        {
            builder.Label(WorldPackLabel);
            builder.Emit(inlineWorldPack);
        }

        byte[] pinnedDataBytes = [];
        IReadOnlyList<NesDpcmBuildPlacement> dpcmPlacements = [];
        if (layout.EmitMmc3Foundation)
        {
            EnsureFixedDataBeforeTrailer(layout, builder.CurrentAddress);
            var dpcmLayout = BuildDpcmSampleLayout(
                builder.CurrentAddress,
                program.MusicAssetsInLoadOrder,
                layout.FixedTrailerStartAddress,
                fixedProfileName: layout.Name);
            var pinnedBuilder = new PrgBuilder(0xA000);
            EmitPinnedRuntimeData(
                pinnedBuilder,
                builder,
                program,
                includeLegacyWorldData,
                worldPackRuntime,
                usePackedCamera);
            EmitMusicAssets(pinnedBuilder, program.MusicAssetsInLoadOrder, dpcmLayout.AddressRegisterMap);
            EmitSoundEffectAssets(pinnedBuilder, program.SoundEffectAssetsInLoadOrder);
            pinnedDataBytes = pinnedBuilder.Build();
            if (pinnedDataBytes.Length > 8 * 1_024)
            {
                throw new InvalidOperationException(
                    $"NES MMC3/TVROM pinned R7 section overflow: {pinnedDataBytes.Length} bytes emitted, {8 * 1_024} bytes available.");
            }

            EmitDpcmSampleBlocks(builder, dpcmLayout.Placements);
            dpcmPlacements = dpcmLayout.Placements
                .Select(placement => new NesDpcmBuildPlacement(
                    placement.Block.SourceAddress,
                    placement.Address,
                    placement.Block.Data.Length))
                .ToArray();
        }
        else
        {
            EmitAudioAssets(builder, program.MusicAssetsInLoadOrder, program.SoundEffectAssetsInLoadOrder, layout.FixedTrailerStartAddress);
        }

        var fixedPayloadBeforeTrailer = builder.CurrentAddress - layout.FixedRuntimeCpuBaseAddress;
        var resetTrampolineBytes = 0;
        if (layout.EmitMmc3Foundation)
        {
            EnsureFixedDataBeforeTrailer(layout, builder.CurrentAddress);
            EmitMmc3ResetTrampoline(builder, layout.FixedTrailerStartAddress);
            resetTrampolineBytes = builder.CurrentAddress - layout.FixedTrailerStartAddress;
        }

        var prg = new byte[layout.PrgRomSize];
        if (layout.EmitMmc3Foundation)
        {
            SeedMmc3SwitchableBanks(prg, layout.PrgSections);
        }

        var code = builder.Build();
        if (code.Length > layout.FixedRuntimeSize - 6)
        {
            var message = layout.EmitMmc3Foundation
                ? $"NES {layout.Name} fixed PRG section overflow: {code.Length} bytes emitted, {layout.FixedRuntimeSize - 6} bytes available."
                : $"NES PRG ROM overflow: {code.Length} bytes emitted, {layout.FixedRuntimeSize - 6} bytes available.";
            throw LinkConstraint(
                layout.EmitMmc3Foundation ? NesLinkConstraint.FixedPrg : NesLinkConstraint.Mapper0Prg,
                message);
        }

        code.CopyTo(prg, layout.FixedRuntimePhysicalOffset);
        var nmiVector = layout.EmitMmc3Foundation || usePackedCamera
            ? builder.AddressOfLabel(FrameSignalNmiHandlerLabel)
            : layout.FixedRuntimeCpuBaseAddress;
        var irqVector = layout.EmitMmc3Foundation
            ? builder.AddressOfLabel(Mmc3IrqHandlerLabel)
            : layout.FixedRuntimeCpuBaseAddress;
        var resetVector = layout.EmitMmc3Foundation
            ? builder.AddressOfLabel("mmc3_reset")
            : layout.FixedRuntimeCpuBaseAddress;
        SetVector(prg, layout.PrgRomSize - 6, nmiVector);
        SetVector(prg, layout.PrgRomSize - 4, resetVector);
        SetVector(prg, layout.PrgRomSize - 2, irqVector);
        int? inlineWorldPackOffset = inlineWorldPack is null
            ? null
            : builder.AddressOfLabel(WorldPackLabel) - layout.FixedRuntimeCpuBaseAddress;
        var fixedPayloadBytes = layout.EmitMmc3Foundation
            ? checked(fixedPayloadBeforeTrailer + resetTrampolineBytes + 6)
            : checked(code.Length + 6);
        var fixedSymbols = worldPackRuntime is null
            ? new Dictionary<string, ushort>()
            : new Dictionary<string, ushort>
            {
                [WorldPackValidateLabel] = builder.AddressOfLabel(WorldPackValidateLabel),
                [WorldPackVisualDecodeLabel] = builder.AddressOfLabel(WorldPackVisualDecodeLabel),
                [WorldPackVisualLookupLabel] = builder.AddressOfLabel(WorldPackVisualLookupLabel),
                [WorldPackCollisionDecodeLabel] = builder.AddressOfLabel(WorldPackCollisionDecodeLabel),
                [WorldPackCollisionLookupLabel] = builder.AddressOfLabel(WorldPackCollisionLookupLabel),
                [WorldPackReadByteLabel] = builder.AddressOfLabel(WorldPackReadByteLabel),
            };
        if (worldPackRuntime is not null)
        {
            fixedSymbols[WorldPackInitializeLabel] = builder.AddressOfLabel(WorldPackInitializeLabel);
        }
        if (worldPackProbe is not null)
        {
            fixedSymbols[WorldPackProbeLabel] = builder.AddressOfLabel(WorldPackProbeLabel);
        }
        if (worldPackRuntime is not null && program.UsesCameraRuntime)
        {
            fixedSymbols[WorldPackPrepareEdgeLabel] = builder.AddressOfLabel(WorldPackPrepareEdgeLabel);
            fixedSymbols[WorldPackCommitEdgeLabel] = builder.AddressOfLabel(WorldPackCommitEdgeLabel);
            fixedSymbols[WorldPackReleaseReversedEdgeLabel] = builder.AddressOfLabel(WorldPackReleaseReversedEdgeLabel);
        }
        return new NesPrgBuild(
            prg,
            code.Length,
            inlineWorldPackOffset,
            pinnedDataBytes,
            dpcmPlacements,
            fixedPayloadBytes,
            fixedSymbols,
            runtimeCompiler.UserVariables);
    }

    private static void EnsureFixedDataBeforeTrailer(NesCartridgeLayout layout, int currentAddress)
    {
        if (currentAddress > layout.FixedTrailerStartAddress)
        {
            throw LinkConstraint(
                NesLinkConstraint.FixedPrg,
                $"NES {layout.Name} fixed PRG section overflow: runtime/data/DPCM end at ${currentAddress:X4}, beyond reset trailer start ${layout.FixedTrailerStartAddress:X4}.");
        }
    }

    private static void DefinePinnedDataLabels(
        PrgBuilder builder,
        NesVideoProgram program,
        int startAddress = 0xA000,
        bool includeLegacyWorldData = true)
    {
        var address = startAddress;
        if (includeLegacyWorldData && program.WorldMap is { } worldMap)
        {
            builder.DefineExternalLabel(WorldMapFlagRowPointerLowLabel, checked((ushort)address));
            address = checked(address + worldMap.Height);
            builder.DefineExternalLabel(WorldMapFlagRowPointerHighLabel, checked((ushort)address));
            address = checked(address + worldMap.Height);
        }

        if (includeLegacyWorldData && program.WorldColumnAttributes is { } attributes)
        {
            for (var row = 0; row < attributes.Rows.Count; row++)
            {
                builder.DefineExternalLabel(WorldMapColumnAttributeRowLabel(row), checked((ushort)address));
                address = checked(address + attributes.Rows[row].BytesByColumnBlock.Length);
            }
        }

        foreach (var asset in program.MusicAssetsInLoadOrder)
        {
            builder.DefineExternalLabel(MusicDataLabel(asset.Name), checked((ushort)address));
            address = checked(address + asset.Data.Length);
        }

        foreach (var asset in program.SoundEffectAssetsInLoadOrder)
        {
            builder.DefineExternalLabel(SoundEffectDataLabel(asset.Name), checked((ushort)address));
            address = checked(address + asset.Data.Length);
        }

        if (address > 0xC000)
        {
            throw new InvalidOperationException(
                $"NES MMC3/TVROM pinned R7 section overflow: {address - 0xA000} bytes required, {8 * 1_024} bytes available.");
        }
    }

    private static void EmitPinnedRuntimeData(
        PrgBuilder pinnedBuilder,
        PrgBuilder fixedBuilder,
        NesVideoProgram program,
        bool includeLegacyWorldData,
        NesWorldPackRuntimePlan? worldPackRuntime,
        bool usePackedCamera)
    {
        if (worldPackRuntime is not null)
        {
            NesWorldPackRuntimeEmitter.EmitPinnedLookupData(pinnedBuilder, worldPackRuntime, usePackedCamera);
        }

        if (includeLegacyWorldData && program.WorldMap is { } worldMap)
        {
            for (var row = 0; row < worldMap.Height; row++)
            {
                pinnedBuilder.DefineExternalLabel(
                    WorldMapFlagRowLabel(row),
                    fixedBuilder.AddressOfLabel(WorldMapFlagRowLabel(row)));
            }

            EmitWorldMapFlagRowPointerTables(pinnedBuilder, worldMap);
        }

        if (includeLegacyWorldData)
        {
            EmitWorldColumnAttributes(pinnedBuilder, program.WorldColumnAttributes);
        }
    }

    private static NesRomBuildReport BuildReport(
        NesVideoProgram program,
        NesCartridgeLayout layout,
        NesPrgBuild prgBuild,
        byte[]? packedWorldBytes,
        NesWorldPackPlacement? worldPackPlacement,
        NesWorldPackRuntimePlan? worldPackRuntime,
        bool includeLegacyWorldData)
    {
        var segments = new List<NesRomBuildSegment>();
        if (worldPackPlacement is not null)
        {
            foreach (var segment in worldPackPlacement.Segments)
            {
                segments.Add(new NesRomBuildSegment(
                    "worldpack:default",
                    "R6 $8000-$9FFF",
                    segment.RelativeOffset,
                    segment.PhysicalOffset,
                    segment.Length,
                    segment.PhysicalBank,
                    segment.CpuAddress));
            }

            AddMmc3BootReportSegments(segments, layout);
            AddMmc3PinnedReportSegments(segments, program, layout, packedWorldBytes, includeLegacyWorldData);
            AddFixedReportSegments(segments, layout, prgBuild);
        }
        else if (packedWorldBytes is not null && prgBuild.InlineWorldPackOffset is { } inlineOffset)
        {
            AddReportSegment(
                segments,
                "fixed:before-worldpack",
                "mapper-0 $8000-$FFFF",
                relativeOffset: -1,
                physicalStart: 0,
                length: inlineOffset,
                cpuAddress: 0x8000);
            AddReportSegment(
                segments,
                "worldpack:default",
                "mapper-0 $8000-$FFFF",
                relativeOffset: 0,
                physicalStart: inlineOffset,
                length: packedWorldBytes.Length,
                cpuAddress: checked((ushort)(0x8000 + inlineOffset)));
            AddReportSegment(
                segments,
                "fixed:after-worldpack",
                "mapper-0 $8000-$FFFF",
                relativeOffset: -1,
                physicalStart: inlineOffset + packedWorldBytes.Length,
                length: prgBuild.UsedBytes - inlineOffset - packedWorldBytes.Length,
                cpuAddress: checked((ushort)(0x8000 + inlineOffset + packedWorldBytes.Length)));
        }
        else
        {
            if (layout.EmitMmc3Foundation)
            {
                AddMmc3BootReportSegments(segments, layout);
                AddMmc3PinnedReportSegments(segments, program, layout, packedWorldBytes, includeLegacyWorldData);
            }

            if (layout.EmitMmc3Foundation)
            {
                AddFixedReportSegments(segments, layout, prgBuild);
            }
            else
            {
                AddReportSegment(
                    segments,
                    "fixed:runtime-data-dpcm-vectors",
                    "mapper-0 $8000-$FFFF",
                    relativeOffset: -1,
                    layout.FixedRuntimePhysicalOffset,
                    prgBuild.UsedBytes,
                    layout.FixedRuntimeCpuBaseAddress);
            }
        }

        AddReportSegment(
            segments,
            "fixed:vectors",
            layout.EmitMmc3Foundation ? "fixed $C000-$FFFF" : "mapper-0 $8000-$FFFF",
            relativeOffset: -1,
            layout.PrgRomSize - 6,
            6,
            0xFFFA);
        var orderedSegments = segments.OrderBy(segment => segment.PhysicalStart).ToArray();
        ValidateReportedSegments(layout, orderedSegments);
        return new NesRomBuildReport(
            layout.EmitMmc3Foundation ? "nes-mmc3-tvrom-v1" : "nes-mapper-0-current",
            layout.PrgRomSize,
            layout.ChrRomSize,
            prgBuild.FixedPayloadBytes,
            prgBuild.PinnedDataBytes.Length,
            layout.EmitMmc3Foundation ? 4_128 : 0,
            CalculateResidentChrBytes(program),
            orderedSegments,
            prgBuild.FixedSymbols,
            prgBuild.UserVariables,
            DescribeRuntimeRegions(worldPackRuntime));
    }

    private static IReadOnlyList<NesRuntimeRegion> DescribeRuntimeRegions(
        NesWorldPackRuntimePlan? worldPackRuntime)
    {
        if (worldPackRuntime is null)
        {
            return [];
        }

        const string owner = nameof(NesRuntimeMemoryLayout.WorldPackStaging);
        return worldPackRuntime.Layout.VisualSlots
            .Select((range, index) => new NesRuntimeRegion($"WorldPack.VisualSlot{index}", range.Start, range.Length, owner))
            .Concat(worldPackRuntime.Layout.CollisionSlots.Select(
                (range, index) => new NesRuntimeRegion($"WorldPack.CollisionSlot{index}", range.Start, range.Length, owner)))
            .Concat(worldPackRuntime.Layout.EdgeSlots.Select(
                (range, index) => new NesRuntimeRegion($"WorldPack.EdgeSlot{index}", range.Start, range.Length, owner)))
            .ToArray();
    }

    private static void ValidateReportedSegments(
        NesCartridgeLayout layout,
        IReadOnlyList<NesRomBuildSegment> segments)
    {
        foreach (var segment in segments)
        {
            if (segment.PhysicalStart < 0 || segment.Length <= 0 ||
                segment.PhysicalStart + segment.Length > layout.PrgRomSize)
            {
                throw new InvalidOperationException(
                    $"NES {layout.Name} section '{segment.Owner}' is outside the {layout.PrgRomSize}-byte PRG image.");
            }
        }

        foreach (var pair in segments.Zip(segments.Skip(1)))
        {
            if (pair.First.PhysicalStart + pair.First.Length > pair.Second.PhysicalStart)
            {
                throw new InvalidOperationException(
                    $"NES {layout.Name} sections '{pair.First.Owner}' and '{pair.Second.Owner}' overlap in physical PRG.");
            }
        }
    }

    private static int CalculateResidentChrBytes(NesVideoProgram program)
    {
        var end = NesVideoProgram.FirstSpriteTile * 16;
        foreach (var asset in program.SpriteAssetsInLoadOrder)
        {
            end = Math.Max(end, asset.FirstTile * 16 + asset.TileData.Length);
        }

        foreach (var background in program.GeneratedBackgroundTiles)
        {
            end = Math.Max(end, background.FirstTile * 16 + background.Data.Length);
        }

        return end;
    }

    private static void AddMmc3BootReportSegments(
        ICollection<NesRomBuildSegment> segments,
        NesCartridgeLayout layout)
    {
        var bootSection = layout.PrgSections.Single(section => section.Kind is NesPrgSectionKind.BootR7);
        AddReportSegment(
            segments,
            "boot:palette",
            "R7 boot-only $A000-$BFFF",
            relativeOffset: -1,
            bootSection.PhysicalOffset,
            32,
            Mmc3BootPaletteAddress);
        AddReportSegment(
            segments,
            "boot:nametable",
            "R7 boot-only $A000-$BFFF",
            relativeOffset: -1,
            bootSection.PhysicalOffset + 32,
            4 * 1_024,
            Mmc3BootNameTableAddress);
    }

    private static void AddMmc3PinnedReportSegments(
        ICollection<NesRomBuildSegment> segments,
        NesVideoProgram program,
        NesCartridgeLayout layout,
        byte[]? packedWorldBytes,
        bool includeLegacyWorldData)
    {
        var pinnedSection = layout.PrgSections.Single(section => section.Kind is NesPrgSectionKind.PinnedR7);
        var offset = 0;
        if (packedWorldBytes is not null)
        {
            var runtime = NesWorldPackRuntimePlan.Create(packedWorldBytes);
            var length = NesWorldPackRuntimeEmitter.PinnedLookupDataLength(runtime, program.UsesCameraRuntime);
            AddReportSegment(
                segments,
                "pinned:worldpack-runtime-index",
                "R7 pinned $A000-$BFFF",
                relativeOffset: -1,
                pinnedSection.PhysicalOffset,
                length,
                0xA000);
            offset += length;
        }

        if (includeLegacyWorldData && program.WorldMap is { } worldMap)
        {
            AddReportSegment(
                segments,
                "pinned:world-flag-pointers",
                "R7 pinned $A000-$BFFF",
                relativeOffset: -1,
                pinnedSection.PhysicalOffset + offset,
                2 * worldMap.Height,
                checked((ushort)(0xA000 + offset)));
            offset += 2 * worldMap.Height;
        }

        if (includeLegacyWorldData && program.WorldColumnAttributes is { } attributes)
        {
            for (var row = 0; row < attributes.Rows.Count; row++)
            {
                var length = attributes.Rows[row].BytesByColumnBlock.Length;
                AddReportSegment(
                    segments,
                    $"pinned:world-column-attributes:{row}",
                    "R7 pinned $A000-$BFFF",
                    relativeOffset: -1,
                    pinnedSection.PhysicalOffset + offset,
                    length,
                    checked((ushort)(0xA000 + offset)));
                offset += length;
            }
        }

        foreach (var asset in program.MusicAssetsInLoadOrder)
        {
            AddReportSegment(
                segments,
                $"pinned:bgm:{asset.Name}",
                "R7 pinned $A000-$BFFF",
                relativeOffset: -1,
                pinnedSection.PhysicalOffset + offset,
                asset.Data.Length,
                checked((ushort)(0xA000 + offset)));
            offset += asset.Data.Length;
        }

        foreach (var asset in program.SoundEffectAssetsInLoadOrder)
        {
            AddReportSegment(
                segments,
                $"pinned:sfx:{asset.Name}",
                "R7 pinned $A000-$BFFF",
                relativeOffset: -1,
                pinnedSection.PhysicalOffset + offset,
                asset.Data.Length,
                checked((ushort)(0xA000 + offset)));
            offset += asset.Data.Length;
        }
    }

    private static void AddFixedReportSegments(
        ICollection<NesRomBuildSegment> segments,
        NesCartridgeLayout layout,
        NesPrgBuild prgBuild)
    {
        var cursor = layout.FixedRuntimePhysicalOffset;
        var end = checked(layout.FixedRuntimePhysicalOffset + prgBuild.UsedBytes);
        var gap = 0;
        foreach (var dpcm in prgBuild.DpcmPlacements.OrderBy(item => item.CpuAddress))
        {
            var physicalStart = checked(layout.FixedRuntimePhysicalOffset + dpcm.CpuAddress - layout.FixedRuntimeCpuBaseAddress);
            AddReportSegment(
                segments,
                $"fixed:runtime-data:{gap++}",
                "fixed $C000-$FFFF",
                relativeOffset: -1,
                cursor,
                physicalStart - cursor,
                checked((ushort)(layout.FixedRuntimeCpuBaseAddress + cursor - layout.FixedRuntimePhysicalOffset)));
            AddReportSegment(
                segments,
                $"fixed:dpcm:{dpcm.SourceAddress:X4}",
                "fixed $C000-$FFF9",
                relativeOffset: -1,
                physicalStart,
                dpcm.Length,
                dpcm.CpuAddress);
            cursor = physicalStart + dpcm.Length;
        }

        AddReportSegment(
            segments,
            $"fixed:runtime-data:{gap}",
            "fixed $C000-$FFFF",
            relativeOffset: -1,
            cursor,
            end - cursor,
            checked((ushort)(layout.FixedRuntimeCpuBaseAddress + cursor - layout.FixedRuntimePhysicalOffset)));
    }

    private static void AddReportSegment(
        ICollection<NesRomBuildSegment> segments,
        string owner,
        string window,
        int relativeOffset,
        int physicalStart,
        int length,
        ushort cpuAddress)
    {
        if (length <= 0)
        {
            return;
        }

        segments.Add(new NesRomBuildSegment(
            owner,
            window,
            relativeOffset,
            physicalStart,
            length,
            physicalStart / (8 * 1_024),
            cpuAddress));
    }

    private static void EmitMmc3ResetTrampoline(PrgBuilder builder, ushort address)
    {
        builder.PadToAddress(address);
        builder.Label("mmc3_reset");
        builder.Emit(0x78);                         // SEI
        builder.Emit(0xD8);                         // CLD
        EmitMmc3Bootstrap(builder);
        builder.JumpAbsolute("fixed_runtime_entry");
    }

    private static void EmitMmc3Bootstrap(PrgBuilder builder)
    {
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(0xE000);             // Disable and acknowledge mapper IRQs.

        EmitMmc3RegisterWrite(builder, register: 0, value: 0); // CHR pages 0-1.
        EmitMmc3RegisterWrite(builder, register: 1, value: 2); // CHR pages 2-3.
        EmitMmc3RegisterWrite(builder, register: 2, value: 4);
        EmitMmc3RegisterWrite(builder, register: 3, value: 5);
        EmitMmc3RegisterWrite(builder, register: 4, value: 6);
        EmitMmc3RegisterWrite(builder, register: 5, value: 7);

        EmitMmc3RegisterWrite(builder, register: 6, value: 0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Banking.Mmc3R6Shadow);
        EmitMmc3RegisterWrite(builder, register: 7, value: 2);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Banking.Mmc3R7Shadow);
    }

    private static void EmitMmc3RegisterWrite(PrgBuilder builder, byte register, byte value)
    {
        // Bit 6 stays clear on every bank-select write, pinning MMC3 PRG mode 0.
        builder.LoadAImmediate(register);
        builder.StoreAAbsolute(0x8000);
        builder.LoadAImmediate(value);
        builder.StoreAAbsolute(0x8001);
    }

    private static void EmitMmc3BankHelpers(PrgBuilder builder)
    {
        EmitMmc3BankHelper(builder, "mmc3_select_r6", register: 6, NesRuntimeMemoryLayout.Banking.Mmc3R6Shadow);
        EmitMmc3BankHelper(builder, "mmc3_select_r7", register: 7, NesRuntimeMemoryLayout.Banking.Mmc3R7Shadow);
    }

    private static void EmitFrameSignalNmiHandler(PrgBuilder builder)
    {
        builder.Label(FrameSignalNmiHandlerLabel);
        builder.PushA();
        var frameReady = builder.CreateLabel("nes_frame_signal_nmi_frame_ready");
        builder.IncrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.FrameCounterLow);
        builder.BranchRelative(0xD0, frameReady);
        builder.IncrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.FrameCounterHigh);
        builder.Label(frameReady);
        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.PackedCamera.FramePending);
        builder.PullA();
        builder.Emit(0x40);                          // RTI; fixed-bank, bank-neutral frame signal only.
    }

    private static void EmitMmc3IrqHandler(PrgBuilder builder)
    {
        builder.Label(Mmc3IrqHandlerLabel);
        builder.Emit(0x40);                          // RTI; mapper IRQs remain disabled.
    }

    private static void EmitMmc3BankHelper(PrgBuilder builder, string label, byte register, ushort shadowAddress)
    {
        builder.Label(label);
        builder.PushA();
        if (register == 6)
        {
            var noCriticalWork = builder.CreateLabel("mmc3_r6_no_critical_work");
            builder.LoadAAbsolute(NesRuntimeMemoryLayout.PackedCamera.CriticalSection);
            builder.BranchRelative(0xF0, noCriticalWork);
            builder.IncrementAbsolute(NesRuntimeMemoryLayout.PackedCamera.BankWorkInCommit);
            builder.Label(noCriticalWork);
        }
        builder.LoadAImmediate(register);
        builder.StoreAAbsolute(0x8000);
        builder.PullA();
        builder.StoreAAbsolute(shadowAddress);
        builder.StoreAAbsolute(0x8001);
        builder.Return();
    }

    private static byte[] PadToLength(IReadOnlyList<byte> bytes, int length)
    {
        var result = new byte[length];
        for (var index = 0; index < Math.Min(bytes.Count, length); index++)
        {
            result[index] = bytes[index];
        }

        return result;
    }

    private static void SeedMmc3SwitchableBanks(byte[] prg, IReadOnlyList<NesPrgSectionLayout> sections)
    {
        foreach (var section in sections.Where(section => section.Kind is not NesPrgSectionKind.FixedRuntime))
        {
            prg.AsSpan(section.PhysicalOffset, section.Size).Fill((byte)(0xA0 + section.PhysicalBank));
        }
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

    private static bool TryWhileEndLabelId(string label, out int id)
    {
        const string prefix = "while_end_";
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

    private static void EmitPaletteUpload(PrgBuilder builder, ushort? externalAddress)
    {
        builder.Emit(0xAD, 0x02, 0x20);             // LDA $2002
        builder.Emit(0xA9, 0x3F);                   // LDA #$3F
        builder.Emit(0x8D, 0x06, 0x20);             // STA $2006
        builder.Emit(0xA9, 0x00);                   // LDA #$00
        builder.Emit(0x8D, 0x06, 0x20);             // STA $2006
        builder.Emit(0xA2, 0x00);                   // LDX #$00
        builder.Label("palette_loop");
        if (externalAddress is { } paletteAddress)
        {
            builder.LoadAAbsoluteX(paletteAddress);
        }
        else
        {
            builder.LdaAbsoluteX("palette");
        }
        builder.Emit(0x8D, 0x07, 0x20);             // STA $2007
        builder.Emit(0xE8);                         // INX
        builder.Emit(0xE0, 0x20);                   // CPX #$20
        builder.BranchRelative(0xD0, "palette_loop"); // BNE palette_loop
    }

    private static void EmitNameTableUpload(PrgBuilder builder, int byteCount, ushort? externalAddress)
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
            if (externalAddress is { } nameTableAddress)
            {
                builder.LoadAAbsoluteX(checked((ushort)(nameTableAddress + page * 256)));
            }
            else
            {
                builder.LdaAbsoluteX("nametable", page * 256);
            }
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

    private static void EmitPpuRowAddressTables(PrgBuilder builder, WorldMap2D? worldMap, bool force = false)
    {
        if (worldMap is null || (!force && worldMap.Height <= 60))
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

    internal static string WorldMapColumnAttributeRowLabel(int row) => $"world_map_col_attr_row_{row}";

    // Emits one attribute-byte table per visible attribute row, indexed by the streamed source column
    // divided by four (one entry per 4-column attribute block). Horizontal column streaming uses these to
    // refresh the NES attribute table so streamed background columns keep the palette slots the initial
    // upload derived.
    private static void EmitWorldColumnAttributes(PrgBuilder builder, NesColumnAttributeStream? attributes)
    {
        if (attributes is null)
        {
            return;
        }

        for (var row = 0; row < attributes.Rows.Count; row++)
        {
            builder.Label(WorldMapColumnAttributeRowLabel(row));
            builder.Emit(attributes.Rows[row].BytesByColumnBlock);
        }
    }

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
        IReadOnlyList<NesCompiledSoundEffectAsset> soundEffectAssets,
        ushort vectorStartAddress)
    {
        // DPCM samples must live in the $C000-$FFF9 window and are placed right after the music data.
        // Sound-effect data is position-independent (walked from a start label at runtime), so it is
        // emitted after the DPCM blocks and does not shrink the DPCM window.
        var musicDataLength = musicAssets.Sum(asset => asset.Data.Length);
        var dpcmLayout = BuildDpcmSampleLayout(builder.CurrentAddress + musicDataLength, musicAssets, vectorStartAddress);
        EmitMusicAssets(builder, musicAssets, dpcmLayout.AddressRegisterMap);
        EmitDpcmSampleBlocks(builder, dpcmLayout.Placements);
        EmitSoundEffectAssets(builder, soundEffectAssets);
    }

    private static void EmitMusicAssets(
        PrgBuilder builder,
        IReadOnlyList<NesCompiledMusicAsset> musicAssets,
        IReadOnlyDictionary<byte, byte> dpcmAddressRegisterMap)
    {
        foreach (var asset in musicAssets)
        {
            var data = PatchDpcmAddressRegisters(asset, dpcmAddressRegisterMap);
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
    }

    private static void EmitSoundEffectAssets(
        PrgBuilder builder,
        IReadOnlyList<NesCompiledSoundEffectAsset> soundEffectAssets)
    {
        foreach (var asset in soundEffectAssets)
        {
            builder.Label(SoundEffectDataLabel(asset.Name));
            builder.Emit(asset.Data);
        }
    }

    private static DpcmSampleLayout BuildDpcmSampleLayout(
        int precedingDataEndAddress,
        IReadOnlyList<NesCompiledMusicAsset> musicAssets,
        ushort vectorStartAddress,
        string? fixedProfileName = null)
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
        var cursor = AlignToDmcAddress(Math.Max(precedingDataEndAddress, 0xC000));
        foreach (var block in uniqueBlocks.OrderByDescending(block => block.Data.Length))
        {
            cursor = AlignToDmcAddress(cursor);
            var endAddress = cursor + block.Data.Length;
            if (cursor < 0xC000 || endAddress > vectorStartAddress)
            {
                var message = fixedProfileName is null
                    ? $"NES DPCM sample block from ${block.SourceAddress:X4} with {block.Data.Length} bytes cannot fit in PRG ROM after music data ending at ${precedingDataEndAddress:X4}."
                    : $"NES {fixedProfileName} DPCM constraint: sample block from ${block.SourceAddress:X4} with {block.Data.Length} bytes cannot fit below ${vectorStartAddress:X4} after fixed runtime/data ending at ${precedingDataEndAddress:X4}.";
                throw LinkConstraint(
                    fixedProfileName is null ? NesLinkConstraint.Mapper0Dpcm : NesLinkConstraint.Dpcm,
                    message);
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

    private static byte[] BuildChrRom(NesVideoProgram program, int chrRomSize)
    {
        var chr = new byte[chrRomSize];
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
