namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.FunctionalAcceptance;
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
        var baseDirectory = WriteActorSpawnMap(ActorSpawnRomTableFixture.ObjectsJson(recordCount));
        var source = ActorSpawnRomTableFixture.ActorSource(includeReturn: true);
        var prepared = RetroSharp.NES.NesRomCompiler.PrepareVideoProgram(
            source,
            baseDirectory,
            SdkLibraryImportMode.ExplicitOnly,
            sdkLibraryRegistry: null,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            sdkPluginRegistry: null);
        AssertSpawnColumns(prepared.VideoProgram.GeneratedRomTables, recordCount);

        var columns = ActorSpawnRomTableFixture.ExpectedColumns(recordCount);
        var emittedSource = ActorSpawnRomTableFixture.TableSource(columns, includeReturn: true);
        var emittedTables = EmittedTables(columns);
        var emittedProgram = RetroSharp.NES.NesRomCompiler.PrepareVideoProgram(
            emittedSource,
            baseDirectory: null,
            SdkLibraryImportMode.ExplicitOnly,
            sdkLibraryRegistry: null,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            sdkPluginRegistry: null,
            generatedRomTablesOverride: emittedTables);
        var build = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            emittedSource,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            generatedRomTablesOverride: emittedTables);

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
            Assert.Equal(1, ActorSpawnRomTableFixture.CountMaskedSequences(build.Rom, shape));
        }

        Assert.Equal(9, 3 + 2 + 4);
        Assert.Equal(10, 3 + 2 + 5);
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "FocusedLowering")]
    public void Rom_tables_record_the_nes_wide_spawn_payload_delta()
    {
        const int recordCount = 16;
        var columns = ActorSpawnRomTableFixture.ExpectedColumns(recordCount)
            .Where(column => column.Key is "x" or "xHi")
            .ToDictionary(column => column.Key, column => column.Value, StringComparer.Ordinal);
        var table = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            ActorSpawnRomTableFixture.TableSource(columns, includeReturn: true),
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            generatedRomTablesOverride: EmittedTables(columns));
        var ladder = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            ActorSpawnRomTableFixture.LadderSource(columns, includeReturn: true),
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
