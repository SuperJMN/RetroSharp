namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.GameBoy;
using RetroSharp.Sdk;
using Xunit;

public partial class GameBoyRomCompilerTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(240)]
    [Trait("RetroSharp.TestOwnership", "FocusedLowering")]
    public void Actor_spawn_rom_lookup_has_constant_game_boy_shape_and_exact_columns(int recordCount)
    {
        var baseDirectory = WriteActorSpawnMap(ScalingSpawnObjects(recordCount));
        var source = ScalingSpawnSource();
        var program = RetroSharp.GameBoy.GameBoyRomCompiler.PrepareVideoProgram(
            source,
            baseDirectory,
            SdkLibraryImportMode.ExplicitOnly,
            sdkLibraryRegistry: null,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            sdkPluginRegistry: null);
        AssertSpawnColumns(program.GeneratedRomTables, recordCount);

        var emittedSource = EmittedTableSource(ExpectedSpawnColumns(recordCount), includeReturn: false);
        var emittedProgram = RetroSharp.GameBoy.GameBoyRomCompiler.PrepareVideoProgram(
            emittedSource,
            baseDirectory: null,
            SdkLibraryImportMode.ExplicitOnly,
            sdkLibraryRegistry: null,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            sdkPluginRegistry: null);
        var build = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            emittedSource,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);

        Assert.Equal("gb-rom-only-current", build.Report.SelectedProfile);
        foreach (var table in emittedProgram.GeneratedRomTables.Values)
        {
            var segment = Assert.Single(
                build.Report.Segments,
                candidate => candidate.Owner == $"generated-rom-table:{table.FunctionName}");
            Assert.Equal(table.Data, build.Rom.AsSpan(segment.PhysicalStart, table.Data.Length).ToArray());

            var shape = new byte?[]
            {
                0xFA, null, null, // LD A,(index)
                0x5F,             // LD E,A
                0x16, 0x00,       // LD D,0
                0x21, (byte)segment.CpuAddress, (byte)(segment.CpuAddress >> 8), // LD HL,table
                0x19,             // ADD HL,DE
                0x7E,             // LD A,(HL)
            };
            Assert.Equal(1, CountMaskedSequences(build.Rom, shape));
        }

        Assert.Equal(56, 16 + 4 + 8 + 12 + 8 + 8);
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "FocusedLowering")]
    public void Rom_tables_record_the_game_boy_wide_spawn_payload_delta()
    {
        const int recordCount = 16;
        var columns = ExpectedSpawnColumns(recordCount)
            .Where(column => column.Key is "x" or "xHi")
            .ToDictionary(column => column.Key, column => column.Value, StringComparer.Ordinal);
        var table = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            EmittedTableSource(columns, includeReturn: false),
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var ladder = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            EmittedLadderSource(columns, includeReturn: false),
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var tablePayload = table.Report.Segments.Sum(segment => segment.Length);
        var ladderPayload = ladder.Report.Segments.Sum(segment => segment.Length);

        Assert.Equal(1_646, tablePayload);
        Assert.Equal(1_986, ladderPayload);
        Assert.Equal(60, 16 + 8 + 12 + 8 + 16);
        Assert.Equal(608, (recordCount - 1) * (16 + 8 + 16) + 8);
        Assert.Equal(552, 608 - 56);
        Assert.True(tablePayload < ladderPayload);
    }

    private static void AssertSpawnColumns(
        IReadOnlyDictionary<string, RetroSharp.Sdk.CompilerGeneratedRomTable> tables,
        int recordCount)
    {
        Assert.Equal(13, tables.Count);
        foreach (var (field, expected) in ExpectedSpawnColumns(recordCount))
        {
            var table = tables[$"__enemies_spawn_0_{field}"];
            Assert.Equal(expected, table.Data);
        }
    }

    private static IReadOnlyDictionary<string, byte[]> ExpectedSpawnColumns(int recordCount)
    {
        var columns = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["kind"] = new byte[recordCount],
            ["x"] = new byte[recordCount],
            ["xHi"] = new byte[recordCount],
            ["y"] = new byte[recordCount],
            ["yHi"] = new byte[recordCount],
            ["active"] = new byte[recordCount],
            ["vx"] = new byte[recordCount],
            ["vy"] = new byte[recordCount],
            ["state"] = new byte[recordCount],
            ["timer"] = new byte[recordCount],
            ["facing"] = new byte[recordCount],
            ["animTick"] = new byte[recordCount],
            ["health"] = new byte[recordCount],
        };
        for (var index = 0; index < recordCount; index++)
        {
            var x = index * 257;
            var y = index * 257;
            columns["kind"][index] = (byte)(index % 2 + 1);
            columns["x"][index] = (byte)x;
            columns["xHi"][index] = (byte)(x >> 8);
            columns["y"][index] = (byte)y;
            columns["yHi"][index] = (byte)(y >> 8);
            for (var field = 0; field < SpawnInitialFields.Length; field++)
            {
                columns[SpawnInitialFields[field]][index] = SpawnInitialValue(index, field);
            }
        }

        return columns;
    }

    private static string ScalingSpawnObjects(int recordCount) =>
        "[" + string.Join(",", Enumerable.Range(0, recordCount).Select(index =>
        {
            var properties = string.Join(",", SpawnInitialFields.Select((field, fieldIndex) =>
                $"{{\"name\":\"{field}\",\"type\":\"int\",\"value\":{SpawnInitialValue(index, fieldIndex)}}}"));
            return $"{{\"id\":{index + 1},\"type\":\"{(index % 2 == 0 ? "Goomba" : "Bat")}\",\"x\":{index * 257},\"y\":{index * 257},\"properties\":[{properties}]}}";
        })) + "]";

    private static string EmittedTableSource(IReadOnlyDictionary<string, byte[]> columns, bool includeReturn)
    {
        var functions = string.Join(Environment.NewLine, columns.Select(column =>
            $"inline [compiler_generated_rom_table({string.Join(", ", column.Value)})] u8 table_{column.Key}(u8 index) => 0;"));
        var reads = string.Join(Environment.NewLine, columns.Keys.Select((field, index) =>
            $"u8 result{index} = table_{field}(index);"));
        return $$"""
                 {{functions}}

                 void Main() {
                     u8 index = 0;
                     {{reads}}
                     {{(includeReturn ? "return;" : string.Empty)}}
                 }
                 """;
    }

    private static string EmittedLadderSource(IReadOnlyDictionary<string, byte[]> columns, bool includeReturn)
    {
        var functions = string.Join(Environment.NewLine, columns.Select(column =>
        {
            var expression = column.Value[^1].ToString();
            for (var index = column.Value.Length - 2; index >= 0; index--)
            {
                expression = $"index == {index} ? {column.Value[index]} : {expression}";
            }

            return $"inline u8 table_{column.Key}(u8 index) => {expression};";
        }));
        var reads = string.Join(Environment.NewLine, columns.Keys.Select((field, index) =>
            $"u8 result{index} = table_{field}(index);"));
        return $$"""
                 {{functions}}

                 void Main() {
                     u8 index = 0;
                     {{reads}}
                     {{(includeReturn ? "return;" : string.Empty)}}
                 }
                 """;
    }

    private static string ScalingSpawnSource() =>
        """
        void Main() {
            Actors.Pool(enemies, 1);
            Enemies.Def(Goomba, behavior: Walker);
            Enemies.Def(Bat, behavior: Flyer);
            Actors.SpawnWindow(enemies, "level.tmj", "actors", 0, 1);
        }
        """;

    private static readonly string[] SpawnInitialFields =
        ["active", "vx", "vy", "state", "timer", "facing", "animTick", "health"];

    private static byte SpawnInitialValue(int index, int fieldIndex) =>
        (byte)((index + fieldIndex + 1) % 255);

    private static int CountMaskedSequences(byte[] bytes, IReadOnlyList<byte?> pattern)
    {
        var count = 0;
        for (var offset = 0; offset <= bytes.Length - pattern.Count; offset++)
        {
            if (pattern.Select((value, index) => !value.HasValue || value.Value == bytes[offset + index]).All(matches => matches))
            {
                count++;
            }
        }

        return count;
    }
}
