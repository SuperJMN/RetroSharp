namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.FunctionalAcceptance;
using RetroSharp.NES;
using RetroSharp.Parser;
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
        var expectedColumns = ActorSpawnRomTableFixture.ExpectedColumns(recordCount);
        Assert.Equal(13, tables.Keys.Count(key => expectedColumns.Keys.Any(field => key == $"__enemies_spawn_0_{field}")));
        foreach (var (field, expected) in ActorSpawnRomTableFixture.ExpectedColumns(recordCount))
        {
            var table = tables[$"__enemies_spawn_0_{field}"];
            Assert.Equal(expected, table.Data);
        }
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "FocusedLowering")]
    public void Wide_spawn_layer_uses_bounded_nes_spatial_candidate_rom_index()
    {
        var baseDirectory = WriteActorSpawnMap(ActorSpawnRomTableFixture.SpacedObjectsJson(recordCount: 240, spacing: 16));
        var source = WideSpawnSource(capacity: 16, "Actors.SpawnLayer(enemies, \"level.tmj\", \"actors\");");
        var prepared = RetroSharp.NES.NesRomCompiler.PrepareVideoProgram(
            source,
            baseDirectory,
            SdkLibraryImportMode.ExplicitOnly,
            sdkLibraryRegistry: null,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            sdkPluginRegistry: null);

        var candidateTables = CandidateTables(prepared.VideoProgram.GeneratedRomTables);
        Assert.NotEmpty(candidateTables);
        Assert.All(candidateTables, table => Assert.InRange(table.Value.Data.Length, 1, 32));
        Assert.Equal(Enumerable.Range(0, 32).Select(index => (byte)index), candidateTables["__enemies_spawn_0_candidate_0"].Data);
        Assert.Equal(Enumerable.Range(16, 32).Select(index => (byte)index), candidateTables["__enemies_spawn_0_candidate_1"].Data);
        Assert.Equal(Enumerable.Range(112, 32).Select(index => (byte)index), candidateTables["__enemies_spawn_0_candidate_7"].Data);
        Assert.Equal(Enumerable.Range(224, 16).Select(index => (byte)index), candidateTables["__enemies_spawn_0_candidate_14"].Data);

        var lowered = PrintedFunction(prepared.VideoProgram, "Main");
        Assert.Contains("__enemies_spawn_0_candidate_0", lowered);
        Assert.DoesNotContain("__enemies_spawn_0_call0_i<240", Compact(lowered));
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "FocusedLowering")]
    public void Spatial_spawn_window_marks_used_only_after_successful_nes_slot_claim()
    {
        var baseDirectory = WriteActorSpawnMap(ActorSpawnRomTableFixture.SpacedObjectsJson(recordCount: 33, spacing: 16));
        var source = RetryAfterFullPoolSource();
        var prepared = RetroSharp.NES.NesRomCompiler.PrepareVideoProgram(
            source,
            baseDirectory,
            SdkLibraryImportMode.ExplicitOnly,
            sdkLibraryRegistry: null,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            sdkPluginRegistry: null);
        var lowered = Compact(PrintedFunction(prepared.VideoProgram, "Main"));

        var slotClaim = lowered.IndexOf("if(enemies[__enemies_spawn_0_call0_slot].active==0)", StringComparison.Ordinal);
        var usedMark = lowered.IndexOf("__enemies_spawn_0_used[__enemies_spawn_0_call0_i]=1", StringComparison.Ordinal);
        Assert.True(slotClaim >= 0, lowered);
        Assert.True(usedMark > slotClaim, lowered);
    }

    private static IReadOnlyDictionary<string, CompilerGeneratedRomTable> EmittedTables(
        IReadOnlyDictionary<string, byte[]> columns) =>
        columns.ToDictionary(
            column => $"table_{column.Key}",
            column => new CompilerGeneratedRomTable($"table_{column.Key}", column.Value),
            StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, CompilerGeneratedRomTable> CandidateTables(
        IReadOnlyDictionary<string, CompilerGeneratedRomTable> tables) =>
        tables
            .Where(table => table.Key.StartsWith("__enemies_spawn_0_candidate_", StringComparison.Ordinal))
            .ToDictionary(table => table.Key, table => table.Value, StringComparer.Ordinal);

    private static string WideSpawnSource(int capacity, string spawnCall) =>
        $$"""
          void Main() {
              World.Column(0, 0, 0);
              World.Map(480, 10, 2);
              Camera.Init(480, 10, 2);
              Actors.Pool(enemies, {{capacity}});
              Enemies.Def(Goomba, behavior: Walker);
              Camera.SetPosition(0, 0);
              {{spawnCall}}
              Camera.SetPosition(160, 0);
              {{spawnCall}}
              Camera.SetPosition(0, 0);
              {{spawnCall}}
              while (true) {
                  Video.WaitVBlank();
              }
          }
          """;

    private static string RetryAfterFullPoolSource() =>
        """
        void Main() {
            World.Column(0, 0, 0);
            World.Map(480, 10, 2);
            Camera.Init(480, 10, 2);
            Actors.Pool(enemies, 2);
            Enemies.Def(Goomba, behavior: Walker);
            enemies[0].active = 1;
            enemies[0].x = 0;
            enemies[0].xHi = 0;
            enemies[1].active = 1;
            enemies[1].x = 16;
            enemies[1].xHi = 0;
            Camera.SetPosition(0, 0);
            Actors.SpawnWindow(enemies, "level.tmj", "actors", 0, 32);
            Camera.SetPosition(160, 0);
            Actors.SpawnWindow(enemies, "level.tmj", "actors", 0, 32);
            Camera.SetPosition(0, 0);
            Actors.SpawnWindow(enemies, "level.tmj", "actors", 0, 32);
            while (true) {
                Video.WaitVBlank();
            }
        }
        """;

    private static string PrintedFunction(NesVideoProgram program, string name)
    {
        var visitor = new PrintNodeVisitor();
        program.Functions[name].Accept(visitor);
        return visitor.ToString();
    }

    private static string Compact(string value) =>
        new(value.Where(character => !char.IsWhiteSpace(character)).ToArray());

}
