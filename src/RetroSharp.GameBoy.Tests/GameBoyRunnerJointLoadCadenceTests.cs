namespace RetroSharp.GameBoy.Tests;

using RetroSharp.FunctionalAcceptance;
using RetroSharp.Sdk;
using Xunit;
using Xunit.Abstractions;
using CameraMemory = RetroSharp.GameBoy.GameBoyRuntimeMemoryLayout.Camera;
using PackedCameraMemory = RetroSharp.GameBoy.GameBoyRuntimeMemoryLayout.PackedCamera;

public sealed class GameBoyRunnerJointLoadCadenceTests(ITestOutputHelper output)
{
    private const ushort PlayerXLow = 0xC000;
    private const ushort PlayerYLow = 0xC002;
    private const ushort PlayerDisplayFrameLow = 0xC006;
    private const ushort PlayerDisplayFlipX = 0xC008;

    [Fact]
    public void Shared_runner_joint_load_cadence_gate()
    {
        var scenario = FunctionalScenarioLoader.Load(RepositoryFile("validation/scenarios/runner-joint-load.gb.json"));
        var source = RunnerSample.CompiledSource();
        var build = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            RunnerSample.Directory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var program = RetroSharp.GameBoy.GameBoyRomCompiler.PrepareVideoProgram(
            source,
            RunnerSample.Directory,
            SdkLibraryImportMode.ExplicitOnly,
            sdkLibraryRegistry: null,
            sdkLibraryImports: [SdkImportResolver.Portable2D],
            sdkPluginRegistry: null);
        var map = GameBoyTiledMapImporter.Load(
            Path.Combine(RunnerSample.Directory, "assets", "maps", "stage1.tmj"),
            GameBoyVideoProgram.FirstGeneratedBackgroundTile);
        var artifact = new FunctionalRomArtifact("samples/runner/bin/runner.gb (fresh manifest build)", build.Rom);

        const int frozenFrame = 330;
        var collisionLookup = build.Report.FixedSymbols[GameBoyRomBuilder.WorldPackCollisionLookupLabel];
        var stalledFactory = new RunnerJointLoadMachineFactory(program, collisionLookup);
        var stalledReport = FunctionalScenarioRunner.Run(
            scenario,
            artifact,
            Adapter(new FrozenServiceMachineFactory(stalledFactory, frozenFrame, frozenFrames: 2)),
            new RunnerJointLoadOracle(map, program, stalledFactory),
            new FunctionalScenarioRunOptions(FunctionalScenarioRunMode.FailFast, EvidenceFramesBeforeFailure: 8));

        Assert.False(stalledReport.Passed);
        Assert.Equal(build.Rom, stalledFactory.LoadedRom);
        Assert.Equal(frozenFrame + 1, stalledReport.FrameWindow.TotalPhysicalFrames);
        Assert.Contains(
            stalledReport.IntegrityFailures,
            failure => failure.Code == "gameplay-cadence-gap" && failure.Frame == frozenFrame + 1);
        Assert.Contains(
            stalledReport.IntegrityFailures,
            failure => failure.Code == "audio-service-gap" && failure.Frame == frozenFrame + 1);
        Assert.Equal(9, stalledReport.FrameEvidence.Count);
        Assert.Equal(frozenFrame + 1, stalledReport.FrameEvidence[^1].Observed.Frame);
        AssertVisualsIntact(stalledReport.FrameEvidence[^1]);

        var firstFactory = new RunnerJointLoadMachineFactory(program, collisionLookup);
        var first = FunctionalScenarioRunner.Run(
            scenario,
            artifact,
            Adapter(firstFactory),
            new RunnerJointLoadOracle(map, program, firstFactory));
        var secondFactory = new RunnerJointLoadMachineFactory(program, collisionLookup);
        var second = FunctionalScenarioRunner.Run(
            scenario,
            artifact,
            Adapter(secondFactory),
            new RunnerJointLoadOracle(map, program, secondFactory));

        Assert.Equal(build.Rom, firstFactory.LoadedRom);
        Assert.Equal(build.Rom, secondFactory.LoadedRom);
        Assert.Equal(first.Passed, second.Passed);
        Assert.Equal(first.ToJson(), second.ToJson());
        AssertFrameEvidence(first);
        var firstFailure = first.IntegrityFailures.FirstOrDefault();
        output.WriteLine(first.Passed ? "runner-joint-load=NOT_REPRODUCED" : "runner-joint-load=RED");
        output.WriteLine($"runner-joint-load first-failure={firstFailure?.Code ?? "none"}@{firstFailure?.Frame.ToString() ?? "-"}");
    }

    private static GameBoyFunctionalRomAdapter Adapter(IFunctionalRomMachineFactory factory) => new(
        factory,
        new FunctionalAdapterCapabilities(
            GameplayTicks: true,
            AudioService: true,
            AudioProgress: true,
            InputTimeline: true,
            CameraLifecycle: true,
            Background: true,
            SpriteOam: true,
            BankRestoration: true,
            VideoWriteTiming: true));

    private static void AssertFrameEvidence(FunctionalAcceptanceReport report)
    {
        Assert.NotEmpty(report.FrameEvidence);
        Assert.All(
            report.FrameEvidence,
            evidence =>
            {
                var signals = Assert.IsAssignableFrom<IReadOnlyDictionary<string, long>>(evidence.Observed.StateSignals);
                Assert.All(
                    new[]
                    {
                        "cpuCycle", "lcdPhase", "sourceWait", "packedAudioTick", "inputMask",
                        "playerX", "playerY", "playerGrounded", "cameraRequest", "cameraPrepare",
                        "cameraResident", "cameraCommit", "cameraRelease", "visibleRomBank",
                    },
                    signal => Assert.True(signals.ContainsKey(signal), $"frame={evidence.Observed.Frame} missing={signal}"));
                Assert.NotNull(evidence.Observed.AudioProgress);
                Assert.NotNull(evidence.Observed.Camera);
                Assert.NotNull(evidence.Observed.Bank);
                Assert.NotNull(evidence.Observed.Background);
                Assert.NotNull(evidence.Observed.Sprites);
                Assert.NotNull(evidence.Observed.VideoWrites);
                Assert.NotNull(evidence.Observed.OamWrites);
                Assert.NotNull(evidence.Expected);
            });
        Assert.Contains(report.FrameEvidence, evidence => (evidence.Observed.StateSignals!["inputMask"] & 4) != 0);
        Assert.Contains(report.FrameEvidence, evidence => evidence.Observed.StateSignals!["collisionQueries"] > 0);
        Assert.Contains(report.FrameEvidence, evidence => evidence.Observed.StateSignals!["cameraRequest"] > 0);
        Assert.Contains(report.FrameEvidence, evidence => evidence.Observed.AudioProgress!.Music.Active);
        Assert.Contains(report.FrameEvidence, evidence => evidence.Observed.AudioProgress!.SoundEffect.Starts > 0);
        Assert.Contains(report.FrameEvidence, evidence => evidence.Observed.AudioProgress!.SoundEffect.Completions > 0);
    }

    private static void AssertVisualsIntact(FunctionalFrameEvidence evidence)
    {
        var expected = Assert.IsType<FunctionalFrameExpectation>(evidence.Expected);
        Assert.Equal(
            expected.Background!.Select(cell => $"{cell.Location}:{cell.Tile}:{cell.Palette}"),
            evidence.Observed.Background!.Select(cell => $"{cell.Location}:{cell.Tile}:{cell.Palette}"));
        Assert.Equal(
            expected.Sprites!.Select(sprite => $"{sprite.Id}:{sprite.Visible}:{sprite.OamSlot}:{string.Join(',', sprite.Oam)}"),
            evidence.Observed.Sprites!.Select(sprite => $"{sprite.Id}:{sprite.Visible}:{sprite.OamSlot}:{string.Join(',', sprite.Oam)}"));
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

    private sealed class RunnerJointLoadMachineFactory(
        GameBoyVideoProgram program,
        ushort collisionLookup) : IFunctionalRomMachineFactory
    {
        public byte[]? LoadedRom { get; private set; }

        public Dictionary<int, RunnerDrawState> DrawStates { get; } = [];

        public Dictionary<int, (int X, int Y)> VisibleCameraByFrame { get; } = [];

        public IFunctionalRomMachine Create(ReadOnlyMemory<byte> exactRom)
        {
            LoadedRom = exactRom.ToArray();
            return new RunnerJointLoadMachine(LoadedRom, program, collisionLookup, DrawStates, VisibleCameraByFrame);
        }
    }

    private sealed class RunnerJointLoadMachine(
        byte[] exactRom,
        GameBoyVideoProgram program,
        ushort collisionLookup,
        IDictionary<int, RunnerDrawState> drawStates,
        IDictionary<int, (int X, int Y)> visibleCameraByFrame) : IFunctionalRomMachine
    {
        private readonly GameBoyTestCpu cpu = new(exactRom)
        {
            CycleAccurateLy = true,
            EnforceVblankVramWrites = true,
            TracedWorldPackCollisionLookupEntry = collisionLookup,
        };
        private readonly GameBoyCompiledSpriteAsset playerAsset = program.SpriteAssets["mario_player"];
        private int lastFrame;
        private int processedVramWrites;
        private int processedOamWrites;
        private int processedApuWrites;
        private byte previousRequest;
        private byte previousResident;
        private byte previousCommit;
        private byte previousRelease;
        private long requestSequence;
        private long residentSequence;
        private long commitSequence;
        private long releaseSequence;
        private bool previousMusicActive;
        private bool previousSfxActive;
        private long musicStarts;
        private long musicCompletions;
        private long sfxStarts;
        private long sfxCompletions;

        public FunctionalFrameObservation ObserveInitial() => Observe(0, EmptyDrawState, 0);

        public FunctionalFrameObservation AdvanceFrame(int frame, IReadOnlySet<string> heldInputs)
        {
            if (frame != lastFrame + 1)
            {
                throw new InvalidOperationException($"Expected frame {lastFrame + 1}, received {frame}.");
            }

            var drawState = CaptureDrawState();
            cpu.Held.Clear();
            cpu.Held.UnionWith(heldInputs);
            cpu.RunAdditionalFrames(1);
            lastFrame = frame;
            return Observe(frame, drawState, InputMask(heldInputs));
        }

        public void Dispose()
        {
        }

        private FunctionalFrameObservation Observe(int frame, RunnerDrawState drawState, int inputMask)
        {
            var visibleCamera = (ReadWord(PackedCameraMemory.VisibleCameraXLow), ReadWord(PackedCameraMemory.VisibleCameraYLow));
            drawStates[frame] = drawState;
            visibleCameraByFrame[frame] = visibleCamera;
            var audio = CaptureAudioProgress();
            var signals = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["cpuCycle"] = cpu.Cycles,
                ["lcdPhase"] = cpu.IoRegister(0xFF44) >= 144 ? 1 : 0,
                ["sourceWait"] = cpu.SourceWaitCompletions,
                ["packedAudioTick"] = cpu.Wram(PackedCameraMemory.AudioTickCount),
                ["apuEventCount"] = audio.RegisterEventCount,
                ["sfxStarts"] = audio.SoundEffect.Starts,
                ["sfxActive"] = audio.SoundEffect.Active ? 1 : 0,
                ["sfxCompletions"] = audio.SoundEffect.Completions,
                ["musicActive"] = audio.Music.Active ? 1 : 0,
                ["inputMask"] = inputMask,
                ["playerX"] = ReadWord(PlayerXLow),
                ["playerY"] = ReadWord(PlayerYLow),
                ["playerGrounded"] = cpu.Wram(0xC005),
                ["playerDisplayFrame"] = ReadWord(PlayerDisplayFrameLow),
                ["cameraRequest"] = cpu.Wram(PackedCameraMemory.RequestCount),
                ["cameraPrepare"] = cpu.Wram(PackedCameraMemory.PrepareCount),
                ["cameraResident"] = cpu.Wram(PackedCameraMemory.ResidentCount),
                ["cameraCommit"] = cpu.Wram(PackedCameraMemory.CommitCount),
                ["cameraRelease"] = cpu.Wram(PackedCameraMemory.ReleaseCount),
                ["visibleRomBank"] = cpu.CurrentRomBank,
                ["collisionQueries"] = cpu.WorldPackCollisionQueries.Count,
            };
            var shadowBank = cpu.Wram(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank);
            var effectiveShadowBank = shadowBank == 0 ? 1 : shadowBank;
            return new FunctionalFrameObservation(
                frame,
                cpu.SourceWaitCompletions,
                cpu.AudioUpdateCalls,
                cpu.ResetCount,
                signals,
                CaptureCamera(visibleCamera),
                new FunctionalBankObservation(cpu.CurrentRomBank, effectiveShadowBank, cpu.CurrentRomBank == effectiveShadowBank, "gb-mbc1"),
                CaptureBackground(visibleCamera),
                CaptureSprites(),
                CaptureVideoWrites(),
                CaptureOamWrites(),
                AudioProgress: audio);
        }

        private RunnerDrawState CaptureDrawState() => new(
            ReadWord(PlayerXLow),
            ReadWord(PlayerYLow),
            ReadWord(PlayerDisplayFrameLow),
            cpu.Wram(PlayerDisplayFlipX) != 0,
            ReadWord(CameraMemory.XLow),
            ReadWord(CameraMemory.YLow));

        private FunctionalCameraLifecycleObservation CaptureCamera((int X, int Y) visibleCamera) => new(
            UpdateSequence(PackedCameraMemory.RequestCount, ref previousRequest, ref requestSequence),
            UpdateSequence(PackedCameraMemory.ResidentCount, ref previousResident, ref residentSequence),
            UpdateSequence(PackedCameraMemory.CommitCount, ref previousCommit, ref commitSequence),
            UpdateSequence(PackedCameraMemory.ReleaseCount, ref previousRelease, ref releaseSequence));

        private long? UpdateSequence(ushort address, ref byte previous, ref long sequence)
        {
            var current = cpu.Wram(address);
            sequence += (byte)(current - previous);
            previous = current;
            return sequence == 0 ? null : sequence;
        }

        private FunctionalAudioProgressObservation CaptureAudioProgress()
        {
            var events = cpu.ApuWrites.Skip(processedApuWrites)
                .Select(write => new FunctionalAudioRegisterEvent("gb-apu", write.Register, write.Value))
                .ToArray();
            processedApuWrites = cpu.ApuWrites.Count;
            var musicActive = cpu.Wram(GameBoyRuntimeMemoryLayout.Audio.MusicActive) != 0;
            var sfxActive = cpu.Wram(GameBoyRuntimeMemoryLayout.Audio.SfxActive) != 0;
            UpdateLifecycle(musicActive, previousMusicActive, ref musicStarts, ref musicCompletions);
            UpdateLifecycle(sfxActive, previousSfxActive, ref sfxStarts, ref sfxCompletions);
            previousMusicActive = musicActive;
            previousSfxActive = sfxActive;
            return new(
                cpu.ApuWrites.Count,
                events,
                new FunctionalAudioPlaybackObservation(musicActive, musicStarts, musicCompletions, 0),
                new FunctionalAudioPlaybackObservation(sfxActive, sfxStarts, sfxCompletions, 0),
                new FunctionalAudioPlaybackObservation(false, 0, 0, 0));
        }

        private IReadOnlyList<FunctionalBackgroundObservation> CaptureBackground((int X, int Y) camera)
        {
            var width = camera.X % 8 == 0 ? 20 : 21;
            var height = camera.Y % 8 == 0 ? 18 : 19;
            var startColumn = camera.X / 8;
            var startRow = camera.Y / 8;
            return Enumerable.Range(0, height)
                .SelectMany(y => Enumerable.Range(0, width).Select(x => new FunctionalBackgroundObservation(
                    $"screen:{x:D2},{y:D2}",
                    cpu.Vram((ushort)(0x9800 + ((startRow + y) & 31) * 32 + ((startColumn + x) & 31))),
                    cpu.IoRegister(0xFF47))))
                .ToArray();
        }

        private IReadOnlyList<FunctionalSpriteObservation> CaptureSprites()
        {
            var playerLength = playerAsset.Pieces.Count * 4;
            var player = Enumerable.Range(0, playerLength)
                .Select(offset => (int)cpu.Oam((ushort)(0xFE00 + offset)))
                .ToArray();
            var unused = Enumerable.Range(playerLength, 160 - playerLength)
                .Select(offset => (int)cpu.Oam((ushort)(0xFE00 + offset)))
                .ToArray();
            return
            [
                new FunctionalSpriteObservation("player", OamVisible(player), player, 0),
                new FunctionalSpriteObservation("unused-oam", OamVisible(unused), unused, playerAsset.Pieces.Count),
            ];
        }

        private IReadOnlyList<FunctionalVideoWriteObservation> CaptureVideoWrites()
        {
            var writes = cpu.VramWrites.Skip(processedVramWrites)
                .Select(write => new FunctionalVideoWriteObservation(
                    "gb-vram",
                    write.Address,
                    write.Applied,
                    Timing(write.Cycles, write.Ly, write.LcdEnabled)))
                .ToArray();
            processedVramWrites = cpu.VramWrites.Count;
            return writes;
        }

        private IReadOnlyList<FunctionalOamWriteObservation> CaptureOamWrites()
        {
            var writes = cpu.OamWrites.Skip(processedOamWrites)
                .Select(write => new FunctionalOamWriteObservation(
                    write.Address,
                    !write.LcdEnabled || write.Ly >= 144,
                    Timing(write.Cycles, write.Ly, write.LcdEnabled)))
                .ToArray();
            processedOamWrites = cpu.OamWrites.Count;
            return writes;
        }

        private static FunctionalWriteTimingObservation Timing(long cycles, byte ly, bool lcdEnabled) => new(
            cycles,
            ly,
            (int)(cycles % 456),
            !lcdEnabled ? "lcd-off" : ly >= 144 ? "vblank" : "visible",
            lcdEnabled);

        private static void UpdateLifecycle(bool active, bool previous, ref long starts, ref long completions)
        {
            if (active && !previous)
            {
                starts++;
            }
            else if (!active && previous)
            {
                completions++;
            }
        }

        private static int InputMask(IReadOnlySet<string> heldInputs) =>
            (heldInputs.Contains("right", StringComparer.OrdinalIgnoreCase) ? 1 : 0)
            | (heldInputs.Contains("b", StringComparer.OrdinalIgnoreCase) ? 2 : 0)
            | (heldInputs.Contains("a", StringComparer.OrdinalIgnoreCase) ? 4 : 0);

        private int ReadWord(ushort low) => cpu.Wram(low) | (cpu.Wram((ushort)(low + 1)) << 8);

        private static bool OamVisible(IReadOnlyList<int> oam) =>
            oam.Chunk(4).Any(piece => piece[0] is > 0 and < 160);

        private static readonly RunnerDrawState EmptyDrawState = new(0, 0, 0, false, 0, 0);
    }

    private sealed class RunnerJointLoadOracle(
        GameBoyTiledMap map,
        GameBoyVideoProgram program,
        RunnerJointLoadMachineFactory factory) : IFunctionalFrameOracle
    {
        private readonly GameBoyCompiledSpriteAsset playerAsset = program.SpriteAssets["mario_player"];

        public FunctionalFrameExpectation ExpectedFrame(int frame)
        {
            var camera = factory.VisibleCameraByFrame[frame];
            var draw = factory.DrawStates[frame];
            var width = camera.X % 8 == 0 ? 20 : 21;
            var height = camera.Y % 8 == 0 ? 18 : 19;
            var background = Enumerable.Range(0, height)
                .SelectMany(y => Enumerable.Range(0, width).Select(x => new FunctionalBackgroundExpectation(
                    $"screen:{x:D2},{y:D2}",
                    AuthoredTile(camera.X / 8 + x, camera.Y / 8 + y),
                    0xE4)))
                .ToArray();
            var player = playerAsset.Pieces.SelectMany(piece =>
            {
                var pieceX = draw.FlipX
                    ? playerAsset.LogicalWidth - 8 - piece.XOffset
                    : piece.XOffset;
                return new[]
                {
                    (draw.PlayerY - draw.CameraY + 16 + piece.YOffset) & 0xFF,
                    (draw.PlayerX - draw.CameraX + 8 + pieceX) & 0xFF,
                    playerAsset.FirstTile + draw.AnimationFrame * playerAsset.TilesPerFrame + piece.TileOffset,
                    draw.FlipX ? 0x20 : 0,
                };
            }).ToArray();
            return new(
                frame,
                background,
                [
                    new FunctionalSpriteExpectation("player", OamVisible(player), player, 0),
                    new FunctionalSpriteExpectation(
                        "unused-oam",
                        false,
                        Enumerable.Repeat(0, 160 - player.Length).ToArray(),
                        playerAsset.Pieces.Count),
                ]);
        }

        private int AuthoredTile(int x, int y)
        {
            var wrappedX = x % map.Width;
            var wrappedY = y % map.Height;
            return map.WorldTileIds[wrappedY * map.Width + wrappedX];
        }

        private static bool OamVisible(IReadOnlyList<int> oam) =>
            oam.Chunk(4).Any(piece => piece[0] is > 0 and < 160);
    }

    private sealed record RunnerDrawState(
        int PlayerX,
        int PlayerY,
        int AnimationFrame,
        bool FlipX,
        int CameraX,
        int CameraY);
}
