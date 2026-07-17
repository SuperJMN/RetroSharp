namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.FunctionalAcceptance;
using RetroSharp.GameBoy;
using RetroSharp.Parser;
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

    [Fact]
    [Trait("RetroSharp.TestOwnership", "FocusedLowering")]
    public void Production_spawn_rom_tables_round_trip_through_runtime_activation()
    {
        var baseDirectory = WriteActorSpawnMap(
            """
            [
              { "id": 1, "type": "Goomba", "x": 24, "y": 32, "properties": [
                { "name": "active", "type": "int", "value": 1 },
                { "name": "vx", "type": "int", "value": 3 },
                { "name": "vy", "type": "int", "value": 6 },
                { "name": "state", "type": "int", "value": 9 },
                { "name": "timer", "type": "int", "value": 12 },
                { "name": "facing", "type": "int", "value": 15 },
                { "name": "animTick", "type": "int", "value": 18 },
                { "name": "health", "type": "int", "value": 21 }
              ] },
              { "id": 2, "type": "Bat", "x": 72, "y": 72, "properties": [
                { "name": "active", "type": "int", "value": 1 },
                { "name": "vx", "type": "int", "value": 4 },
                { "name": "vy", "type": "int", "value": 7 },
                { "name": "state", "type": "int", "value": 10 },
                { "name": "timer", "type": "int", "value": 13 },
                { "name": "facing", "type": "int", "value": 16 },
                { "name": "animTick", "type": "int", "value": 19 },
                { "name": "health", "type": "int", "value": 22 }
              ] },
              { "id": 3, "type": "Goomba", "x": 120, "y": 304, "properties": [
                { "name": "active", "type": "int", "value": 1 },
                { "name": "vx", "type": "int", "value": 5 },
                { "name": "vy", "type": "int", "value": 8 },
                { "name": "state", "type": "int", "value": 11 },
                { "name": "timer", "type": "int", "value": 14 },
                { "name": "facing", "type": "int", "value": 17 },
                { "name": "animTick", "type": "int", "value": 20 },
                { "name": "health", "type": "int", "value": 23 }
              ] }
            ]
            """);
        const string source = """
                              void Main() {
                                  World.Column(0, 0, 0);
                                  World.Map(40, 10, 2);
                                  Camera.Init(40, 10, 2);
                                  Actors.Pool(enemies, 3);
                                  Enemies.Def(Goomba, behavior: Walker);
                                  Enemies.Def(Bat, behavior: Flyer);
                                  Actors.SpawnWindow(enemies, "level.tmj", "actors", 0, 160);
                                  while (true) {
                                      Video.WaitVBlank();
                                  }
                              }
                              """;
        var expected = EndToEndSpawnColumns();
        var prepared = RetroSharp.GameBoy.GameBoyRomCompiler.PrepareVideoProgram(
            source,
            baseDirectory,
            SdkLibraryImportMode.ExplicitOnly,
            sdkLibraryRegistry: null,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            sdkPluginRegistry: null);
        foreach (var (field, values) in expected)
        {
            var functionName = $"__enemies_spawn_0_{field}";
            if (values.Distinct().Count() == 1)
            {
                Assert.DoesNotContain(functionName, prepared.GeneratedRomTables.Keys);
            }
            else
            {
                Assert.Equal(values, prepared.GeneratedRomTables[functionName].Data);
            }
        }

        var build = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            baseDirectory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        foreach (var table in prepared.GeneratedRomTables.Values)
        {
            var segment = Assert.Single(
                build.Report.Segments,
                candidate => candidate.Owner == $"generated-rom-table:{table.FunctionName}");
            Assert.Equal(table.Data, build.Rom.AsSpan(segment.PhysicalStart, table.Data.Length).ToArray());
        }

        var cpu = new GameBoyTestCpu(build.Rom);
        cpu.RunFrames(10);
        var variables = build.Report.UserVariables.ToDictionary(variable => variable.Name, StringComparer.Ordinal);
        for (var slot = 0; slot < 3; slot++)
        {
            foreach (var (field, values) in expected)
            {
                var variable = variables[$"enemies[{slot}].{field}"];
                var actual = cpu.Wram(variable.Address);
                Assert.True(
                    values[slot] == actual,
                    $"enemies[{slot}].{field} expected {values[slot]} but observed {actual} at ${variable.Address:X4}.");
            }
        }
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "FocusedLowering")]
    public void Banked_spawn_rom_table_lookup_restores_the_actual_visible_bank()
    {
        var table = new CompilerGeneratedRomTable("lookup", [0x10, 0x20, 0x30, 0x40]);
        var build = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            LargeProgramThatReadsLookup(),
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            generatedRomTablesOverride: new Dictionary<string, CompilerGeneratedRomTable>(StringComparer.Ordinal)
            {
                ["lookup"] = table,
            });
        var segment = Assert.Single(
            build.Report.Segments,
            candidate => candidate.Owner == $"read-only:{table.Label}");
        Assert.Equal("gb-simple-mbc1-current", build.Report.SelectedProfile);
        Assert.True(segment.Bank > 0);
        Assert.True(segment.CpuAddress + table.Data.Length <= 0x8000);

        var helper = FindGeneratedTableReadHelper(build.Rom, segment.CpuAddress);
        var cpu = new GameBoyTestCpu(build.Rom);
        cpu.SetCurrentRomBank(3);
        cpu.SetWram(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank, 3);
        cpu.SetWram(GameBoyRuntimeMemoryLayout.Banking.ProgramCurrentBank, 9);

        var result = cpu.RunFarReadSubroutine(helper, bank: 2, address: 0x4000);

        Assert.Equal(0x30, result.Data);
        Assert.Equal(3, cpu.CurrentRomBank);
        Assert.Equal(3, cpu.Wram(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank));
        Assert.Equal(9, cpu.Wram(GameBoyRuntimeMemoryLayout.Banking.ProgramCurrentBank));
        Assert.Contains(cpu.RomBankWrites, write => write.SelectedBank == segment.Bank);
        Assert.Equal(3, cpu.RomBankWrites[^1].SelectedBank);
        Assert.All(cpu.RomBankWrites, write => Assert.InRange(write.ProgramCounter, 0x0150, 0x3FFF));
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
    public void Wide_spawn_window_uses_bounded_game_boy_spatial_candidate_rom_index()
    {
        var baseDirectory = WriteActorSpawnMap(ActorSpawnRomTableFixture.SpacedObjectsJson(recordCount: 240, spacing: 16));
        var source = WideSpawnSource(capacity: 10, "Actors.SpawnWindow(enemies, \"level.tmj\", \"actors\", 0, 160);");
        var prepared = RetroSharp.GameBoy.GameBoyRomCompiler.PrepareVideoProgram(
            source,
            baseDirectory,
            SdkLibraryImportMode.ExplicitOnly,
            sdkLibraryRegistry: null,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            sdkPluginRegistry: null);

        var candidateTables = CandidateTables(prepared.GeneratedRomTables);
        Assert.NotEmpty(candidateTables);
        Assert.All(candidateTables, table => Assert.InRange(table.Value.Data.Length, 1, 32));
        Assert.Equal(Enumerable.Range(0, 20).Select(index => (byte)index), candidateTables["__enemies_spawn_0_candidate_0"].Data);
        Assert.Equal(Enumerable.Range(10, 20).Select(index => (byte)index), candidateTables["__enemies_spawn_0_candidate_1"].Data);
        Assert.Equal(Enumerable.Range(120, 20).Select(index => (byte)index), candidateTables["__enemies_spawn_0_candidate_12"].Data);
        Assert.Equal(Enumerable.Range(230, 10).Select(index => (byte)index), candidateTables["__enemies_spawn_0_candidate_23"].Data);

        var lowered = PrintedFunction(prepared, "Main");
        Assert.Contains("__enemies_spawn_0_candidate_0", lowered);
        Assert.DoesNotContain("__enemies_spawn_0_call0_i<240", Compact(lowered));
    }

    [Fact]
    [Trait("RetroSharp.TestOwnership", "FocusedLowering")]
    public void Spatial_spawn_window_marks_used_only_after_successful_game_boy_slot_claim()
    {
        var baseDirectory = WriteActorSpawnMap(ActorSpawnRomTableFixture.SpacedObjectsJson(recordCount: 33, spacing: 16));
        var source = RetryAfterFullPoolSource();
        var prepared = RetroSharp.GameBoy.GameBoyRomCompiler.PrepareVideoProgram(
            source,
            baseDirectory,
            SdkLibraryImportMode.ExplicitOnly,
            sdkLibraryRegistry: null,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            sdkPluginRegistry: null);
        var lowered = Compact(PrintedFunction(prepared, "Main"));

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

    private static string PrintedFunction(RetroSharp.GameBoy.GameBoyVideoProgram program, string name)
    {
        var visitor = new PrintNodeVisitor();
        program.Functions[name].Accept(visitor);
        return visitor.ToString();
    }

    private static string Compact(string value) =>
        new(value.Where(character => !char.IsWhiteSpace(character)).ToArray());

    private static IReadOnlyDictionary<string, byte[]> EndToEndSpawnColumns() =>
        new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["kind"] = [1, 2, 1],
            ["x"] = [24, 72, 120],
            ["xHi"] = [0, 0, 0],
            ["y"] = [32, 72, 48],
            ["yHi"] = [0, 0, 1],
            ["active"] = [1, 1, 1],
            ["vx"] = [3, 4, 5],
            ["vy"] = [6, 7, 8],
            ["state"] = [9, 10, 11],
            ["timer"] = [12, 13, 14],
            ["facing"] = [15, 16, 17],
            ["animTick"] = [18, 19, 20],
            ["health"] = [21, 22, 23],
        };

    private static string LargeProgramThatReadsLookup()
    {
        var filler = string.Join(Environment.NewLine, Enumerable.Repeat("    value += 1;", 6000));
        return """
               inline u8 lookup(u8 index) => 0;

               void Main() {
                   Video.Init();
                   u8 value = 0;
               """ + Environment.NewLine + filler + """
                   value = lookup(2);
                   while (true) {
                       Video.WaitVBlank();
                   }
               }
               """;
    }

    private static ushort FindGeneratedTableReadHelper(byte[] rom, ushort tableAddress)
    {
        var loadTable = new byte[] { 0x21, (byte)tableAddress, (byte)(tableAddress >> 8) };
        var loadTableOffset = IndexOfSequence(rom.AsSpan(0, 0x4000).ToArray(), loadTable);
        Assert.True(loadTableOffset >= 0, $"Could not find LD HL,${tableAddress:X4} in fixed bank.");
        for (var offset = loadTableOffset; offset >= Math.Max(0, loadTableOffset - 24); offset--)
        {
            if (rom[offset] == 0x5F)
            {
                return (ushort)offset;
            }
        }

        throw new InvalidOperationException($"Could not find generated table helper entry before LD HL,${tableAddress:X4}.");
    }

}
