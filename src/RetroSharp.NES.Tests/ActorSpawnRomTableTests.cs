namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.NES;
using RetroSharp.Sdk;
using Xunit;

public partial class NesRomCompilerTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(240)]
    [Trait("RetroSharp.TestOwnership", "FocusedLowering")]
    public void Actor_spawn_rom_lookup_has_constant_nes_shape_and_exact_columns(int recordCount)
    {
        var baseDirectory = WriteActorSpawnMap(ScalingSpawnObjects(recordCount));
        var source = ScalingSpawnSource();
        var prepared = RetroSharp.NES.NesRomCompiler.PrepareVideoProgram(
            source,
            baseDirectory,
            SdkLibraryImportMode.ExplicitOnly,
            sdkLibraryRegistry: null,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            sdkPluginRegistry: null);
        AssertSpawnColumns(prepared.VideoProgram.GeneratedRomTables, recordCount);

        var emittedSource = EmittedTableSource(ExpectedSpawnColumns(recordCount), includeReturn: true);
        var emittedProgram = RetroSharp.NES.NesRomCompiler.PrepareVideoProgram(
            emittedSource,
            baseDirectory: null,
            SdkLibraryImportMode.ExplicitOnly,
            sdkLibraryRegistry: null,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            sdkPluginRegistry: null);
        var build = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            emittedSource,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);

        Assert.Equal("nes-mapper-0-current", build.Report.SelectedProfile);
        foreach (var table in emittedProgram.VideoProgram.GeneratedRomTables.Values)
        {
            var address = build.Report.FixedSymbols[table.Label];
            var physicalOffset = 16 + address - 0x8000;
            Assert.Equal(table.Data, build.Rom.AsSpan(physicalOffset, table.Data.Length).ToArray());

            var shape = new byte?[]
            {
                0xA5, null, // LDA index
                0xAA,       // TAX
                0xBD, (byte)address, (byte)(address >> 8), // LDA table,X
            };
            Assert.Equal(1, CountMaskedSequences(build.Rom, shape));
        }

        Assert.Equal(9, 3 + 2 + 4);
        Assert.Equal(10, 3 + 2 + 5);
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "FocusedLowering")]
    public void Rom_tables_record_the_nes_wide_spawn_payload_delta()
    {
        const int recordCount = 16;
        var columns = ExpectedSpawnColumns(recordCount)
            .Where(column => column.Key is "x" or "xHi")
            .ToDictionary(column => column.Key, column => column.Value, StringComparer.Ordinal);
        var table = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            EmittedTableSource(columns, includeReturn: true),
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var ladder = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            EmittedLadderSource(columns, includeReturn: true),
            sdkLibraryImports: [SdkImportResolver.Portable2D]);

        Assert.Equal(2_366, table.Report.FixedPayloadBytes);
        Assert.Equal(2_656, ladder.Report.FixedPayloadBytes);
        Assert.Equal(12, 3 + 2 + 2 + 2 + 3);
        Assert.Equal(122, (recordCount - 1) * (3 + 2 + 3) + 2);
        Assert.Equal(112, 122 - 10);
        Assert.True(table.Report.FixedPayloadBytes < ladder.Report.FixedPayloadBytes);
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
            return;
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
