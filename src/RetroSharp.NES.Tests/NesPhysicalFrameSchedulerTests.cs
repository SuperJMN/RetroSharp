namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.NES;
using Xunit;
using static NesSdkOperationBoundaryTests;

public sealed class NesPhysicalFrameSchedulerTests
{
    [Fact]
    public void Packed_phase_exposes_the_physical_segments_bytes_and_worst_case_cost_it_owns()
    {
        var phaseType = typeof(NesPackedCameraPhase);
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        Assert.NotNull(phaseType.GetProperty("Segments", flags));
        Assert.NotNull(phaseType.GetProperty("PhysicalWriteBytes", flags));
        Assert.NotNull(phaseType.GetProperty("WorstCaseCpuCycles", flags));
    }

    [Fact]
    public void Standard_runner_column_schedule_mixes_tiles_and_attributes_within_the_physical_budget()
    {
        var schedule = NesPackedCameraPhaseSchedule.Create(retainedOamByteCount: 76);
        var phases = schedule.PlanColumn(payloadLength: 30, targetStart: 10);

        Assert.Equal(
            [
                (18, 0),
                (12, 0),
                (0, 8),
            ],
            phases.Select(Counts));
        Assert.All(phases, phase => Assert.InRange(
            phase.WorstCaseCpuCycles,
            1,
            NesPackedCameraPhaseSchedule.MaximumPhysicalFrameCycles));
        Assert.Equal((3, 3), (schedule.MaximumColumnFrames, schedule.MaximumRowFrames));
    }

    [Fact]
    public void Standard_worst_column_schedule_keeps_rows_30_and_60_as_segments_inside_a_phase()
    {
        var schedule = NesPackedCameraPhaseSchedule.Create(retainedOamByteCount: 76);
        var phases = schedule.PlanColumn(payloadLength: 32, targetStart: 29);

        Assert.Equal(
            [
                (18, 0),
                (14, 0),
                (0, 9),
            ],
            phases.Select(Counts));
        Assert.Equal(
            [
                new NesPackedCameraSegment(NesPackedCameraWriteKind.Tile, 29, 1),
                new NesPackedCameraSegment(NesPackedCameraWriteKind.Tile, 30, 17),
            ],
            phases[0].Segments);
    }

    [Fact]
    public void Large_worst_column_schedule_fits_four_bounded_phases()
    {
        var schedule = NesPackedCameraPhaseSchedule.Create(retainedOamByteCount: 152);

        Assert.Equal(
            [
                (11, 0),
                (11, 0),
                (10, 0),
                (0, 9),
            ],
            schedule.PlanColumn(payloadLength: 32, targetStart: 29).Select(Counts));
        Assert.Equal((4, 4), (schedule.MaximumColumnFrames, schedule.MaximumRowFrames));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(76)]
    [InlineData(152)]
    public void Packed_schedule_enumerates_every_payload_start_and_profile_deterministically_within_budget(
        int retainedOamByteCount)
    {
        var schedule = NesPackedCameraPhaseSchedule.Create(retainedOamByteCount);
        for (var payloadLength = 1; payloadLength <= 32; payloadLength++)
        {
            for (var targetStart = 0; targetStart < 60; targetStart++)
            {
                AssertPlan(
                    schedule.PlanColumn(payloadLength, targetStart),
                    schedule.PlanColumn(payloadLength, targetStart),
                    payloadLength,
                    targetStart,
                    column: true);
            }

            for (var targetStart = 0; targetStart < 64; targetStart++)
            {
                AssertPlan(
                    schedule.PlanRow(payloadLength, targetStart),
                    schedule.PlanRow(payloadLength, targetStart),
                    payloadLength,
                    targetStart,
                    column: false);
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(76)]
    [InlineData(152)]
    public void Packed_schedule_sustains_diagonal_crossings_every_eight_frames_with_two_slots(
        int retainedOamByteCount)
    {
        var schedule = NesPackedCameraPhaseSchedule.Create(retainedOamByteCount);
        var worstColumn = Enumerable.Range(1, 32)
            .SelectMany(payloadLength => Enumerable.Range(0, 60)
                .Select(targetStart => schedule.PlanColumn(payloadLength, targetStart)))
            .MaxBy(phases => phases.Count)!;
        var worstRow = Enumerable.Range(1, 32)
            .SelectMany(payloadLength => Enumerable.Range(0, 64)
                .Select(targetStart => schedule.PlanRow(payloadLength, targetStart)))
            .MaxBy(phases => phases.Count)!;

        var result = SimulateCrossings(worstColumn, worstRow, frames: 2_048);

        Assert.True(
            result.MaximumQueuedEdges <= 2,
            $"OAM {retainedOamByteCount} accumulated {result.MaximumQueuedEdges} edges " +
            $"for column={worstColumn.Count} and row={worstRow.Count} phases.");
        Assert.Equal(0, result.RemainingPhasesAfterDrain);
    }

    [Fact]
    public void Mapper_zero_gameplay_boundary_emits_the_existing_fresh_vblank_edge()
    {
        var builder = new PrgBuilder();
        var plan = NesFramePlan.Create(
            "nes-mapper-0-current",
            hasFrameBoundary: true,
            usesRetainedOam: false,
            retainedOamByteCount: 0,
            usesPackedCameraRuntime: false,
            useSequentialOamPublication: false,
            useFourScreenNametables: false);
        var scheduler = new NesPhysicalFrameScheduler(builder, plan);

        scheduler.EmitFrameBoundary(NesFrameBoundaryPurpose.Gameplay);

        Assert.Equal(
            [0x2C, 0x02, 0x20, 0x30, 0xFB, 0x2C, 0x02, 0x20, 0x10, 0xFB],
            builder.Build());
    }

    [Fact]
    public void Packed_gameplay_boundary_publishes_oam_only_after_a_fresh_hardware_vblank()
    {
        var builder = new PrgBuilder();
        var plan = NesFramePlan.Create(
            "nes-mmc3-tvrom-v1",
            hasFrameBoundary: true,
            usesRetainedOam: true,
            retainedOamByteCount: 4,
            usesPackedCameraRuntime: true,
            useSequentialOamPublication: false,
            useFourScreenNametables: true);
        var scheduler = new NesPhysicalFrameScheduler(builder, plan);

        scheduler.EmitFrameBoundary(NesFrameBoundaryPurpose.Gameplay);

        var bytes = builder.Build();
        var pendingSignal = IndexOfSequence(bytes, [0xAD, 0x8F, 0x03]);
        var hardwareVBlank = IndexOfSequence(bytes, [0x2C, 0x02, 0x20, 0x10, 0xFB]);
        var oamDma = IndexOfSequence(bytes, [0xA9, 0x02, 0x8D, 0x14, 0x40]);
        var shadowReset = IndexOfSequence(bytes, [0xA9, 0xFF, 0xA2, 0x00, 0x9D, 0x00, 0x02]);

        Assert.True(pendingSignal >= 0);
        Assert.True(hardwareVBlank > pendingSignal);
        Assert.True(oamDma > hardwareVBlank);
        Assert.True(shadowReset > oamDma);
    }

    [Fact]
    public void Sequential_publication_bytes_and_cpu_projection_come_from_the_same_scheduler()
    {
        var builder = new PrgBuilder();
        var plan = NesFramePlan.Create(
            "nes-mmc3-tvrom-v1",
            hasFrameBoundary: true,
            usesRetainedOam: true,
            retainedOamByteCount: 152,
            usesPackedCameraRuntime: true,
            useSequentialOamPublication: true,
            useFourScreenNametables: true);
        var scheduler = new NesPhysicalFrameScheduler(builder, plan);

        scheduler.EmitFrameBoundary(NesFrameBoundaryPurpose.Gameplay);

        Assert.True(IndexOfSequence(
            builder.Build(),
            [0xA9, 0x00, 0x8D, 0x03, 0x20, 0xAD, 0x00, 0x02, 0x8D, 0x04, 0x20, 0xAD, 0x01, 0x02]) >= 0);
        var report = scheduler.CreateCpuWorkReport([]);
        var publication = Assert.Single(report.Contributors);
        Assert.Equal(SdkCpuWorkContributorIds.SpritePublish, publication.Id);
        Assert.Equal((1_222L, 1_222L), (publication.TotalLower, publication.TotalUpper));
        Assert.DoesNotContain(report.Contributors, contributor =>
            contributor.Id == SdkCpuWorkContributorIds.SpritePublishTransfer);
        Assert.DoesNotContain(report.Unknowns, unknown =>
            unknown.Id == SdkCpuWorkContributorIds.SpritePublish);
        var videoSafe = Assert.Single(report.Windows, window => window.Id == SdkCpuWorkWindowIds.VideoSafe);
        Assert.Equal((1_222L, 1_222L), (videoSafe.KnownLower, videoSafe.KnownUpper));
    }

    [Fact]
    public void Sequential_profile_without_retained_oam_needs_no_publication_schedule()
    {
        var builder = new PrgBuilder();
        var plan = NesFramePlan.Create(
            "nes-mmc3-tvrom-v1",
            hasFrameBoundary: true,
            usesRetainedOam: false,
            retainedOamByteCount: 0,
            usesPackedCameraRuntime: true,
            useSequentialOamPublication: true,
            useFourScreenNametables: true);

        var scheduler = new NesPhysicalFrameScheduler(builder, plan);
        scheduler.EmitFrameBoundary(NesFrameBoundaryPurpose.Gameplay);

        Assert.DoesNotContain(
            scheduler.CreateCpuWorkReport([]).Contributors,
            contributor => contributor.Id == SdkCpuWorkContributorIds.SpritePublish);
        Assert.True(IndexOfSequence(builder.Build(), [0x8D, 0x03, 0x20]) < 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Camera_row_deadline_must_match_the_emitted_phase_schedule(bool usesPackedCameraRuntime)
    {
        var plan = NesFramePlan.Create(
            "nes-mmc3-tvrom-v1",
            hasFrameBoundary: true,
            usesRetainedOam: false,
            retainedOamByteCount: 0,
            usesPackedCameraRuntime,
            useSequentialOamPublication: false,
            useFourScreenNametables: true);
        var staging = Assert.Single(plan.StagedWork);
        plan = plan with
        {
            StagedWork = [staging with { MaximumPhysicalFrames = staging.MaximumPhysicalFrames + 1 }],
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new NesPhysicalFrameScheduler(new PrgBuilder(), plan));

        Assert.Contains("deadline", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static (int MaximumQueuedEdges, int RemainingPhasesAfterDrain) SimulateCrossings(
        IReadOnlyList<NesPackedCameraPhase> column,
        IReadOnlyList<NesPackedCameraPhase> row,
        int frames)
    {
        var queuedPhases = new Queue<int>();
        var maximumQueuedEdges = 0;
        for (var frame = 0; frame < frames; frame++)
        {
            if (frame % 8 == 0)
            {
                queuedPhases.Enqueue(column.Count);
                queuedPhases.Enqueue(row.Count);
                maximumQueuedEdges = Math.Max(maximumQueuedEdges, queuedPhases.Count);
            }

            ServiceOnePhase(queuedPhases);
        }

        var remainingPhases = queuedPhases.Sum();
        while (queuedPhases.Count > 0)
        {
            ServiceOnePhase(queuedPhases);
        }

        return (maximumQueuedEdges, remainingPhases);
    }

    private static void ServiceOnePhase(Queue<int> queuedPhases)
    {
        if (!queuedPhases.TryDequeue(out var remaining))
        {
            return;
        }

        if (remaining > 1)
        {
            queuedPhases.Enqueue(remaining - 1);
        }
    }

    private static (int TileWrites, int AttributeWrites) Counts(NesPackedCameraPhase phase) =>
        (phase.TileWrites, phase.AttributeWrites);

    private static void AssertPlan(
        IReadOnlyList<NesPackedCameraPhase> first,
        IReadOnlyList<NesPackedCameraPhase> second,
        int payloadLength,
        int targetStart,
        bool column)
    {
        Assert.Equal(
            first.Select(DescribePhase),
            second.Select(DescribePhase));
        Assert.Equal(payloadLength, first.Sum(phase => phase.TileWrites));
        Assert.Equal((targetStart % 4 + payloadLength + 3) / 4, first.Sum(phase => phase.AttributeWrites));
        Assert.All(first, phase =>
        {
            Assert.InRange(
                phase.WorstCaseCpuCycles,
                1,
                NesPackedCameraPhaseSchedule.MaximumPhysicalFrameCycles);
            Assert.True(phase.PhysicalWriteBytes > 0);
            Assert.NotEmpty(phase.Segments);
            foreach (var segment in phase.Segments)
            {
                var boundary = column
                    ? segment.Kind == NesPackedCameraWriteKind.Tile
                        ? segment.TargetStart < 30 ? 30 : 60
                        : segment.TargetStart + 1
                    : segment.Kind == NesPackedCameraWriteKind.Tile
                        ? segment.TargetStart < 32 ? 32 : 64
                        : segment.TargetStart < 8 ? 8 : 16;
                Assert.True(
                    segment.TargetStart + segment.WriteCount <= boundary,
                    $"{(column ? "column" : "row")} segment {segment} crossed physical boundary {boundary}.");
            }
        });
    }

    private static string DescribePhase(NesPackedCameraPhase phase) =>
        $"{phase.TileWrites}/{phase.AttributeWrites}:{phase.PhysicalWriteBytes}:{phase.WorstCaseCpuCycles}:" +
        string.Join(',', phase.Segments.Select(segment =>
            $"{segment.Kind}-{segment.TargetStart}-{segment.WriteCount}"));
}
