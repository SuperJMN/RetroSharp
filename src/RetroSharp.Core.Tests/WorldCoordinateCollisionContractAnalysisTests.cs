namespace RetroSharp.Core.Tests;

using RetroSharp.Core.Sdk;
using Xunit;

public sealed class WorldCoordinateCollisionContractAnalysisTests
{
    [Fact]
    public void Full_stage1_coordinate_and_collision_layout_is_reproducible()
    {
        Assert.Equal(new byte[] { 0x38, 0x01 }, WordBytes(312));
        Assert.Equal(new byte[] { 0x28, 0x00 }, WordBytes(40));
        Assert.Equal(new byte[] { 0xC0, 0x09 }, WordBytes(2_496));
        Assert.Equal(new byte[] { 0x40, 0x01 }, WordBytes(320));
        Assert.Equal(new byte[] { 0xFF, 0x00 }, WordBytes(255));
        Assert.Equal(new byte[] { 0x00, 0x01 }, WordBytes(256));
        Assert.Equal(new byte[] { 0x30, 0x01 }, WordBytes(304));
        Assert.Equal(new byte[] { 0xF8, 0x7F }, WordBytes(32_760));
        Assert.Equal(new byte[] { 0xFF, 0xFF }, WordBytes(-1));
        Assert.Equal(new byte[] { 0xF8, 0x00 }, WordBytes(248));
        Assert.Equal(new byte[] { 0xFF, 0x00 }, WordBytes(255));
        Assert.Equal(304, SignedWord(WordBytes(304)));
        Assert.Equal(-1, SignedWord(WordBytes(-1)));
        Assert.Equal(255, SignedWord(WordBytes(255)));
        Assert.False(WordBytes(255).SequenceEqual(WordBytes(-1)));
        Assert.Equal(4_096, 32_768 / 8);
        Assert.Equal(32_760, (4_096 - 1) * 8);
        Assert.True(4_097 * 8 > 32_768);
        Assert.True(ushort.MaxValue > 4_096);
        Assert.NotEqual(255, 248 & ~7);

        var column255 = AddressHardwareCell(255, metatileSize: 2);
        var column256 = AddressHardwareCell(256, metatileSize: 2);
        Assert.Equal(new CellAddress(Metatile: 127, Subcell: 1, Chunk: 15, Local: 7), column255);
        Assert.Equal(new CellAddress(Metatile: 128, Subcell: 0, Chunk: 16, Local: 0), column256);
        Assert.Equal(new[] { column255, column256 }, new[]
        {
            AddressHardwareCell(255, 2),
            AddressHardwareCell(256, 2),
        });
        Assert.Equal(new[] { column256, column255 }, new[]
        {
            AddressHardwareCell(256, 2),
            AddressHardwareCell(255, 2),
        });

        Assert.Equal(256, GameBoyRightEdge(beforeCameraPixel: 1_887));
        Assert.Equal(256, GameBoyLeftEdge(beforeCameraPixel: 2_056));
        Assert.Equal(256, NesRightEdge(afterCameraPixel: 1_792));
        Assert.Equal(256, NesLeftEdge(beforeCameraPixel: 2_056));
        Assert.Equal(new byte[] { 0x00, 0x01 }, WordBytes(GameBoyRightEdge(1_887)));
        Assert.Equal(new byte[] { 0x00, 0x01 }, WordBytes(GameBoyLeftEdge(2_056)));
        Assert.Equal(new byte[] { 0x00, 0x01 }, WordBytes(NesRightEdge(1_792)));
        Assert.Equal(new byte[] { 0x00, 0x01 }, WordBytes(NesLeftEdge(2_056)));
        Assert.Equal(256, CollisionHardwareColumn(cameraPixel: 1_920, screenPixel: 128));

        const int footWorldY = 304;
        const int landingSearchTopOffset = 4;
        var queryBase = footWorldY - landingSearchTopOffset;
        var sampleOffsets = AabbSampleOffsets(size: 12);
        Assert.Equal(new[] { 0, 8, 11 }, sampleOffsets);

        var matchingProbe = queryBase + sampleOffsets[1];
        var hardwareRow = matchingProbe >> 3;
        var hitTop = matchingProbe & ~7;
        var floorAddress = AddressHardwareCell(hardwareRow, metatileSize: 2);
        Assert.Equal(38, hardwareRow);
        Assert.Equal(new CellAddress(Metatile: 19, Subcell: 0, Chunk: 2, Local: 3), floorAddress);
        Assert.Equal(304, hitTop);
        Assert.False(WordBytes(-1).SequenceEqual(WordBytes(hitTop)));
        Assert.Equal(273, hitTop - 31);
        Assert.Equal(128, hitTop - 176);
        Assert.Equal(224, hitTop - 80);

        const int wordBytes = 2;
        var cameraBytes = 2 * wordBytes;
        var requestedAxisBytes = wordBytes;
        var twoEdgeTagBytes = 2 * wordBytes;
        Assert.Equal(10, cameraBytes + requestedAxisBytes + twoEdgeTagBytes);
        Assert.Equal(6, 10 - cameraBytes);
        Assert.Equal(8, 10 - 2);
        Assert.Equal(2, wordBytes);

        var screenHitTopDescriptor = TargetIntrinsicDescriptor.CameraScreenAabbHitTop(
            "camera_screen_aabb_hit_top",
            runtimeArity: 4,
            compileTimeOperands: []);
        Assert.Equal(TargetIntrinsicReturnKind.I16, screenHitTopDescriptor.ReturnKind);

        Assert.Equal(9, GameBoyAlignedHitSketch.Length);
        Assert.Equal(88, TotalCycles(GameBoyAlignedHitSketch));
        Assert.Equal(5, GameBoyNoHitSketch.Length);
        Assert.Equal(52, TotalCycles(GameBoyNoHitSketch));
        Assert.Equal(5, NesAlignedHitSketch.Length);
        Assert.Equal(14, TotalCycles(NesAlignedHitSketch));
        Assert.Equal(4, NesNoHitSketch.Length);
        Assert.Equal(10, TotalCycles(NesNoHitSketch));

        var adr = File.ReadAllText(RepositoryFile("docs/WorldCoordinateCollisionContract.md"));
        Assert.Contains("Status: **accepted for LW-0.4 on 2026-07-10.**", adr, StringComparison.Ordinal);
        Assert.Contains("0xFFFF", adr, StringComparison.Ordinal);
        Assert.Contains("1887 -> 1888", adr, StringComparison.Ordinal);
        Assert.Contains("2056 -> 2055", adr, StringComparison.Ordinal);
        Assert.Contains("1791 -> 1792", adr, StringComparison.Ordinal);
        Assert.Contains("result bytes     = 30 01", adr, StringComparison.Ordinal);
        Assert.Contains("`HL` on Game Boy", adr, StringComparison.Ordinal);
        Assert.Contains("`A:X` on NES", adr, StringComparison.Ordinal);
        Assert.Contains("0x0000..0x00F8", adr, StringComparison.Ordinal);
        Assert.Contains("0x00FF", adr, StringComparison.Ordinal);
        Assert.Contains("pixel extent exceeds 32768", adr, StringComparison.Ordinal);
        Assert.Contains("GB already supplies the four camera bytes", adr, StringComparison.Ordinal);
        Assert.Contains("NES supplies the two low camera bytes", adr, StringComparison.Ordinal);
    }

    private static byte[] WordBytes(int value)
    {
        var bits = unchecked((ushort)value);
        return [(byte)bits, (byte)(bits >> 8)];
    }

    private static int SignedWord(IReadOnlyList<byte> bytes)
    {
        var bits = (ushort)(bytes[0] | (bytes[1] << 8));
        return unchecked((short)bits);
    }

    private static CellAddress AddressHardwareCell(int hardwareCell, int metatileSize)
    {
        var metatile = hardwareCell / metatileSize;
        return new CellAddress(
            Metatile: metatile,
            Subcell: hardwareCell % metatileSize,
            Chunk: metatile / 8,
            Local: metatile % 8);
    }

    private static int GameBoyRightEdge(int beforeCameraPixel) => beforeCameraPixel / 8 + 21;

    private static int NesRightEdge(int afterCameraPixel) => afterCameraPixel / 8 + 32;

    private static int GameBoyLeftEdge(int beforeCameraPixel) => beforeCameraPixel / 8 - 1;

    private static int NesLeftEdge(int beforeCameraPixel) => beforeCameraPixel / 8 - 1;

    private static int CollisionHardwareColumn(int cameraPixel, int screenPixel) =>
        (cameraPixel + screenPixel) >> 3;

    private static int[] AabbSampleOffsets(int size)
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

        return offsets.ToArray();
    }

    private static int TotalCycles(IEnumerable<InstructionCost> sketch) => sketch.Sum(instruction => instruction.Cycles);

    private static readonly InstructionCost[] GameBoyAlignedHitSketch =
    [
        new("LD A,[worldYLo]", 16),
        new("AND $F8", 8),
        new("LD L,A", 4),
        new("LD A,[worldYHi]", 16),
        new("LD H,A", 4),
        new("LD A,L", 4),
        new("LD [destinationLo],A", 16),
        new("LD A,H", 4),
        new("LD [destinationHi],A", 16),
    ];

    private static readonly InstructionCost[] GameBoyNoHitSketch =
    [
        new("LD HL,$FFFF", 12),
        new("LD A,L", 4),
        new("LD [destinationLo],A", 16),
        new("LD A,H", 4),
        new("LD [destinationHi],A", 16),
    ];

    private static readonly InstructionCost[] NesAlignedHitSketch =
    [
        new("LDA worldYLo", 3),
        new("AND #$F8", 2),
        new("LDX worldYHi", 3),
        new("STA destinationLo", 3),
        new("STX destinationHi", 3),
    ];

    private static readonly InstructionCost[] NesNoHitSketch =
    [
        new("LDA #$FF", 2),
        new("TAX", 2),
        new("STA destinationLo", 3),
        new("STX destinationHi", 3),
    ];

    private readonly record struct CellAddress(int Metatile, int Subcell, int Chunk, int Local);

    private readonly record struct InstructionCost(string Instruction, int Cycles);

    private static string RepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not find repository file '{relativePath}'.");
    }
}
