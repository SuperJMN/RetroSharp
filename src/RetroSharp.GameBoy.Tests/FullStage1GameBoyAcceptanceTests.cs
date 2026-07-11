namespace RetroSharp.GameBoy.Tests;

using System.Text.Json;
using System.Text.Json.Nodes;
using RetroSharp.GameBoy;
using RetroSharp.Sdk;
using Xunit;

public sealed class FullStage1GameBoyAcceptanceTests
{
    private const ushort WorldPackValidationState = 0xC1FB;
    private const ushort SfxActive = 0xC131;
    private const ushort VisibleCameraXLow = 0xC14D;
    private const ushort VisibleCameraXHigh = 0xC14E;
    private const ushort RequestCount = 0xC152;
    private const ushort PrepareCount = 0xC153;
    private const ushort ResidentCount = 0xC154;
    private const ushort CommitCount = 0xC155;
    private const ushort ReleaseCount = 0xC156;
    private const ushort BankWorkInCommit = 0xC157;
    private const ushort DecodeWorkInCommit = 0xC158;
    private const ushort LastCommitVramWrites = 0xC159;
    private const ushort LastCommittedWorldEdgeLow = 0xC188;
    private const ushort LastCommittedWorldEdgeHigh = 0xC189;
    private const ushort DirectoryWorkInVBlank = 0xC19C;
    private const ushort AudioTickCount = 0xC19D;
    private const ushort Slot0State = 0xC170;
    private const ushort Slot0WorldEdgeHigh = 0xC174;
    private const byte Resident = 3;
    private const byte Released = 5;

    [Fact]
    public void Full_stage1_runner_fixture_uses_the_production_packed_mbc1_final_link_with_complete_resources()
    {
        using var fixture = FullStage1Fixture.Create();
        var compiled = fixture.CompileRunner();
        WriteExternalRomIfRequested("RETROSHARP_FULL_STAGE1_RUNNER_ROM", compiled.Rom);
        var pack = GameBoyTiledMapImporter.CompileWorldPack(
            fixture.MapPath,
            GameBoyVideoProgram.FirstGeneratedBackgroundTile);

        Assert.Equal(312, pack.Pack.Descriptor.HardwareWidth);
        Assert.Equal(40, pack.Pack.Descriptor.HardwareHeight);
        Assert.Equal(2_550, pack.SerializedBytes.Length);
        Assert.Equal("gb-simple-mbc1-current", compiled.Report.SelectedProfile);
        Assert.Equal(1, compiled.Rom[0x147]);
        Assert.Equal(131_072, compiled.Rom.Length);
        Assert.Equal(2_550, compiled.Report.Segments
            .Where(segment => segment.Owner == "worldpack:default")
            .Sum(segment => segment.Length));
        Assert.Equal(11_614, compiled.Report.Segments
            .Where(segment => segment.Owner == "bgm:runner_theme")
            .Sum(segment => segment.Length));
        Assert.Equal(28, compiled.Report.Segments
            .Where(segment => segment.Owner == "sfx:jump_sfx")
            .Sum(segment => segment.Length));
        Assert.Equal(2_368, compiled.Report.Segments
            .Where(segment => segment.Owner is "read-only:tile-data" or "read-only:tile_data")
            .Sum(segment => segment.Length));
        Assert.DoesNotContain(
            compiled.Report.Segments,
            segment => segment.Owner.StartsWith("legacy-world-data", StringComparison.Ordinal));
        AssertReportedSegmentsDoNotOverlap(compiled.Report);
    }

    [Fact]
    public void Full_stage1_probe_ticks_bgm_once_per_real_frame_and_plays_the_complete_sfx()
    {
        using var fixture = FullStage1Fixture.Create();
        var compiled = fixture.CompileTraversalProbe();
        var cpu = new GameBoyTestCpu(compiled.Rom)
        {
            CycleAccurateLy = true,
            EnforceVblankVramWrites = true,
        };

        cpu.RunFrames(180);
        Assert.Equal(1, cpu.Wram(WorldPackValidationState));
        Assert.Equal("gb-rom-only-current", compiled.Report.SelectedProfile);
        Assert.Equal(0, compiled.Rom[0x147]);
        var counterDeltas = new List<byte>(120);
        var previousCounter = cpu.Wram(AudioTickCount);
        for (var frame = 181; frame <= 300; frame++)
        {
            cpu.RunFrames(frame);
            var currentCounter = cpu.Wram(AudioTickCount);
            counterDeltas.Add((byte)(currentCounter - previousCounter));
            previousCounter = currentCounter;
        }

        cpu.RunFrames(301);
        var audioTicksByFrame = cpu.AudioUpdateCycles
            .GroupBy(cycle => cycle / 70_224)
            .ToDictionary(group => group.Key, group => group.Count());
        var audioDeltas = Enumerable.Range(180, 120)
            .Select(frame => audioTicksByFrame.GetValueOrDefault(frame))
            .ToArray();
        var irregularFrames = Enumerable.Range(180, 120)
            .Where(frame => audioTicksByFrame.GetValueOrDefault(frame) != 1)
            .Select(frame => $"{frame}=[{string.Join(',', cpu.AudioUpdateTrace.Where(trace => trace.Cycles / 70_224 == frame).Select(trace => $"LY{trace.Ly}/PC{trace.ProgramCounter:X4}"))}]")
            .ToArray();
        Assert.True(
            audioDeltas.All(delta => delta == 1),
            $"Expected one BGM tick in every real frame; irregular: {string.Join(' ', irregularFrames)}");
        Assert.All(counterDeltas, delta => Assert.Equal(1, delta));
        cpu.Held.Add("a");
        cpu.RunUntilWramEquals(SfxActive, 1, 50_000_000);
        cpu.Held.Remove("a");
        var sfxWrites = cpu.ApuWrites.Count(write => write.Register is >= 0xFF10 and <= 0xFF14);
        cpu.RunAdditionalFrames(8);

        Assert.True(sfxWrites > 0);
    }

    [Fact]
    public void Full_stage1_runner_keeps_worldpack_bank_reads_outside_the_guard_band_and_restores_the_live_bank()
    {
        using var fixture = FullStage1Fixture.Create();
        var compiled = fixture.CompileRunner();
        var worldPackBanks = compiled.Report.Segments
            .Where(segment => segment.Owner == "worldpack:default")
            .Select(segment => segment.Bank)
            .ToHashSet();
        var audioBanks = compiled.Report.Segments
            .Where(segment => segment.Owner.StartsWith("bgm:", StringComparison.Ordinal) || segment.Owner.StartsWith("sfx:", StringComparison.Ordinal))
            .Select(segment => segment.Bank)
            .ToHashSet();
        Assert.DoesNotContain(worldPackBanks, audioBanks.Contains);

        var readerCpu = new GameBoyTestCpu(compiled.Rom) { CycleAccurateLy = true };
        readerCpu.RunUntilWramEquals(WorldPackValidationState, 1, 500_000_000);
        readerCpu.RunUntilLy(136);
        readerCpu.SetCurrentRomBank(1);
        readerCpu.SetWram(GameBoyRomBuilder.ActualVisibleBankAddress, 1);
        var readerBankWriteStart = readerCpu.RomBankWrites.Count;
        var lookup = readerCpu.RunWorldPackCollisionLookup(
            compiled.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionLookupLabel],
            hardwareX: 255,
            hardwareY: 38);
        var guardedReaderWrites = readerCpu.RomBankWrites.Skip(readerBankWriteStart).ToArray();

        Assert.Equal(GameBoyWorldPackResult.Success, lookup.Status);
        Assert.NotEmpty(guardedReaderWrites);
        Assert.All(guardedReaderWrites, write => Assert.InRange(write.Ly, (byte)0, (byte)135));
        Assert.Equal(1, readerCpu.CurrentRomBank);
        Assert.Equal(1, readerCpu.Wram(GameBoyRomBuilder.ActualVisibleBankAddress));

        var cpu = new GameBoyTestCpu(compiled.Rom)
        {
            CycleAccurateLy = true,
            EnforceVblankVramWrites = true,
        };
        cpu.RunUntilWramEquals(WorldPackValidationState, 1, 500_000_000);
        cpu.Held.Add("right");
        cpu.Held.Add("b");
        cpu.RunAdditionalFrames(160);
        cpu.Held.Add("a");
        cpu.RunAdditionalFrames(24);
        cpu.Held.Remove("a");
        cpu.RunAdditionalFrames(80);

        Assert.Equal(cpu.CurrentRomBank, cpu.Wram(GameBoyRomBuilder.ActualVisibleBankAddress));
        Assert.Equal(0, cpu.Wram(BankWorkInCommit));
        Assert.Equal(0, cpu.Wram(DecodeWorkInCommit));
        Assert.Equal(0, cpu.Wram(DirectoryWorkInVBlank));
    }

    [Fact]
    public void Full_stage1_traversal_crosses_column_255_and_chunk_boundaries_both_directions_without_breaking_scheduler_invariants()
    {
        using var fixture = FullStage1Fixture.Create();
        var canonical = GameBoyTiledMapImporter.CompileWorldPack(
            fixture.MapPath,
            GameBoyVideoProgram.FirstGeneratedBackgroundTile);
        var raw = GameBoyTiledMapImporter.Load(
            fixture.MapPath,
            GameBoyVideoProgram.FirstGeneratedBackgroundTile);
        var compiled = fixture.CompileTraversalProbe();
        WriteExternalRomIfRequested("RETROSHARP_FULL_STAGE1_TRAVERSAL_ROM", compiled.Rom);
        var cpu = new GameBoyTestCpu(compiled.Rom)
        {
            CycleAccurateLy = true,
            EnforceVblankVramWrites = true,
        };
        cpu.RunUntilWramEquals(WorldPackValidationState, 1, 500_000_000);
        cpu.RunFrames(20);

        AssertVisibleTilesMatchRawMap(cpu, raw, "origin");
        cpu.Held.Add("right");
        RunUntilWordEquals(cpu, LastCommittedWorldEdgeLow, 0x0100, maxFrames: 1_200);

        Assert.Equal(1_888, ReadWord(cpu, VisibleCameraXLow));
        Assert.Equal(19, cpu.Wram(LastCommitVramWrites));
        AssertVisibleTilesMatchRawMap(cpu, raw, "right across column 255");

        RunUntilWordEquals(
            cpu,
            LastCommittedWorldEdgeLow,
            checked((ushort)(canonical.Pack.Descriptor.HardwareWidth - 1)),
            maxFrames: 450);
        Assert.Equal(2_328, ReadWord(cpu, VisibleCameraXLow));
        Assert.Equal(canonical.Pack.Descriptor.HardwareWidth - 1, ReadWord(cpu, LastCommittedWorldEdgeLow));
        RunUntilWordEquals(cpu, VisibleCameraXLow, 2_336, maxFrames: 40);
        AssertVisibleTilesMatchRawMap(cpu, raw, "far right");
        Assert.Equal(2_336, ReadWord(cpu, VisibleCameraXLow));

        cpu.Held.Clear();
        cpu.Held.Add("left");
        RunUntilWordEquals(cpu, LastCommittedWorldEdgeLow, 0x0100, maxFrames: 450);
        Assert.Equal(2_055, ReadWord(cpu, VisibleCameraXLow));
        AssertVisibleTilesMatchRawMap(cpu, raw, "left across column 255");

        RunUntilWordEquals(cpu, VisibleCameraXLow, 0, maxFrames: 1_200);
        AssertVisibleTilesMatchRawMap(cpu, raw, "returned origin");

        cpu.Held.Clear();
        cpu.Held.Add("down");
        RunUntilWordEquals(cpu, 0xC14F, 80, maxFrames: 400);
        Assert.Equal(21, cpu.Wram(LastCommitVramWrites));
        AssertVisibleTilesMatchRawMap(cpu, raw, "vertical bottom");
        cpu.Held.Clear();
        cpu.Held.Add("up");
        RunUntilWordEquals(cpu, 0xC14F, 0, maxFrames: 400);
        AssertVisibleTilesMatchRawMap(cpu, raw, "vertical origin");

        Assert.Equal(cpu.Wram(RequestCount), cpu.Wram(PrepareCount));
        Assert.Equal(cpu.Wram(PrepareCount), cpu.Wram(ResidentCount));
        Assert.Equal(cpu.Wram(ResidentCount), cpu.Wram(CommitCount));
        Assert.Equal(cpu.Wram(CommitCount), cpu.Wram(ReleaseCount));
        Assert.Equal(0, cpu.Wram(BankWorkInCommit));
        Assert.Equal(0, cpu.Wram(DecodeWorkInCommit));
        Assert.Equal(0, cpu.Wram(DirectoryWorkInVBlank));
        Assert.All(cpu.VramWrites, write =>
        {
            if (write.LcdEnabled)
            {
                Assert.InRange(write.Ly, (byte)144, (byte)153);
            }
        });
    }

    [Fact]
    public void Full_stage1_runtime_reconstructs_every_visual_and_collision_chunk_and_preserves_y304_hit_top_abi()
    {
        using var fixture = FullStage1Fixture.Create();
        var canonical = GameBoyTiledMapImporter.CompileWorldPack(
            fixture.MapPath,
            GameBoyVideoProgram.FirstGeneratedBackgroundTile);
        var compiled = fixture.CompilePackReaderProbe();
        var cpu = new GameBoyTestCpu(compiled.Rom);
        cpu.SetCurrentRomBank(1);
        cpu.SetWram(GameBoyRomBuilder.ActualVisibleBankAddress, 1);

        for (var chunkIndex = 0; chunkIndex < canonical.Pack.Chunks.Count; chunkIndex++)
        {
            var chunk = canonical.Pack.Chunks[chunkIndex];
            Assert.Equal(
                GameBoyWorldPackResult.Success,
                cpu.RunWorldPackDecode(
                    compiled.Report.FixedSymbols[GameBoyRomBuilder.WorldPackVisualDecodeLabel],
                    checked((ushort)chunkIndex),
                    slot: 0));
            Assert.Equal(
                chunk.VisualIds.Select(id => (byte)id),
                Enumerable.Range(0xC300, chunk.VisualIds.Count).Select(address => cpu.Wram((ushort)address)));
            Assert.Equal(
                GameBoyWorldPackResult.Success,
                cpu.RunWorldPackDecode(
                    compiled.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionDecodeLabel],
                    checked((ushort)chunkIndex),
                    slot: 0));
            Assert.Equal(
                chunk.CollisionProfileIds.Select(id => (byte)id),
                Enumerable.Range(0xC380, chunk.CollisionProfileIds.Count).Select(address => cpu.Wram((ushort)address)));
            Assert.Equal(cpu.CurrentRomBank, cpu.Wram(GameBoyRomBuilder.ActualVisibleBankAddress));
        }

        var collisionProbe = fixture.CompileCollisionProbe();
        var collisionCpu = new GameBoyTestCpu(collisionProbe.Rom);
        collisionCpu.RunUntilWramEquals(WorldPackValidationState, 1, 500_000_000);
        collisionCpu.RunAdditionalFrames(20);

        Assert.Equal(
            new byte[] { 0x30, 0x01, 0xFF, 0xFF },
            Enumerable.Range(0xC000, 4).Select(address => collisionCpu.Wram((ushort)address)).ToArray());
        Assert.DoesNotContain(
            collisionProbe.Report.Segments,
            segment => segment.Owner.StartsWith("legacy-world-data", StringComparison.Ordinal));
    }

    [Fact]
    public void Full_stage1_wrong_tag_deferral_and_reversal_never_publish_or_mutate_an_uncommitted_edge()
    {
        using var fixture = FullStage1Fixture.Create();
        var compiled = fixture.CompileDeferralProbe();
        var cpu = new GameBoyTestCpu(compiled.Rom)
        {
            CycleAccurateLy = true,
            EnforceVblankVramWrites = true,
        };
        cpu.RunUntilWramEquals(WorldPackValidationState, 1, 500_000_000);
        cpu.RunFrames(20);
        cpu.Held.Add("right");
        cpu.RunUntilWramEquals(Slot0State, Resident, 500_000_000);
        var residentMetadata = Enumerable.Range(0, 10)
            .Select(offset => cpu.Wram((ushort)(Slot0State + offset)))
            .ToArray();
        var commitBefore = cpu.Wram(CommitCount);
        var releaseBefore = cpu.Wram(ReleaseCount);
        var visibleBefore = ReadWord(cpu, VisibleCameraXLow);
        var vramWritesBefore = cpu.VramWrites.Count;

        cpu.SetWram(Slot0WorldEdgeHigh, (byte)(residentMetadata[4] ^ 1));
        cpu.RunAdditionalFrames(4);

        Assert.Equal(commitBefore, cpu.Wram(CommitCount));
        Assert.Equal(releaseBefore, cpu.Wram(ReleaseCount));
        Assert.Equal(visibleBefore, ReadWord(cpu, VisibleCameraXLow));
        Assert.Equal(vramWritesBefore, cpu.VramWrites.Count);

        cpu.Held.Clear();
        cpu.Held.Add("left");
        cpu.RunUntilWramEquals(ReleaseCount, (byte)(releaseBefore + 1), 500_000_000);

        Assert.Equal(Released, cpu.Wram(Slot0State));
        Assert.Equal(commitBefore, cpu.Wram(CommitCount));
        Assert.Equal(0, ReadWord(cpu, VisibleCameraXLow));
        Assert.Equal(vramWritesBefore, cpu.VramWrites.Count);
    }

    private sealed class FullStage1Fixture(string path, string mapPath) : IDisposable
    {
        public string MapPath { get; } = mapPath;

        public static FullStage1Fixture Create()
        {
            var path = Path.Combine(Path.GetTempPath(), "retrosharp-full-stage1-gb-acceptance", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            var sourcePath = RepositoryFile("samples/runner/assets/maps/stage1.tmj");
            var root = JsonNode.Parse(File.ReadAllText(sourcePath))?.AsObject()
                       ?? throw new InvalidOperationException($"{sourcePath} is empty.");
            var height = root["height"]?.GetValue<int>()
                         ?? throw new InvalidOperationException($"{sourcePath} does not declare height.");
            root["properties"] = new JsonArray(
                MapProperty("retrosharpStreamY", 0),
                MapProperty("retrosharpWorldY", 0),
                MapProperty("retrosharpWorldHeight", height));
            foreach (var layer in root["layers"]?.AsArray() ?? [])
            {
                if (layer?["type"]?.GetValue<string>() == "tilelayer")
                {
                    layer["name"] = "world";
                }
            }

            var mapPath = Path.Combine(path, "stage1.full-acceptance.tmj");
            File.WriteAllText(mapPath, root.ToJsonString(JsonOptions));
            File.Copy(RepositoryFile("samples/runner/assets/maps/stage1.tsx"), Path.Combine(path, "stage1.tsx"));
            File.Copy(RepositoryFile("samples/runner/assets/maps/stage1.png"), Path.Combine(path, "stage1.png"));
            return new FullStage1Fixture(path, mapPath);
        }

        public GameBoyRomBuildResult CompileRunner()
        {
            var source = RunnerSample.CompiledSource()
                .Replace(
                    "assets/maps/stage1.playable.tmj",
                    MapPath.Replace('\\', '/'),
                    StringComparison.Ordinal)
                .Replace("const i16 Width = 176;", "const i16 Width = 312;", StringComparison.Ordinal)
                .Replace("const i16 Height = 30;", "const i16 Height = 40;", StringComparison.Ordinal)
                .Replace("const i16 StreamHeight = 30;", "const i16 StreamHeight = 40;", StringComparison.Ordinal)
                .Replace("const i16 PixelWidth = 1408;", "const i16 PixelWidth = 2496;", StringComparison.Ordinal);
            var packed = GameBoyTiledMapImporter.CompileWorldPack(
                MapPath,
                GameBoyVideoProgram.FirstGeneratedBackgroundTile);
            return RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
                source,
                RunnerSample.Directory,
                sdkLibraryImports: [SdkImportResolver.Portable2D],
                packedWorldOverride: packed.SerializedBytes);
        }

        public GameBoyRomBuildResult CompileTraversalProbe()
        {
            var portableMapPath = MapPath.Replace('\\', '/');
            var source = $$"""
                void Main() {
                    Video.Init();
                    Palette.Background(0, 0, 1, 2, 3);
                    Palette.Sprite(0, 0, 0, 1, 3);
                    Sprite.Asset(mario_player, "assets/mario-player.png", 18, 32);
                    World.Load("{{portableMapPath}}");
                    Music.Asset(runner_theme, "assets/music/runner.vgz");
                    Sfx.Asset(jump_sfx, "assets/sfx/smb-jump.vgm");
                    Audio.Init();
                    Music.Play(runner_theme);
                    Camera.Init(312, 0, 30);
                    i16 targetX = 0;
                    i16 targetY = 0;
                    while (true) {
                        Video.WaitVBlank();
                        Camera.Apply();
                        Audio.Update();
                        Input.Poll();
                        if (Input.IsDown(Button.Right)) {
                            targetX = 2336;
                        }
                        if (Input.IsDown(Button.Left)) {
                            targetX = 0;
                        }
                        if (Input.IsDown(Button.Down)) {
                            targetY = 80;
                        }
                        if (Input.IsDown(Button.Up)) {
                            targetY = 0;
                        }
                        if (Input.WasPressed(Button.A)) {
                            Sfx.Play(jump_sfx);
                        }
                        Camera.SetPosition(targetX, targetY);
                    }
                }
                """;
            var packed = GameBoyTiledMapImporter.CompileWorldPack(
                MapPath,
                GameBoyVideoProgram.FirstGeneratedBackgroundTile);
            return RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
                source,
                RunnerSample.Directory,
                sdkLibraryImports: [SdkImportResolver.Portable2D],
                packedWorldOverride: packed.SerializedBytes);
        }

        public GameBoyRomBuildResult CompilePackReaderProbe() => CompileSourceWithPack("void Main() { }");

        public GameBoyRomBuildResult CompileDeferralProbe()
        {
            var source = $$"""
                void Main() {
                    World.Load("{{MapPath.Replace('\\', '/')}}");
                    Music.Asset(runner_theme, "assets/music/runner.vgz");
                    Audio.Init();
                    Music.Play(runner_theme);
                    Camera.Init(312, 0, 30);
                    i16 targetX = 0;
                    while (true) {
                        Video.WaitVBlank();
                        Camera.Apply();
                        Audio.Update();
                        Input.Poll();
                        if (Input.IsDown(Button.Right)) {
                            targetX = 8;
                        }
                        if (Input.IsDown(Button.Left)) {
                            targetX = 0;
                        }
                        Camera.SetPosition(targetX, 0);
                    }
                }
                """;
            return CompileSourceWithPack(source);
        }

        public GameBoyRomBuildResult CompileCollisionProbe()
        {
            var source = $$"""
                void Main() {
                    World.Load("{{MapPath.Replace('\\', '/')}}");
                    Camera.Init(312, 0, 30);
                    i16 hitTop = Camera.AabbHitTop(0, 300, 8, 12, 1);
                    i16 noHit = Camera.AabbHitTop(0, 300, 8, 12, 4);
                    while (true) {
                        Video.WaitVBlank();
                    }
                }
                """;
            return CompileSourceWithPack(source);
        }

        private GameBoyRomBuildResult CompileSourceWithPack(string source)
        {
            var packed = GameBoyTiledMapImporter.CompileWorldPack(
                MapPath,
                GameBoyVideoProgram.FirstGeneratedBackgroundTile);
            return RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
                source,
                RunnerSample.Directory,
                sdkLibraryImports: [SdkImportResolver.Portable2D],
                packedWorldOverride: packed.SerializedBytes);
        }

        public void Dispose()
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        private static JsonObject MapProperty(string name, int value) => new()
        {
            ["name"] = name,
            ["type"] = "int",
            ["value"] = value,
        };
    }

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

    private static ushort ReadWord(GameBoyTestCpu cpu, ushort lowAddress) =>
        (ushort)(cpu.Wram(lowAddress) | (cpu.Wram((ushort)(lowAddress + 1)) << 8));

    private static void RunUntilWordEquals(GameBoyTestCpu cpu, ushort lowAddress, ushort expected, int maxFrames)
    {
        for (var frame = 0; frame < maxFrames; frame++)
        {
            if (ReadWord(cpu, lowAddress) == expected)
            {
                return;
            }

            cpu.RunAdditionalFrames(1);
        }

        Assert.Fail($"WRAM word 0x{lowAddress:X4} was {ReadWord(cpu, lowAddress)} instead of {expected} after {maxFrames} frames.");
    }

    private static void AssertVisibleTilesMatchRawMap(GameBoyTestCpu cpu, GameBoyTiledMap raw, string label)
    {
        var cameraX = ReadWord(cpu, VisibleCameraXLow);
        var cameraY = ReadWord(cpu, 0xC14F);
        var scx = cpu.IoRegister(0xFF43);
        var scy = cpu.IoRegister(0xFF42);
        var firstBufferColumn = scx / 8;
        var firstBufferRow = scy / 8;
        var mismatches = new List<string>();

        for (var screenRow = 0; screenRow < 18; screenRow++)
        {
            var sourceRow = (cameraY / 8) + screenRow;
            var bufferRow = (firstBufferRow + screenRow) % 32;
            for (var screenColumn = 0; screenColumn < 20; screenColumn++)
            {
                var sourceColumn = (cameraX / 8) + screenColumn;
                var bufferColumn = (firstBufferColumn + screenColumn) % 32;
                var expected = (byte)raw.WorldTileIds[sourceRow * raw.Width + sourceColumn];
                var actual = cpu.Vram((ushort)(0x9800 + bufferRow * 32 + bufferColumn));
                if (expected != actual)
                {
                    mismatches.Add($"{label}: source=({sourceColumn},{sourceRow}) buffer=({bufferColumn},{bufferRow}) expected={expected} actual={actual}");
                    if (mismatches.Count == 12)
                    {
                        break;
                    }
                }
            }

            if (mismatches.Count == 12)
            {
                break;
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    private static void AssertReportedSegmentsDoNotOverlap(GameBoyRomBuildReport report)
    {
        var ordered = report.Segments.OrderBy(segment => segment.PhysicalStart).ToArray();
        Assert.All(
            ordered.Zip(ordered.Skip(1)),
            pair => Assert.True(
                pair.First.PhysicalStart + pair.First.Length <= pair.Second.PhysicalStart,
                $"{pair.First.Owner} overlaps {pair.Second.Owner}"));
    }

    private static void WriteExternalRomIfRequested(string environmentVariable, byte[] rom)
    {
        var outputPath = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, rom);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };
}
