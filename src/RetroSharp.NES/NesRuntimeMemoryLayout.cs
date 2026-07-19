namespace RetroSharp.NES;

using System.Reflection;

internal readonly record struct NesRamRange(string Name, ushort Start, int Length)
{
    internal NesRamRange(ushort start, int length)
        : this("WorldPack staging slot", start, length)
    {
    }

    public int EndExclusive => checked(Start + Length);

    public ushort EndInclusive => checked((ushort)(EndExclusive - 1));

    public bool Contains(int address) => address >= Start && address < EndExclusive;

    public bool Overlaps(NesRamRange other) => Start < other.EndExclusive && other.Start < EndExclusive;
}

internal sealed record NesRuntimeMemoryAddress(
    string Domain,
    string Name,
    ushort Address,
    NesRamRange Owner);

internal sealed record NesRuntimeMemoryRangeAlias(
    string Name,
    NesRamRange Canonical,
    NesRamRange Alias);

internal sealed record NesRuntimeMemoryAddressAlias(
    string Name,
    ushort Address,
    IReadOnlyList<string> Roles);

// This is the single ownership boundary for compiler-reserved NES CPU RAM.
// Emitters consume the domain groups below; hardware registers are not runtime allocations.
internal static class NesRuntimeMemoryLayout
{
    internal static readonly NesRamRange UserLocals = new("user zero-page locals", 0x0000, 0x00DE);
    internal static readonly NesRamRange SoundEffectZeroPage = new("sound-effect zero-page state", 0x00DE, 0x0002);
    internal static readonly NesRamRange CameraAndRuntimeZeroPage = new("camera and runtime zero-page scratch", 0x00E0, 0x0010);
    internal static readonly NesRamRange InputZeroPage = new("input zero-page state", 0x00F0, 0x000A);
    internal static readonly NesRamRange AudioZeroPage = new("audio zero-page state", 0x00FA, 0x0006);
    internal static readonly NesRamRange Stack = new("CPU stack", 0x0100, 0x0100);
    internal static readonly NesRamRange OamShadow = new("OAM shadow", 0x0200, 0x0100);
    internal static readonly NesRamRange CameraControlState = new("camera control state", 0x0300, 0x000D);
    internal static readonly NesRamRange AudioState = new("audio state", 0x0310, 0x0008);
    internal static readonly NesRamRange ExtendedCameraState = new("extended camera state", 0x0318, 0x000C);
    internal static readonly NesRamRange MapperBankShadows = new("mapper bank shadows", 0x0324, 0x0002);
    internal static readonly NesRamRange WorldPackScalarState = new("WorldPack scalar state", 0x0326, 0x0048);
    internal static readonly NesRamRange PackedCameraAndWorldPackAuxiliaryState = new("packed camera and WorldPack auxiliary state", 0x036E, 0x0092);
    internal static readonly NesRamRange WorldPackStaging = new("WorldPack staging", 0x0400, WorldPack.MaximumStagingBytes);
    internal static readonly NesRamRange CpuRamMirror1 = new("CPU RAM mirror 1", 0x0800, 0x0800);
    internal static readonly NesRamRange CpuRamMirror2 = new("CPU RAM mirror 2", 0x1000, 0x0800);
    internal static readonly NesRamRange CpuRamMirror3 = new("CPU RAM mirror 3", 0x1800, 0x0800);

    internal static IReadOnlyList<NesRamRange> ReservedRanges { get; } =
    [
        UserLocals,
        SoundEffectZeroPage,
        CameraAndRuntimeZeroPage,
        InputZeroPage,
        AudioZeroPage,
        Stack,
        OamShadow,
        CameraControlState,
        AudioState,
        ExtendedCameraState,
        MapperBankShadows,
        WorldPackScalarState,
        PackedCameraAndWorldPackAuxiliaryState,
        WorldPackStaging,
        CpuRamMirror1,
        CpuRamMirror2,
        CpuRamMirror3,
    ];

    internal static IReadOnlyList<NesRuntimeMemoryRangeAlias> IntentionalRangeAliases { get; } =
    [
        new("CPU RAM mirror 1", new NesRamRange("CPU RAM canonical address space", 0x0000, 0x0800), CpuRamMirror1),
        new("CPU RAM mirror 2", new NesRamRange("CPU RAM canonical address space", 0x0000, 0x0800), CpuRamMirror2),
        new("CPU RAM mirror 3", new NesRamRange("CPU RAM canonical address space", 0x0000, 0x0800), CpuRamMirror3),
    ];

    internal static IReadOnlyList<NesRuntimeMemoryAddressAlias> IntentionalAddressAliases { get; } =
    [
        new(
            "sprite-frame/payload-index scratch",
            0x00E4,
            ["packed camera.PayloadIndexScratch", "runtime.SpriteFrameScratch"]),
        new(
            "runtime/WorldPack/packed-camera pointer low scratch",
            0x00E8,
            ["WorldPack.PointerLow", "packed camera.PointerLow", "runtime.IndexScratch"]),
        new(
            "runtime/WorldPack/packed-camera pointer high scratch",
            0x00E9,
            ["WorldPack.PointerHigh", "packed camera.PointerHigh", "runtime.ExpressionScratch"]),
        new(
            "fast visual cache tag 0",
            0x03B8,
            ["WorldPack.FastVisualCacheTag0", "WorldPack.VisualCache0Valid"]),
        new(
            "fast visual cache tag 2",
            0x03B9,
            ["WorldPack.FastVisualCacheTag2", "WorldPack.VisualCache0ChunkLow"]),
        new(
            "fast visual cache tag 3",
            0x03BA,
            ["WorldPack.FastVisualCacheTag3", "WorldPack.VisualCache0ChunkHigh"]),
        new(
            "fast visual cache tag 1",
            0x03BB,
            ["WorldPack.FastVisualCacheTag1", "WorldPack.VisualCache1Valid"]),
        new(
            "fast visual cache tag 4",
            0x03BC,
            ["WorldPack.FastVisualCacheTag4", "WorldPack.VisualCache1ChunkLow"]),
        new(
            "fast visual cache tag 5",
            0x03BD,
            ["WorldPack.FastVisualCacheTag5", "WorldPack.VisualCache1ChunkHigh"]),
    ];

    internal static IReadOnlyList<NesRuntimeMemoryAddress> NamedAddresses { get; }

    static NesRuntimeMemoryLayout()
    {
        NamedAddresses = DescribeNamedAddresses();
        Validate();
    }

    internal static class Banking
    {
        internal const ushort Mmc3R6Shadow = 0x0324;
        internal const ushort Mmc3R7Shadow = 0x0325;
    }

    internal static class Camera
    {
        internal const byte X = 0xE0;
        internal const byte TileColumn = 0xE1;
        internal const byte TargetColumn = 0xE2;
        internal const byte SourceColumn = 0xE3;
        internal const byte NewX = 0xE7;
        internal const byte Y = 0xEA;
        internal const byte TileRow = 0xEB;
        internal const byte NewY = 0xEC;
        internal const byte TargetRow = 0xED;
        internal const byte SourceRow = 0xEE;
        internal const byte StreamRemaining = 0xEF;
        internal const ushort PendingStreamFlags = 0x0300;
        internal const ushort PendingNextStream = 0x0301;
        internal const ushort PendingColumnTarget = 0x0302;
        internal const ushort PendingColumnSource = 0x0303;
        internal const ushort PendingRowTarget = 0x0304;
        internal const ushort PendingRowSource = 0x0305;
        internal const ushort PendingRowTargetColumn = 0x0306;
        internal const ushort PendingRowSourceColumn = 0x0307;
        internal const ushort PendingRowPhase = 0x0308;
        internal const ushort ScrollApplied = 0x0309;
        internal const ushort WalkTarget = 0x030A;
        internal const ushort WalkSteps = 0x030B;
        // The packed WorldPack runtime shares $E8/$E9 as a transient pointer. Keep the
        // high byte of a multi-frame camera target in stable absolute state.
        internal const ushort WalkTargetHigh = 0x030C;
        internal const ushort XHigh = 0x0318;
        internal const ushort YHigh = 0x0319;
        internal const ushort TileColumnHigh = 0x031A;
        internal const ushort TileRowHigh = 0x031B;
        internal const ushort SourceRowHigh = 0x031C;
        internal const ushort SourceColumnHigh = 0x031E;
        internal const ushort PendingColumnSourceHigh = 0x0321;
        internal const ushort PendingRowSourceColumnHigh = 0x0323;
    }

    internal static class Input
    {
        internal const byte Current = 0xF0;
        internal const byte Previous = 0xF1;
        internal const byte HoldTicksStart = 0xF2;
    }

    internal static class Audio
    {
        // SFX playback walks one frame body per audio-update tick through this zero-page cursor.
        internal const byte SfxPointerLow = 0xDE;
        internal const byte SfxPointerHigh = 0xDF;
        internal const byte MusicOrderPointerLow = 0xFA;
        internal const byte MusicOrderPointerHigh = 0xFB;
        internal const byte MusicBodyPointerLow = 0xFC;
        internal const byte MusicBodyPointerHigh = 0xFD;
        internal const byte MusicTick = 0xFE;
        internal const byte MusicCommandCount = 0xFF;
        internal const ushort MusicLoopPointerLow = 0x0310;
        internal const ushort MusicLoopPointerHigh = 0x0311;
        // Music suppresses pulse 1 while this sound-effect ownership flag is non-zero.
        internal const ushort SfxActive = 0x0312;
        // The BGM updates its intended $4000-$4003 state here even while an SFX owns pulse 1.
        internal const ushort Pulse1ShadowBase = 0x0313;
        // Retain pulse 1 for the authored ring-out before restoring the music shadow.
        internal const ushort SfxLinger = 0x0317;
    }

    internal static class Runtime
    {
        internal const byte SpriteFrameScratch = 0xE4;
        internal const byte CollisionColumnScratch = 0xE5;
        internal const byte CollisionRowScratch = 0xE6;
        internal const byte IndexScratch = 0xE8;
        internal const byte ExpressionScratch = 0xE9;
    }

    internal static class Sprite
    {
        internal const ushort OamShadow = 0x0200;
    }

    internal static class PackedCamera
    {
        internal const ushort FrameCounterLow = 0x036E;
        internal const ushort FrameCounterHigh = 0x036F;
        internal const ushort RequestCount = 0x0370;
        internal const ushort PrepareCount = 0x0371;
        internal const ushort ResidentCount = 0x0372;
        internal const ushort CommitCount = 0x0373;
        internal const ushort ReleaseCount = 0x0374;
        internal const ushort BankWorkInCommit = 0x0375;
        internal const ushort DirectoryWorkInCommit = 0x0376;
        internal const ushort DecodeWorkInCommit = 0x0377;
        internal const ushort LastTileWrites = 0x0378;
        internal const ushort LastAttributeWrites = 0x0379;
        internal const ushort RequestFrameLow = 0x037A;
        internal const ushort RequestFrameHigh = 0x037B;
        internal const ushort ResidentFrameLow = 0x037C;
        internal const ushort ResidentFrameHigh = 0x037D;
        internal const ushort CommitFrameLow = 0x037E;
        internal const ushort CommitFrameHigh = 0x037F;
        internal const ushort CriticalSection = 0x0380;
        internal const ushort SelectedSlot = 0x0381;
        internal const ushort CommitAxis = 0x0382;
        internal const ushort CommitDirection = 0x0383;
        internal const ushort CommitWorldEdgeLow = 0x0384;
        internal const ushort CommitWorldEdgeHigh = 0x0385;
        internal const ushort CommitTarget = 0x0386;
        internal const ushort CommitOrthogonalLow = 0x0387;
        internal const ushort CommitOrthogonalHigh = 0x0388;
        internal const ushort CommitPayloadLength = 0x0389;
        internal const ushort RowPhase = 0x038A;
        internal const ushort CommitTargetStart = 0x038B;
        internal const ushort NextAxis = 0x038C;
        internal const ushort SelectedMetadataOffset = 0x038D;
        internal const ushort FramePending = 0x038F;
        internal const ushort Slot0 = 0x0390;
        internal const ushort Slot1 = 0x03A0;
        // Semantic observation aliases keep external harnesses independent from
        // the packed metadata byte offsets owned by NesPackedCameraRuntime.
        internal const ushort Slot0CommitPhase = Slot0 + NesPackedCameraRuntime.CommitPhaseOffset;
        internal const ushort Slot0PayloadCursor = Slot0 + NesPackedCameraRuntime.PayloadCursorOffset;
        internal const ushort Slot1CommitPhase = Slot1 + NesPackedCameraRuntime.CommitPhaseOffset;
        internal const ushort Slot1PayloadCursor = Slot1 + NesPackedCameraRuntime.PayloadCursorOffset;
        internal const ushort Iterator = 0x03B0;
        internal const ushort DestinationLow = 0x03B1;
        internal const ushort DestinationHigh = 0x03B2;
        internal const ushort Status = 0x03B3;
        internal const ushort TargetCursor = 0x03B4;
        internal const ushort AddressColumn = 0x03B5;
        internal const ushort PhaseRemaining = 0x03B6;
        internal const ushort AttributeBlock = 0x03B7;
        internal const ushort AttributeIndexLow = 0x03C3;
        internal const ushort AttributeIndexHigh = 0x03C4;
        internal const ushort AttributeCount = 0x03C5;
        internal const ushort AttributeBlockXLow = 0x03C6;
        internal const ushort AttributeBlockXHigh = 0x03C7;
        internal const ushort AttributeBlockYLow = 0x03C8;
        internal const ushort AttributeBlockYHigh = 0x03C9;
        internal const ushort PendingAxes = 0x03CA;
        internal const ushort VisibleCameraXLow = 0x03CB;
        internal const ushort VisibleCameraXHigh = 0x03CC;
        internal const ushort VisibleCameraYLow = 0x03CD;
        internal const ushort VisibleCameraYHigh = 0x03CE;
        internal const ushort VisibleCameraTileColumn = 0x03CF;
        internal const ushort PendingColumn = 0x03D0;
        internal const ushort PendingRow = 0x03E0;
        internal const ushort VisibleCameraTileRow = 0x03F0;
        internal const byte PayloadIndexScratch = 0xE4;
        internal const byte PointerLow = 0xE8;
        internal const byte PointerHigh = 0xE9;
    }

    internal static class WorldPack
    {
        internal const int CurrentStagingBytes = 338;
        internal const int MaximumStagingBytes = 594;
        internal const ushort StagingStart = 0x0400;
        internal const byte PointerLow = 0xE8;
        internal const byte PointerHigh = 0xE9;
        internal const ushort ValidationState = 0x0326;
        internal const ushort SourceOffset0 = 0x0327;
        internal const ushort SourceOffset1 = 0x0328;
        internal const ushort SourceOffset2 = 0x0329;
        internal const ushort SourceOffset3 = 0x032A;
        internal const ushort ReadValue = 0x032B;
        internal const ushort ValidationCrcLow = 0x032C;
        internal const ushort ValidationCrcHigh = 0x032D;
        internal const ushort ValidationByte = 0x032E;
        internal const ushort ChunkIndexLow = 0x032F;
        internal const ushort ChunkIndexHigh = 0x0330;
        internal const ushort SlotIndex = 0x0331;
        internal const ushort PlaneKind = 0x0332;
        internal const ushort DirectoryEntryOffset0 = 0x0333;
        internal const ushort DirectoryEntryOffset1 = 0x0334;
        internal const ushort DirectoryEntryOffset2 = 0x0335;
        internal const ushort DirectoryEntryOffset3 = 0x0336;
        internal const ushort PlaneOffset0 = 0x0337;
        internal const ushort PlaneOffset1 = 0x0338;
        internal const ushort PlaneOffset2 = 0x0339;
        internal const ushort PlaneOffset3 = 0x033A;
        internal const ushort StoredRemaining = 0x033B;
        internal const ushort DecodedRemaining = 0x033C;
        internal const ushort Codec = 0x033D;
        internal const ushort DestinationLow = 0x033E;
        internal const ushort DestinationHigh = 0x033F;
        internal const ushort WorkLow = 0x0340;
        internal const ushort WorkHigh = 0x0341;
        internal const ushort PacketCount = 0x0342;
        internal const ushort IdBytes = 0x0343;
        internal const ushort TempIdLow = 0x0344;
        internal const ushort TempIdHigh = 0x0345;
        internal const ushort HardwareXLow = 0x0346;
        internal const ushort HardwareXHigh = 0x0347;
        internal const ushort HardwareYLow = 0x0348;
        internal const ushort HardwareYHigh = 0x0349;
        internal const ushort ResultTile = 0x034A;
        internal const ushort ResultMetadata = 0x034B;
        internal const ushort ResultCollision = 0x034C;
        internal const ushort MetatileXLow = 0x034D;
        internal const ushort MetatileXHigh = 0x034E;
        internal const ushort MetatileYLow = 0x034F;
        internal const ushort MetatileYHigh = 0x0350;
        internal const ushort SubcellX = 0x0351;
        internal const ushort SubcellY = 0x0352;
        internal const ushort ChunkXLow = 0x0353;
        internal const ushort ChunkXHigh = 0x0354;
        internal const ushort ChunkYLow = 0x0355;
        internal const ushort ChunkYHigh = 0x0356;
        internal const ushort LocalX = 0x0357;
        internal const ushort LocalY = 0x0358;
        internal const ushort CellIndex = 0x0359;
        internal const ushort SubcellIndexLow = 0x035A;
        internal const ushort SubcellIndexHigh = 0x035B;
        internal const ushort IdLow = 0x035C;
        internal const ushort IdHigh = 0x035D;
        internal const ushort MathValueLow = 0x035E;
        internal const ushort MathValueHigh = 0x035F;
        internal const ushort MathCountLow = 0x0360;
        internal const ushort MathCountHigh = 0x0361;
        internal const ushort MathResultLow = 0x0362;
        internal const ushort MathResultHigh = 0x0363;
        internal const ushort ValidWidth = 0x0364;
        internal const ushort VisualSlot0State = 0x0365;
        internal const ushort VisualSlot1State = 0x0366;
        internal const ushort CollisionSlot0State = 0x0367;
        internal const ushort CollisionSlot1State = 0x0368;
        internal const ushort SelectedStateAddressLow = 0x0369;
        internal const ushort SelectedStateAddressHigh = 0x036A;
        internal const ushort ProbeVisualStatus = 0x036B;
        internal const ushort ProbeCollisionStatus = 0x036C;
        internal const ushort ProbeCompleted = 0x036D;
        internal const ushort VisualCache0Valid = 0x03B8;
        internal const ushort VisualCache0ChunkLow = 0x03B9;
        internal const ushort VisualCache0ChunkHigh = 0x03BA;
        internal const ushort VisualCache1Valid = 0x03BB;
        internal const ushort VisualCache1ChunkLow = 0x03BC;
        internal const ushort VisualCache1ChunkHigh = 0x03BD;
        internal const ushort VisualReplacementNext = 0x03BE;
        internal const ushort VisualDecodeCount = 0x03BF;
        internal const ushort FastVisualCacheTag0 = VisualCache0Valid;
        internal const ushort FastVisualCacheTag1 = VisualCache1Valid;
        internal const ushort FastVisualCacheTag2 = VisualCache0ChunkLow;
        internal const ushort FastVisualCacheTag3 = VisualCache0ChunkHigh;
        internal const ushort FastVisualCacheTag4 = VisualCache1ChunkLow;
        internal const ushort FastVisualCacheTag5 = VisualCache1ChunkHigh;
        internal const ushort BulkReadActive = 0x03C0;
        internal const ushort BulkReadEntryBank = 0x03C1;
        internal const ushort BulkReadCurrentBank = 0x03C2;
        internal const ushort CollisionCache0Valid = 0x03F1;
        internal const ushort CollisionCache0ChunkLow = 0x03F2;
        internal const ushort CollisionCache0ChunkHigh = 0x03F3;
        internal const ushort CollisionCache1Valid = 0x03F4;
        internal const ushort CollisionCache1ChunkLow = 0x03F5;
        internal const ushort CollisionCache1ChunkHigh = 0x03F6;
        internal const ushort CollisionReplacementNext = 0x03F7;
        internal const ushort CollisionDecodeCountLow = 0x03F8;
        internal const ushort CollisionDecodeCountHigh = 0x03F9;
        internal const ushort GameplayTickCount = 0x03FA;
        internal const ushort AudioTickCount = 0x03FB;
        internal const ushort CollisionCellYTag = 0x03FC;
        internal const ushort CollisionCellXLow = 0x03FD;
        internal const ushort CollisionCellXHigh = 0x03FE;
        internal const ushort CollisionCellResult = 0x03FF;
    }

    internal static void Validate()
    {
        for (var index = 0; index < ReservedRanges.Count; index++)
        {
            var range = ReservedRanges[index];
            if (range.Length <= 0)
            {
                throw new InvalidOperationException($"NES runtime range '{range.Name}' must have a positive length.");
            }

            for (var otherIndex = index + 1; otherIndex < ReservedRanges.Count; otherIndex++)
            {
                var other = ReservedRanges[otherIndex];
                if (range.Overlaps(other))
                {
                    throw new InvalidOperationException(
                        $"NES runtime ranges '{range.Name}' and '{other.Name}' overlap at ${Math.Max(range.Start, other.Start):X4}.");
                }
            }
        }

        foreach (var alias in IntentionalRangeAliases)
        {
            if (alias.Canonical.Length != alias.Alias.Length || alias.Canonical.Overlaps(alias.Alias))
            {
                throw new InvalidOperationException($"NES runtime range alias '{alias.Name}' is not a complete disjoint hardware mirror.");
            }
        }

        var duplicateRoles = NamedAddresses
            .GroupBy(address => address.Address)
            .Where(group => group.Count() > 1)
            .ToDictionary(
                group => group.Key,
                group => group.Select(address => $"{address.Domain}.{address.Name}").Order(StringComparer.Ordinal).ToArray());
        var declaredAliases = IntentionalAddressAliases.ToDictionary(alias => alias.Address);
        if (!duplicateRoles.Keys.Order().SequenceEqual(declaredAliases.Keys.Order()))
        {
            throw new InvalidOperationException("NES runtime named-address aliases do not match the duplicate ownership roles.");
        }

        foreach (var duplicate in duplicateRoles)
        {
            if (!duplicate.Value.SequenceEqual(declaredAliases[duplicate.Key].Roles.Order(StringComparer.Ordinal)))
            {
                throw new InvalidOperationException($"NES runtime alias at ${duplicate.Key:X4} does not declare every ownership role.");
            }
        }

        ValidateUserLocalBytes(UserLocals.Length);
        ValidateWorldPackStagingBytes(WorldPack.MaximumStagingBytes);
    }

    internal static NesRamRange ValidateUserLocalBytes(int localBytes)
    {
        if (localBytes < 0 || localBytes > UserLocals.Length)
        {
            throw new InvalidOperationException(
                $"NES user-local storage requires 0..{UserLocals.Length} bytes; {localBytes} were requested.");
        }

        return UserLocals with { Length = localBytes };
    }

    internal static NesRamRange ValidateWorldPackStagingBytes(int stagingBytes)
    {
        if (stagingBytes is < 1 or > WorldPack.MaximumStagingBytes)
        {
            throw new InvalidOperationException(
                $"NES WorldPack staging requires 1..{WorldPack.MaximumStagingBytes} bytes; {stagingBytes} were requested.");
        }

        var requested = WorldPackStaging with { Length = stagingBytes };
        foreach (var reserved in ReservedRanges.Where(range => range != WorldPackStaging))
        {
            if (requested.Overlaps(reserved))
            {
                throw new InvalidOperationException(
                    $"NES {requested.Name} {requested.Start:X4}-{requested.EndInclusive:X4} overlaps {reserved.Name} {reserved.Start:X4}-{reserved.EndInclusive:X4}.");
            }
        }

        return requested;
    }

    private static IReadOnlyList<NesRuntimeMemoryAddress> DescribeNamedAddresses()
    {
        var domains = new (string Name, Type Type)[]
        {
            ("audio", typeof(Audio)),
            ("banking", typeof(Banking)),
            ("camera", typeof(Camera)),
            ("input", typeof(Input)),
            ("packed camera", typeof(PackedCamera)),
            ("runtime", typeof(Runtime)),
            ("sprite", typeof(Sprite)),
            ("WorldPack", typeof(WorldPack)),
        };

        return domains
            .SelectMany(domain => domain.Type
                .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(field => field.IsLiteral &&
                                (field.FieldType == typeof(byte) || field.FieldType == typeof(ushort)))
                .Select(field =>
                {
                    var address = Convert.ToUInt16(field.GetRawConstantValue());
                    var owners = ReservedRanges.Where(range => range.Contains(address)).ToArray();
                    if (owners.Length != 1)
                    {
                        throw new InvalidOperationException(
                            $"NES runtime address {domain.Name}.{field.Name} at {address:X4} must belong to exactly one reserved range.");
                    }

                    return new NesRuntimeMemoryAddress(domain.Name, field.Name, address, owners[0]);
                }))
            .OrderBy(address => address.Address)
            .ThenBy(address => address.Domain, StringComparer.Ordinal)
            .ThenBy(address => address.Name, StringComparer.Ordinal)
            .ToArray();
    }
}
