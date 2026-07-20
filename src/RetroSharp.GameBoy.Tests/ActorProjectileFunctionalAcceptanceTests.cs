namespace RetroSharp.GameBoy.Tests;

using RetroSharp.FunctionalAcceptance;
using RetroSharp.Sdk;
using Xunit;
using Xunit.Abstractions;
using CameraMemory = RetroSharp.GameBoy.GameBoyRuntimeMemoryLayout.Camera;

public sealed class ActorProjectileFunctionalAcceptanceTests(ITestOutputHelper output)
{
    [Fact]
    public void Logical_sprite_draws_publish_shadow_oam_once_at_the_vblank_boundary()
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
                                      x += 1;
                                  }
                              }
                              """;
        var cpu = new GameBoyTestCpu(GameBoyRomCompiler.CompileSource(
            source,
            RepositoryDirectory("samples/actor-framework")))
        {
            CycleAccurateLy = true,
        };

        cpu.RunFrames(4);

        var dmaWrites = cpu.IoWrites.Where(write => write.Register == 0xFF46).ToArray();
        Assert.NotEmpty(dmaWrites);
        Assert.Equal(cpu.OamDmaTransfers.Count, dmaWrites.Length);
        Assert.Equal(
            cpu.OamDmaTransfers.Count,
            cpu.OamDmaTransfers.Select(transfer => transfer.StartCycle / GameBoyTestCpu.DmgCyclesPerFrame).Distinct().Count());
        Assert.All(cpu.OamDmaTransfers, transfer => Assert.Equal(640, transfer.EndCycle - transfer.StartCycle));
        Assert.All(dmaWrites, write => Assert.Equal(0xC6, write.Value));
        var latestTransfer = cpu.OamDmaTransfers[^1];
        Assert.Equal(latestTransfer.SourceSnapshot, latestTransfer.WramSnapshot[0x600..0x6A0]);
        Assert.Equal(latestTransfer.SourceSnapshot, Enumerable.Range(0, 160).Select(offset => cpu.Oam((ushort)(0xFE00 + offset))));
        Assert.Equal(36, cpu.Wram(0xC600));
        Assert.Equal(6, cpu.Wram(0xC602));
        Assert.Equal(0, cpu.Wram(0xC603));
        Assert.Equal(cpu.Oam(0xFE01) + 1, cpu.Wram(0xC601));
        Assert.NotEmpty(cpu.OamWrites);
        Assert.All(cpu.OamWrites, write => Assert.True(!write.LcdEnabled || write.Ly >= 144));
    }

    [Fact]
    public void Packed_camera_vblank_reuse_does_not_duplicate_shadow_oam_dma()
    {
        var source = File.ReadAllText(RepositoryFile("samples/actor-framework/actors.rs"));
        var rom = GameBoyRomCompiler.CompileSource(source, RepositoryDirectory("samples/actor-framework"));
        var cpu = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        cpu.Held.Add("right");

        cpu.RunFrames(80);

        Assert.NotEmpty(cpu.OamDmaTransfers);
        Assert.Equal(
            cpu.OamDmaTransfers.Count,
            cpu.OamDmaTransfers.Select(transfer => transfer.StartCycle / GameBoyTestCpu.DmgCyclesPerFrame).Distinct().Count());
        Assert.All(cpu.OamDmaTransfers, transfer =>
        {
            Assert.Equal(640, transfer.EndCycle - transfer.StartCycle);
            Assert.InRange((transfer.StartCycle / 456) % 154, 144, 152);
            Assert.InRange(((transfer.EndCycle - 1) / 456) % 154, 144, 153);
        });
    }

    public static TheoryData<string, string, string, string?, string?, string, string> ProductionSamples => new()
    {
        {
            "actor-framework",
            "samples/actor-framework/actors.rs",
            "samples/actor-framework",
            null,
            null,
            "samples/actor-framework/actors.gb",
            "validation/scenarios/actor-framework.gb.json"
        },
        {
            "shots-simple",
            "samples/shots-simple/src/main.rs",
            "samples/shots-simple",
            "ShotsSimple",
            "samples/shots-simple/src",
            "samples/shots-simple/bin/shots-simple.gb",
            "validation/scenarios/shots-simple.gb.json"
        },
        {
            "shots-bouncy",
            "samples/shots-bouncy/src/main.rs",
            "samples/shots-bouncy",
            "ShotsBouncy",
            "samples/shots-bouncy/src",
            "samples/shots-bouncy/bin/shots-bouncy.gb",
            "validation/scenarios/shots-bouncy.gb.json"
        },
        {
            "runner-projectile",
            "samples/runner-projectile/src/main.rs",
            "samples/runner-projectile",
            "RunnerProjectile",
            "samples/runner-projectile/src",
            "samples/runner-projectile/bin/runner-projectile.gb",
            "validation/scenarios/runner-projectile.gb.json"
        },
    };

    [Fact]
    public void Game_boy_build_report_exposes_user_variables_for_runtime_oracles()
    {
        var property = typeof(GameBoyRomBuildReport).GetProperty("UserVariables");

        Assert.NotNull(property);
        Assert.Equal("IReadOnlyList`1", property.PropertyType.Name);
        Assert.Equal("GameBoyRuntimeUserVariable", property.PropertyType.GenericTypeArguments.Single().Name);
    }

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
        var compilation = Compile(
            sourceRelativePath,
            baseDirectoryRelativePath,
            rootNamespace,
            sourceRootRelativePath);
        var rom = compilation.Build.Rom;

        var scenario = FunctionalScenarioLoader.Load(RepositoryFile(scenarioRelativePath));
        var factory = new ActorProjectileGameBoyMachineFactory(sampleId, compilation.Program, compilation.Build.Report);
        var adapter = new GameBoyFunctionalRomAdapter(
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
            new FunctionalRomArtifact(romRelativePath, rom),
            adapter,
            new ActorProjectileGameBoyOracle(factory));

        output.WriteLine(Summary(report));
        Assert.True(report.Passed, Diagnostic(report));
        Assert.Equal(rom, factory.LoadedRom);
        Assert.All(report.TimingChecks, check => Assert.True(check.Passed, check.Metric));
        Assert.Empty(report.IntegrityFailures);
        Assert.Equal(0, report.Summary.BackgroundMismatches);
        Assert.Equal(0, report.Summary.SpriteMismatches);
        Assert.Equal(0, report.Summary.UnsafeVideoWrites);
        Assert.Equal(0, report.Summary.UnsafeOamWrites);
        Assert.InRange(factory.MaximumActiveProjectiles, 0, 2);
        Assert.True(factory.MaximumSpawnToVisibleFrames <= scenario.Budgets.MaximumSpawnToVisibleFrames!.Value);
        Assert.All(factory.Snapshots.Values, snapshot => Assert.Equal(0, snapshot.UnexpectedVisibleOamSlots));

        if (sampleId == "actor-framework")
        {
            Assert.Equal(2, factory.MaximumUsedActorSpawns);
            Assert.True(factory.ActorSlotRecycles > 0);
            Assert.True(factory.MaximumActorTileContacts > 0, "The exact actor ROM must retain tile-contact state on active actors.");
            Assert.Equal(8, factory.MinimumGroundedActorWorldY);
            Assert.Equal(8, factory.MaximumGroundedActorWorldY);
            Assert.Equal(0, factory.Snapshots[scenario.WarmUpFrames + scenario.ObservationFrames].Signals["sourceCameraX"]);
        }
        else
        {
            Assert.True(factory.DroppedRequests > 0, "The authored maximum-load timeline must prove dropped requests while the pool is full.");
            Assert.True(factory.ReusedProjectileSlots > 0, "At least one fixed projectile slot must despawn and be reused.");
        }

        if (sampleId == "shots-bouncy")
        {
            Assert.True(factory.BounceContacts > 0, "The bouncy sample must reverse vertical velocity after a floor contact.");
        }

        if (sampleId == "runner-projectile")
        {
            Assert.True(factory.ExpiredEffects > 0, "The muzzle-flash effect must expire during the retained run.");
            Assert.True(factory.HiddenExpiredEffects > 0, "Expired muzzle effects must publish hidden OAM.");
        }
    }

    private static string Diagnostic(FunctionalAcceptanceReport report)
    {
        var frames = report.IntegrityFailures.Select(failure => failure.Frame).Distinct().Take(4).ToHashSet();
        var evidence = report.FrameEvidence
            .Where(item => frames.Contains(item.Observed.Frame))
            .Select(item => $"frame={item.Observed.Frame} "
                + string.Join(',', item.Observed.StateSignals?.Select(signal => $"{signal.Key}={signal.Value}") ?? []));
        return Summary(report)
            + Environment.NewLine
            + string.Join(Environment.NewLine, evidence);
    }

    private static string Summary(FunctionalAcceptanceReport report) => string.Join(
        Environment.NewLine,
        new[]
        {
            $"scenario={report.ScenarioId} passed={report.Passed}",
            $"summary={report.Summary}",
        }
        .Concat(report.TimingChecks.Select(check =>
            $"timing metric={check.Metric} passed={check.Passed} observed={check.Observed} limit={check.Limit}"))
        .Concat(report.IntegrityFailures.Take(24).Select(failure =>
            $"integrity code={failure.Code} frame={failure.Frame} detail={failure.Detail}")));

    private static GameBoyCompilation Compile(
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

        var program = RetroSharp.GameBoy.GameBoyRomCompiler.PrepareVideoProgram(
            source,
            RepositoryDirectory(baseDirectoryRelativePath),
            SdkLibraryImportMode.ExplicitOnly,
            null,
            [SdkImportResolver.Portable2D],
            null);
        return new(program, GameBoyRomBuilder.BuildWithReport(program));
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

    private sealed record GameBoyCompilation(GameBoyVideoProgram Program, GameBoyRomBuildResult Build);

    private sealed class ActorProjectileGameBoyMachineFactory : IFunctionalRomMachineFactory
    {
        private readonly string sampleId;
        private readonly GameBoyVideoProgram program;
        private readonly GameBoyRomBuildReport report;

        public ActorProjectileGameBoyMachineFactory(
            string sampleId,
            GameBoyVideoProgram program,
            GameBoyRomBuildReport report)
        {
            this.sampleId = sampleId;
            this.program = program;
            this.report = report;
            Lifecycle = new(sampleId);
        }

        public byte[]? LoadedRom { get; private set; }

        public Dictionary<int, GameBoySnapshot> Snapshots { get; } = [];

        public FunctionalActorProjectileLifecycleTracker Lifecycle { get; }

        public int MaximumActiveProjectiles => Lifecycle.MaximumActiveProjectiles;

        public int MaximumUsedActorSpawns => Lifecycle.MaximumUsedActorSpawns;

        public int ActorSlotRecycles => Lifecycle.ActorSlotRecycles;

        public int DroppedRequests => Lifecycle.DroppedRequests;

        public int ReusedProjectileSlots => Lifecycle.ReusedProjectileSlots;

        public int BounceContacts => Lifecycle.BounceContacts;

        public int ExpiredEffects => Lifecycle.ExpiredEffects;

        public int HiddenExpiredEffects => Lifecycle.HiddenExpiredEffects;

        public int MaximumSpawnToVisibleFrames => Lifecycle.MaximumSpawnToVisibleFrames;

        public int MaximumActorTileContacts => Lifecycle.MaximumActorTileContacts;

        public int? MinimumGroundedActorWorldY => Lifecycle.MinimumGroundedActorWorldY;

        public int? MaximumGroundedActorWorldY => Lifecycle.MaximumGroundedActorWorldY;

        public IFunctionalRomMachine Create(ReadOnlyMemory<byte> exactRom)
        {
            LoadedRom = exactRom.ToArray();
            return new ActorProjectileGameBoyMachine(sampleId, program, report, LoadedRom, this);
        }
    }

    private sealed class ActorProjectileGameBoyMachine : IFunctionalRomMachine
    {
        private readonly string sampleId;
        private readonly GameBoyVideoProgram program;
        private readonly ActorProjectileGameBoyMachineFactory owner;
        private readonly GameBoyTestCpu cpu;
        private readonly IReadOnlyDictionary<string, GameBoyRuntimeUserVariable> variables;
        private readonly IReadOnlyList<GameBoySpritePlan> spritePlans;
        private readonly Dictionary<string, int[]> expectedOam = new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> expectedVisible = new(StringComparer.Ordinal);
        private int lastFrame;
        private int processedVramWrites;
        private int processedOamWrites;
        private (int X, int Y)? previousRequestedCamera;
        private readonly Dictionary<(int X, int Y), long> cameraSequenceByPosition = [];
        private long cameraRequestSequence;
        private long cameraVisibleSequence;

        public ActorProjectileGameBoyMachine(
            string sampleId,
            GameBoyVideoProgram program,
            GameBoyRomBuildReport report,
            byte[] exactRom,
            ActorProjectileGameBoyMachineFactory owner)
        {
            this.sampleId = sampleId;
            this.program = program;
            this.owner = owner;
            variables = report.UserVariables.ToDictionary(variable => variable.Name, StringComparer.Ordinal);
            spritePlans = BuildSpritePlans(sampleId, program);
            foreach (var plan in spritePlans)
            {
                expectedOam.Add(plan.Id, new int[plan.Asset.Pieces.Count * 4]);
                expectedVisible.Add(plan.Id, false);
            }
            cpu = new GameBoyTestCpu(exactRom)
            {
                CycleAccurateLy = true,
                EnforceVblankVramWrites = true,
            };
        }

        public FunctionalFrameObservation ObserveInitial() => Observe(0, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

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
            return Observe(frame, heldInputs);
        }

        public void Dispose()
        {
        }

        private FunctionalFrameObservation Observe(int frame, IReadOnlySet<string> heldInputs)
        {
            var visibleCamera = (X: (int)cpu.IoRegister(0xFF43), Y: (int)cpu.IoRegister(0xFF42));
            var projected = ProjectLogicalSprites(visibleCamera, Byte);
            var published = PublishedLogicalSprites(visibleCamera);
            var actual = CaptureSprites(published);
            UpdateLifecycle(frame, heldInputs, projected, actual);
            var expectedSprites = ProjectExpectedSprites(published);
            CrossCheckPublishedProjection(expectedSprites);
            var requestedCamera = (
                X: Word(CameraMemory.XLow, CameraMemory.XHigh),
                Y: Word(CameraMemory.YLow, CameraMemory.YHigh));
            var state = BuildStateSignals(visibleCamera, projected);
            var background = CaptureBackground(visibleCamera);
            var videoWrites = cpu.VramWrites.Skip(processedVramWrites).Select(VideoWrite).ToArray();
            var oamWrites = cpu.OamWrites.Skip(processedOamWrites).Select(OamWrite).ToArray();
            processedVramWrites = cpu.VramWrites.Count;
            processedOamWrites = cpu.OamWrites.Count;
            var unexpected = actual
                .Where(sprite => sprite.Id == "unused-oam")
                .SelectMany(sprite => sprite.Oam.Chunk(4))
                .Count(OamPieceVisible);
            var snapshot = new GameBoySnapshot(
                expectedSprites,
                background.Select(cell => new FunctionalBackgroundExpectation(cell.Location, cell.Tile, cell.Palette)).ToArray(),
                state,
                unexpected);
            owner.Snapshots[frame] = snapshot;

            return new FunctionalFrameObservation(
                frame,
                cpu.VBlankWaitCompletions,
                cpu.AudioUpdateCalls,
                cpu.ResetCount,
                state,
                CameraObservation(requestedCamera, visibleCamera),
                Background: CaptureObservedBackground(visibleCamera),
                Sprites: actual,
                VideoWrites: videoWrites,
                OamWrites: oamWrites,
                Spawn: owner.Lifecycle.Spawn);
        }

        private IReadOnlyDictionary<string, long> BuildStateSignals(
            (int X, int Y) visibleCamera,
            IReadOnlyList<GameBoyProjectedSprite> projected)
        {
            var lifecycle = owner.Lifecycle;
            var signals = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["sourceTick"] = cpu.VBlankWaitCompletions,
                ["sourceCameraX"] = sampleId == "actor-framework" ? Byte("cameraX") : 0,
                ["visibleCameraX"] = visibleCamera.X,
                ["displayEnabled"] = (cpu.IoRegister(0xFF40) & 0x80) != 0 ? 1 : 0,
                ["poolCapacity"] = 2,
                ["requestCapacity"] = sampleId == "actor-framework" ? 0 : 2,
                ["effectCapacity"] = sampleId == "runner-projectile" ? 4 : 0,
                ["activeProjectileCount"] = lifecycle.CurrentActiveProjectiles,
                ["visibleProjectileCount"] = projected.Count(sprite => sprite.Kind == SpritePlanKind.Projectile && sprite.Visible),
                ["droppedRequests"] = lifecycle.DroppedRequests,
                ["reusedProjectileSlots"] = lifecycle.ReusedProjectileSlots,
                ["bounceContacts"] = lifecycle.BounceContacts,
                ["expiredEffects"] = lifecycle.ExpiredEffects,
                ["actorTileContactCount"] = lifecycle.CurrentActorTileContacts,
                ["groundedActorCount"] = lifecycle.CurrentGroundedActors,
                ["groundedActorMinimumWorldY"] = lifecycle.CurrentMinimumGroundedActorWorldY ?? -1,
                ["groundedActorMaximumWorldY"] = lifecycle.CurrentMaximumGroundedActorWorldY ?? -1,
            };
            return signals;
        }

        private void UpdateLifecycle(
            int frame,
            IReadOnlySet<string> heldInputs,
            IReadOnlyList<GameBoyProjectedSprite> projected,
            IReadOnlyList<FunctionalSpriteObservation> actual)
        {
            var projectiles = sampleId == "actor-framework"
                ? []
                : Enumerable.Range(0, 2)
                    .Select(index => new FunctionalProjectileMotionObservation(
                        Byte($"shotsHero[{index}].active") != 0,
                        unchecked((sbyte)Byte($"shotsHero[{index}].vy"))))
                    .ToArray();
            var actors = sampleId == "actor-framework"
                ? Enumerable.Range(0, 2)
                    .Select(index => new FunctionalActorContactObservation(
                        Byte($"enemies[{index}].active") != 0,
                        Byte($"enemies[{index}].state"),
                        Word(Byte, $"enemies[{index}].y", $"enemies[{index}].yHi"),
                        unchecked((sbyte)Byte($"enemies[{index}].vy"))))
                    .ToArray()
                : [];
            owner.Lifecycle.Update(new(
                frame,
                heldInputs.ToArray(),
                projected
                    .Where(sprite => sprite.Kind is SpritePlanKind.Actor or SpritePlanKind.Projectile)
                    .Select(sprite => new FunctionalDynamicSpriteLifecycleObservation(
                        sprite.Id,
                        sprite.Kind == SpritePlanKind.Actor
                            ? FunctionalDynamicSpriteKind.Actor
                            : FunctionalDynamicSpriteKind.Projectile,
                        sprite.Active,
                        actual.Single(item => item.Id == sprite.Id).Visible))
                    .ToArray(),
                FireTick: sampleId is "shots-simple" or "shots-bouncy" ? Byte("fireTick") : null,
                UsedActorSpawns: sampleId == "actor-framework"
                    ? Enumerable.Range(0, 3).Count(index => Byte($"__enemies_spawn_0_used[{index}]") != 0)
                    : 0,
                ActiveActorCount: actors.Count(actor => actor.Active),
                Projectiles: projectiles,
                Effects: projected
                    .Where(sprite => sprite.Kind == SpritePlanKind.Effect)
                    .Select(sprite => new FunctionalEffectLifecycleObservation(sprite.Active, sprite.Visible))
                    .ToArray(),
                Actors: actors));
        }

        private IReadOnlyList<GameBoyProjectedSprite> PublishedLogicalSprites((int X, int Y) fallbackCamera)
        {
            if (cpu.OamDmaTransfers.Count == 0)
            {
                return ProjectLogicalSprites(fallbackCamera, Byte);
            }

            // Actor-framework draws after simulation, so its variables at the DMA boundary are
            // the tuple that fed shadow OAM. The projectile samples draw before simulation, so
            // their published tuple is the one captured at the preceding DMA boundary.
            var transferIndex = sampleId == "actor-framework" || cpu.OamDmaTransfers.Count == 1
                ? cpu.OamDmaTransfers.Count - 1
                : cpu.OamDmaTransfers.Count - 2;
            var logicalTransfer = cpu.OamDmaTransfers[transferIndex];
            var camera = (
                X: (int)logicalTransfer.IoSnapshot[0x43],
                Y: (int)logicalTransfer.IoSnapshot[0x42]);
            return ProjectLogicalSprites(camera, name => Byte(logicalTransfer.WramSnapshot, name));
        }

        private void CrossCheckPublishedProjection(IReadOnlyList<FunctionalSpriteExpectation> expected)
        {
            if (cpu.OamDmaTransfers.Count < 3)
            {
                return;
            }

            var expectedOamBytes = expected.SelectMany(sprite => sprite.Oam).Select(value => (byte)value).ToArray();
            var actualShadowPage = cpu.OamDmaTransfers[^1].SourceSnapshot;
            if (!expectedOamBytes.SequenceEqual(actualShadowPage.Take(expectedOamBytes.Length)))
            {
                throw new InvalidOperationException(
                    $"Semantic sprite projection for '{sampleId}' does not match the shadow OAM page published by the latest DMA.");
            }
        }

        private IReadOnlyList<GameBoyProjectedSprite> ProjectLogicalSprites(
            (int X, int Y) visibleCamera,
            Func<string, byte> readByte)
        {
            return spritePlans.Select(plan =>
            {
                if (plan.Kind == SpritePlanKind.Static)
                {
                    return new GameBoyProjectedSprite(plan.Id, plan.Kind, plan.OamSlot, plan.Asset, true, true, plan.FixedX, plan.FixedY, 0);
                }

                if (plan.Kind == SpritePlanKind.Actor)
                {
                    var actorActive = readByte($"enemies[{plan.Index}].active") != 0;
                    var worldX = Word(readByte, $"enemies[{plan.Index}].x", $"enemies[{plan.Index}].xHi");
                    var worldY = Word(readByte, $"enemies[{plan.Index}].y", $"enemies[{plan.Index}].yHi");
                    var deltaX = worldX - visibleCamera.X;
                    var deltaY = worldY - visibleCamera.Y;
                    var visible = actorActive && deltaX is >= 0 and < 160 && deltaY is >= 0 and < 144;
                    return new GameBoyProjectedSprite(
                        plan.Id,
                        plan.Kind,
                        plan.OamSlot,
                        plan.Asset,
                        actorActive,
                        visible,
                        visible ? deltaX : 0,
                        visible ? deltaY : 144,
                        0);
                }

                var active = readByte($"{plan.Pool}[{plan.Index}].active") != 0;
                var dynamicWorldX = Word(readByte, $"{plan.Pool}[{plan.Index}].x", $"{plan.Pool}[{plan.Index}].xHi");
                var dynamicWorldY = Word(readByte, $"{plan.Pool}[{plan.Index}].y", $"{plan.Pool}[{plan.Index}].yHi");
                var x = dynamicWorldX - visibleCamera.X;
                var y = dynamicWorldY - visibleCamera.Y;
                var dynamicVisible = active && x is >= 0 and < 160 && y is >= 0 and < 144;
                return new GameBoyProjectedSprite(
                    plan.Id,
                    plan.Kind,
                    plan.OamSlot,
                    plan.Asset,
                    active,
                    dynamicVisible,
                    dynamicVisible ? x : 0,
                    dynamicVisible ? y : 144,
                    0);
            }).ToArray();
        }

        private IReadOnlyList<FunctionalSpriteExpectation> ProjectExpectedSprites(
            IReadOnlyList<GameBoyProjectedSprite> projected)
        {
            var result = new List<FunctionalSpriteExpectation>();
            foreach (var sprite in projected)
            {
                var oam = expectedOam[sprite.Id];
                if (sprite.Visible)
                {
                    var offset = 0;
                    foreach (var piece in sprite.Asset.Pieces)
                    {
                        oam[offset++] = sprite.Y + piece.YOffset + 16;
                        oam[offset++] = sprite.X + piece.XOffset + 8;
                        oam[offset++] = sprite.Asset.FirstTile + sprite.Frame * sprite.Asset.TilesPerFrame + piece.TileOffset;
                        oam[offset++] = 0;
                    }
                }
                else if (sprite.Active || expectedVisible[sprite.Id])
                {
                    var offset = 0;
                    foreach (var piece in sprite.Asset.Pieces)
                    {
                        oam[offset++] = 160;
                        oam[offset++] = piece.XOffset + 8;
                        oam[offset++] = sprite.Asset.FirstTile + sprite.Frame * sprite.Asset.TilesPerFrame + piece.TileOffset;
                        oam[offset++] = 0;
                    }
                }

                expectedVisible[sprite.Id] = sprite.Visible;
                result.Add(new FunctionalSpriteExpectation(
                    sprite.Id,
                    sprite.Visible,
                    oam.ToArray(),
                    sprite.OamSlot));
            }

            return result;
        }

        private IReadOnlyList<FunctionalSpriteObservation> CaptureSprites(IReadOnlyList<GameBoyProjectedSprite> projected)
        {
            var sprites = projected.Select(sprite =>
            {
                var pieceCount = sprite.Asset.Pieces.Count;
                var oam = Enumerable.Range(0, pieceCount * 4)
                    .Select(offset => (int)cpu.Oam((ushort)(0xFE00 + sprite.OamSlot * 4 + offset)))
                    .ToArray();
                return new FunctionalSpriteObservation(sprite.Id, OamVisible(oam), oam, sprite.OamSlot);
            }).ToList();
            var usedSlots = spritePlans.Sum(plan => plan.Asset.Pieces.Count);
            var unused = Enumerable.Range(usedSlots * 4, (40 - usedSlots) * 4)
                .Select(offset => (int)cpu.Oam((ushort)(0xFE00 + offset)))
                .ToArray();
            sprites.Add(new FunctionalSpriteObservation("unused-oam", OamVisible(unused), unused, usedSlots));
            return sprites;
        }

        private IReadOnlyList<FunctionalBackgroundExpectation> CaptureBackground((int X, int Y) camera)
        {
            var width = camera.X % 8 == 0 ? 20 : 21;
            var height = camera.Y % 8 == 0 ? 18 : 19;
            var startColumn = camera.X / 8;
            var startRow = camera.Y / 8;
            return Enumerable.Range(0, height)
                .SelectMany(y => Enumerable.Range(0, width).Select(x =>
                {
                    var physical = ((startRow + y) & 31) * 32 + ((startColumn + x) & 31);
                    return new FunctionalBackgroundExpectation(
                        $"screen:{x:D2},{y:D2}",
                        program.TileMap[physical],
                        program.BackgroundPalette);
                }))
                .ToArray();
        }

        private IReadOnlyList<FunctionalBackgroundObservation> CaptureObservedBackground((int X, int Y) camera)
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

        private FunctionalCameraLifecycleObservation? CameraObservation(
            (int X, int Y) requested,
            (int X, int Y) visible)
        {
            if (sampleId != "actor-framework")
            {
                return null;
            }

            if (previousRequestedCamera != requested)
            {
                cameraRequestSequence++;
                previousRequestedCamera = requested;
                cameraSequenceByPosition[requested] = cameraRequestSequence;
            }

            if (cameraSequenceByPosition.TryGetValue(visible, out var sequence))
            {
                cameraVisibleSequence = sequence;
            }

            return new(cameraRequestSequence, cameraVisibleSequence, cameraVisibleSequence, cameraVisibleSequence);
        }

        private byte Byte(string name) => cpu.Wram(variables[name].Address);

        private byte Byte(IReadOnlyList<byte> wram, string name) => wram[variables[name].Address - 0xC000];

        private int Word(string low, string high) => Byte(low) | (Byte(high) << 8);

        private static int Word(Func<string, byte> readByte, string low, string high) =>
            readByte(low) | (readByte(high) << 8);

        private int Word(ushort low, ushort high) => cpu.Wram(low) | (cpu.Wram(high) << 8);

        private static bool OamVisible(IReadOnlyList<int> oam) =>
            oam.Chunk(4).Any(OamPieceVisible);

        private static bool OamPieceVisible(IReadOnlyList<int> piece) => piece[0] is > 0 and < 160;

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

        private static IReadOnlyList<GameBoySpritePlan> BuildSpritePlans(string sampleId, GameBoyVideoProgram program)
        {
            var plans = new List<(string Id, SpritePlanKind Kind, string Asset, int Index, int X, int Y, string Pool, string Prefix, string Definition)>();
            switch (sampleId)
            {
                case "actor-framework":
                    plans.Add(("actor-0", SpritePlanKind.Actor, "actor_marker", 0, 0, 0, "enemies", "", ""));
                    plans.Add(("actor-1", SpritePlanKind.Actor, "actor_marker", 1, 0, 0, "enemies", "", ""));
                    break;
                case "shots-simple":
                case "shots-bouncy":
                    plans.Add(("player", SpritePlanKind.Static, "player", 0, sampleId == "shots-simple" ? 24 : 16, sampleId == "shots-simple" ? 72 : 120, "", "", ""));
                    for (var index = 0; index < 2; index++)
                    {
                        plans.Add(($"projectile-{index}", SpritePlanKind.Projectile, "shot", index, 0, 0, "shotsHero", $"__shots_draw_hero_{index}", "Shot"));
                    }
                    break;
                case "runner-projectile":
                    plans.Add(("player", SpritePlanKind.Static, "mario_player", 0, 72, 96, "", "", ""));
                    for (var index = 0; index < 2; index++)
                    {
                        plans.Add(($"projectile-{index}", SpritePlanKind.Projectile, "mario_shot", index, 0, 0, "shotsHero", $"__shots_draw_hero_{index}", "MarioFireball"));
                    }
                    for (var index = 0; index < 4; index++)
                    {
                        plans.Add(($"effect-{index}", SpritePlanKind.Effect, "muzzle_flash", index, 0, 0, "fx", $"__fx_draw_{index}", "MuzzleFlash"));
                    }
                    break;
                default:
                    throw new InvalidOperationException(sampleId);
            }

            var result = new List<GameBoySpritePlan>();
            var slot = 0;
            foreach (var plan in plans)
            {
                var asset = program.SpriteAssets.Single(pair =>
                    pair.Key == plan.Asset || pair.Key.EndsWith($".{plan.Asset}", StringComparison.Ordinal)).Value;
                result.Add(new(
                    plan.Id,
                    plan.Kind,
                    asset,
                    plan.Index,
                    slot,
                    plan.X,
                    plan.Y,
                    plan.Pool,
                    plan.Prefix,
                    plan.Definition));
                slot += asset.Pieces.Count;
            }

            return result;
        }
    }

    private sealed class ActorProjectileGameBoyOracle(ActorProjectileGameBoyMachineFactory factory) : IFunctionalFrameOracle
    {
        public FunctionalFrameExpectation ExpectedFrame(int frame)
        {
            var snapshot = factory.Snapshots[frame];
            var sprites = snapshot.Sprites.ToList();
            var usedSlots = snapshot.Sprites.Sum(sprite => sprite.Oam.Count / 4);
            var unused = Enumerable.Range(usedSlots, 40 - usedSlots)
                .SelectMany(_ => new[] { 0, 0, 0, 0 })
                .ToArray();
            sprites.Add(new FunctionalSpriteExpectation("unused-oam", false, unused, usedSlots));
            return new(frame, snapshot.Background, sprites);
        }

    }

    private sealed record GameBoySnapshot(
        IReadOnlyList<FunctionalSpriteExpectation> Sprites,
        IReadOnlyList<FunctionalBackgroundExpectation> Background,
        IReadOnlyDictionary<string, long> Signals,
        int UnexpectedVisibleOamSlots);

    private sealed record GameBoyProjectedSprite(
        string Id,
        SpritePlanKind Kind,
        int OamSlot,
        GameBoyCompiledSpriteAsset Asset,
        bool Active,
        bool Visible,
        int X,
        int Y,
        int Frame);

    private sealed record GameBoySpritePlan(
        string Id,
        SpritePlanKind Kind,
        GameBoyCompiledSpriteAsset Asset,
        int Index,
        int OamSlot,
        int FixedX,
        int FixedY,
        string Pool,
        string ProjectionPrefix,
        string Definition);

    private enum SpritePlanKind
    {
        Static,
        Actor,
        Projectile,
        Effect,
    }
}
