namespace RetroSharp.NES.Tests;

using RetroSharp.FunctionalAcceptance;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;
using Xunit.Abstractions;

public sealed class ActorProjectileFunctionalAcceptanceTests(ITestOutputHelper output)
{
    [Fact]
    public void Logical_sprite_draws_publish_one_retained_oam_dma_per_vblank()
    {
        const string source = """
                              import RetroSharp.Portable2D;
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(marker, "actor.json");
                                  u8 x = 12;
                                  while (true) {
                                      Video.WaitVBlank();
                                      Sprite.Draw(marker, x, 20, 0, false, 0);
                                      Sprite.Draw(marker, 40, 40, 0, false, 0);
                                      x += 1;
                                  }
                              }
                              """;
        var rom = RetroSharp.NES.NesRomCompiler.CompileSource(
            source,
            RepositoryDirectory("samples/actor-framework"),
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var cpu = new NesTestCpu(rom);

        cpu.RunFrames(6);

        Assert.InRange(cpu.OamDmaTransfers.Count, 3, 7);
        Assert.All(cpu.OamDmaTransfers, transfer =>
        {
            Assert.Equal(0x02, transfer.SourcePage);
            Assert.Equal(transfer.SourceSnapshot, transfer.RamSnapshot[0x200..0x300]);
            Assert.True(!transfer.RenderingEnabled || cpu.PpuTiming(transfer.Cycle, true).Phase == "vblank");
        });
        Assert.All(
            cpu.OamDmaTransfers.Skip(1).Zip(cpu.OamDmaTransfers.Skip(2)),
            pair => Assert.True(pair.Second.Cycle - pair.First.Cycle > 20_000));
        var latest = cpu.OamDmaTransfers[^1];
        Assert.Equal(latest.SourceSnapshot, Enumerable.Range(0, 256).Select(index => cpu.Oam((byte)index)));
        var publishedX = cpu.OamDmaTransfers
            .Select(transfer => transfer.SourceSnapshot[3])
            .Where(value => value != 0xFF)
            .ToArray();
        Assert.True(publishedX.Length >= 2);
        Assert.All(publishedX.Zip(publishedX.Skip(1)), pair => Assert.Equal((byte)(pair.First + 1), pair.Second));
    }

    [Fact]
    public void Unreachable_logical_sprite_draw_does_not_enable_per_frame_oam_dma()
    {
        const string source = """
                              import RetroSharp.Portable2D;
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(marker, "actor.json");
                                  while (true) {
                                      Video.WaitVBlank();
                                      continue;
                                      Sprite.Draw(marker, 12, 20, 0, false, 0);
                                  }
                              }
                              """;
        var baseDirectory = RepositoryDirectory("samples/actor-framework");
        var rom = RetroSharp.NES.NesRomCompiler.CompileSource(
            source,
            baseDirectory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var cpu = new NesTestCpu(rom);

        cpu.RunFrames(6);

        Assert.Single(cpu.OamDmaTransfers);
    }

    [Fact]
    public void Retained_oam_reset_clears_more_than_signed_index_range()
    {
        var draws = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 33).Select(index =>
                $"Sprite.Draw(marker, 8, {index * 7}, 0, false, 0);"));
        var source = $$"""
                       import RetroSharp.Portable2D;
                       void Main() {
                           Video.Init();
                           Sprite.Asset(marker, "actor.json");
                           u8 frame = 0;
                           while (true) {
                               Video.WaitVBlank();
                               if (frame == 0) {
                                   {{draws}}
                               }
                               frame = 1;
                           }
                       }
                       """;
        var rom = RetroSharp.NES.NesRomCompiler.CompileSource(
            source,
            RepositoryDirectory("samples/actor-framework"),
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
        var cpu = new NesTestCpu(rom);

        cpu.RunFrames(8);

        const int retainedByteCount = 33 * 4;
        var populatedTransfer = cpu.OamDmaTransfers.FindIndex(transfer =>
            transfer.SourceSnapshot.Take(retainedByteCount).Any(value => value != 0xFF));
        Assert.True(populatedTransfer >= 0);
        Assert.Contains(
            cpu.OamDmaTransfers.Skip(populatedTransfer + 1),
            transfer => transfer.SourceSnapshot.Take(retainedByteCount).All(value => value == 0xFF));
    }

    public static TheoryData<string, string, string, string?, string?, string, string> ProductionSamples => new()
    {
        { "actor-framework", "samples/actor-framework/actors.rs", "samples/actor-framework", null, null, "samples/actor-framework/actors.nes", "validation/scenarios/actor-framework.nes.json" },
        { "shots-simple", "samples/shots-simple/src/main.rs", "samples/shots-simple", "ShotsSimple", "samples/shots-simple/src", "samples/shots-simple/bin/shots-simple.nes", "validation/scenarios/shots-simple.nes.json" },
        { "shots-bouncy", "samples/shots-bouncy/src/main.rs", "samples/shots-bouncy", "ShotsBouncy", "samples/shots-bouncy/src", "samples/shots-bouncy/bin/shots-bouncy.nes", "validation/scenarios/shots-bouncy.nes.json" },
        { "runner-projectile", "samples/runner-projectile/src/main.rs", "samples/runner-projectile", "RunnerProjectile", "samples/runner-projectile/src", "samples/runner-projectile/bin/runner-projectile.nes", "validation/scenarios/runner-projectile.nes.json" },
    };

    [Theory]
    [MemberData(nameof(ProductionSamples))]
    public void Exact_production_rom_preserves_actor_and_projectile_integrity(
        string sampleId,
        string sourceRelativePath,
        string baseDirectoryRelativePath,
        string? rootNamespace,
        string? sourceRootRelativePath,
        string romRelativePath,
        string scenarioRelativePath)
    {
        var compilation = Compile(sourceRelativePath, baseDirectoryRelativePath, rootNamespace, sourceRootRelativePath);
        var trackedRom = File.ReadAllBytes(RepositoryFile(romRelativePath));
        Assert.Equal(trackedRom, compilation.Build.Rom);

        var scenario = FunctionalScenarioLoader.Load(RepositoryFile(scenarioRelativePath));
        var factory = new MachineFactory(sampleId, compilation.Program, compilation.Build.Report);
        var adapter = new NesFunctionalRomAdapter(
            factory,
            new FunctionalAdapterCapabilities(
                GameplayTicks: true,
                InputTimeline: true,
                CameraLifecycle: true,
                Background: true,
                SpriteOam: true,
                VideoWriteTiming: true));
        var report = FunctionalScenarioRunner.Run(
            scenario,
            new FunctionalRomArtifact(romRelativePath, trackedRom),
            adapter,
            new Oracle(factory));

        output.WriteLine($"{report.ScenarioId}: {report.Summary}");
        Assert.True(report.Passed, Diagnostic(report));
        Assert.Equal(trackedRom, factory.LoadedRom);
        Assert.All(report.TimingChecks, check => Assert.True(check.Passed, check.Metric));
        Assert.Empty(report.IntegrityFailures);
        Assert.Equal(0, report.Summary.BackgroundMismatches);
        Assert.Equal(0, report.Summary.SpriteMismatches);
        Assert.Equal(0, report.Summary.UnsafeVideoWrites);
        Assert.Equal(0, report.Summary.UnsafeOamWrites);
        Assert.InRange(factory.MaximumActiveProjectiles, 0, 2);
        Assert.True(factory.MaximumSpawnToVisibleFrames <= scenario.Budgets.MaximumSpawnToVisibleFrames!.Value);
        Assert.All(
            factory.Snapshots.Where(item => item.Key >= scenario.WarmUpFrames).Select(item => item.Value),
            snapshot => Assert.Equal(0, snapshot.UnexpectedVisibleOamSlots));

        if (sampleId == "actor-framework")
        {
            Assert.Equal(3, factory.MaximumUsedActorSpawns);
            Assert.True(factory.ActorSlotRecycles > 0);
            Assert.True(factory.MaximumActorTileContacts > 0, "The exact actor ROM must retain tile-contact state on active actors.");
            Assert.Equal(8, factory.MinimumGroundedActorWorldY);
            Assert.Equal(9, factory.MaximumGroundedActorWorldY);
        }
        else
        {
            Assert.True(factory.DroppedRequests > 0);
            Assert.True(factory.ReusedProjectileSlots > 0);
        }

        if (sampleId == "shots-bouncy")
        {
            Assert.True(factory.BounceContacts > 0);
        }

        if (sampleId == "runner-projectile")
        {
            Assert.True(factory.ExpiredEffects > 0);
            Assert.True(factory.HiddenExpiredEffects > 0);
        }
    }

    private static Compilation Compile(
        string sourceRelativePath,
        string baseDirectoryRelativePath,
        string? rootNamespace,
        string? sourceRootRelativePath)
    {
        var sourcePath = RepositoryFile(sourceRelativePath);
        var source = File.ReadAllText(sourcePath);
        if (rootNamespace is not null)
        {
            source = PhysicalNamespaceSourceComposer.Compose(
                [new PhysicalNamespaceSourceFile(sourcePath, source)],
                rootNamespace,
                RepositoryDirectory(sourceRootRelativePath!));
        }

        var program = PrepareVideoProgram(source, RepositoryDirectory(baseDirectoryRelativePath));
        var build = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            source,
            RepositoryDirectory(baseDirectoryRelativePath),
            SdkLibraryImportMode.ExplicitOnly,
            null,
            [SdkImportResolver.Portable2D],
            null);
        return new(program, build);
    }

    private static NesVideoProgram PrepareVideoProgram(string source, string baseDirectory)
    {
        var parse = new SomeParser().Parse(
            SdkLibrarySource.Merge(
                NesTarget.Intrinsics,
                source,
                libraryImportPaths: [SdkImportResolver.Portable2D]));
        Assert.True(parse.IsSuccess, parse.IsFailure ? parse.Error : null);
        var targetProgram = TargetProgramSelector.Select(parse.Value, NesTarget.Intrinsics);
        var actorProgram = ActorFrameworkLowerer.Lower(
            targetProgram,
            NesTarget.Capabilities,
            supportsUpdate: true,
            supportsDraw: true,
            baseDirectory);
        var loweredProgram = LetTypeInference.ResolveOrThrow(SdkSourcePackageFacadeLowerer.Lower(actorProgram));
        return NesVideoProgram.FromProgram(loweredProgram, baseDirectory);
    }

    private static string RepositoryFile(string relativePath)
    {
        var path = RepositoryDirectory(relativePath);
        return File.Exists(path) ? path : throw new InvalidOperationException($"Could not find repository file '{relativePath}'.");
    }

    private static string RepositoryDirectory(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not find repository path '{relativePath}'.");
    }

    private static string Diagnostic(FunctionalAcceptanceReport report) =>
        $"{report.ScenarioId}: {report.Summary}{Environment.NewLine}"
        + string.Join(Environment.NewLine, report.TimingChecks.Select(check =>
            $"{check.Metric}: passed={check.Passed} observed={check.Observed} limit={check.Limit}"))
        + Environment.NewLine
        + string.Join(Environment.NewLine, report.IntegrityFailures.Take(24));

    private sealed record Compilation(NesVideoProgram Program, NesRomBuildResult Build);

    private enum SpriteKind
    {
        Static,
        Actor,
        Projectile,
        Effect,
    }

    private sealed record SpritePlan(
        string Id,
        SpriteKind Kind,
        NesCompiledSpriteAsset Asset,
        int Index,
        int OamSlot,
        int FixedX,
        int FixedY,
        string Pool);

    private sealed record ProjectedSprite(
        SpritePlan Plan,
        bool Exists,
        bool Active,
        bool Visible,
        int X,
        int Y);

    private sealed record Snapshot(
        IReadOnlyList<FunctionalBackgroundExpectation> Background,
        IReadOnlyList<FunctionalSpriteExpectation> Sprites,
        int UnexpectedVisibleOamSlots);

    private sealed class MachineFactory(string sampleId, NesVideoProgram program, NesRomBuildReport report) : IFunctionalRomMachineFactory
    {
        public byte[]? LoadedRom { get; private set; }
        public Dictionary<int, Snapshot> Snapshots { get; } = [];
        public FunctionalActorProjectileLifecycleTracker Lifecycle { get; } = new(sampleId);
        public int MaximumActiveProjectiles => Lifecycle.MaximumActiveProjectiles;
        public int MaximumSpawnToVisibleFrames => Lifecycle.MaximumSpawnToVisibleFrames;
        public int DroppedRequests => Lifecycle.DroppedRequests;
        public int ReusedProjectileSlots => Lifecycle.ReusedProjectileSlots;
        public int BounceContacts => Lifecycle.BounceContacts;
        public int ExpiredEffects => Lifecycle.ExpiredEffects;
        public int HiddenExpiredEffects => Lifecycle.HiddenExpiredEffects;
        public int MaximumUsedActorSpawns => Lifecycle.MaximumUsedActorSpawns;
        public int ActorSlotRecycles => Lifecycle.ActorSlotRecycles;
        public int MaximumActorTileContacts => Lifecycle.MaximumActorTileContacts;
        public int? MinimumGroundedActorWorldY => Lifecycle.MinimumGroundedActorWorldY;
        public int? MaximumGroundedActorWorldY => Lifecycle.MaximumGroundedActorWorldY;

        public IFunctionalRomMachine Create(ReadOnlyMemory<byte> exactRom)
        {
            LoadedRom = exactRom.ToArray();
            return new Machine(sampleId, program, report, LoadedRom, this);
        }
    }

    private sealed class Machine : IFunctionalRomMachine
    {
        private readonly string sampleId;
        private readonly NesVideoProgram program;
        private readonly MachineFactory owner;
        private readonly NesTestCpu cpu;
        private readonly IReadOnlyDictionary<string, NesRuntimeUserVariable> variables;
        private readonly IReadOnlyList<SpritePlan> plans;
        private readonly Dictionary<string, int[]> expectedOam = [];
        private readonly Dictionary<string, bool> expectedSpriteVisible = [];
        private int processedPpuWrites;
        private int processedOamWrites;
        private int lastFrame;
        private (int X, int Y)? previousRequestedCamera;
        private readonly Dictionary<(int X, int Y), long> cameraSequenceByPosition = [];
        private long cameraRequestSequence;
        private long cameraVisibleSequence;

        public Machine(string sampleId, NesVideoProgram program, NesRomBuildReport report, byte[] rom, MachineFactory owner)
        {
            this.sampleId = sampleId;
            this.program = program;
            this.owner = owner;
            cpu = new(rom);
            variables = report.UserVariables.ToDictionary(variable => variable.Name, StringComparer.Ordinal);
            plans = BuildPlans(sampleId, program);
            foreach (var plan in plans)
            {
                expectedOam[plan.Id] = Enumerable.Repeat(255, plan.Asset.Pieces.Count * 4).ToArray();
                expectedSpriteVisible[plan.Id] = false;
            }
        }

        public FunctionalFrameObservation ObserveInitial()
        {
            cpu.RunFrames(0);
            return Observe(0, new HashSet<string>());
        }

        public FunctionalFrameObservation AdvanceFrame(int frame, IReadOnlySet<string> heldInputs)
        {
            if (frame != lastFrame + 1)
            {
                throw new InvalidOperationException($"Expected frame {lastFrame + 1}, received {frame}.");
            }

            cpu.Held.Clear();
            cpu.Held.UnionWith(heldInputs);
            cpu.RunFrames(frame);
            lastFrame = frame;
            return Observe(frame, heldInputs);
        }

        public void Dispose()
        {
        }

        private FunctionalFrameObservation Observe(int frame, IReadOnlySet<string> heldInputs)
        {
            var visibleCamera = VisibleCamera();
            var logical = Project(visibleCamera, name => Byte(cpu, name));
            var published = Published(visibleCamera);
            var actual = CaptureSprites();
            UpdateLifecycle(frame, heldInputs, logical, actual);
            var expectedSprites = ExpectedSprites(published);
            CrossCheckShadow(frame);
            var background = CaptureBackground(visibleCamera);
            var expectedBackground = ExpectedBackground(visibleCamera);
            var state = State(logical, actual, visibleCamera);
            var ppuWrites = cpu.PpuWrites.Skip(processedPpuWrites).ToArray();
            var videoWrites = ppuWrites
                .Where(write => write.Register is 0x2000 or 0x2005 or 0x2006 or 0x2007)
                .Select(VideoWrite)
                .ToArray();
            var oamWrites = cpu.OamWrites.Skip(processedOamWrites).Select(OamWrite).ToArray();
            processedPpuWrites = cpu.PpuWrites.Count;
            processedOamWrites = cpu.OamWrites.Count;
            var unexpected = Enumerable.Range(plans.Sum(plan => plan.Asset.Pieces.Count), 64 - plans.Sum(plan => plan.Asset.Pieces.Count))
                .Count(slot => OamPieceVisible(Enumerable.Range(0, 4).Select(offset => (int)cpu.Oam((byte)(slot * 4 + offset))).ToArray()));
            owner.Snapshots[frame] = new(expectedBackground, expectedSprites, unexpected);

            var requestedCamera = (
                X: (int)cpu.Ram(NesRuntimeMemoryLayout.Camera.X) | cpu.Ram(NesRuntimeMemoryLayout.Camera.XHigh) << 8,
                Y: (int)cpu.Ram(NesRuntimeMemoryLayout.Camera.Y) | cpu.Ram(NesRuntimeMemoryLayout.Camera.YHigh) << 8);
            return new FunctionalFrameObservation(
                frame,
                cpu.VBlankWaitCompletions,
                0,
                cpu.ResetCount,
                state,
                CameraObservation(requestedCamera, visibleCamera),
                Background: background,
                Sprites: actual,
                VideoWrites: videoWrites,
                OamWrites: oamWrites,
                Spawn: owner.Lifecycle.Spawn);
        }

        private IReadOnlyDictionary<string, long> State(
            IReadOnlyList<ProjectedSprite> logical,
            IReadOnlyList<FunctionalSpriteObservation> actual,
            (int X, int Y) visibleCamera)
        {
            var lifecycle = owner.Lifecycle;
            return new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["sourceTick"] = cpu.VBlankWaitCompletions,
                ["sourceCameraX"] = sampleId == "actor-framework" ? Byte(cpu, "cameraX") : 0,
                ["visibleCameraX"] = visibleCamera.X,
                ["displayEnabled"] = cpu.RenderingEnabled ? 1 : 0,
                ["poolCapacity"] = 2,
                ["requestCapacity"] = sampleId == "actor-framework" ? 0 : 2,
                ["effectCapacity"] = sampleId == "runner-projectile" ? 4 : 0,
                ["activeProjectileCount"] = lifecycle.CurrentActiveProjectiles,
                ["visibleProjectileCount"] = actual.Count(sprite => sprite.Id.StartsWith("projectile-", StringComparison.Ordinal) && sprite.Visible),
                ["droppedRequests"] = lifecycle.DroppedRequests,
                ["reusedProjectileSlots"] = lifecycle.ReusedProjectileSlots,
                ["bounceContacts"] = lifecycle.BounceContacts,
                ["expiredEffects"] = lifecycle.ExpiredEffects,
                ["actorTileContactCount"] = lifecycle.CurrentActorTileContacts,
                ["groundedActorCount"] = lifecycle.CurrentGroundedActors,
                ["groundedActorMinimumWorldY"] = lifecycle.CurrentMinimumGroundedActorWorldY ?? -1,
                ["groundedActorMaximumWorldY"] = lifecycle.CurrentMaximumGroundedActorWorldY ?? -1,
            };
        }

        private void UpdateLifecycle(
            int frame,
            IReadOnlySet<string> heldInputs,
            IReadOnlyList<ProjectedSprite> logical,
            IReadOnlyList<FunctionalSpriteObservation> actual)
        {
            var projectiles = sampleId == "actor-framework"
                ? []
                : Enumerable.Range(0, 2)
                    .Select(index => new FunctionalProjectileMotionObservation(
                        Byte(cpu, $"shotsHero[{index}].active") != 0,
                        unchecked((sbyte)Byte(cpu, $"shotsHero[{index}].vy"))))
                    .ToArray();
            var actors = sampleId == "actor-framework"
                ? Enumerable.Range(0, 2)
                    .Select(index => new FunctionalActorContactObservation(
                        Byte(cpu, $"enemies[{index}].active") != 0,
                        Byte(cpu, $"enemies[{index}].state"),
                        Word(name => Byte(cpu, name), $"enemies[{index}].y", $"enemies[{index}].yHi"),
                        unchecked((sbyte)Byte(cpu, $"enemies[{index}].vy"))))
                    .ToArray()
                : [];
            owner.Lifecycle.Update(new(
                frame,
                heldInputs.ToArray(),
                logical
                    .Where(sprite => sprite.Plan.Kind is SpriteKind.Actor or SpriteKind.Projectile)
                    .Select(sprite => new FunctionalDynamicSpriteLifecycleObservation(
                        sprite.Plan.Id,
                        sprite.Plan.Kind == SpriteKind.Actor
                            ? FunctionalDynamicSpriteKind.Actor
                            : FunctionalDynamicSpriteKind.Projectile,
                        sprite.Active,
                        actual.Single(item => item.Id == sprite.Plan.Id).Visible))
                    .ToArray(),
                FireTick: sampleId is "shots-simple" or "shots-bouncy" ? Byte(cpu, "fireTick") : null,
                UsedActorSpawns: sampleId == "actor-framework"
                    ? Enumerable.Range(0, 3).Count(index => Byte(cpu, $"__enemies_spawn_0_used[{index}]") != 0)
                    : 0,
                ActiveActorCount: actors.Count(actor => actor.Active),
                Projectiles: projectiles,
                Effects: logical
                    .Where(sprite => sprite.Plan.Kind == SpriteKind.Effect)
                    .Select(sprite => new FunctionalEffectLifecycleObservation(sprite.Active, sprite.Visible))
                    .ToArray(),
                Actors: actors));
        }

        private IReadOnlyList<ProjectedSprite> Published((int X, int Y) camera)
        {
            if (cpu.OamDmaTransfers.Count == 0) return Project(camera, name => Byte(cpu, name));
            var transferIndex = sampleId == "actor-framework" || cpu.OamDmaTransfers.Count == 1
                ? cpu.OamDmaTransfers.Count - 1
                : cpu.OamDmaTransfers.Count - 2;
            var transfer = cpu.OamDmaTransfers[transferIndex];
            var publishedCamera = sampleId == "actor-framework"
                ? (X: transfer.RamSnapshot[NesRuntimeMemoryLayout.Camera.X]
                      | transfer.RamSnapshot[NesRuntimeMemoryLayout.Camera.XHigh] << 8,
                    Y: transfer.RamSnapshot[NesRuntimeMemoryLayout.Camera.Y]
                      | transfer.RamSnapshot[NesRuntimeMemoryLayout.Camera.YHigh] << 8)
                : camera;
            return Project(publishedCamera, name => Byte(transfer.RamSnapshot, name));
        }

        private IReadOnlyList<ProjectedSprite> Project((int X, int Y) camera, Func<string, byte> read)
        {
            return plans.Select(plan =>
            {
                if (plan.Kind == SpriteKind.Static) return new ProjectedSprite(plan, true, true, true, plan.FixedX, plan.FixedY);
                var poolIndex = plan.Kind == SpriteKind.Actor
                    ? Enumerable.Range(0, 2).FirstOrDefault(
                        index => read($"{plan.Pool}[{index}].kind") == plan.Index,
                        -1)
                    : plan.Index;
                var active = poolIndex >= 0 && read($"{plan.Pool}[{poolIndex}].active") != 0;
                var worldX = poolIndex >= 0
                    ? Word(read, $"{plan.Pool}[{poolIndex}].x", $"{plan.Pool}[{poolIndex}].xHi")
                    : 0;
                var worldY = poolIndex >= 0
                    ? Word(read, $"{plan.Pool}[{poolIndex}].y", $"{plan.Pool}[{poolIndex}].yHi")
                    : 0;
                var x = worldX - camera.X;
                var y = worldY - camera.Y;
                var visible = active && x is >= 0 and < 256 && y is >= 0 and < 240;
                return new ProjectedSprite(plan, poolIndex >= 0, active, visible, visible ? x : 0, visible ? y : 240);
            }).ToArray();
        }

        private IReadOnlyList<FunctionalSpriteExpectation> ExpectedSprites(IReadOnlyList<ProjectedSprite> projected)
        {
            return projected.Select(sprite =>
            {
                if (sprite.Plan.Kind == SpriteKind.Actor && !sprite.Exists)
                {
                    expectedOam[sprite.Plan.Id] = Enumerable.Repeat(0xFF, sprite.Plan.Asset.Pieces.Count * 4).ToArray();
                }
                else if (sprite.Visible || sprite.Active || expectedSpriteVisible[sprite.Plan.Id])
                {
                    expectedOam[sprite.Plan.Id] = SpriteOam(sprite).ToArray();
                }
                expectedSpriteVisible[sprite.Plan.Id] = sprite.Visible;
                return new FunctionalSpriteExpectation(
                    sprite.Plan.Id,
                    sprite.Visible,
                    expectedOam[sprite.Plan.Id],
                    sprite.Plan.OamSlot);
            }).ToArray();
        }

        private static IReadOnlyList<int> SpriteOam(ProjectedSprite sprite) => sprite.Plan.Asset.Pieces.SelectMany(piece => new[]
        {
            (sprite.Visible ? sprite.Y + piece.YOffset - 1 : 239) & 0xFF,
            sprite.Plan.Asset.FirstTile + piece.TileOffset,
            piece.PaletteSlotOffset,
            (sprite.Visible ? sprite.X + piece.XOffset : piece.XOffset) & 0xFF,
        }).ToArray();

        private void CrossCheckShadow(int frame)
        {
            if (frame < 20 || cpu.OamDmaTransfers.Count == 0) return;
            var source = cpu.OamDmaTransfers[^1].SourceSnapshot;
            Assert.Equal(source, Enumerable.Range(0, 256).Select(index => cpu.Oam((byte)index)));
        }

        private IReadOnlyList<FunctionalSpriteObservation> CaptureSprites()
        {
            var result = plans.Select(plan =>
            {
                var oam = Enumerable.Range(0, plan.Asset.Pieces.Count * 4)
                    .Select(offset => (int)cpu.Oam((byte)(plan.OamSlot * 4 + offset)))
                    .ToArray();
                return new FunctionalSpriteObservation(plan.Id, OamVisible(oam), oam, plan.OamSlot);
            }).ToList();
            var usedSlots = plans.Sum(plan => plan.Asset.Pieces.Count);
            var unused = Enumerable.Range(usedSlots * 4, (64 - usedSlots) * 4)
                .Select(offset => (int)cpu.Oam((byte)offset))
                .ToArray();
            result.Add(new("unused-oam", OamVisible(unused), unused, usedSlots));
            return result;
        }

        private IReadOnlyList<FunctionalBackgroundObservation> CaptureBackground((int X, int Y) camera)
        {
            var width = camera.X % 8 == 0 ? 32 : 33;
            var startColumn = camera.X / 8;
            var startRow = camera.Y / 8;
            return Enumerable.Range(0, 30).SelectMany(y => Enumerable.Range(0, width).Select(x =>
            {
                var address = BackgroundAddress(startColumn + x, startRow + y);
                return new FunctionalBackgroundObservation($"screen:{x:D2},{y:D2}", cpu.PpuVram(address), PaletteIdentity(cpu.PpuVram, address));
            })).ToArray();
        }

        private IReadOnlyList<FunctionalBackgroundExpectation> ExpectedBackground((int X, int Y) camera)
        {
            var width = camera.X % 8 == 0 ? 32 : 33;
            var startColumn = camera.X / 8;
            var startRow = camera.Y / 8;
            return Enumerable.Range(0, 30).SelectMany(y => Enumerable.Range(0, width).Select(x =>
            {
                var address = BackgroundAddress(startColumn + x, startRow + y);
                return new FunctionalBackgroundExpectation(
                    $"screen:{x:D2},{y:D2}",
                    program.NameTable[address - 0x2000],
                    PaletteIdentity(address => ProgramByte(address), address));
            })).ToArray();
        }

        private byte ProgramByte(ushort address) => address >= 0x3F00
            ? program.Palette[(address - 0x3F00) & 0x1F]
            : program.NameTable[address - 0x2000];

        private static int PaletteIdentity(Func<ushort, byte> read, ushort tileAddress)
        {
            var offset = tileAddress - 0x2000;
            var table = offset / 0x400;
            var within = offset % 0x400;
            var row = within / 32;
            var column = within % 32;
            var attributeAddress = (ushort)(0x2000 + table * 0x400 + 0x3C0 + row / 4 * 8 + column / 4);
            var shift = ((row & 2) != 0 ? 4 : 0) + ((column & 2) != 0 ? 2 : 0);
            var slot = (read(attributeAddress) >> shift) & 3;
            var colors = Enumerable.Range(0, 4).Select(index => (int)read((ushort)(0x3F00 + slot * 4 + index))).ToArray();
            return slot | colors[0] << 2 | colors[1] << 8 | colors[2] << 14 | colors[3] << 20;
        }

        private static ushort BackgroundAddress(int x, int y)
        {
            x = ((x % 64) + 64) % 64;
            y = ((y % 60) + 60) % 60;
            return (ushort)(0x2000 + y / 30 * 0x800 + x / 32 * 0x400 + y % 30 * 32 + x % 32);
        }

        private (int X, int Y) VisibleCamera()
        {
            if (sampleId != "actor-framework") return (0, 0);
            return (cpu.ScrollX + ((cpu.PpuControl & 1) != 0 ? 256 : 0), cpu.ScrollY + ((cpu.PpuControl & 2) != 0 ? 240 : 0));
        }

        private FunctionalCameraLifecycleObservation? CameraObservation((int X, int Y) requested, (int X, int Y) visible)
        {
            if (sampleId != "actor-framework") return null;
            if (previousRequestedCamera != requested)
            {
                cameraRequestSequence++;
                previousRequestedCamera = requested;
                cameraSequenceByPosition[requested] = cameraRequestSequence;
            }
            if (cameraSequenceByPosition.TryGetValue(visible, out var sequence)) cameraVisibleSequence = sequence;
            return new(cameraRequestSequence, cameraVisibleSequence, cameraVisibleSequence, cameraVisibleSequence);
        }

        private FunctionalVideoWriteObservation VideoWrite(NesPpuWrite write)
        {
            var timing = Timing(write.Cycle, write.RenderingEnabled);
            return new($"nes-ppu-${write.Register:X4}", write.VramAddress ?? write.Register, !write.RenderingEnabled || timing.Phase == "vblank", timing);
        }

        private FunctionalOamWriteObservation OamWrite(NesOamWrite write)
        {
            var timing = Timing(write.Cycle, write.RenderingEnabled);
            return new(write.Address, !write.RenderingEnabled || timing.Phase == "vblank", timing);
        }

        private FunctionalWriteTimingObservation Timing(long cycle, bool renderingEnabled)
        {
            var timing = cpu.PpuTiming(cycle, renderingEnabled);
            return new(cycle, timing.Scanline, timing.Dot, timing.Phase, renderingEnabled);
        }

        private byte Byte(NesTestCpu source, string name) => source.Ram(variables[name].Address);
        private byte Byte(IReadOnlyList<byte> ram, string name) => ram[variables[name].Address & 0x07FF];
        private static int Word(Func<string, byte> read, string low, string high) => read(low) | read(high) << 8;
        private static bool OamVisible(IReadOnlyList<int> oam) => oam.Chunk(4).Any(OamPieceVisible);
        private static bool OamPieceVisible(IReadOnlyList<int> piece) => piece[0] < 239;

        private static IReadOnlyList<SpritePlan> BuildPlans(string sampleId, NesVideoProgram program)
        {
            var definitions = new List<(string Id, SpriteKind Kind, string Asset, int Index, int X, int Y, string Pool)>();
            if (sampleId == "actor-framework")
            {
                definitions.Add(("actor-goomba", SpriteKind.Actor, "actor_marker", 1, 0, 0, "enemies"));
                definitions.Add(("actor-bat", SpriteKind.Actor, "actor_marker", 2, 0, 0, "enemies"));
                definitions.Add(("actor-koopa", SpriteKind.Actor, "actor_marker", 3, 0, 0, "enemies"));
            }
            else if (sampleId is "shots-simple" or "shots-bouncy")
            {
                definitions.Add(("player", SpriteKind.Static, "player", 0, sampleId == "shots-simple" ? 24 : 16, sampleId == "shots-simple" ? 72 : 120, ""));
                for (var index = 0; index < 2; index++) definitions.Add(($"projectile-{index}", SpriteKind.Projectile, "shot", index, 0, 0, "shotsHero"));
            }
            else
            {
                definitions.Add(("player", SpriteKind.Static, "mario_player", 0, 72, 96, ""));
                for (var index = 0; index < 2; index++) definitions.Add(($"projectile-{index}", SpriteKind.Projectile, "mario_shot", index, 0, 0, "shotsHero"));
                for (var index = 0; index < 4; index++) definitions.Add(($"effect-{index}", SpriteKind.Effect, "muzzle_flash", index, 0, 0, "fx"));
            }

            var result = new List<SpritePlan>();
            var slot = 0;
            foreach (var definition in definitions)
            {
                var asset = program.SpriteAssets.Single(pair => pair.Key == definition.Asset || pair.Key.EndsWith($".{definition.Asset}", StringComparison.Ordinal)).Value;
                result.Add(new(definition.Id, definition.Kind, asset, definition.Index, slot, definition.X, definition.Y, definition.Pool));
                slot += asset.Pieces.Count;
            }
            return result;
        }
    }

    private sealed class Oracle(MachineFactory factory) : IFunctionalFrameOracle
    {
        public FunctionalFrameExpectation ExpectedFrame(int frame)
        {
            var snapshot = factory.Snapshots[frame];
            var sprites = snapshot.Sprites.ToList();
            var usedSlots = snapshot.Sprites.Sum(sprite => sprite.Oam.Count / 4);
            sprites.Add(new("unused-oam", false, Enumerable.Repeat(255, (64 - usedSlots) * 4).ToArray(), usedSlots));
            return new(frame, snapshot.Background, sprites);
        }
    }
}
