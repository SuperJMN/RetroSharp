namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.FunctionalAcceptance;
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
        var baseDirectory = WriteActorSpawnMap(ActorSpawnRomTableFixture.ObjectsJson(recordCount));
        var source = ActorSpawnRomTableFixture.ActorSource(includeReturn: false);
        var program = RetroSharp.GameBoy.GameBoyRomCompiler.PrepareVideoProgram(
            source,
            baseDirectory,
            SdkLibraryImportMode.ExplicitOnly,
            sdkLibraryRegistry: null,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            sdkPluginRegistry: null);
        AssertSpawnColumns(program.GeneratedRomTables, recordCount);

        var columns = ActorSpawnRomTableFixture.ExpectedColumns(recordCount);
        var emittedSource = ActorSpawnRomTableFixture.TableSource(columns, includeReturn: false);
        var emittedTables = EmittedTables(columns);
        var emittedProgram = RetroSharp.GameBoy.GameBoyRomCompiler.PrepareVideoProgram(
            emittedSource,
            baseDirectory: null,
            SdkLibraryImportMode.ExplicitOnly,
            sdkLibraryRegistry: null,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            sdkPluginRegistry: null,
            generatedRomTablesOverride: emittedTables);
        var build = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            emittedSource,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            generatedRomTablesOverride: emittedTables);

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
            Assert.Equal(1, ActorSpawnRomTableFixture.CountMaskedSequences(build.Rom, shape));
        }

        Assert.Equal(56, 16 + 4 + 8 + 12 + 8 + 8);
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "FocusedLowering")]
    public void Rom_tables_record_the_game_boy_wide_spawn_payload_delta()
    {
        const int recordCount = 16;
        var columns = ActorSpawnRomTableFixture.ExpectedColumns(recordCount)
            .Where(column => column.Key is "x" or "xHi")
            .ToDictionary(column => column.Key, column => column.Value, StringComparer.Ordinal);
        var table = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            ActorSpawnRomTableFixture.TableSource(columns, includeReturn: false),
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            generatedRomTablesOverride: EmittedTables(columns));
        var ladder = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            ActorSpawnRomTableFixture.LadderSource(columns, includeReturn: false),
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
        foreach (var (field, expected) in ActorSpawnRomTableFixture.ExpectedColumns(recordCount))
        {
            var table = tables[$"__enemies_spawn_0_{field}"];
            Assert.Equal(expected, table.Data);
        }
    }

    private static IReadOnlyDictionary<string, CompilerGeneratedRomTable> EmittedTables(
        IReadOnlyDictionary<string, byte[]> columns) =>
        columns.ToDictionary(
            column => $"table_{column.Key}",
            column => new CompilerGeneratedRomTable($"table_{column.Key}", column.Value),
            StringComparer.Ordinal);
}
