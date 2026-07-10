using RetroSharp.Core.Sdk;

namespace RetroSharp.Cli;

public sealed record WorldBudgetUsage(
    int AddressingPixels,
    int RomPrgBytes,
    int ChrTileCount,
    int ResidentChrBytes,
    int StagingRamBytes,
    int VBlankTileWrites,
    int VBlankAttributeWrites);

public sealed record WorldBudgetLimits(
    string Profile,
    int AddressingPixels,
    int RomPrgBytes,
    int ChrTileCount,
    int ResidentChrBytes,
    int StagingRamBytes,
    int VBlankTileWrites,
    int VBlankAttributeWrites)
{
    public string? AddressingProfile { get; init; }

    public string? RomPrgProfile { get; init; }

    public string? ChrTileProfile { get; init; }

    public string? StagingRamProfile { get; init; }

    public string? VblankProfile { get; init; }
}

public sealed record WorldBudgetDiagnostic(
    string Category,
    string Status,
    IReadOnlyDictionary<string, int> Usage,
    IReadOnlyDictionary<string, int> Limit,
    string Profile,
    string Remedy);

public static class WorldBudgetProfileValidator
{
    public const int MaxLogicalExtentPixels = short.MaxValue + 1;

    public static IReadOnlyList<WorldBudgetDiagnostic> Validate(
        WorldBudgetUsage usage,
        WorldBudgetLimits limits)
    {
        ArgumentNullException.ThrowIfNull(usage);
        ArgumentNullException.ThrowIfNull(limits);

        return
        [
            Diagnostic(
                "addressing",
                Values(("extentPixels", usage.AddressingPixels)),
                Values(("extentPixels", limits.AddressingPixels)),
                usage.AddressingPixels > limits.AddressingPixels,
                limits.AddressingProfile ?? limits.Profile,
                "Reduce the world extent so its last coordinate is at most 32767, or split the authored world; never truncate logical coordinates to hardware scroll bytes."),
            Diagnostic(
                "rom-prg",
                Values(("bytes", usage.RomPrgBytes)),
                Values(("bytes", limits.RomPrgBytes)),
                usage.RomPrgBytes > limits.RomPrgBytes,
                limits.RomPrgProfile ?? limits.Profile,
                "Reduce packed ROM/PRG data or implement an accepted explicit banked profile; this report does not select a banker or mapper."),
            Diagnostic(
                "chr-tile-count",
                Values(
                    ("tileIndexes", usage.ChrTileCount),
                    ("residentChrBytes", usage.ResidentChrBytes)),
                Values(
                    ("tileIndexes", limits.ChrTileCount),
                    ("residentChrBytes", limits.ResidentChrBytes)),
                usage.ChrTileCount > limits.ChrTileCount ||
                usage.ResidentChrBytes > limits.ResidentChrBytes,
                limits.ChrTileProfile ?? limits.Profile,
                "Reduce generated patterns or implement explicit art residency; keep physical CHR, resident CHR, and tile-index limits separate."),
            Diagnostic(
                "staging-ram",
                Values(("bytes", usage.StagingRamBytes)),
                Values(("bytes", limits.StagingRamBytes)),
                usage.StagingRamBytes > limits.StagingRamBytes,
                limits.StagingRamProfile ?? limits.Profile,
                "Reduce the accepted staging shape or revise the WorldPack staging contract before implementing a runtime reader."),
            Diagnostic(
                "vblank",
                Values(
                    ("tileWrites", usage.VBlankTileWrites),
                    ("attributeWrites", usage.VBlankAttributeWrites)),
                Values(
                    ("tileWrites", limits.VBlankTileWrites),
                    ("attributeWrites", limits.VBlankAttributeWrites)),
                usage.VBlankTileWrites > limits.VBlankTileWrites ||
                usage.VBlankAttributeWrites > limits.VBlankAttributeWrites,
                limits.VblankProfile ?? limits.Profile,
                "Stage and decode outside VBlank, then split tile and attribute commits across bounded frame phases."),
        ];
    }

    private static WorldBudgetDiagnostic Diagnostic(
        string category,
        IReadOnlyDictionary<string, int> usage,
        IReadOnlyDictionary<string, int> limit,
        bool overflow,
        string profile,
        string remedy) =>
        new(category, overflow ? "overflow" : "ok", usage, limit, profile, overflow ? remedy : "No action required.");

    private static IReadOnlyDictionary<string, int> Values(params (string Name, int Value)[] values)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (name, value) in values)
        {
            result.Add(name, value);
        }

        return result;
    }
}

public sealed record WorldBudgetWorld(
    int SourceWidth,
    int SourceHeight,
    int HardwareWidth,
    int HardwareHeight,
    int HardwareCells,
    int PixelWidth,
    int PixelHeight,
    int MetatileWidth,
    int MetatileHeight,
    int MetatilePlacements,
    int UniqueVisualMetatiles,
    int CollisionProfiles,
    int Chunks);

public sealed record WorldBudgetPack(int VisualStoredBytes, int CollisionStoredBytes, int TotalBytes);

public sealed record WorldBudgetTargetTiles(
    int ReservedTiles,
    int GeneratedBackgroundTiles,
    int GeneratedBackgroundBytes,
    int TileIndexUsed,
    int TileIndexLimit,
    int? CurrentPhysicalChrCapacityBytes,
    int? AcceptedFuturePhysicalChrCapacityBytes,
    int ResidentChrBytesUsed,
    int ResidentChrByteLimit);

public sealed record WorldBudgetStagingRam(
    int VisualChunkSlotsBytes,
    int CollisionChunkSlotsBytes,
    int EdgeSlotsBytes,
    int UsedBytes,
    int LimitBytes,
    int PhysicalRamCapacityBytes,
    string Profile);

public sealed record WorldBudgetCartridge(
    int RomPrgBytesUsed,
    int RomPrgByteLimit,
    int BankBytes,
    int RequiredBanks,
    string EvaluatedProfile,
    int? AllocatedRomBytes);

public sealed record WorldBudgetVBlank(
    int TileWritesUsed,
    int TileWriteLimit,
    int AttributeWritesUsed,
    int AttributeWriteLimit,
    string CommitShape);

public sealed record WorldBudgetProfileRequirement(
    string Name,
    bool ImplementedByCurrentCompiler,
    int RomPrgAllocationBytes,
    int RomPrgByteLimit,
    int BankBytes,
    int RequiredBanks,
    int? PhysicalChrCapacityBytes,
    int? ResidentChrByteLimit);

public sealed record WorldBudgetReport(
    string Schema,
    string Target,
    WorldBudgetWorld World,
    WorldBudgetPack Pack,
    WorldBudgetTargetTiles TargetTiles,
    WorldBudgetStagingRam StagingRam,
    WorldBudgetCartridge Cartridge,
    WorldBudgetVBlank Vblank,
    IReadOnlyList<string> AcceptedProfiles,
    string? SelectedProfile,
    IReadOnlyList<WorldBudgetProfileRequirement> ProfileRequirements,
    IReadOnlyList<WorldBudgetDiagnostic> Diagnostics);

public static class WorldBudgetReportFactory
{
    private const int PatternBytes = 16;

    public static WorldBudgetReport Create(string target, string mapPath)
    {
        return target switch
        {
            "gb" or "gameboy" => FromGameBoy(
                RetroSharp.GameBoy.GameBoyWorldPackInspector.Inspect(mapPath)),
            "nes" => FromNes(RetroSharp.NES.NesWorldPackInspector.Inspect(mapPath)),
            _ => throw new ArgumentException(
                $"--world-budget-report supports targets gb and nes; received '{target}'."),
        };
    }

    private static WorldBudgetReport FromGameBoy(RetroSharp.GameBoy.GameBoyWorldPackInspection inspection)
    {
        const int stagingLimit = 554;
        const int physicalRamCapacity = 8 * 1024;
        const int bankBytes = 16 * 1024;
        const int romLimit = 32 * bankBytes;
        var capabilities = RetroSharp.GameBoy.GameBoyTarget.Capabilities;
        var edgeSlotsBytes = checked(2 * (
            capabilities.MaxBackgroundTileWritesPerFrame + capabilities.MaxAttributeWritesPerFrame));
        var tileIndexUsed = checked(inspection.FirstGeneratedTileId + inspection.GeneratedBackgroundTiles);
        var romBytes = checked(inspection.SerializedBytes + inspection.GeneratedBackgroundBytes);
        return Create(
            "gb",
            inspection.Pack,
            inspection.SerializedBytes,
            inspection.GeneratedBackgroundTiles,
            inspection.GeneratedBackgroundBytes,
            tileIndexUsed,
            currentPhysicalChrCapacityBytes: null,
            acceptedFuturePhysicalChrCapacityBytes: null,
            residentChrByteLimit: 256 * PatternBytes,
            edgeSlotsBytes,
            stagingLimit,
            physicalRamCapacity,
            stagingProfile: "worldpack-v1-staging-maximum",
            romBytes,
            romLimit,
            bankBytes,
            evaluatedProfile: "gb-simple-mbc1-current",
            tileWrites: capabilities.MaxBackgroundTileWritesPerFrame,
            attributeWrites: capabilities.MaxAttributeWritesPerFrame,
            commitShape: "one 21-tile edge; two same-axis GB edges use both accepted peer slots",
            ["gb-rom-only-current", "gb-simple-mbc1-current"]);
    }

    private static WorldBudgetReport FromNes(RetroSharp.NES.NesWorldPackInspection inspection)
    {
        const int stagingLimit = 594;
        const int physicalRamCapacity = 2 * 1024;
        const int prgLimit = 32 * 1024 - 6;
        const int bankBytes = 32 * 1024;
        var capabilities = RetroSharp.NES.NesTarget.Capabilities;
        var edgeSlotsBytes = checked(2 * (
            capabilities.MaxBackgroundTileWritesPerFrame + capabilities.MaxAttributeWritesPerFrame));
        var tileIndexUsed = checked(inspection.FirstGeneratedTileId + inspection.GeneratedBackgroundTiles);
        return Create(
            "nes",
            inspection.Pack,
            inspection.SerializedBytes,
            inspection.GeneratedBackgroundTiles,
            inspection.GeneratedBackgroundBytes,
            tileIndexUsed,
            currentPhysicalChrCapacityBytes: 8 * 1024,
            acceptedFuturePhysicalChrCapacityBytes: 16 * 1024,
            residentChrByteLimit: 8 * 1024,
            edgeSlotsBytes,
            stagingLimit,
            physicalRamCapacity,
            stagingProfile: "worldpack-v1-staging-maximum",
            romBytes: inspection.SerializedBytes,
            romLimit: prgLimit,
            bankBytes,
            evaluatedProfile: "nes-mapper-0-current",
            tileWrites: capabilities.MaxBackgroundTileWritesPerFrame,
            attributeWrites: capabilities.MaxAttributeWritesPerFrame,
            commitShape: "32 tile writes or one of four 8-tile row phases, followed by a 9-attribute phase",
            ["nes-mapper-0-current", "nes-mmc3-tvrom-v1-accepted-future"]);
    }

    private static WorldBudgetReport Create(
        string target,
        WorldPack pack,
        int serializedBytes,
        int generatedBackgroundTiles,
        int generatedBackgroundBytes,
        int tileIndexUsed,
        int? currentPhysicalChrCapacityBytes,
        int? acceptedFuturePhysicalChrCapacityBytes,
        int residentChrByteLimit,
        int edgeSlotsBytes,
        int stagingLimit,
        int physicalRamCapacity,
        string stagingProfile,
        int romBytes,
        int romLimit,
        int bankBytes,
        string evaluatedProfile,
        int tileWrites,
        int attributeWrites,
        string commitShape,
        IReadOnlyList<string> acceptedProfiles)
    {
        var descriptor = pack.Descriptor;
        var visualStoredBytes = pack.Chunks.Sum(chunk => (int)chunk.Directory.VisualStoredBytes);
        var collisionStoredBytes = pack.Chunks.Sum(chunk => (int)chunk.Directory.CollisionStoredBytes);
        var visualChunkSlotsBytes = checked(2 * descriptor.ChunkWidth * descriptor.ChunkHeight * descriptor.VisualIdBytes);
        var collisionChunkSlotsBytes = checked(2 * descriptor.ChunkWidth * descriptor.ChunkHeight * descriptor.CollisionIdBytes);
        var stagingBytes = checked(visualChunkSlotsBytes + collisionChunkSlotsBytes + edgeSlotsBytes);
        var pixelWidth = checked(descriptor.HardwareWidth * 8);
        var pixelHeight = checked(descriptor.HardwareHeight * 8);
        var usage = new WorldBudgetUsage(
            Math.Max(pixelWidth, pixelHeight),
            romBytes,
            tileIndexUsed,
            checked(tileIndexUsed * PatternBytes),
            stagingBytes,
            tileWrites,
            attributeWrites);
        var limits = new WorldBudgetLimits(
            evaluatedProfile,
            WorldBudgetProfileValidator.MaxLogicalExtentPixels,
            romLimit,
            256,
            residentChrByteLimit,
            stagingLimit,
            tileWrites,
            attributeWrites)
        {
            AddressingProfile = "world-coordinate-i16-v1",
            RomPrgProfile = evaluatedProfile,
            ChrTileProfile = $"{target}-tile-index-and-resident-chr",
            StagingRamProfile = "worldpack-v1-staging-maximum",
            VblankProfile = $"{target}-vblank-capabilities",
        };

        return new WorldBudgetReport(
            "retrosharp.world-budget/v1",
            target,
            new WorldBudgetWorld(
                pack.SourceWidth,
                pack.SourceHeight,
                descriptor.HardwareWidth,
                descriptor.HardwareHeight,
                checked(descriptor.HardwareWidth * descriptor.HardwareHeight),
                pixelWidth,
                pixelHeight,
                descriptor.MetatileWidth,
                descriptor.MetatileHeight,
                checked(pack.SourceWidth * pack.SourceHeight),
                descriptor.VisualMetatileCount,
                descriptor.CollisionProfileCount,
                pack.Chunks.Count),
            new WorldBudgetPack(visualStoredBytes, collisionStoredBytes, serializedBytes),
            new WorldBudgetTargetTiles(
                tileIndexUsed - generatedBackgroundTiles,
                generatedBackgroundTiles,
                generatedBackgroundBytes,
                tileIndexUsed,
                256,
                currentPhysicalChrCapacityBytes,
                acceptedFuturePhysicalChrCapacityBytes,
                tileIndexUsed * PatternBytes,
                residentChrByteLimit),
            new WorldBudgetStagingRam(
                visualChunkSlotsBytes,
                collisionChunkSlotsBytes,
                edgeSlotsBytes,
                stagingBytes,
                stagingLimit,
                physicalRamCapacity,
                stagingProfile),
            new WorldBudgetCartridge(
                romBytes,
                romLimit,
                bankBytes,
                CeilingDivide(romBytes, bankBytes),
                evaluatedProfile,
                AllocatedRomBytes: null),
            new WorldBudgetVBlank(tileWrites, tileWrites, attributeWrites, attributeWrites, commitShape),
            acceptedProfiles,
            SelectedProfile: null,
            ProfileRequirements(target, romBytes),
            WorldBudgetProfileValidator.Validate(usage, limits));
    }

    private static IReadOnlyList<WorldBudgetProfileRequirement> ProfileRequirements(string target, int romBytes)
    {
        if (target == "gb")
        {
            return
            [
                new WorldBudgetProfileRequirement(
                    "gb-rom-only-current",
                    ImplementedByCurrentCompiler: true,
                    RomPrgAllocationBytes: 32 * 1024,
                    RomPrgByteLimit: 32 * 1024 - 0x150,
                    BankBytes: 32 * 1024,
                    RequiredBanks: CeilingDivide(romBytes, 32 * 1024),
                    PhysicalChrCapacityBytes: null,
                    ResidentChrByteLimit: 256 * PatternBytes),
                new WorldBudgetProfileRequirement(
                    "gb-simple-mbc1-current",
                    ImplementedByCurrentCompiler: true,
                    RomPrgAllocationBytes: 32 * 16 * 1024,
                    RomPrgByteLimit: 32 * 16 * 1024,
                    BankBytes: 16 * 1024,
                    RequiredBanks: CeilingDivide(romBytes, 16 * 1024),
                    PhysicalChrCapacityBytes: null,
                    ResidentChrByteLimit: 256 * PatternBytes),
            ];
        }

        return
        [
            new WorldBudgetProfileRequirement(
                "nes-mapper-0-current",
                ImplementedByCurrentCompiler: true,
                RomPrgAllocationBytes: 32 * 1024,
                RomPrgByteLimit: 32 * 1024 - 6,
                BankBytes: 32 * 1024,
                RequiredBanks: CeilingDivide(romBytes, 32 * 1024),
                PhysicalChrCapacityBytes: 8 * 1024,
                ResidentChrByteLimit: 8 * 1024),
            new WorldBudgetProfileRequirement(
                "nes-mmc3-tvrom-v1-accepted-future",
                ImplementedByCurrentCompiler: false,
                RomPrgAllocationBytes: 64 * 1024,
                RomPrgByteLimit: 64 * 1024,
                BankBytes: 8 * 1024,
                RequiredBanks: CeilingDivide(romBytes, 8 * 1024),
                PhysicalChrCapacityBytes: 16 * 1024,
                ResidentChrByteLimit: 8 * 1024),
        ];
    }

    private static int CeilingDivide(int value, int divisor) => checked((value + divisor - 1) / divisor);
}
