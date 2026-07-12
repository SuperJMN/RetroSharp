namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.NES;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;

public sealed class NesRunnerAcceptanceTests
{
    private const ushort PlayerWorldXLow = 0x0000;
    private const ushort PlayerWorldXHigh = 0x0001;
    private const ushort RequestedCameraXLow = 0x00E0;
    private const ushort RequestedCameraXHigh = 0x0318;

    [Fact]
    public void Nes_runner_uses_the_shared_runner_source()
    {
        Assert.False(File.Exists(RepositoryFileOrNull("samples/runner/runner.nes.rs")));
    }

    [Fact]
    public void Nes_runner_sample_compiles_with_vgm_audio()
    {
        var source = RunnerSample.CompiledSource();
        var rom = NesRomCompiler.CompileSource(source, RunnerSample.Directory);

        Assert.Equal(81936, rom.Length);
        Assert.Equal((byte)'N', rom[0]);
        Assert.Equal((byte)'E', rom[1]);
        Assert.Equal((byte)'S', rom[2]);
    }

    [Fact]
    public void Nes_full_stage1_runner_uses_the_packed_camera_before_honest_profile_selection()
    {
        var source = RunnerSample.CompiledSource();
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            source,
            RunnerSample.Directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var forced = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTestsWithReport(
            source,
            RunnerSample.Directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var canonical = NesTiledWorldImporter.CompileWorldPack(
            RepositoryFile("samples/runner/assets/maps/stage1.tmj"),
            NesVideoProgram.FirstSpriteTile + 95);

        Assert.Equal(312, canonical.Pack.Descriptor.HardwareWidth);
        Assert.Equal(40, canonical.Pack.Descriptor.HardwareHeight);
        Assert.Equal(60, canonical.Pack.Chunks.Count);
        Assert.Equal(770, canonical.Pack.Chunks.Sum(chunk => chunk.Directory.VisualStoredBytes));
        Assert.Equal(312, canonical.Pack.Chunks.Sum(chunk => chunk.Directory.CollisionStoredBytes));
        Assert.Equal(2_762, canonical.SerializedBytes.Length);
        Assert.Equal("nes-mmc3-tvrom-v1", result.Report.SelectedProfile);
        Assert.Equal(new byte[] { 0x04, 0x02, 0x48, 0x00 }, result.Rom[4..8]);
        Assert.Contains(result.Report.Segments, segment => segment.Owner == "worldpack:default");
        Assert.Equal(
            canonical.SerializedBytes.Length,
            result.Report.Segments.Where(segment => segment.Owner == "worldpack:default").Sum(segment => segment.Length));
        Assert.Equal("nes-mmc3-tvrom-v1", forced.Report.SelectedProfile);
        Assert.Equal(new byte[] { 0x04, 0x02, 0x48, 0x00 }, forced.Rom[4..8]);
        Assert.Equal(result.Rom, forced.Rom);
        Assert.Equal(result.Report.Segments, forced.Report.Segments);
        Assert.False(
            ContainsSequence(forced.Rom, [0xA9, 0x02, 0x8D, 0x14, 0x40]),
            "MMC3 must avoid AprNes' corrupt page-$02 OAM DMA path.");
        Assert.True(
            ContainsSequence(forced.Rom, [0xA9, 0x00, 0x8D, 0x03, 0x20, 0xA9, 0xFF, 0xA2, 0x00, 0x8D, 0x04, 0x20]),
            "MMC3 should clear OAM sequentially through $2003/$2004 before rendering starts.");
    }

    [Fact]
    public void Nes_full_stage1_runner_cadence_harness_exposes_frame_tick_decode_progress_camera_and_audio_measurements()
    {
        var result = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            RunnerSample.CompiledSource(),
            RunnerSample.Directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);

        Assert.Equal("nes-mmc3-tvrom-v1", result.Report.SelectedProfile);
        Assert.True(ContainsSequence(result.Rom, IncrementWordSequence(NesPackedCameraRuntime.FrameCounterLow)));
        Assert.True(ContainsSequence(result.Rom, IncrementWordSequence(NesWorldPackRuntimeAbi.CollisionDecodeCountLow)));
        Assert.True(ContainsSequence(result.Rom, IncrementByteSequence(NesWorldPackRuntimeAbi.GameplayTickCount)));
        Assert.True(ContainsSequence(result.Rom, IncrementByteSequence(NesWorldPackRuntimeAbi.AudioTickCount)));
        Assert.True(
            ContainsSequence(result.Rom, [0xA2, 0x03, 0xA9, 0x01, 0x8D, 0x16, 0x40]),
            "The packed runner must retry paired controller snapshots so DPCM DMA cannot create phantom direction input.");

        WriteCadenceHarnessIfRequested(result);
    }

    [Fact]
    public void Nes_runner_uses_dead_zone_2d_camera_path()
    {
        var runnerDirectory = RunnerSample.Directory;
        var source = RunnerSample.CompiledSource();

        var operations = NesRomCompiler.CollectSdkOperations(source, runnerDirectory);
        Assert.Equal(1, operations.OfType<Sdk2DOperation.SetCameraPosition>().Count());
        Assert.All(
            operations.OfType<Sdk2DOperation.SetCameraPosition>(),
            operation => Assert.Equal(ScrollAxes.Horizontal | ScrollAxes.Vertical, operation.Axes));

        var rom = NesRomCompiler.CompileSource(source, runnerDirectory);
        Assert.Equal(0x08, rom[6] & 0x08);
    }

    [Fact]
    public void Nes_full_stage1_runner_uses_vertical_scroll_without_the_fixed_height_overscan_inset()
    {
        // The complete stage1 is taller than the screen. It must use the vertical camera path instead
        // of the fixed-height runner's historical 8 px bottom-overscan inset.
        var runnerRom = NesRomCompiler.CompileSource(RunnerSample.CompiledSource(), RunnerSample.Directory);
        byte[] verticalScrollInset = [0x18, 0x69, 0x08, 0x8D, 0x05, 0x20];
        Assert.False(
            ContainsSequence(runnerRom, verticalScrollInset),
            "The full-stage runner must keep vertical camera framing without the fixed-height inset.");

        var scrollingSamplePath = RepositoryFile("samples/tiled-vscroll/vscroll.rs");
        var scrollingSource = File.ReadAllText(scrollingSamplePath);
        var scrollingRom = NesRomCompiler.CompileSource(scrollingSource, Path.GetDirectoryName(scrollingSamplePath));
        Assert.False(
            ContainsSequence(scrollingRom, verticalScrollInset),
            "A vertically scrolling world must keep its framing without the bottom-overscan inset.");
    }

    private static bool ContainsSequence(IReadOnlyList<byte> data, IReadOnlyList<byte> pattern)
    {
        for (var i = 0; i <= data.Count - pattern.Count; i++)
        {
            var match = true;
            for (var j = 0; j < pattern.Count; j++)
            {
                if (data[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }

    private static byte[] IncrementWordSequence(ushort lowAddress) =>
    [
        0xEE, (byte)lowAddress, (byte)(lowAddress >> 8),
        0xD0, 0x03,
        0xEE, (byte)(lowAddress + 1), (byte)((lowAddress + 1) >> 8),
    ];

    private static byte[] IncrementByteSequence(ushort address) =>
        [0xEE, (byte)address, (byte)(address >> 8)];

    private static void WriteCadenceHarnessIfRequested(NesRomBuildResult result)
    {
        var outputPath = Environment.GetEnvironmentVariable("RETROSHARP_NES_RUNNER_CADENCE_ROM");
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, result.Rom);
        var manifest = new
        {
            scenario = new
            {
                settleFrames = 500,
                buttons = new[] { "RIGHT" },
                measuredFrames = 120,
                expectedGameplayTicks = 120,
                expectedAudioTicks = 120,
                expectedPlayerProgress = 150,
                maximumRequestToVisibleFrames = 2,
            },
            measurements = new
            {
                playerWorldX = new[] { PlayerWorldXLow, PlayerWorldXHigh },
                requestedCameraX = new[] { RequestedCameraXLow, RequestedCameraXHigh },
                visibleCameraX = new[] { NesPackedCameraRuntime.VisibleCameraXLow, NesPackedCameraRuntime.VisibleCameraXHigh },
                hardwareFrames = new[] { NesPackedCameraRuntime.FrameCounterLow, NesPackedCameraRuntime.FrameCounterHigh },
                gameplayTicksModulo256 = NesWorldPackRuntimeAbi.GameplayTickCount,
                collisionDecodes = new[] { NesWorldPackRuntimeAbi.CollisionDecodeCountLow, NesWorldPackRuntimeAbi.CollisionDecodeCountHigh },
                audioTicksModulo256 = NesWorldPackRuntimeAbi.AudioTickCount,
                r6Shadow = NesRomBuilder.Mmc3R6BankShadowAddress,
                bankWorkInCommit = NesPackedCameraRuntime.BankWorkInCommit,
                directoryWorkInCommit = NesPackedCameraRuntime.DirectoryWorkInCommit,
                decodeWorkInCommit = NesPackedCameraRuntime.DecodeWorkInCommit,
                criticalSection = NesPackedCameraRuntime.CriticalSection,
                lastTileWrites = NesPackedCameraRuntime.LastTileWrites,
                lastAttributeWrites = NesPackedCameraRuntime.LastAttributeWrites,
            },
            fixedSymbols = result.Report.FixedSymbols,
        };
        File.WriteAllText(
            Path.ChangeExtension(fullPath, ".cadence.json"),
            System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public void Nes_runner_initial_four_screen_nametables_match_imported_world_tiles()
    {
        var runnerDirectory = RunnerSample.Directory;
        var source = RunnerSample.CompiledSource();
        var program = CompileVideoProgram(source, runnerDirectory);
        var worldMap = Assert.IsType<WorldMap2D>(program.WorldMap);
        var worldTileGrid = Assert.IsType<WorldTileGrid>(program.WorldTileGrid);
        var mismatches = new List<string>();

        for (var y = 0; y < Math.Min(worldMap.Height, 60); y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var expected = (byte)worldTileGrid.TileIdAt(x % worldMap.Width, y);
                var actual = NameTableTileAt(program, x, y);
                if (actual != expected)
                {
                    mismatches.Add($"nametable=({x},{y}) expected=0x{expected:X2} actual=0x{actual:X2}");
                    if (mismatches.Count >= 12)
                    {
                        break;
                    }
                }
            }

            if (mismatches.Count >= 12)
            {
                break;
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public void Nes_lowers_runtime_animation_frame_for_runner_state()
    {
        const string source = """
            void Main() {
                Animation.Clip(run, 1, 6, 6, 6);
                Sprite.Asset(player, "samples/runner/assets/mario-player.png", 18, 32);
                u8 tick = 0;
                while (true) {
                    Video.WaitVBlank();
                    tick++;
                    let frame = Animation.Frame(run, tick);
                    Sprite.Draw(player, 72, 88, frame, false, 0);
                }
            }
            """;

        var rom = NesRomCompiler.CompileSource(source, RepoRoot());
        Assert.NotEmpty(rom);
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

    private static string? RepositoryFileOrNull(string relativePath)
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

        return null;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RetroSharp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new InvalidOperationException("Could not locate RetroSharp repository root.");
    }

    private static NesVideoProgram CompileVideoProgram(string source, string? baseDirectory)
    {
        var parse = new SomeParser().Parse(
            SdkLibrarySource.Merge(
                NesTarget.Intrinsics,
                source,
                libraryImportPaths: [SdkImportResolver.Portable2D]));
        if (parse.IsFailure)
        {
            throw new InvalidOperationException(parse.Error);
        }

        var targetProgram = TargetProgramSelector.Select(parse.Value, NesTarget.Intrinsics);
        var actorProgram = ActorFrameworkLowerer.Lower(targetProgram, NesTarget.Capabilities, supportsUpdate: true, supportsDraw: true, baseDirectory);
        var lowered = SdkSourcePackageFacadeLowerer.Lower(actorProgram);
        return NesVideoProgram.FromProgram(lowered, baseDirectory);
    }

    private static byte NameTableTileAt(NesVideoProgram program, int x, int y)
    {
        var nameTableX = x / 32;
        var nameTableY = y / 30;
        var nameTableBase = (nameTableY * 2 + nameTableX) * 1024;
        return program.NameTable[nameTableBase + y % 30 * 32 + x % 32];
    }
}
