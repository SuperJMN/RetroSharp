namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using RetroSharp.Sdk;
using Xunit;

/// <summary>
/// Acceptance evidence for #524: cold and one-shot user functions are emitted once and reached by
/// <c>JSR</c>, while the frame loop keeps the inline lowering #514 still owns.
/// </summary>
public sealed class NesUserFunctionOutliningTests
{
    private const string ColdHelperSource = """
                                            class Counter {
                                                u8 value;

                                                inline void Step8() {
                                                    value += 1;
                                                    value += 1;
                                                    value += 1;
                                                    value += 1;
                                                    value += 1;
                                                    value += 1;
                                                    value += 1;
                                                    value += 1;
                                                }

                                                inline void Step64() {
                                                    Step8();
                                                    Step8();
                                                    Step8();
                                                    Step8();
                                                    Step8();
                                                    Step8();
                                                    Step8();
                                                    Step8();
                                                }
                                            }

                                            void Main() {
                                                Counter counter;
                                                counter.value = 0;
                                                counter.Step64();
                                                counter.Step64();
                                                counter.Step64();
                                            }
                                            """;

    [Fact]
    public void Cold_helpers_are_emitted_once_while_every_executed_call_is_still_counted()
    {
        var build = CompileSource(ColdHelperSource);
        var report = build.Report.UserFunctionCalls;
        var step8 = Assert.Single(report.Functions, function => function.Name == "Step8");
        var step64 = Assert.Single(report.Functions, function => function.Name == "Step64");

        Assert.False(report.HasFrameLoop);
        Assert.Equal(NesUserFunctionPhase.OneShot, step8.Phase);
        Assert.Equal(NesUserFunctionPhase.OneShot, step64.Phase);

        // #516's two projections: one emitted body each, every executed call still visible.
        Assert.Equal(1, step8.EmittedCopies);
        Assert.Equal(1, step64.EmittedCopies);
        Assert.Equal(3, step64.Calls);
        Assert.Equal(24, step8.Calls);
        Assert.Equal(0, step8.DuplicatedBytes);
        Assert.Equal(0, step64.DuplicatedBytes);
        Assert.Equal(0, report.DuplicatedBytes);

        // The nested call still belongs to the body that contains it, not to the program root.
        Assert.All(
            report.ForRuntimeWork.Where(call => call.Function == "Step8"),
            call => Assert.Equal("Step64", call.Caller));
        Assert.All(
            report.ForRuntimeWork,
            call => Assert.Equal(NesRomBuilder.MainInitPlacementUnitName, call.PlacementUnit));
    }

    [Fact]
    public void An_outlined_body_is_reported_with_its_call_sites_and_the_overridden_inline_hint()
    {
        var build = CompileSource(ColdHelperSource);
        var step64 = Assert.Single(build.Report.OutlinedUserFunctions, outlined => outlined.Function == "Step64");
        var step8 = Assert.Single(build.Report.OutlinedUserFunctions, outlined => outlined.Function == "Step8");

        Assert.Equal("user_fn_Step64", step64.Label);
        Assert.Equal(3, step64.CallSites);
        Assert.Equal(8, step8.CallSites);
        Assert.All(
            build.Report.OutlinedUserFunctions,
            outlined =>
            {
                Assert.Equal(NesUserFunctionPhase.OneShot, outlined.Phase);
                Assert.True(outlined.OverridesInlineHint, "Both helpers are declared inline.");
                Assert.InRange(outlined.CpuAddress, (ushort)0x8000, (ushort)0xFFFF);
            });
    }

    [Fact]
    public void Outlining_keeps_the_result_the_inline_lowering_produced()
    {
        var build = CompileSource(ColdHelperSource);
        var address = Assert.Single(
            build.Report.UserVariables,
            variable => variable.Name == "counter.value").Address;
        var cpu = new NesTestCpu(build.Rom);

        cpu.RunFrames(4);

        Assert.Equal(192, cpu.Ram(address));
        Assert.Equal(1, cpu.ResetCount);
    }

    [Fact]
    public void A_helper_reached_from_the_frame_loop_keeps_the_inline_lowering()
    {
        const string source = """
                              class Counter {
                                  u8 value;

                                  inline void Bump() {
                                      value += 1;
                                      value += 3;
                                      value += 5;
                                  }
                              }

                              void Main() {
                                  Video.Init();
                                  Counter counter;
                                  counter.value = 0;
                                  while (true) {
                                      Video.WaitVBlank();
                                      counter.Bump();
                                      counter.Bump();
                                  }
                              }
                              """;
        var build = CompileSource(source);
        var bump = Assert.Single(build.Report.UserFunctionCalls.Functions, function => function.Name == "Bump");

        Assert.Equal(NesUserFunctionPhase.Hot, bump.Phase);
        Assert.Equal(2, bump.CallsPerFrame);
        Assert.Equal(2, bump.EmittedCopies);
        Assert.Empty(build.Report.OutlinedUserFunctions);
    }

    [Theory]
    [InlineData("samples/falling-blocks/src/rules.rs,samples/falling-blocks/src/main.rs", "samples/falling-blocks", "FallingBlocks", "samples/falling-blocks/src")]
    public void No_hot_helper_of_a_tracked_frame_loop_sample_is_outlined(
        string sources,
        string baseDirectory,
        string? rootNamespace,
        string? sourceRoot)
    {
        var build = NesUserFunctionCallAccountingTests.CompileSampleForTests(
            sources.Split(','),
            baseDirectory,
            rootNamespace,
            sourceRoot);

        Assert.DoesNotContain(
            build.Report.OutlinedUserFunctions,
            outlined => outlined.Phase is NesUserFunctionPhase.Hot);
        Assert.All(
            build.Report.UserFunctionCalls.Functions.Where(function => function.Phase is NesUserFunctionPhase.Hot),
            function => Assert.DoesNotContain(
                build.Report.OutlinedUserFunctions,
                outlined => outlined.Function == function.Name));
    }

    [Fact]
    public void A_cold_helper_called_once_stays_inline_because_a_call_would_only_add_bytes()
    {
        const string source = """
                              void Main() {
                                  u8 seed = 1;
                                  Bump(seed);
                                  return;
                              }

                              void Bump(u8 amount) {
                                  u8 scratch = amount;
                                  scratch += 1;
                                  scratch += 2;
                                  return;
                              }
                              """;
        var build = CompileSource(source);

        Assert.Empty(build.Report.OutlinedUserFunctions);
        Assert.Equal(1, Assert.Single(build.Report.UserFunctionCalls.Functions, f => f.Name == "Bump").EmittedCopies);
    }

    [Fact]
    public void A_cold_helper_that_consumes_sdk_operations_stays_inline_so_the_stream_stays_in_step()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Paint(1);
                                  Paint(2);
                                  return;
                              }

                              void Paint(u8 tile) {
                                  Tilemap.Set(3, 4, tile);
                                  return;
                              }
                              """;
        var build = CompileSource(source);
        var paint = Assert.Single(build.Report.UserFunctionCalls.Functions, function => function.Name == "Paint");

        // The SDK operation stream is replayed positionally, so a body emitted once but executed
        // twice would consume the wrong operation. Such helpers must stay substituted.
        Assert.Empty(build.Report.OutlinedUserFunctions);
        Assert.Equal(2, paint.EmittedCopies);
        Assert.Equal(2, paint.Calls);
    }

    [Fact]
    public void Call_sites_with_different_compile_time_operands_get_their_own_body()
    {
        const string source = """
                              class Counter {
                                  u8 value;
                              }

                              void Main() {
                                  Counter first;
                                  Counter second;
                                  first.value = 0;
                                  second.value = 0;
                                  Fold(first);
                                  Fold(first);
                                  Fold(second);
                                  Fold(second);
                                  return;
                              }

                              void Fold(Counter counter) {
                                  counter.value += 1;
                                  counter.value += 3;
                                  counter.value += 5;
                                  return;
                              }
                              """;
        var build = CompileSource(source);
        var bodies = build.Report.OutlinedUserFunctions
            .Where(outlined => outlined.Function == "Fold")
            .ToArray();

        // Monomorphising on the compile-time receiver keeps the zero-argument calling convention.
        Assert.Equal(2, bodies.Length);
        Assert.Equal(new[] { "user_fn_Fold", "user_fn_Fold__1" }, bodies.Select(body => body.Label));
        Assert.All(bodies, body => Assert.Equal(2, body.CallSites));
        Assert.Equal(4, Assert.Single(build.Report.UserFunctionCalls.Functions, f => f.Name == "Fold").Calls);

        var first = Assert.Single(build.Report.UserVariables, variable => variable.Name == "first.value").Address;
        var second = Assert.Single(build.Report.UserVariables, variable => variable.Name == "second.value").Address;
        var cpu = new NesTestCpu(build.Rom);
        cpu.RunFrames(4);

        Assert.Equal(18, cpu.Ram(first));
        Assert.Equal(18, cpu.Ram(second));
    }

    private static NesRomBuildResult CompileSource(string source) =>
        RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            source,
            null,
            SdkLibraryImportMode.ExplicitOnly,
            null,
            [SdkImportResolver.Portable2D],
            null);
}
