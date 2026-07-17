namespace RetroSharp.FunctionalAcceptance.Tests;

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

public sealed class FunctionalScenarioRunnerTests
{
    [Fact]
    public void Timing_contract_exposes_spawn_to_visible_budget()
    {
        Assert.NotNull(typeof(FunctionalTimingBudgets).GetProperty("MaximumSpawnToVisibleFrames"));
        Assert.NotNull(typeof(FunctionalFrameObservation).GetProperty("Spawn"));
    }

    [Fact]
    public void Sprite_contract_exposes_explicit_oam_slot_order()
    {
        Assert.NotNull(typeof(FunctionalSpriteObservation).GetProperty("OamSlot"));
        Assert.NotNull(typeof(FunctionalSpriteExpectation).GetProperty("OamSlot"));
    }

    [Fact]
    public void Checked_in_json_schema_requires_the_stable_contract_keys()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(RepositoryFile("validation/scenarios/functional-scenario.schema.json")));
        var root = document.RootElement;
        var required = root.GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToArray();

        Assert.Contains("sampleId", required);
        Assert.Contains("target", required);
        Assert.Contains("warmUpFrames", required);
        Assert.Contains("observationFrames", required);
        Assert.Contains("inputs", required);
        Assert.Contains("checkpoints", required);
        Assert.Contains("expectedFeatures", required);
        Assert.Contains("audio", required);
        Assert.Contains("budgetEvidence", required);
        Assert.Contains("budgets", required);
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[] { "gb", "nes" },
            root.GetProperty("properties").GetProperty("target").GetProperty("enum")
                .EnumerateArray().Select(item => item.GetString()).ToArray());
    }

    [Fact]
    public void Checked_in_scenario_declares_identity_timeline_window_checkpoints_features_and_budgets()
    {
        var scenario = FunctionalScenarioLoader.Load(RepositoryFile("validation/scenarios/fixtures/contract-probe.json"));

        Assert.Equal("contract-probe-gb", scenario.Id);
        Assert.Equal("contract-probe", scenario.SampleId);
        Assert.Equal(FunctionalTarget.GameBoy, scenario.Target);
        Assert.Equal(2, scenario.WarmUpFrames);
        Assert.Equal(4, scenario.ObservationFrames);
        var input = Assert.Single(scenario.Inputs);
        Assert.Equal(("move-right", 3, 2, "playerX"), (input.Id, input.StartFrame, input.DurationFrames, input.ResponseSignal));
        Assert.Equal(["right"], input.Buttons);
        var checkpoint = Assert.Single(scenario.Checkpoints);
        Assert.Equal(6, checkpoint.Frame);
        Assert.Equal(2, checkpoint.ExpectedSignals["playerX"]);
        Assert.True(scenario.ExpectedFeatures.GameplayTicks);
        Assert.True(scenario.ExpectedFeatures.AudioService);
        Assert.True(scenario.ExpectedFeatures.CameraLifecycle);
        Assert.True(scenario.Audio.ServiceExpectedByDefault);
        Assert.Empty(scenario.Audio.AuthoredSilence);
        Assert.Equal("95f166886713ff3b88bc1e17c03ef0ffe93d649a", scenario.BudgetEvidence.BaselineCommit);
        Assert.False(string.IsNullOrWhiteSpace(scenario.BudgetEvidence.HardwareTimingRationale));
        Assert.False(string.IsNullOrWhiteSpace(scenario.BudgetEvidence.ProductionTraceRationale));
        Assert.Equal(0.90, scenario.Budgets.MinimumGameplayTickRatio);
        Assert.Equal(1, scenario.Budgets.MaximumConsecutiveMissedGameplayTicks);
        Assert.Equal(2, scenario.Budgets.MaximumInputToStateFrames);
        Assert.Equal(2, scenario.Budgets.MaximumRequestToResidentFrames);
        Assert.Equal(3, scenario.Budgets.MaximumRequestToVisibleFrames);
        Assert.Equal(1, scenario.Budgets.MaximumUnplannedAudioGapFrames);
        Assert.Equal(1, scenario.Budgets.MaximumAudioDriftTicks);
    }

    [Fact]
    public void Csl3_scenarios_cover_every_declared_static_and_source_camera_target()
    {
        var expected = new[]
        {
            ("static-drawing.gb.json", "static-drawing", FunctionalTarget.GameBoy, false, false),
            ("static-drawing.nes.json", "static-drawing", FunctionalTarget.Nes, false, false),
            ("cross-target-camera.gb.json", "cross-target-camera", FunctionalTarget.GameBoy, true, true),
            ("cross-target-camera.nes.json", "cross-target-camera", FunctionalTarget.Nes, true, true),
            ("source-vscroll.gb.json", "source-vscroll", FunctionalTarget.GameBoy, true, true),
            ("source-free-scroll.gb.json", "source-free-scroll", FunctionalTarget.GameBoy, true, true),
            ("source-free-scroll.nes.json", "source-free-scroll", FunctionalTarget.Nes, true, true),
            ("window-hud.gb.json", "window-hud", FunctionalTarget.GameBoy, true, false),
        };

        foreach (var (file, sampleId, target, gameplay, camera) in expected)
        {
            var scenario = FunctionalScenarioLoader.Load(RepositoryFile($"validation/scenarios/{file}"));

            Assert.Equal(sampleId, scenario.SampleId);
            Assert.Equal(target, scenario.Target);
            Assert.Equal("95f166886713ff3b88bc1e17c03ef0ffe93d649a", scenario.BudgetEvidence.BaselineCommit);
            Assert.Equal(gameplay, scenario.ExpectedFeatures.GameplayTicks);
            Assert.Equal(camera, scenario.ExpectedFeatures.CameraLifecycle);
            Assert.True(scenario.ExpectedFeatures.Background);
            Assert.True(scenario.ExpectedFeatures.SafeVideoWrites);
            Assert.False(scenario.ExpectedFeatures.AudioService);
            Assert.False(scenario.ExpectedFeatures.SpriteOam);
            Assert.False(scenario.ExpectedFeatures.BankRestoration);
            Assert.True(scenario.ObservationFrames >= 30);
            Assert.False(string.IsNullOrWhiteSpace(scenario.BudgetEvidence.HardwareTimingRationale));
            Assert.False(string.IsNullOrWhiteSpace(scenario.BudgetEvidence.ProductionTraceRationale));
        }
    }

    [Fact]
    public void Csl6_scenarios_cover_every_declared_actor_and_projectile_target()
    {
        var expected = new[]
        {
            ("actor-framework.gb.json", "actor-framework", FunctionalTarget.GameBoy),
            ("actor-framework.nes.json", "actor-framework", FunctionalTarget.Nes),
            ("shots-simple.gb.json", "shots-simple", FunctionalTarget.GameBoy),
            ("shots-simple.nes.json", "shots-simple", FunctionalTarget.Nes),
            ("shots-bouncy.gb.json", "shots-bouncy", FunctionalTarget.GameBoy),
            ("shots-bouncy.nes.json", "shots-bouncy", FunctionalTarget.Nes),
            ("runner-projectile.gb.json", "runner-projectile", FunctionalTarget.GameBoy),
            ("runner-projectile.nes.json", "runner-projectile", FunctionalTarget.Nes),
        };

        foreach (var (file, sampleId, target) in expected)
        {
            var scenario = FunctionalScenarioLoader.Load(RepositoryFile($"validation/scenarios/{file}"));

            Assert.Equal(sampleId, scenario.SampleId);
            Assert.Equal(target, scenario.Target);
            Assert.Equal("95f166886713ff3b88bc1e17c03ef0ffe93d649a", scenario.BudgetEvidence.BaselineCommit);
            Assert.True(scenario.ExpectedFeatures.GameplayTicks);
            Assert.True(scenario.ExpectedFeatures.Background);
            Assert.True(scenario.ExpectedFeatures.SpriteOam);
            Assert.True(scenario.ExpectedFeatures.SafeVideoWrites);
            Assert.False(scenario.ExpectedFeatures.AudioService);
            Assert.NotNull(scenario.Budgets.MaximumSpawnToVisibleFrames);
        }
    }

    [Fact]
    public void Scenario_loader_rejects_unknown_contract_fields()
    {
        var source = File.ReadAllText(RepositoryFile("validation/scenarios/fixtures/contract-probe.json"));
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, source.Replace("\"id\":", "\"unexpected\": true,\n  \"id\":", StringComparison.Ordinal));

            var exception = Assert.Throws<InvalidOperationException>(() => FunctionalScenarioLoader.Load(path));

            Assert.Contains("unexpected", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Scenario_loader_rejects_a_feature_omitted_from_the_checked_schema_contract()
    {
        var source = File.ReadAllText(RepositoryFile("validation/scenarios/fixtures/contract-probe.json"));
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, source.Replace("    \"spriteOam\": false,\n", string.Empty, StringComparison.Ordinal));

            var exception = Assert.Throws<InvalidOperationException>(() => FunctionalScenarioLoader.Load(path));

            Assert.Contains("spriteOam", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Warm_up_variance_is_excluded_from_timing_budgets()
    {
        var scenario = TimingScenario();
        var observations = Frames(
            gameplayTicks: [0, 0, 0, 1, 2, 3, 4],
            audioTicks: [0, 0, 0, 1, 2, 3, 4]);
        var adapter = GameBoyAdapter(observations);

        var report = FunctionalScenarioRunner.Run(scenario, Rom(), adapter);

        Assert.True(report.Passed, report.ToHumanReadable());
        Assert.All(report.TimingChecks, check => Assert.True(check.Passed, check.Metric));
    }

    [Fact]
    public void Degraded_gameplay_and_audio_fail_absolute_budgets()
    {
        var scenario = TimingScenario();
        var observations = Frames(
            gameplayTicks: [0, 0, 0, 1, 1, 1, 2],
            audioTicks: [0, 0, 0, 1, 1, 1, 2]);
        var adapter = GameBoyAdapter(observations);

        var report = FunctionalScenarioRunner.Run(scenario, Rom(), adapter);

        Assert.False(report.Passed);
        Assert.False(Assert.Single(report.TimingChecks, check => check.Metric == "gameplay-tick-ratio").Passed);
        Assert.False(Assert.Single(report.TimingChecks, check => check.Metric == "gameplay-missed-streak").Passed);
        Assert.False(Assert.Single(report.TimingChecks, check => check.Metric == "audio-service-gap").Passed);
        Assert.False(Assert.Single(report.TimingChecks, check => check.Metric == "audio-drift").Passed);
    }

    [Fact]
    public void Audio_service_cannot_claim_authored_silence_that_the_scenario_did_not_declare()
    {
        var scenario = TimingScenario();
        var observations = Frames(
            gameplayTicks: [0, 0, 0, 1, 2, 3, 4],
            audioTicks: [0, 0, 0, 0, 0, 0, 0]);

        var report = FunctionalScenarioRunner.Run(scenario, Rom(), GameBoyAdapter(observations));

        Assert.False(report.Passed);
        Assert.False(Assert.Single(report.TimingChecks, check => check.Metric == "audio-service-gap").Passed);
        Assert.False(Assert.Single(report.TimingChecks, check => check.Metric == "audio-drift").Passed);
    }

    [Fact]
    public void Ordered_audio_progress_accepts_the_exact_register_and_lifecycle_trace()
    {
        var observations = HealthyAudioProgressFrames();
        var scenario = AudioProgressScenario(observations);

        var report = FunctionalScenarioRunner.Run(scenario, Rom(), GameBoyAdapter(observations));

        Assert.True(report.Passed, report.ToHumanReadable());
        Assert.All(report.TimingChecks, check => Assert.True(check.Passed, check.Metric));
    }

    [Fact]
    public void Ordered_audio_progress_rejects_a_frozen_register_stream()
    {
        var healthy = HealthyAudioProgressFrames();
        var scenario = AudioProgressScenario(healthy);
        var frozen = healthy.Select((frame, index) => index < 3
                ? frame
                : frame with
                {
                    AudioProgress = frame.AudioProgress! with
                    {
                        RegisterEventCount = frame.AudioProgress!.RegisterEventCount - 1,
                        RegisterEvents = index == 3 ? [] : frame.AudioProgress.RegisterEvents,
                    },
                })
            .ToArray();

        var report = FunctionalScenarioRunner.Run(scenario, Rom(), GameBoyAdapter(frozen));

        Assert.False(report.Passed);
        Assert.False(Assert.Single(report.TimingChecks, check => check.Metric == "audio-register-events").Passed);
        Assert.Contains(report.IntegrityFailures, failure => failure.Code == "audio-register-order");
    }

    [Fact]
    public void Ordered_audio_progress_rejects_restart_storms_and_a_stuck_sound_effect()
    {
        var healthy = HealthyAudioProgressFrames();
        var scenario = AudioProgressScenario(healthy);
        var degraded = healthy.Select((frame, index) => index < 3
                ? frame
                : frame with
                {
                    AudioProgress = frame.AudioProgress! with
                    {
                        SoundEffect = new(
                            Active: true,
                            Starts: index == 3 ? 3 : 4,
                            Completions: 1,
                            Restarts: index == 3 ? 1 : 2),
                    },
                })
            .ToArray();

        var report = FunctionalScenarioRunner.Run(scenario, Rom(), GameBoyAdapter(degraded));

        Assert.False(report.Passed);
        Assert.False(Assert.Single(report.TimingChecks, check => check.Metric == "audio-sfx-starts-maximum").Passed);
        Assert.False(Assert.Single(report.TimingChecks, check => check.Metric == "audio-sfx-completions").Passed);
        Assert.False(Assert.Single(report.TimingChecks, check => check.Metric == "audio-sfx-restarts").Passed);
        Assert.Contains(report.IntegrityFailures, failure => failure.Code == "audio-sfx-active");
    }

    [Fact]
    public void Ordered_audio_progress_rejects_reordered_registers_and_truncated_dpcm()
    {
        var healthy = HealthyAudioProgressFrames();
        var scenario = AudioProgressScenario(healthy);
        var degraded = healthy.Select((frame, index) => index switch
            {
                3 => frame with
                {
                    AudioProgress = frame.AudioProgress! with
                    {
                        RegisterEvents = [new("apu", 0x11, 0x7F)],
                        Dpcm = new(Active: true, Starts: 1, Completions: 0, Restarts: 0),
                    },
                },
                >= 3 => frame with
                {
                    AudioProgress = frame.AudioProgress! with
                    {
                        Dpcm = new(Active: true, Starts: 1, Completions: 0, Restarts: 0),
                    },
                },
                _ => frame,
            })
            .ToArray();

        var report = FunctionalScenarioRunner.Run(scenario, Rom(), GameBoyAdapter(degraded));

        Assert.False(report.Passed);
        Assert.False(Assert.Single(report.TimingChecks, check => check.Metric == "audio-dpcm-completions").Passed);
        Assert.Contains(report.IntegrityFailures, failure => failure.Code == "audio-dpcm-active");
        Assert.Contains(report.IntegrityFailures, failure => failure.Code == "audio-register-order");
    }

    [Fact]
    public void Scenario_owned_authored_silence_still_requires_audio_service_heartbeat()
    {
        var scenario = TimingScenario() with
        {
            Audio = new(ServiceExpectedByDefault: true, AuthoredSilence: [new(3, 4)]),
        };
        var observations = Frames(
            gameplayTicks: [0, 0, 0, 1, 2, 3, 4],
            audioTicks: [0, 0, 0, 0, 0, 0, 0]);

        var report = FunctionalScenarioRunner.Run(scenario, Rom(), GameBoyAdapter(observations));

        Assert.False(report.Passed);
        Assert.False(Assert.Single(report.TimingChecks, check => check.Metric == "audio-service-gap").Passed);
        Assert.False(Assert.Single(report.TimingChecks, check => check.Metric == "audio-drift").Passed);
    }

    [Fact]
    public void A_transient_background_corruption_fails_even_when_the_final_frame_is_correct()
    {
        var scenario = IntegrityScenario(background: true);
        var observations = new[]
        {
            Frame(0, background: [Background("0,0", 7, 2)]),
            Frame(1, background: [Background("0,0", 3, 1)]),
            Frame(2, background: [Background("0,0", 7, 2)]),
        };
        var oracle = new ScriptedOracle(
            frame => new(frame, Background: [ExpectedBackground("0,0", 7, 2)]));

        var report = FunctionalScenarioRunner.Run(scenario, Rom(), GameBoyAdapter(observations), oracle);

        Assert.False(report.Passed);
        Assert.Contains(report.IntegrityFailures, failure => failure.Code == "background-tile" && failure.Frame == 1);
        Assert.Contains(report.IntegrityFailures, failure => failure.Code == "background-palette" && failure.Frame == 1);
        var evidence = Assert.Single(report.FrameEvidence, item => item.Observed.Frame == 1);
        Assert.Equal(3, Assert.Single(evidence.Observed.Background!).Tile);
        Assert.Equal(7, Assert.Single(evidence.Expected!.Background!).Tile);
    }

    [Fact]
    public void A_transient_oam_corruption_fails_even_when_the_final_frame_is_correct()
    {
        var scenario = IntegrityScenario(spriteOam: true);
        var observations = new[]
        {
            Frame(0, sprites: [Sprite("player", true, [16, 24, 3, 0])]),
            Frame(1, sprites: [Sprite("player", false, [0, 0, 0, 0])]),
            Frame(2, sprites: [Sprite("player", true, [16, 24, 3, 0])]),
        };
        var oracle = new ScriptedOracle(
            frame => new(frame, Sprites: [ExpectedSprite("player", true, [16, 24, 3, 0])]));

        var report = FunctionalScenarioRunner.Run(scenario, Rom(), GameBoyAdapter(observations), oracle);

        Assert.False(report.Passed);
        Assert.Contains(report.IntegrityFailures, failure => failure.Code == "sprite-visibility" && failure.Frame == 1);
        Assert.Contains(report.IntegrityFailures, failure => failure.Code == "sprite-oam" && failure.Frame == 1);
    }

    [Fact]
    public void Spawn_to_visible_latency_is_measured_from_accepted_spawn_transitions()
    {
        var scenario = IntegrityScenario(spriteOam: true) with
        {
            Budgets = new(0, 2, MaximumSpawnToVisibleFrames: 1),
        };
        var hidden = Sprite("projectile-0", false, [160, 8, 6, 0]);
        var visible = Sprite("projectile-0", true, [64, 40, 6, 0]);
        var observations = new[]
        {
            Frame(0, sprites: [hidden], spawn: new(null, null)),
            Frame(1, sprites: [hidden], spawn: new(1, null)),
            Frame(2, sprites: [visible], spawn: new(1, 1)),
        };
        var oracle = new ScriptedOracle(frame => new(
            frame,
            Sprites: frame == 2
                ? [ExpectedSprite("projectile-0", true, [64, 40, 6, 0])]
                : [ExpectedSprite("projectile-0", false, [160, 8, 6, 0])]));

        var report = FunctionalScenarioRunner.Run(scenario, Rom(), GameBoyAdapter(observations), oracle);

        Assert.True(report.Passed, report.ToHumanReadable());
        Assert.Equal(1, Assert.Single(report.TimingChecks, check => check.Metric == "spawn-to-visible").Observed);
    }

    [Fact]
    public void Spawn_budget_rejects_a_window_without_any_accepted_spawn()
    {
        var scenario = IntegrityScenario(spriteOam: true) with
        {
            Budgets = new(0, 2, MaximumSpawnToVisibleFrames: 1),
        };
        var hidden = Sprite("projectile-0", false, [160, 8, 6, 0]);
        var observations = new[]
        {
            Frame(0, sprites: [hidden], spawn: new(null, null)),
            Frame(1, sprites: [hidden], spawn: new(null, null)),
            Frame(2, sprites: [hidden], spawn: new(null, null)),
        };
        var oracle = new ScriptedOracle(frame => new(frame, Sprites: [ExpectedSprite("projectile-0", false, [160, 8, 6, 0])]));

        var report = FunctionalScenarioRunner.Run(scenario, Rom(), GameBoyAdapter(observations), oracle);

        Assert.False(report.Passed);
        Assert.Contains(report.IntegrityFailures, failure => failure.Code == "missing-spawn-transition");
        Assert.False(Assert.Single(report.TimingChecks, check => check.Metric == "spawn-to-visible").Passed);
    }

    [Fact]
    public void Spawn_budget_rejects_an_unresolved_spawn_on_the_final_frame()
    {
        var scenario = IntegrityScenario(spriteOam: true) with
        {
            Budgets = new(0, 2, MaximumSpawnToVisibleFrames: 1),
        };
        var hidden = Sprite("projectile-0", false, [160, 8, 6, 0]);
        var observations = new[]
        {
            Frame(0, sprites: [hidden], spawn: new(null, null)),
            Frame(1, sprites: [hidden], spawn: new(null, null)),
            Frame(2, sprites: [hidden], spawn: new(1, null)),
        };
        var oracle = new ScriptedOracle(frame => new(frame, Sprites: [ExpectedSprite("projectile-0", false, [160, 8, 6, 0])]));

        var report = FunctionalScenarioRunner.Run(scenario, Rom(), GameBoyAdapter(observations), oracle);

        Assert.False(report.Passed);
        Assert.Contains(report.IntegrityFailures, failure => failure.Code == "spawn-not-visible-within-budget");
        Assert.False(Assert.Single(report.TimingChecks, check => check.Metric == "spawn-to-visible").Passed);
    }

    [Fact]
    public void Later_visible_spawn_cannot_mask_a_skipped_sequence()
    {
        var scenario = IntegrityScenario(spriteOam: true) with
        {
            Budgets = new(0, 2, MaximumSpawnToVisibleFrames: 1),
        };
        var hidden = Sprite("projectile-0", false, [160, 8, 6, 0]);
        var visible = Sprite("projectile-0", true, [64, 40, 6, 0]);
        var observations = new[]
        {
            Frame(0, sprites: [hidden], spawn: new(null, null)),
            Frame(1, sprites: [hidden], spawn: new(1, null)),
            Frame(2, sprites: [visible], spawn: new(2, 2)),
        };
        var oracle = new ScriptedOracle(frame => new(
            frame,
            Sprites: frame == 2
                ? [ExpectedSprite("projectile-0", true, [64, 40, 6, 0])]
                : [ExpectedSprite("projectile-0", false, [160, 8, 6, 0])]));

        var report = FunctionalScenarioRunner.Run(scenario, Rom(), GameBoyAdapter(observations), oracle);

        Assert.False(report.Passed);
        Assert.Contains(report.IntegrityFailures, failure => failure.Code == "spawn-not-visible-within-budget");
    }

    [Fact]
    public void Controlled_stale_oam_from_disappeared_logical_sprite_is_rejected()
    {
        var scenario = IntegrityScenario(spriteOam: true);
        var visible = new FunctionalSpriteObservation("projectile-0", true, [64, 40, 6, 0], OamSlot: 0);
        var hidden = new FunctionalSpriteObservation("projectile-0", false, [160, 8, 6, 0], OamSlot: 0);
        var observations = new[]
        {
            Frame(0, sprites: [visible]),
            Frame(1, sprites: [visible]),
            Frame(2, sprites: [hidden]),
        };
        var oracle = new ScriptedOracle(frame => new(
            frame,
            Sprites:
            [
                frame == 0
                    ? new FunctionalSpriteExpectation("projectile-0", true, [64, 40, 6, 0], OamSlot: 0)
                    : new FunctionalSpriteExpectation("projectile-0", false, [160, 8, 6, 0], OamSlot: 0),
            ]));

        var report = FunctionalScenarioRunner.Run(scenario, Rom(), GameBoyAdapter(observations), oracle);

        Assert.False(report.Passed);
        Assert.Contains(report.IntegrityFailures, failure => failure.Code == "sprite-visibility" && failure.Frame == 1);
        Assert.Contains(report.IntegrityFailures, failure => failure.Code == "sprite-oam" && failure.Frame == 1);
        Assert.DoesNotContain(report.IntegrityFailures, failure => failure.Code == "sprite-oam-slot");
    }

    [Fact]
    public void Wrong_oam_slot_metadata_is_rejected_independently_of_sprite_bytes()
    {
        var scenario = IntegrityScenario(spriteOam: true);
        var observations = new[]
        {
            Frame(0, sprites: [new("projectile-0", false, [160, 8, 6, 0], OamSlot: 0)]),
            Frame(1, sprites: [new("projectile-0", false, [160, 8, 6, 0], OamSlot: 1)]),
            Frame(2, sprites: [new("projectile-0", false, [160, 8, 6, 0], OamSlot: 0)]),
        };
        var oracle = new ScriptedOracle(frame => new(
            frame,
            Sprites: [new FunctionalSpriteExpectation("projectile-0", false, [160, 8, 6, 0], OamSlot: 0)]));

        var report = FunctionalScenarioRunner.Run(scenario, Rom(), GameBoyAdapter(observations), oracle);

        Assert.False(report.Passed);
        var failure = Assert.Single(report.IntegrityFailures);
        Assert.Equal(("sprite-oam-slot", 1), (failure.Code, failure.Frame));
    }

    [Fact]
    public void An_empty_visual_oracle_cannot_satisfy_a_background_scenario()
    {
        var scenario = IntegrityScenario(background: true);
        var observations = new[]
        {
            Frame(0, background: []),
            Frame(1, background: []),
            Frame(2, background: []),
        };
        var oracle = new ScriptedOracle(frame => new(frame, Background: []));

        var report = FunctionalScenarioRunner.Run(scenario, Rom(), GameBoyAdapter(observations), oracle);

        Assert.False(report.Passed);
        Assert.Contains(report.IntegrityFailures, failure => failure.Code == "empty-background-oracle");
    }

    [Fact]
    public void Unsafe_video_and_oam_writes_resets_and_bank_leaks_are_strict_failures()
    {
        var scenario = IntegrityScenario(bank: true, safeVideoWrites: true, spriteOam: true);
        var observations = new[]
        {
            Frame(0),
            Frame(
                1,
                resetCount: 1,
                bank: new FunctionalBankObservation(3, 2, false),
                videoWrites: [new("vram", 0x9800, false, WriteTiming())],
                oamWrites: [new(0xFE00, false, WriteTiming())],
                sprites: [Sprite("player", true, [16, 24, 3, 0])]),
            Frame(2, resetCount: 1),
        };
        var oracle = new ScriptedOracle(frame => new(
            frame,
            Sprites: frame == 1 ? [ExpectedSprite("player", true, [16, 24, 3, 0])] : [],
            Background: []));

        var report = FunctionalScenarioRunner.Run(scenario, Rom(), GameBoyAdapter(observations), oracle);

        Assert.False(report.Passed);
        Assert.Contains(report.IntegrityFailures, failure => failure.Code == "reset");
        Assert.Contains(report.IntegrityFailures, failure => failure.Code == "bank-restoration");
        Assert.Contains(report.IntegrityFailures, failure => failure.Code == "unsafe-video-write");
        Assert.Contains(report.IntegrityFailures, failure => failure.Code == "unsafe-oam-write");
        Assert.Contains(report.IntegrityFailures, failure => failure.Detail.Contains("scanline=42", StringComparison.Ordinal));
        var writeEvidence = Assert.Single(report.FrameEvidence, item => item.Observed.Frame == 1);
        Assert.Equal(1_234, Assert.Single(writeEvidence.Observed.VideoWrites!).Timing!.Cycle);
        Assert.Equal("visible", Assert.Single(writeEvidence.Observed.OamWrites!).Timing!.Phase);
    }

    [Fact]
    public void Input_and_camera_lifecycle_latencies_are_measured_from_observed_transitions()
    {
        var scenario = LifecycleScenario();
        var observations = new[]
        {
            Frame(0, signals: Signals(("playerX", 0)), camera: Camera()),
            Frame(1, signals: Signals(("playerX", 0)), camera: Camera(requested: 10)),
            Frame(2, signals: Signals(("playerX", 1)), camera: Camera(requested: 10, resident: 10)),
            Frame(3, signals: Signals(("playerX", 2)), camera: Camera(requested: 10, resident: 10, committed: 10)),
            Frame(4, signals: Signals(("playerX", 3)), camera: Camera(requested: 10, resident: 10, committed: 10, visible: 10)),
        };

        var report = FunctionalScenarioRunner.Run(scenario, Rom(), GameBoyAdapter(observations));

        Assert.True(report.Passed, report.ToHumanReadable());
        Assert.Equal(1, Assert.Single(report.TimingChecks, check => check.Metric == "input-to-state").Observed);
        Assert.Equal(1, Assert.Single(report.TimingChecks, check => check.Metric == "request-to-resident").Observed);
        Assert.Equal(3, Assert.Single(report.TimingChecks, check => check.Metric == "request-to-visible").Observed);
    }

    [Fact]
    public void Multiple_camera_lifecycle_transitions_in_one_sampled_frame_are_retained_and_counted()
    {
        var scenario = LifecycleScenario();
        var observations = new[]
        {
            Frame(0, signals: Signals(("playerX", 0)), camera: Camera(10, 10, 10, 10)),
            Frame(1, signals: Signals(("playerX", 1)), camera: Camera(13, 13, 13, 13)),
            Frame(2, signals: Signals(("playerX", 2)), camera: Camera(13, 13, 13, 13)),
            Frame(3, signals: Signals(("playerX", 3)), camera: Camera(13, 13, 13, 13)),
            Frame(4, signals: Signals(("playerX", 3)), camera: Camera(13, 13, 13, 13)),
        };

        var report = FunctionalScenarioRunner.Run(scenario, Rom(), GameBoyAdapter(observations));

        Assert.True(report.Passed, report.ToHumanReadable());
        Assert.Equal(3, report.Summary.CameraRequests);
        Assert.Equal(3, report.Summary.CameraResidents);
        Assert.Equal(3, report.Summary.CameraCommits);
        Assert.Equal(3, report.Summary.CameraVisible);
        Assert.Equal(0, Assert.Single(report.TimingChecks, check => check.Metric == "request-to-resident").Observed);
        Assert.Equal(0, Assert.Single(report.TimingChecks, check => check.Metric == "request-to-visible").Observed);
    }

    [Fact]
    public void Final_camera_request_drains_only_within_its_declared_latency_budget()
    {
        var scenario = LifecycleScenario() with
        {
            ObservationFrames = 2,
            Inputs = [new("move-right", 1, 1, ["right"], "playerX")],
            Checkpoints = [new("moved", 2, Signals(("playerX", 1)))],
            Budgets = new(
                MinimumGameplayTickRatio: 1,
                MaximumConsecutiveMissedGameplayTicks: 0,
                MaximumInputToStateFrames: 1,
                MaximumRequestToResidentFrames: 1,
                MaximumRequestToVisibleFrames: 2),
        };
        var observations = new[]
        {
            Frame(0, gameplayTicks: 0, signals: Signals(("playerX", 0)), camera: Camera()),
            Frame(1, gameplayTicks: 1, signals: Signals(("playerX", 1)), camera: Camera(requested: 10)),
            Frame(2, gameplayTicks: 2, signals: Signals(("playerX", 1)), camera: Camera(requested: 10, resident: 10, committed: 10)),
            Frame(3, gameplayTicks: 3, signals: Signals(("playerX", 1)), camera: Camera(requested: 10, resident: 10, committed: 10, visible: 10)),
        };

        var report = FunctionalScenarioRunner.Run(scenario, Rom(), GameBoyAdapter(observations));

        Assert.True(report.Passed, report.ToHumanReadable());
        Assert.Equal(3, report.FrameWindow.TotalPhysicalFrames);
        Assert.Equal(2, report.FrameEvidence.Count);
        Assert.Equal(2, report.Summary.GameplayTicks);
        Assert.Equal(2, Assert.Single(report.TimingChecks, check => check.Metric == "request-to-visible").Observed);
    }

    [Fact]
    public void An_empty_camera_lifecycle_cannot_satisfy_camera_acceptance()
    {
        var scenario = LifecycleScenario();
        var observations = Enumerable.Range(0, 5)
            .Select(frame => Frame(frame, signals: Signals(("playerX", frame)), camera: Camera()))
            .ToArray();

        var report = FunctionalScenarioRunner.Run(scenario, Rom(), GameBoyAdapter(observations));

        Assert.False(report.Passed);
        Assert.Contains(report.IntegrityFailures, failure => failure.Code == "missing-camera-request");
    }

    [Fact]
    public void A_stale_completed_camera_sequence_cannot_pose_as_a_request_in_the_measurement_window()
    {
        var scenario = LifecycleScenario();
        var observations = Enumerable.Range(0, 5)
            .Select(frame => Frame(
                frame,
                signals: Signals(("playerX", Math.Min(frame, 3))),
                camera: Camera(requested: 10, resident: 10, committed: 10, visible: 10)))
            .ToArray();

        var report = FunctionalScenarioRunner.Run(scenario, Rom(), GameBoyAdapter(observations));

        Assert.False(report.Passed);
        Assert.Contains(report.IntegrityFailures, failure => failure.Code == "missing-camera-request");
    }

    [Fact]
    public void Capability_diagnostics_identify_target_and_execution_source()
    {
        var scenario = new FunctionalScenario(
            "capability-probe",
            "capability-probe",
            FunctionalTarget.Nes,
            WarmUpFrames: 0,
            ObservationFrames: 1,
            Inputs: [],
            Checkpoints: [],
            ExpectedFeatures: new(GameplayTicks: true),
            Audio: new(ServiceExpectedByDefault: false, AuthoredSilence: []),
            BudgetEvidence: Evidence(),
            Budgets: new(1, 0));
        var adapter = new NesFunctionalRomAdapter(
            new ScriptedMachineFactory(Frames([0, 1], [0, 0])),
            new FunctionalAdapterCapabilities(),
            FunctionalExecutionSource.NesMcp);

        var exception = Assert.Throws<InvalidOperationException>(
            () => FunctionalScenarioRunner.Run(scenario, Rom(), adapter));

        Assert.Contains("nes", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nes-mcp", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Game_boy_and_nes_adapters_execute_the_exact_rom_and_apply_the_same_input_timeline()
    {
        var rom = Rom();
        var scenario = AdapterScenario(FunctionalTarget.GameBoy);
        var gbFactory = new ScriptedMachineFactory(Frames([0, 0, 1], [0, 0, 0]));
        var gb = new GameBoyFunctionalRomAdapter(gbFactory, GameplayCapabilities());

        var gbReport = FunctionalScenarioRunner.Run(scenario, rom, gb);

        Assert.True(gbReport.Passed, gbReport.ToHumanReadable());
        Assert.Equal(rom.Bytes, gbFactory.LoadedRom);
        Assert.Equal(new[] { "right" }, gbFactory.Session!.InputsByFrame[1]);

        var nesFactory = new ScriptedMachineFactory(Frames([0, 0, 1], [0, 0, 0]));
        var nes = new NesFunctionalRomAdapter(nesFactory, GameplayCapabilities());
        var nesReport = FunctionalScenarioRunner.Run(AdapterScenario(FunctionalTarget.Nes), rom, nes);

        Assert.True(nesReport.Passed, nesReport.ToHumanReadable());
        Assert.Equal(rom.Bytes, nesFactory.LoadedRom);
        Assert.Equal(new[] { "right" }, nesFactory.Session!.InputsByFrame[1]);
    }

    [Fact]
    public void Adapter_counters_must_be_cumulative_and_monotonic()
    {
        var observations = Frames([1, 0, 1], [0, 0, 0]);

        var exception = Assert.Throws<InvalidOperationException>(() => FunctionalScenarioRunner.Run(
            AdapterScenario(FunctionalTarget.GameBoy),
            Rom(),
            GameBoyAdapter(observations)));

        Assert.Contains("cumulative", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Machine_and_human_reports_are_deterministic_and_include_observed_limit_and_headroom()
    {
        var report = FunctionalScenarioRunner.Run(
            TimingScenario(),
            Rom(),
            GameBoyAdapter(Frames([0, 0, 0, 1, 2, 3, 4], [0, 0, 0, 1, 2, 3, 4])));

        var firstJson = report.ToJson();
        var secondJson = report.ToJson();
        var firstText = report.ToHumanReadable();
        var secondText = report.ToHumanReadable();

        Assert.Equal(firstJson, secondJson);
        Assert.Equal(firstText, secondText);
        using var document = JsonDocument.Parse(firstJson);
        var check = document.RootElement.GetProperty("timingChecks")[0];
        Assert.True(check.TryGetProperty("observed", out _));
        Assert.True(check.TryGetProperty("limit", out _));
        Assert.True(check.TryGetProperty("headroom", out _));
        var window = document.RootElement.GetProperty("frameWindow");
        Assert.Equal(2, window.GetProperty("warmUpFrames").GetInt32());
        Assert.Equal(4, window.GetProperty("observationFrames").GetInt32());
        Assert.Equal(6, window.GetProperty("totalPhysicalFrames").GetInt32());
        Assert.Equal(4, document.RootElement.GetProperty("frameEvidence").GetArrayLength());
        Assert.Equal(2, report.FrameWindow.WarmUpFrames);
        Assert.Equal(4, report.FrameWindow.ObservationFrames);
        Assert.Equal(6, report.FrameWindow.TotalPhysicalFrames);
        Assert.Equal([3, 4, 5, 6], report.FrameEvidence.Select(item => item.Observed.Frame));
        Assert.Contains("observed=", firstText, StringComparison.Ordinal);
        Assert.Contains("limit=", firstText, StringComparison.Ordinal);
        Assert.Contains("headroom=", firstText, StringComparison.Ordinal);
        Assert.Contains("bankFailures=", firstText, StringComparison.Ordinal);
        Assert.Contains("unsafeVideoWrites=", firstText, StringComparison.Ordinal);
        Assert.Contains("unsafeOamWrites=", firstText, StringComparison.Ordinal);
        Assert.Contains("backgroundMismatches=", firstText, StringComparison.Ordinal);
        Assert.Contains("spriteMismatches=", firstText, StringComparison.Ordinal);
        Assert.Contains("frames warmUp=2 observation=4 totalPhysical=6", firstText, StringComparison.Ordinal);
        Assert.Contains("frame physical=3", firstText, StringComparison.Ordinal);
        Assert.Contains(report.RomSha256, firstText, StringComparison.Ordinal);
    }

    private static FunctionalScenario TimingScenario() => new(
        "timing-probe-gb",
        "timing-probe",
        FunctionalTarget.GameBoy,
        WarmUpFrames: 2,
        ObservationFrames: 4,
        Inputs: [],
        Checkpoints: [],
        ExpectedFeatures: new(GameplayTicks: true, AudioService: true),
        Audio: new(ServiceExpectedByDefault: true, AuthoredSilence: []),
        BudgetEvidence: Evidence(),
        Budgets: new(
            MinimumGameplayTickRatio: 0.90,
            MaximumConsecutiveMissedGameplayTicks: 1,
            MaximumUnplannedAudioGapFrames: 1,
            MaximumAudioDriftTicks: 1));

    private static FunctionalScenario IntegrityScenario(
        bool background = false,
        bool spriteOam = false,
        bool bank = false,
        bool safeVideoWrites = false) => new(
        "integrity-probe-gb",
        "integrity-probe",
        FunctionalTarget.GameBoy,
        WarmUpFrames: 0,
        ObservationFrames: 2,
        Inputs: [],
        Checkpoints: [],
        ExpectedFeatures: new(
            Background: background,
            SpriteOam: spriteOam,
            BankRestoration: bank,
            SafeVideoWrites: safeVideoWrites),
        Audio: new(ServiceExpectedByDefault: false, AuthoredSilence: []),
        BudgetEvidence: Evidence(),
        Budgets: new(0, 2));

    private static FunctionalScenario LifecycleScenario() => new(
        "lifecycle-probe-gb",
        "lifecycle-probe",
        FunctionalTarget.GameBoy,
        WarmUpFrames: 0,
        ObservationFrames: 4,
        Inputs: [new("move-right", 1, 3, ["right"], "playerX")],
        Checkpoints: [new("moved", 4, Signals(("playerX", 3)))],
        ExpectedFeatures: new(CameraLifecycle: true),
        Audio: new(ServiceExpectedByDefault: false, AuthoredSilence: []),
        BudgetEvidence: Evidence(),
        Budgets: new(
            MinimumGameplayTickRatio: 0,
            MaximumConsecutiveMissedGameplayTicks: 4,
            MaximumInputToStateFrames: 1,
            MaximumRequestToResidentFrames: 1,
            MaximumRequestToVisibleFrames: 3));

    private static FunctionalScenario AdapterScenario(FunctionalTarget target) => new(
        $"adapter-probe-{target.ToString().ToLowerInvariant()}",
        "adapter-probe",
        target,
        WarmUpFrames: 0,
        ObservationFrames: 2,
        Inputs: [new("move-right", 1, 1, ["right"])],
        Checkpoints: [],
        ExpectedFeatures: new(GameplayTicks: true),
        Audio: new(ServiceExpectedByDefault: false, AuthoredSilence: []),
        BudgetEvidence: Evidence(),
        Budgets: new(0.5, 1));

    private static FunctionalScenario AudioProgressScenario(IReadOnlyList<FunctionalFrameObservation> frames) => new(
        "audio-progress-probe-gb",
        "audio-progress-probe",
        FunctionalTarget.GameBoy,
        WarmUpFrames: 0,
        ObservationFrames: 4,
        Inputs: [],
        Checkpoints: [],
        ExpectedFeatures: new(AudioProgress: true),
        Audio: new(
            ServiceExpectedByDefault: false,
            AuthoredSilence: [],
            MinimumRegisterEvents: 3,
            MaximumRegisterEvents: 3,
            MaximumRegisterEventGapFrames: 1,
            MinimumSoundEffectStarts: 2,
            MaximumSoundEffectStarts: 2,
            MinimumSoundEffectCompletions: 2,
            MaximumSoundEffectCompletions: 2,
            MaximumSoundEffectRestarts: 0,
            MinimumDpcmStarts: 1,
            MaximumDpcmStarts: 1,
            MinimumDpcmCompletions: 1,
            MaximumDpcmCompletions: 1,
            MaximumDpcmRestarts: 0,
            MusicActiveAtEnd: true,
            SoundEffectActiveAtEnd: false,
            DpcmActiveAtEnd: false,
            OrderedRegisterEventSha256: OrderedAudioEventSha256(frames, 1, 4)),
        BudgetEvidence: Evidence(),
        Budgets: new(0, 4));

    private static FunctionalBudgetEvidence Evidence() => new(
        "95f166886713ff3b88bc1e17c03ef0ffe93d649a",
        "Synthetic contract probe: healthy observations advance once per physical frame; degraded observations advance every other frame.",
        "The limits separate the deliberately degraded fixture from the 1:1 contract without calibrating a canonical sample.");

    private static FunctionalRomArtifact Rom() => new("samples/contract-probe/bin/contract.gb", [0x52, 0x53, 0x33, 0x37]);

    private static GameBoyFunctionalRomAdapter GameBoyAdapter(IReadOnlyList<FunctionalFrameObservation> observations) =>
        new(new ScriptedMachineFactory(observations), AllCapabilities());

    private static FunctionalAdapterCapabilities GameplayCapabilities() => new(GameplayTicks: true, InputTimeline: true);

    private static FunctionalAdapterCapabilities AllCapabilities() => new(
        GameplayTicks: true,
        AudioService: true,
        AudioProgress: true,
        InputTimeline: true,
        CameraLifecycle: true,
        Background: true,
        SpriteOam: true,
        BankRestoration: true,
        VideoWriteTiming: true);

    private static IReadOnlyList<FunctionalFrameObservation> Frames(
        IReadOnlyList<long> gameplayTicks,
        IReadOnlyList<long> audioTicks)
    {
        Assert.Equal(gameplayTicks.Count, audioTicks.Count);
        return Enumerable.Range(0, gameplayTicks.Count)
            .Select(frame => Frame(frame, gameplayTicks[frame], audioTicks[frame]))
            .ToArray();
    }

    private static FunctionalFrameObservation Frame(
        int frame,
        long gameplayTicks = 0,
        long audioTicks = 0,
        int resetCount = 0,
        IReadOnlyDictionary<string, long>? signals = null,
        FunctionalCameraLifecycleObservation? camera = null,
        FunctionalBankObservation? bank = null,
        IReadOnlyList<FunctionalBackgroundObservation>? background = null,
        IReadOnlyList<FunctionalSpriteObservation>? sprites = null,
        IReadOnlyList<FunctionalVideoWriteObservation>? videoWrites = null,
        IReadOnlyList<FunctionalOamWriteObservation>? oamWrites = null,
        FunctionalSpawnLifecycleObservation? spawn = null,
        FunctionalAudioProgressObservation? audioProgress = null) =>
        new(
            frame,
            gameplayTicks,
            audioTicks,
            resetCount,
            signals,
            camera,
            bank,
            background,
            sprites,
            videoWrites,
            oamWrites,
            spawn,
            audioProgress);

    private static IReadOnlyList<FunctionalFrameObservation> HealthyAudioProgressFrames()
    {
        var music = new FunctionalAudioPlaybackObservation(Active: true, Starts: 1, Completions: 0, Restarts: 0);
        return
        [
            Frame(0, audioProgress: new(0, [], music,
                new(false, 0, 0, 0), new(false, 0, 0, 0))),
            Frame(1, audioProgress: new(1, [new("apu", 0x10, 0x01)], music,
                new(true, 1, 0, 0), new(true, 1, 0, 0))),
            Frame(2, audioProgress: new(1, [], music,
                new(false, 1, 1, 0), new(true, 1, 0, 0))),
            Frame(3, audioProgress: new(2, [new("apu", 0x11, 0x02)], music,
                new(true, 2, 1, 0), new(false, 1, 1, 0))),
            Frame(4, audioProgress: new(3, [new("apu", 0x12, 0x03)], music,
                new(false, 2, 2, 0), new(false, 1, 1, 0))),
        ];
    }

    private static string OrderedAudioEventSha256(
        IReadOnlyList<FunctionalFrameObservation> frames,
        int start,
        int end)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var frame = start; frame <= end; frame++)
        {
            foreach (var item in frames[frame].AudioProgress!.RegisterEvents)
            {
                AppendInt32LittleEndian(hash, frame - start + 1);
                hash.AppendData(Encoding.UTF8.GetBytes(item.Domain));
                hash.AppendData([0]);
                AppendInt32LittleEndian(hash, item.Address);
                AppendInt32LittleEndian(hash, item.Value);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void AppendInt32LittleEndian(IncrementalHash hash, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        hash.AppendData(bytes);
    }

    private static FunctionalBackgroundObservation Background(string location, int tile, int palette) =>
        new(location, tile, palette);

    private static FunctionalBackgroundExpectation ExpectedBackground(string location, int tile, int palette) =>
        new(location, tile, palette);

    private static FunctionalSpriteObservation Sprite(
        string id,
        bool visible,
        IReadOnlyList<int> oam) =>
        new(id, visible, oam);

    private static FunctionalSpriteExpectation ExpectedSprite(
        string id,
        bool visible,
        IReadOnlyList<int> oam) =>
        new(id, visible, oam);

    private static FunctionalCameraLifecycleObservation Camera(long? requested = null, long? resident = null, long? committed = null, long? visible = null) =>
        new(requested, resident, committed, visible);

    private static FunctionalWriteTimingObservation WriteTiming() =>
        new(Cycle: 1_234, Scanline: 42, Dot: 80, Phase: "visible", DisplayEnabled: true);

    private static IReadOnlyDictionary<string, long> Signals(params (string Key, long Value)[] values) =>
        values.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);

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

    private sealed class ScriptedMachineFactory(IReadOnlyList<FunctionalFrameObservation> observations) : IFunctionalRomMachineFactory
    {
        public byte[]? LoadedRom { get; private set; }

        public ScriptedMachine? Session { get; private set; }

        public IFunctionalRomMachine Create(ReadOnlyMemory<byte> exactRom)
        {
            LoadedRom = exactRom.ToArray();
            Session = new ScriptedMachine(observations);
            return Session;
        }
    }

    private sealed class ScriptedMachine(IReadOnlyList<FunctionalFrameObservation> observations) : IFunctionalRomMachine
    {
        public Dictionary<int, string[]> InputsByFrame { get; } = [];

        public FunctionalFrameObservation ObserveInitial() => observations[0];

        public FunctionalFrameObservation AdvanceFrame(int frame, IReadOnlySet<string> heldInputs)
        {
            InputsByFrame[frame] = heldInputs.Order(StringComparer.Ordinal).ToArray();
            return observations[frame];
        }

        public void Dispose()
        {
        }
    }

    private sealed class ScriptedOracle(Func<int, FunctionalFrameExpectation> expectedFrame) : IFunctionalFrameOracle
    {
        public FunctionalFrameExpectation ExpectedFrame(int frame) => expectedFrame(frame);
    }
}
