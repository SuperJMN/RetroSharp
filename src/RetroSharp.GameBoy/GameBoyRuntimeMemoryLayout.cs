namespace RetroSharp.GameBoy;

using System.Reflection;

internal readonly record struct GameBoyWramRange(string Name, ushort Start, int Length)
{
    public int EndExclusive => checked(Start + Length);

    public ushort EndInclusive => checked((ushort)(EndExclusive - 1));

    public bool Contains(int address) => address >= Start && address < EndExclusive;

    public bool Overlaps(GameBoyWramRange other) => Start < other.EndExclusive && other.Start < EndExclusive;
}

internal sealed record GameBoyRuntimeMemoryAddress(
    string Domain,
    string Name,
    ushort Address,
    GameBoyWramRange Owner);

internal sealed record GameBoyRuntimeMemoryAlias(
    string Name,
    GameBoyWramRange Canonical,
    GameBoyWramRange Alias);

// This is the single ownership boundary for compiler-reserved Game Boy RAM.
// Emitters consume the domain groups below; they must not introduce private WRAM maps.
// Hardware registers, ROM banking ports, and source-program local offsets are not runtime allocations.
internal static class GameBoyRuntimeMemoryLayout
{
    internal static readonly GameBoyWramRange UserLocals = new("user locals", 0xC000, 0x00E0);
    internal static readonly GameBoyWramRange CameraState = new("camera state", 0xC0E0, 0x0010);
    internal static readonly GameBoyWramRange InputState = new("input state", 0xC0F0, 0x000A);
    internal static readonly GameBoyWramRange AudioState = new("audio state", 0xC0FA, 0x001F);
    internal static readonly GameBoyWramRange CameraStreamingState = new("camera streaming and banking state", 0xC119, 0x0016);
    internal static readonly GameBoyWramRange RuntimeScratchState = new("runtime scratch state", 0xC12F, 0x0002);
    internal static readonly GameBoyWramRange SoundEffectState = new("sound-effect state", 0xC131, 0x0009);
    internal static readonly GameBoyWramRange ExtendedCameraStreamingState = new("extended camera streaming state", 0xC13A, 0x0013);
    internal static readonly GameBoyWramRange PackedCameraState = new("packed camera state", 0xC14D, 0x009E);
    internal static readonly GameBoyWramRange CollisionQueryState = new("collision query state", 0xC1EB, 0x0005);
    internal static readonly GameBoyWramRange WorldPackScalarState = new("WorldPack scalar and collision state", 0xC1F0, 0x0020);
    internal static readonly GameBoyWramRange AudioChannel1Shadow = new("audio channel-1 shadow", 0xC210, 0x0005);
    internal static readonly GameBoyWramRange CollisionMemoState = new("collision memo state", 0xC220, 0x00C0);
    internal static readonly GameBoyWramRange WorldPackStaging = new("WorldPack staging", 0xC300, WorldPack.MaximumStagingBytes);
    internal static readonly GameBoyWramRange SpriteOamShadow = new("sprite OAM shadow", 0xC600, 0x00A0);
    internal static readonly GameBoyWramRange WramEcho = new("WRAM echo", 0xE000, 0x1E00);
    internal static readonly GameBoyWramRange OamDmaRoutine = new("OAM DMA HRAM routine", 0xFF80, 0x000A);
    internal static readonly GameBoyWramRange Stack = new("stack/HRAM", 0xFF8A, 0x0076);

    internal static IReadOnlyList<GameBoyWramRange> ReservedRanges { get; } =
    [
        UserLocals,
        CameraState,
        InputState,
        AudioState,
        CameraStreamingState,
        RuntimeScratchState,
        SoundEffectState,
        ExtendedCameraStreamingState,
        PackedCameraState,
        CollisionQueryState,
        WorldPackScalarState,
        AudioChannel1Shadow,
        CollisionMemoState,
        WorldPackStaging,
        SpriteOamShadow,
        WramEcho,
        OamDmaRoutine,
        Stack,
    ];

    internal static IReadOnlyList<GameBoyRuntimeMemoryAlias> IntentionalAliases { get; } =
    [
        new(
            "WRAM echo",
            new GameBoyWramRange("WRAM bank 0 canonical address space", 0xC000, 0x1E00),
            WramEcho),
    ];

    internal static IReadOnlyList<GameBoyRuntimeMemoryAddress> NamedAddresses { get; }

    static GameBoyRuntimeMemoryLayout()
    {
        NamedAddresses = DescribeNamedAddresses();
        Validate();
    }

    internal static class Banking
    {
        internal const ushort ProgramCurrentBank = 0xC11C;
        internal const ushort ActualVisibleBank = 0xC1FA;
    }

    internal static class Camera
    {
        internal const ushort XLow = 0xC0E0;
        internal const ushort XHigh = 0xC0E1;
        internal const ushort FineX = 0xC0E2;
        internal const ushort ScreenLeftColumn = 0xC0E3;
        internal const ushort RightBackgroundColumn = 0xC0E4;
        internal const ushort LeftBackgroundColumn = 0xC0E5;
        internal const ushort RightSourceColumn = 0xC0E6;
        internal const ushort LeftSourceColumn = 0xC0E7;
        internal const ushort YLow = 0xC0E8;
        internal const ushort YHigh = 0xC0E9;
        internal const ushort FineY = 0xC0EA;
        internal const ushort TopBackgroundRow = 0xC0EB;
        internal const ushort BottomBackgroundRow = 0xC0EC;
        internal const ushort TopSourceRow = 0xC0ED;
        internal const ushort BottomSourceRow = 0xC0EE;
        // Camera movement queues exposed tile edges here. The camera-apply operation drains them during VBlank.
        internal const ushort PendingStreamKind = 0xC119;
        internal const ushort PendingStreamTarget = 0xC11A;
        internal const ushort PendingStreamSource = 0xC11B;
        internal const ushort PendingStreamRowDataLow = 0xC11D;
        internal const ushort PendingStreamRowDataHigh = 0xC11E;
        internal const ushort PendingDiagonalColumnKind = 0xC11F;
        internal const ushort PendingDiagonalColumnTarget = 0xC120;
        internal const ushort PendingDiagonalColumnSource = 0xC121;
        internal const ushort PendingDiagonalRowKind = 0xC122;
        internal const ushort PendingDiagonalRowTarget = 0xC123;
        internal const ushort PendingDiagonalRowSource = 0xC124;
        internal const ushort ScreenTileFlagsColumn = 0xC125;
        internal const ushort ScreenTileFlagsColumnHigh = 0xC126;
        internal const ushort PendingDiagonalNextStreamKind = 0xC127;
        internal const ushort StreamSourceRowScratch = 0xC128;
        internal const ushort StreamSourceColumnScratch = 0xC12A;
        internal const ushort StreamTargetColumnScratch = 0xC12B;
        internal const ushort StreamColumnsRemaining = 0xC12C;
        internal const ushort SetPositionTarget = 0xC12D;
        internal const ushort SetPositionStepsRemaining = 0xC12E;
        internal const ushort PendingStreamCount = 0xC13A;
        internal const ushort PendingStreamSecondTarget = 0xC13B;
        internal const ushort PendingStreamSecondSource = 0xC13C;
        internal const ushort PendingDiagonalColumnCount = 0xC13D;
        internal const ushort PendingDiagonalColumnSecondTarget = 0xC13E;
        internal const ushort PendingDiagonalColumnSecondSource = 0xC13F;
        internal const ushort PendingDiagonalRowCount = 0xC140;
        internal const ushort PendingDiagonalRowSecondTarget = 0xC141;
        internal const ushort PendingDiagonalRowSecondSource = 0xC142;
        internal const ushort ScreenLeftColumnHigh = 0xC143;
        internal const ushort RightSourceColumnHigh = 0xC144;
        internal const ushort LeftSourceColumnHigh = 0xC145;
        internal const ushort PendingStreamSourceHigh = 0xC146;
        internal const ushort PendingStreamSecondSourceHigh = 0xC147;
        internal const ushort PendingDiagonalColumnSourceHigh = 0xC148;
        internal const ushort PendingDiagonalColumnSecondSourceHigh = 0xC149;
        internal const ushort SetPositionTargetHigh = 0xC14A;
        internal const ushort StreamLogicalSourceColumnLow = 0xC14B;
        internal const ushort StreamLogicalSourceColumnHigh = 0xC14C;
    }

    internal static class Input
    {
        internal const ushort Current = 0xC0F0;
        internal const ushort Previous = 0xC0F1;
        internal const ushort HoldTicksStart = 0xC0F2;
    }

    internal static class Audio
    {
        internal const ushort MusicActive = 0xC0FA;
        internal const ushort MusicRow = 0xC0FB;
        internal const ushort MusicTick = 0xC0FC;
        internal const ushort MusicDataPointerLow = 0xC0FD;
        internal const ushort MusicDataPointerHigh = 0xC0FE;
        internal const ushort MusicCurrentPointerLow = 0xC0FF;
        internal const ushort MusicCurrentPointerHigh = 0xC100;
        internal const ushort MusicScratchPointerLow = 0xC101;
        internal const ushort MusicScratchPointerHigh = 0xC102;
        internal const ushort MusicTicksPerRow = 0xC103;
        internal const ushort MusicRowHigh = 0xC104;
        internal const ushort MusicRowMask = 0xC105;
        internal const ushort MusicRowCacheStart = 0xC106;
        internal const ushort MusicDataBank = 0xC115;
        internal const ushort MusicCurrentBank = 0xC116;
        internal const ushort MusicScratchBank = 0xC117;
        internal const ushort MusicDataCursorBank = 0xC118;
        internal const ushort SfxActive = 0xC131;
        internal const ushort SfxTick = 0xC132;
        internal const ushort SfxDataPointerLow = 0xC133;
        internal const ushort SfxDataPointerHigh = 0xC134;
        internal const ushort SfxCurrentPointerLow = 0xC135;
        internal const ushort SfxCurrentPointerHigh = 0xC136;
        internal const ushort SfxDataBank = 0xC137;
        internal const ushort SfxCurrentBank = 0xC138;
        internal const ushort SfxDataCursorBank = 0xC139;
        // The shadow is page-aligned with NR10-NR14 so indexed music writes can use the register low byte.
        // It preserves the BGM channel while a sound effect temporarily owns channel 1.
        internal const ushort Channel1ShadowNr10 = 0xC210;
        internal const ushort Channel1ShadowNr11 = 0xC211;
        internal const ushort Channel1ShadowNr12 = 0xC212;
        internal const ushort Channel1ShadowNr13 = 0xC213;
        internal const ushort Channel1ShadowNr14 = 0xC214;
        internal const byte Channel1ShadowPageHigh = 0xC2;
    }

    internal static class Runtime
    {
        internal const ushort WordScratchLow = 0xC12F;
        internal const ushort WordScratchHigh = 0xC130;
    }

    internal static class Sprites
    {
        internal const ushort OamShadowStart = 0xC600;
        internal const ushort DmaRoutineAddress = 0xFF80;
        internal const byte OamShadowPage = 0xC6;
    }

    internal static class PackedCamera
    {
        internal const ushort VisibleCameraXLow = 0xC14D;
        internal const ushort VisibleCameraXHigh = 0xC14E;
        internal const ushort VisibleCameraYLow = 0xC14F;
        internal const ushort VisibleCameraYHigh = 0xC150;
        internal const ushort CriticalSection = 0xC151;
        internal const ushort RequestCount = 0xC152;
        internal const ushort PrepareCount = 0xC153;
        internal const ushort ResidentCount = 0xC154;
        internal const ushort CommitCount = 0xC155;
        internal const ushort ReleaseCount = 0xC156;
        internal const ushort BankWorkInCommit = 0xC157;
        internal const ushort DecodeWorkInCommit = 0xC158;
        internal const ushort LastCommitVramWrites = 0xC159;
        internal const ushort IteratorLow = 0xC15A;
        internal const ushort IteratorHigh = 0xC15B;
        internal const ushort PreparedSlot = 0xC15C;
        internal const ushort CommitTarget = 0xC15D;
        internal const ushort CommitTargetStart = 0xC15E;
        internal const ushort CommitWorldEdgeLow = 0xC15F;
        internal const ushort PendingDirection = 0xC160;
        internal const ushort PendingSecondDirection = 0xC161;
        internal const ushort PendingSlot = 0xC162;
        internal const ushort PendingSecondSlot = 0xC163;
        internal const ushort PendingDiagonalColumnDirection = 0xC164;
        internal const ushort PendingDiagonalColumnSecondDirection = 0xC165;
        internal const ushort PendingDiagonalColumnSlot = 0xC166;
        internal const ushort PendingDiagonalColumnSecondSlot = 0xC167;
        internal const ushort PendingDiagonalRowDirection = 0xC168;
        internal const ushort PendingDiagonalRowSecondDirection = 0xC169;
        internal const ushort PendingDiagonalRowSlot = 0xC16A;
        internal const ushort PendingDiagonalRowSecondSlot = 0xC16B;
        internal const ushort CommitWorldEdgeHigh = 0xC16C;
        internal const ushort CommitDirection = 0xC16D;
        internal const ushort CommitAxis = 0xC16E;
        internal const ushort SelectedSlot = 0xC16F;
        internal const ushort Slot0 = 0xC170;
        internal const ushort Slot1 = 0xC17A;
        internal const ushort DestinationLow = 0xC184;
        internal const ushort DestinationHigh = 0xC185;
        internal const ushort PayloadRemaining = 0xC186;
        internal const ushort TileScratch = 0xC187;
        internal const ushort LastCommittedWorldEdgeLow = 0xC188;
        internal const ushort LastCommittedWorldEdgeHigh = 0xC189;
        internal const ushort LastCommittedAxis = 0xC18A;
        internal const ushort LastCommittedDirection = 0xC18B;
        internal const ushort CommitSucceeded = 0xC18C;
        internal const ushort VisualCacheValid = 0xC18D;
        internal const ushort VisualCacheChunkLow = 0xC18E;
        internal const ushort VisualCacheChunkHigh = 0xC18F;
        internal const ushort PendingVisualChunkLow = 0xC190;
        internal const ushort PendingVisualChunkHigh = 0xC191;
        internal const ushort VisualSelectedSlot = 0xC192;
        internal const ushort VisualCache1Valid = 0xC193;
        internal const ushort VisualCache1ChunkLow = 0xC194;
        internal const ushort VisualCache1ChunkHigh = 0xC195;
        internal const ushort VisualReplacementNext = 0xC196;
        internal const ushort WaitAudioEnabled = 0xC197;
        internal const ushort WaitAudioTicked = 0xC198;
        internal const ushort CurrentLy = 0xC199;
        internal const ushort LastObservedLy = 0xC19A;
        internal const ushort LastObservedLyValid = 0xC19B;
        internal const ushort DirectoryWorkInVBlank = 0xC19C;
        internal const ushort AudioTickCount = 0xC19D;
        internal const ushort EdgePreparationBankSession = 0xC19E;
        internal const ushort EdgeSubcellX = 0xC19F;
        internal const ushort EdgeSubcellY = 0xC1A0;
        internal const ushort EdgeLocalX = 0xC1A1;
        internal const ushort EdgeLocalY = 0xC1A2;
        internal const ushort EdgeValidWidth = 0xC1A3;
        internal const ushort EdgeValidHeight = 0xC1A4;
        internal const ushort EdgeReuseReady = 0xC1A5;
        internal const ushort EdgeVisualSlot = 0xC1A6;
        internal const ushort EdgeCellIndex = 0xC1A7;
        internal const ushort EdgeSameMetatile = 0xC1A8;
        internal const ushort EdgeExpansionAddressLow = 0xC1A9;
        internal const ushort EdgeExpansionAddressHigh = 0xC1AA;
        internal const ushort EdgeExpansionBank = 0xC1AB;
        internal const ushort DiagonalTargetXLow = 0xC1AC;
        internal const ushort DiagonalTargetXHigh = 0xC1AD;
        internal const ushort DiagonalTargetYLow = 0xC1AE;
        internal const ushort DiagonalTargetYHigh = 0xC1AF;
        internal const ushort DiagonalPreferredPreparationAxis = 0xC1B0;
        internal const ushort DiagonalPreparedAxis = 0xC1B1;
        internal const ushort DiagonalNextPreparationAxis = 0xC1B2;
        internal const ushort VisualCache2Valid = 0xC1B3;
        internal const ushort VisualCache2ChunkLow = 0xC1B4;
        internal const ushort VisualCache2ChunkHigh = 0xC1B5;
        internal const ushort VisualCache3Valid = 0xC1B6;
        internal const ushort VisualCache3ChunkLow = 0xC1B7;
        internal const ushort VisualCache3ChunkHigh = 0xC1B8;
        internal const ushort VisualCache4Valid = 0xC1B9;
        internal const ushort VisualCache4ChunkLow = 0xC1BA;
        internal const ushort VisualCache4ChunkHigh = 0xC1BB;
        internal const ushort VisualCache5Valid = 0xC1BC;
        internal const ushort VisualCache5ChunkLow = 0xC1BD;
        internal const ushort VisualCache5ChunkHigh = 0xC1BE;
        internal const ushort VisualCache0Age = 0xC1BF;
        internal const ushort VisualCache1Age = 0xC1C0;
        internal const ushort VisualCache2Age = 0xC1C1;
        internal const ushort VisualCache3Age = 0xC1C2;
        internal const ushort VisualCache4Age = 0xC1C3;
        internal const ushort VisualCache5Age = 0xC1C4;
        internal const ushort VisualPendingRowGroupLow = 0xC1C5;
        internal const ushort VisualPendingRowGroupHigh = 0xC1C6;
        internal const ushort VisualPendingColumnGroupLow = 0xC1C7;
        internal const ushort VisualPendingColumnGroupHigh = 0xC1C8;
        internal const ushort VisualLastColumnGroupValid = 0xC1C9;
        internal const ushort VisualLastColumnGroupLow = 0xC1CA;
        internal const ushort VisualLastColumnGroupHigh = 0xC1CB;
        internal const ushort VisualLastRowGroupValid = 0xC1CC;
        internal const ushort VisualLastRowGroupLow = 0xC1CD;
        internal const ushort VisualLastRowGroupHigh = 0xC1CE;
        internal const ushort VisualCacheRowGroupLowStart = 0xC1CF;
        internal const ushort VisualCacheRowGroupHighStart = 0xC1D5;
        internal const ushort VisualCacheColumnGroupLowStart = 0xC1DB;
        internal const ushort VisualCacheColumnGroupHighStart = 0xC1E1;
        internal const ushort DiagonalTargetFresh = 0xC1E7;
        internal const ushort CommitEnteredVBlank = 0xC1E8;
        internal const ushort DirectoryWorkInCommit = 0xC1E9;
        internal const ushort DecodeWorkInVBlank = 0xC1EA;
    }

    internal static class Collision
    {
        internal const ushort QueryXLow = 0xC1EB;
        internal const ushort QueryXHigh = 0xC1EC;
        internal const ushort QueryYLow = 0xC1ED;
        internal const ushort QueryYHigh = 0xC1EE;
        internal const ushort SelectedSlot = 0xC1EF;
        internal const ushort Cache0Valid = 0xC1FD;
        internal const ushort Cache0ChunkLow = 0xC1FE;
        internal const ushort Cache0ChunkHigh = 0xC1FF;
        internal const ushort Cache1Valid = 0xC200;
        internal const ushort Cache1ChunkLow = 0xC201;
        internal const ushort Cache1ChunkHigh = 0xC202;
        internal const ushort ReplacementNext = 0xC203;
        internal const ushort DecodeCountLow = 0xC204;
        internal const ushort DecodeCountHigh = 0xC205;
        internal const ushort CellValid = 0xC206;
        internal const ushort CellXLow = 0xC207;
        internal const ushort CellXHigh = 0xC208;
        internal const ushort CellYLow = 0xC209;
        internal const ushort CellYHigh = 0xC20A;
        internal const ushort CellResult = 0xC20B;
        internal const ushort GameplayTickCount = 0xC20C;
        internal const ushort PendingChunkLow = 0xC20D;
        internal const ushort PendingChunkHigh = 0xC20E;
        internal const ushort MemoHitCount = 0xC20F;
    }

    internal static class WorldPack
    {
        internal const int CurrentStagingBytes = 362;
        internal const int MaximumStagingBytes = 554;
        // Decoder scratch is shared by mutually exclusive WorldPack routines; staging payloads live in
        // WorldPackStaging and are sized through ValidateWorldPackStagingBytes.
        internal const ushort ScratchPacketCount = 0xC1F0;
        internal const ushort ScratchXLow = 0xC1F1;
        internal const ushort ScratchXHigh = 0xC1F2;
        internal const ushort ScratchYLow = 0xC1F3;
        internal const ushort ScratchYHigh = 0xC1F4;
        internal const ushort ScratchCellIndex = 0xC1F5;
        internal const ushort ScratchStoredRemaining = 0xC1F6;
        internal const ushort ScratchSourceBank = 0xC1F7;
        internal const ushort ScratchReadByte = 0xC1F8;
        internal const ushort ScratchSubcellIndex = 0xC1F9;
        internal const ushort ValidationState = 0xC1FB;
        internal const ushort ScratchSubcellIndexHigh = 0xC1FC;
    }

    internal static void Validate()
    {
        for (var index = 0; index < ReservedRanges.Count; index++)
        {
            var range = ReservedRanges[index];
            if (range.Length <= 0)
            {
                throw new InvalidOperationException($"Game Boy runtime range '{range.Name}' must have a positive length.");
            }

            for (var otherIndex = index + 1; otherIndex < ReservedRanges.Count; otherIndex++)
            {
                var other = ReservedRanges[otherIndex];
                if (range.Overlaps(other))
                {
                    throw new InvalidOperationException(
                        $"Game Boy runtime range {range.Name} {range.Start:X4}-{range.EndInclusive:X4} overlaps {other.Name} {other.Start:X4}-{other.EndInclusive:X4}.");
                }
            }
        }

        if (UserLocals.EndExclusive != CameraState.Start)
        {
            throw new InvalidOperationException("Game Boy user locals must end where target-owned runtime state begins.");
        }

        foreach (var alias in IntentionalAliases)
        {
            if (alias.Canonical.Length != alias.Alias.Length || !ReservedRanges.Contains(alias.Alias))
            {
                throw new InvalidOperationException($"Game Boy runtime alias '{alias.Name}' is not a length-matched declared range.");
            }
        }

        foreach (var duplicate in NamedAddresses.GroupBy(address => address.Address).Where(group => group.Count() > 1))
        {
            throw new InvalidOperationException(
                $"Game Boy runtime address {duplicate.Key:X4} has multiple owners: {string.Join(", ", duplicate.Select(address => $"{address.Domain}.{address.Name}"))}.");
        }
    }

    internal static GameBoyWramRange ValidateUserLocalBytes(int bytes)
    {
        if (bytes < 0 || bytes > UserLocals.Length)
        {
            throw new InvalidOperationException(
                $"Game Boy target local variables exceed the current prototype WRAM allocation; user locals require 0..{UserLocals.Length} bytes, but {bytes} were requested.");
        }

        return UserLocals with { Length = bytes };
    }

    internal static GameBoyWramRange ValidateWorldPackStagingBytes(int stagingBytes)
    {
        if (stagingBytes is <= 0 or > WorldPack.MaximumStagingBytes)
        {
            throw new InvalidOperationException(
                $"Game Boy WorldPack staging requires 1..{WorldPack.MaximumStagingBytes} bytes; {stagingBytes} were requested.");
        }

        var requested = WorldPackStaging with { Length = stagingBytes };
        foreach (var reserved in ReservedRanges.Where(range => range != WorldPackStaging))
        {
            if (requested.Overlaps(reserved))
            {
                throw new InvalidOperationException(
                    $"Game Boy {requested.Name} {requested.Start:X4}-{requested.EndInclusive:X4} overlaps {reserved.Name} {reserved.Start:X4}-{reserved.EndInclusive:X4}.");
            }
        }

        return requested;
    }

    private static IReadOnlyList<GameBoyRuntimeMemoryAddress> DescribeNamedAddresses()
    {
        var domains = new (string Name, Type Type)[]
        {
            ("audio", typeof(Audio)),
            ("banking", typeof(Banking)),
            ("camera", typeof(Camera)),
            ("collision", typeof(Collision)),
            ("input", typeof(Input)),
            ("packed camera", typeof(PackedCamera)),
            ("runtime", typeof(Runtime)),
            ("sprites", typeof(Sprites)),
            ("WorldPack", typeof(WorldPack)),
        };

        return domains
            .SelectMany(domain => domain.Type
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(field => field.IsLiteral && field.FieldType == typeof(ushort))
                .Select(field =>
                {
                    var address = (ushort)field.GetRawConstantValue()!;
                    var owners = ReservedRanges.Where(range => range.Contains(address)).ToArray();
                    if (owners.Length != 1)
                    {
                        throw new InvalidOperationException(
                            $"Game Boy runtime address {domain.Name}.{field.Name} at {address:X4} must belong to exactly one reserved range.");
                    }

                    return new GameBoyRuntimeMemoryAddress(domain.Name, field.Name, address, owners[0]);
                }))
            .OrderBy(address => address.Address)
            .ThenBy(address => address.Domain, StringComparer.Ordinal)
            .ThenBy(address => address.Name, StringComparer.Ordinal)
            .ToArray();
    }
}
