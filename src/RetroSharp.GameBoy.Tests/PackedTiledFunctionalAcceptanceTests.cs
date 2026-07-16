namespace RetroSharp.GameBoy.Tests;

using RetroSharp.FunctionalAcceptance;
using Xunit;
using Xunit.Abstractions;
using PackedCameraMemory = RetroSharp.GameBoy.GameBoyRuntimeMemoryLayout.PackedCamera;

public sealed class PackedTiledFunctionalAcceptanceTests(ITestOutputHelper output)
{

    public static TheoryData<string, string, string, string, string> ProductionSamples => new()
    {
        { "tiled-tall", "samples/tiled-tall/tall.rs", "samples/tiled-tall/tall.tmj", "samples/tiled-tall/tall.gb", "validation/scenarios/tiled-tall.gb.json" },
        { "tiled-hscroll-short", "samples/tiled-hscroll/hscroll-short.rs", "samples/tiled-hscroll/stage1-short.tmj", "samples/tiled-hscroll/hscroll-short.gb", "validation/scenarios/tiled-hscroll-short.gb.json" },
        { "tiled-hscroll-full", "samples/tiled-hscroll/hscroll-full.rs", "samples/tiled-hscroll/stage1-full.tmj", "samples/tiled-hscroll/hscroll-full.gb", "validation/scenarios/tiled-hscroll-full.gb.json" },
        { "tiled-hscroll-offset", "samples/tiled-hscroll/hscroll-offset.rs", "samples/tiled-hscroll/stage1-full.tmj", "samples/tiled-hscroll/hscroll-offset.gb", "validation/scenarios/tiled-hscroll-offset.gb.json" },
        { "tiled-vscroll", "samples/tiled-vscroll/vscroll.rs", "samples/tiled-vscroll/vscroll.tmj", "samples/tiled-vscroll/vscroll.gb", "validation/scenarios/tiled-vscroll.gb.json" },
        { "tiled-diagonal", "samples/tiled-diagonal/diag.rs", "samples/tiled-diagonal/diag.tmj", "samples/tiled-diagonal/diag.gb", "validation/scenarios/tiled-diagonal.gb.json" },
        { "tiled-free-scroll", "samples/tiled-free-scroll/free-scroll.rs", "samples/tiled-free-scroll/free-scroll.tmj", "samples/tiled-free-scroll/free-scroll.gb", "validation/scenarios/tiled-free-scroll.gb.json" },
        { "deadzone-follow", "samples/deadzone-follow/deadzone.rs", "samples/deadzone-follow/deadzone.tmj", "samples/deadzone-follow/deadzone.gb", "validation/scenarios/deadzone-follow.gb.json" },
        { "platformer-landing", "samples/platformer-landing/src/main.rs", "samples/platformer-landing/assets/platformer-landing.tmj", "samples/platformer-landing/bin/platformer-landing.gb", "validation/scenarios/platformer-landing.gb.json" },
    };

    [Theory]
    [MemberData(nameof(ProductionSamples))]
    public void Exact_production_rom_passes_packed_tiled_functional_acceptance(
        string sampleId,
        string sourceRelativePath,
        string mapRelativePath,
        string romRelativePath,
        string scenarioRelativePath)
    {
        var sourcePath = RepositoryFile(sourceRelativePath);
        var sourceDirectory = sampleId == "platformer-landing"
            ? Path.GetDirectoryName(Path.GetDirectoryName(sourcePath))
            : Path.GetDirectoryName(sourcePath);
        sourceDirectory = sourceDirectory
            ?? throw new InvalidOperationException($"Could not locate '{sourceRelativePath}'.");
        var source = File.ReadAllText(sourcePath);
        GameBoyVideoProgram? platformerProgram = null;
        GameBoyRomBuildReport? platformerBuildReport = null;
        byte[] regeneratedRom;
        if (sampleId == "platformer-landing")
        {
            platformerProgram = RetroSharp.GameBoy.GameBoyRomCompiler.PrepareVideoProgram(
                source,
                sourceDirectory,
                RetroSharp.Sdk.SdkLibraryImportMode.ExplicitOnly,
                null,
                [RetroSharp.Sdk.SdkImportResolver.Portable2D],
                null);
            var build = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
                source,
                sourceDirectory,
                RetroSharp.Sdk.SdkLibraryImportMode.ExplicitOnly,
                null,
                [RetroSharp.Sdk.SdkImportResolver.Portable2D],
                null);
            platformerBuildReport = build.Report;
            regeneratedRom = build.Rom;
        }
        else
        {
            regeneratedRom = GameBoyRomCompiler.CompileSource(source, sourceDirectory);
        }
        var trackedRom = File.ReadAllBytes(RepositoryFile(romRelativePath));
        Assert.Equal(trackedRom, regeneratedRom);

        var map = GameBoyTiledMapImporter.Load(RepositoryFile(mapRelativePath));
        if (sampleId.StartsWith("tiled-hscroll-", StringComparison.Ordinal))
        {
            Assert.All(map.WorldFlags, flags => Assert.Equal(0, (int)flags));
        }

        var scenario = FunctionalScenarioLoader.Load(RepositoryFile(scenarioRelativePath));
        Assert.Equal(sampleId, scenario.SampleId);
        var factory = new PackedGameBoyMachineFactory(platformerProgram, platformerBuildReport);
        var adapter = new GameBoyFunctionalRomAdapter(
            factory,
            new FunctionalAdapterCapabilities(
                GameplayTicks: true,
                InputTimeline: sampleId == "platformer-landing",
                CameraLifecycle: true,
                BankRestoration: true,
                Background: true,
                SpriteOam: sampleId == "platformer-landing",
                VideoWriteTiming: true));
        var report = FunctionalScenarioRunner.Run(
            scenario,
            new FunctionalRomArtifact(romRelativePath, trackedRom),
            adapter,
            new AuthoredTiledBackgroundOracle(map, factory, platformerProgram, scenario));

        output.WriteLine($"{report.ScenarioId}: {report.Summary}");
        foreach (var check in report.TimingChecks)
        {
            output.WriteLine($"{check.Metric}: observed={check.Observed:0.###} limit={check.Limit:0.###} headroom={check.Headroom:0.###}");
        }

        Assert.True(report.Passed, Diagnostic(report));
        Assert.Equal(trackedRom, factory.LoadedRom);
        Assert.All(report.TimingChecks, check => Assert.True(check.Passed, check.Metric));
        Assert.Empty(report.IntegrityFailures);
        Assert.Equal(0, report.Summary.BankRestorationFailures);
        Assert.Equal(0, report.Summary.UnsafeVideoWrites);
        Assert.Equal(0, report.Summary.UnsafeOamWrites);
        Assert.Equal(0, report.Summary.BackgroundMismatches);
        Assert.All(
            report.FrameEvidence,
            evidence =>
            {
                foreach (var signal in new[]
                         {
                             "bankWorkInCommit",
                             "decodeWorkInCommit",
                             "directoryWorkInVBlank",
                             "directoryWorkInCommit",
                             "decodeWorkInVBlank",
                         })
                {
                    var value = evidence.Observed.StateSignals![signal];
                    Assert.True(value == 0, $"frame={evidence.Observed.Frame} signal={signal} value={value}");
                }
            });
    }

    private static string Diagnostic(FunctionalAcceptanceReport report)
    {
        var failureFrames = report.IntegrityFailures.Select(failure => failure.Frame).Distinct().Take(5).ToHashSet();
        var state = report.FrameEvidence
            .Where(evidence => failureFrames.Contains(evidence.Observed.Frame))
            .Select(evidence => $"frame={evidence.Observed.Frame} "
                + string.Join(',', evidence.Observed.StateSignals!.Select(signal => $"{signal.Key}={signal.Value}")));
        return $"{report.ScenarioId}: {report.Summary}"
            + Environment.NewLine
            + string.Join(Environment.NewLine, report.IntegrityFailures.Take(20))
            + Environment.NewLine
            + string.Join(Environment.NewLine, state);
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

    private sealed class PackedGameBoyMachineFactory(
        GameBoyVideoProgram? platformerProgram,
        GameBoyRomBuildReport? platformerBuildReport) : IFunctionalRomMachineFactory
    {
        public byte[]? LoadedRom { get; private set; }

        public Dictionary<int, (int X, int Y)> VisibleCameraByFrame { get; } = [];

        public IFunctionalRomMachine Create(ReadOnlyMemory<byte> exactRom)
        {
            LoadedRom = exactRom.ToArray();
            return new PackedGameBoyMachine(
                LoadedRom,
                VisibleCameraByFrame,
                platformerProgram,
                platformerBuildReport);
        }
    }

    private sealed class PackedGameBoyMachine(
        byte[] exactRom,
        IDictionary<int, (int X, int Y)> visibleCameraByFrame,
        GameBoyVideoProgram? platformerProgram,
        GameBoyRomBuildReport? platformerBuildReport) : IFunctionalRomMachine
    {
        private readonly GameBoyTestCpu cpu = new(exactRom)
        {
            CycleAccurateLy = true,
            EnforceVblankVramWrites = true,
        };
        private int lastFrame;
        private int processedVramWrites;
        private int processedOamWrites;
        private byte previousRequestCount;
        private byte previousResidentCount;
        private byte previousCommitCount;
        private long requestSequence;
        private long residentSequence;
        private long commitSequence;
        private long visibleSequence;
        private (int X, int Y)? previousVisibleCamera;
        private readonly IReadOnlyDictionary<string, GameBoyRuntimeUserVariable> variables =
            platformerBuildReport?.UserVariables.ToDictionary(variable => variable.Name, StringComparer.Ordinal)
            ?? new Dictionary<string, GameBoyRuntimeUserVariable>(StringComparer.Ordinal);
        private readonly GameBoyCompiledSpriteAsset? playerAsset = platformerProgram?.SpriteAssets
            .Single(pair => pair.Key.EndsWith("player_sprite", StringComparison.Ordinal)).Value;

        public FunctionalFrameObservation ObserveInitial() => Observe(0);

        public FunctionalFrameObservation AdvanceFrame(int frame, IReadOnlySet<string> heldInputs)
        {
            if (frame != lastFrame + 1)
            {
                throw new InvalidOperationException($"Expected frame {lastFrame + 1}, received {frame}.");
            }

            cpu.Held.Clear();
            cpu.Held.UnionWith(heldInputs.Select(button => button.ToLowerInvariant()));
            cpu.RunFrames(frame);
            lastFrame = frame;
            return Observe(frame);
        }

        public void Dispose()
        {
        }

        private FunctionalFrameObservation Observe(int frame)
        {
            var visibleCamera = (X: Word(PackedCameraMemory.VisibleCameraXLow, PackedCameraMemory.VisibleCameraXHigh), Y: Word(PackedCameraMemory.VisibleCameraYLow, PackedCameraMemory.VisibleCameraYHigh));
            visibleCameraByFrame[frame] = visibleCamera;
            var videoWrites = cpu.VramWrites.Skip(processedVramWrites).Select(VideoWrite).ToArray();
            var oamWrites = cpu.OamWrites.Skip(processedOamWrites).Select(OamWrite).ToArray();
            processedVramWrites = cpu.VramWrites.Count;
            processedOamWrites = cpu.OamWrites.Count;
            var state = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["user0"] = cpu.Wram(0xC000),
                ["user1"] = cpu.Wram(0xC001),
                ["user2"] = cpu.Wram(0xC002),
                ["user3"] = cpu.Wram(0xC003),
                ["user4"] = cpu.Wram(0xC004),
                ["user5"] = cpu.Wram(0xC005),
                ["visibleCameraX"] = visibleCamera.X,
                ["visibleCameraY"] = visibleCamera.Y,
                ["requestCount"] = cpu.Wram(PackedCameraMemory.RequestCount),
                ["residentCount"] = cpu.Wram(PackedCameraMemory.ResidentCount),
                ["commitCount"] = cpu.Wram(PackedCameraMemory.CommitCount),
                ["releaseCount"] = cpu.Wram(PackedCameraMemory.ReleaseCount),
                ["bankWorkInCommit"] = cpu.Wram(PackedCameraMemory.BankWorkInCommit),
                ["decodeWorkInCommit"] = cpu.Wram(PackedCameraMemory.DecodeWorkInCommit),
                ["directoryWorkInVBlank"] = cpu.Wram(PackedCameraMemory.DirectoryWorkInVBlank),
                ["directoryWorkInCommit"] = cpu.Wram(PackedCameraMemory.DirectoryWorkInCommit),
                ["decodeWorkInVBlank"] = cpu.Wram(PackedCameraMemory.DecodeWorkInVBlank),
            };
            AddPlatformerState(state);
            var sprites = CapturePlatformerSprites();
            var shadowBank = cpu.Wram(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank);
            var effectiveShadowBank = shadowBank == 0 ? 1 : shadowBank;
            var bank = new FunctionalBankObservation(
                cpu.CurrentRomBank,
                effectiveShadowBank,
                cpu.CurrentRomBank == effectiveShadowBank,
                "gb-mbc1");
            var requested = UpdateSequence(PackedCameraMemory.RequestCount, ref previousRequestCount, ref requestSequence);
            var resident = UpdateSequence(PackedCameraMemory.ResidentCount, ref previousResidentCount, ref residentSequence);
            var committed = UpdateSequence(PackedCameraMemory.CommitCount, ref previousCommitCount, ref commitSequence);
            if (commitSequence != 0
                && previousVisibleCamera is { } previousVisible
                && visibleCamera != previousVisible)
            {
                visibleSequence = commitSequence;
            }

            previousVisibleCamera = visibleCamera;
            long? visible = visibleSequence == 0 ? null : visibleSequence;
            var camera = new FunctionalCameraLifecycleObservation(requested, resident, committed, visible);

            return new FunctionalFrameObservation(
                frame,
                cpu.SourceWaitCompletions,
                cpu.AudioUpdateCalls,
                cpu.ResetCount,
                state,
                camera,
                bank,
                CaptureBackground(visibleCamera),
                Sprites: sprites,
                VideoWrites: videoWrites,
                OamWrites: oamWrites);
        }

        private void AddPlatformerState(IDictionary<string, long> state)
        {
            if (playerAsset is null)
            {
                return;
            }

            var playerY = VariableWord("player.y");
            state["playerX"] = VariableWord("player.x");
            state["playerY"] = playerY;
            state["worldFootY"] = playerY + 31;
            state["grounded"] = VariableByte("player.grounded");
            state["jumpCount"] = VariableByte("player.jumpCount");
            state["landingCount"] = VariableByte("player.landingCount");
            state["gameplayResetCount"] = VariableByte("player.gameplayResetCount");
            state["supportProbeCount"] = VariableByte("player.supportProbeCount");
            state["wallContactCount"] = VariableByte("player.wallContactCount");
            state["sourceCameraX"] = VariableWord("view.x");
            state["sourceCameraY"] = VariableWord("view.y");
            state["displayEnabled"] = (cpu.IoRegister(0xFF40) & 0x80) != 0 ? 1 : 0;
        }

        private IReadOnlyList<FunctionalSpriteObservation>? CapturePlatformerSprites()
        {
            if (playerAsset is null)
            {
                return null;
            }

            var playerOamBytes = playerAsset.Pieces.Count * 4;
            var actualPlayer = Enumerable.Range(0, playerOamBytes)
                .Select(offset => (int)cpu.Oam((ushort)(0xFE00 + offset)))
                .ToArray();
            var actualUnused = Enumerable.Range(playerOamBytes, 160 - playerOamBytes)
                .Select(offset => (int)cpu.Oam((ushort)(0xFE00 + offset)))
                .ToArray();

            return
            [
                new FunctionalSpriteObservation("player", OamVisible(actualPlayer), actualPlayer, 0),
                new FunctionalSpriteObservation("unused-oam", OamVisible(actualUnused), actualUnused, playerAsset.Pieces.Count),
            ];
        }

        private int VariableWord(string name) => VariableByte(name) | (VariableByte(name, 1) << 8);

        private byte VariableByte(string name, int offset = 0) => cpu.Wram(checked((ushort)(variables[name].Address + offset)));

        private static bool OamVisible(IReadOnlyList<int> oam) =>
            oam.Chunk(4).Any(piece => piece[0] is > 0 and < 160);

        private IReadOnlyList<FunctionalBackgroundObservation> CaptureBackground((int X, int Y) camera)
        {
            var width = camera.X % 8 == 0 ? 20 : 21;
            var height = camera.Y % 8 == 0 ? 18 : 19;
            var startColumn = camera.X / 8;
            var startRow = camera.Y / 8;
            return Enumerable.Range(0, height)
                .SelectMany(y => Enumerable.Range(0, width).Select(x =>
                {
                    var address = (ushort)(0x9800 + ((startRow + y) & 31) * 32 + ((startColumn + x) & 31));
                    return new FunctionalBackgroundObservation(
                        $"screen:{x:D2},{y:D2}",
                        cpu.Vram(address),
                        cpu.IoRegister(0xFF47));
                }))
                .ToArray();
        }

        private int Word(ushort low, ushort high) => cpu.Wram(low) | (cpu.Wram(high) << 8);

        private long? UpdateSequence(ushort address, ref byte previous, ref long sequence)
        {
            var current = cpu.Wram(address);
            sequence += (byte)(current - previous);
            previous = current;
            return sequence == 0 ? null : sequence;
        }

        private static FunctionalVideoWriteObservation VideoWrite(VramWrite write) => new(
            "gb-vram",
            write.Address,
            write.Applied,
            Timing(write.Cycles, write.Ly, write.LcdEnabled));

        private static FunctionalOamWriteObservation OamWrite(RetroSharp.GameBoy.Tests.OamWrite write) => new(
            write.Address,
            !write.LcdEnabled || write.Ly >= 144,
            Timing(write.Cycles, write.Ly, write.LcdEnabled));

        private static FunctionalWriteTimingObservation Timing(long cycles, byte ly, bool lcdEnabled) => new(
            cycles,
            ly,
            (int)(cycles % 456),
            !lcdEnabled ? "lcd-off" : ly >= 144 ? "vblank" : "visible",
            lcdEnabled);
    }

    private sealed class AuthoredTiledBackgroundOracle(
        GameBoyTiledMap map,
        PackedGameBoyMachineFactory factory,
        GameBoyVideoProgram? platformerProgram,
        FunctionalScenario scenario) : IFunctionalFrameOracle
    {
        private static readonly HashSet<int> PlatformerMissedGameplayFrames =
        [
            194, 202, 210, 218, 226, 234, 242, 250, 258, 266, 274, 281, 283,
            291, 299, 307, 315, 323, 331, 339, 347, 355, 363, 371, 379, 387,
            395, 403, 410, 412, 420, 428, 436, 444, 452, 600, 602, 609, 611,
            618, 620, 627, 629, 636, 638, 645, 647, 654, 656, 663, 665, 672,
            674, 681, 683, 690, 692, 699, 701, 708, 710, 717, 719, 726, 728,
            735, 737, 744, 746, 753, 755, 762, 764, 771, 773, 780, 782, 789,
            791, 798, 800, 807, 809, 816, 818, 825, 827, 1005, 1007, 1014,
            1016, 1023, 1025, 1032, 1034, 1041, 1043, 1050, 1052, 1059, 1061,
        ];
        private static readonly HashSet<int> PlatformerDoubleGameplayFrames =
        [
            195, 203, 211, 219, 227, 235, 243, 251, 259, 267, 275, 284, 292,
            300, 308, 316, 324, 332, 340, 348, 356, 364, 372, 380, 388, 396,
            404, 413, 421, 429, 437, 445, 453, 603, 612, 621, 630, 639, 648,
            657, 666, 675, 684, 693, 702, 711, 720, 729, 738, 747, 756, 765,
            774, 783, 792, 801, 810, 819, 828, 1008, 1017, 1026, 1035, 1044,
            1053, 1062,
        ];
        private readonly GameBoyCompiledSpriteAsset? playerAsset = platformerProgram?.SpriteAssets
            .Single(pair => pair.Key.EndsWith("player_sprite", StringComparison.Ordinal)).Value;
        private readonly PlatformerLandingReferenceTimeline? platformerTimeline = platformerProgram is null
            ? null
            : new PlatformerLandingReferenceTimeline(
                scenario,
                cameraY: 176,
                inputDelayFrames: 1,
                missedGameplayFrames: PlatformerMissedGameplayFrames,
                doubleGameplayFrames: PlatformerDoubleGameplayFrames);

        public FunctionalFrameExpectation ExpectedFrame(int frame)
        {
            var camera = factory.VisibleCameraByFrame[frame];
            var width = camera.X % 8 == 0 ? 20 : 21;
            var height = camera.Y % 8 == 0 ? 18 : 19;
            var startColumn = camera.X / 8;
            var startRow = camera.Y / 8;
            return new FunctionalFrameExpectation(
                frame,
                Enumerable.Range(0, height)
                    .SelectMany(y => Enumerable.Range(0, width).Select(x =>
                        new FunctionalBackgroundExpectation(
                            $"screen:{x:D2},{y:D2}",
                            AuthoredTile(startColumn + x, startRow + y),
                            0xE4)))
                    .ToArray(),
                PlatformerSprites(frame));
        }

        private IReadOnlyList<FunctionalSpriteExpectation>? PlatformerSprites(int frame)
        {
            if (playerAsset is null || platformerTimeline is null)
            {
                return null;
            }

            var state = platformerTimeline.ExpectedDrawState(frame);
            var screenX = state.PlayerX - state.CameraX;
            var screenY = state.PlayerY - state.CameraY;
            var spriteFrame = state.Grounded ? 0 : 4;
            var expectedPlayer = playerAsset.Pieces.SelectMany(piece =>
            {
                var pieceX = state.FlipX
                    ? playerAsset.LogicalWidth - 8 - piece.XOffset
                    : piece.XOffset;
                return new[]
                {
                    (screenY + piece.YOffset + 16) & 0xFF,
                    (screenX + pieceX + 8) & 0xFF,
                    playerAsset.FirstTile + spriteFrame * playerAsset.TilesPerFrame + piece.TileOffset,
                    state.FlipX ? 0x20 : 0,
                };
            }).ToArray();
            return
            [
                new FunctionalSpriteExpectation("player", OamVisible(expectedPlayer), expectedPlayer, 0),
                new FunctionalSpriteExpectation(
                    "unused-oam",
                    false,
                    Enumerable.Repeat(0, 160 - expectedPlayer.Length).ToArray(),
                    playerAsset.Pieces.Count),
            ];
        }

        private static bool OamVisible(IReadOnlyList<int> oam) =>
            oam.Chunk(4).Any(piece => piece[0] is > 0 and < 160);

        private int AuthoredTile(int x, int y)
        {
            var wrappedX = x % map.Width;
            var wrappedY = y % map.Height;
            return map.WorldTileIds[wrappedY * map.Width + wrappedX];
        }
    }
}
