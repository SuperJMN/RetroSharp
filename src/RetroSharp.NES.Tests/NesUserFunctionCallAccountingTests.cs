namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using RetroSharp.Sdk;
using Xunit;

public sealed class NesUserFunctionCallAccountingTests
{
    private const string SharedHelperSource = """
                                              void Main() {
                                                  Video.Init();
                                                  u8 seed = 3;
                                                  while (true) {
                                                      Video.WaitVBlank();
                                                      Frame(seed);
                                                      Frame(seed);
                                                      seed++;
                                                  }
                                              }

                                              void Frame(u8 value) {
                                                  Bump(value);
                                                  return;
                                              }

                                              void Bump(u8 amount) {
                                                  u8 scratch = amount;
                                                  scratch += amount;
                                                  scratch += scratch;
                                                  Tilemap.Set(2, 2, scratch);
                                                  return;
                                              }
                                              """;

    private const string StartupOnlySource = """
                                             void Main() {
                                                 Video.Init();
                                                 u8 seed = 1;
                                                 Bump(seed);
                                                 Bump(seed);
                                                 return;
                                             }

                                             void Bump(u8 amount) {
                                                 u8 scratch = amount;
                                                 scratch += amount;
                                                 scratch += scratch;
                                                 return;
                                             }
                                             """;

    [Fact]
    public void Collected_projection_reports_one_body_while_runtime_work_counts_every_call()
    {
        var report = CompileSource(SharedHelperSource).Report.UserFunctionCalls;

        Assert.True(report.HasFrameLoop);
        Assert.Single(report.Collected, body => body.Function == "Bump");
        Assert.Single(report.Collected, body => body.Function == "Frame");
        Assert.Equal(2, report.ForRuntimeWork.Count(call => call.Function == "Frame"));
        Assert.Equal(2, report.ForRuntimeWork.Count(call => call.Function == "Bump"));
    }

    [Fact]
    public void A_shared_body_does_not_hide_the_calls_it_executes()
    {
        var report = CompileSource(SharedHelperSource).Report.UserFunctionCalls;

        // Bump is written once inside Frame, so the collected projection has a single body for it.
        var body = Assert.Single(report.Collected, body => body.Function == "Frame");
        Assert.Equal(["Bump"], body.Calls);

        // The runtime-work projection still charges one Bump per executed Frame.
        var bump = Assert.Single(report.Functions, function => function.Name == "Bump");
        Assert.Equal(NesUserFunctionPhase.Hot, bump.Phase);
        Assert.Equal(2, bump.Calls);
        Assert.Equal(2, bump.CallsPerFrame);
        Assert.All(
            report.ForRuntimeWork.Where(call => call.Function == "Bump"),
            call => Assert.Equal("Frame", call.Caller));
    }

    [Fact]
    public void Duplicated_bytes_measure_what_inline_expansion_spends_beyond_one_body()
    {
        var report = CompileSource(SharedHelperSource).Report.UserFunctionCalls;
        var bump = Assert.Single(report.Functions, function => function.Name == "Bump");

        Assert.Equal(2, bump.EmittedCopies);
        Assert.True(bump.EmittedBodyBytes > 0);
        Assert.Equal(bump.TotalEmittedBytes - bump.EmittedBodyBytes, bump.DuplicatedBytes);
        Assert.Equal(bump.EmittedBodyBytes, bump.DuplicatedBytes);
        Assert.Equal(1, bump.Arguments.RuntimeBytes);
        Assert.Equal(0, bump.Arguments.ReferenceArguments);
    }

    [Fact]
    public void A_program_without_a_frame_loop_reports_one_shot_work_instead_of_a_hot_phase()
    {
        var report = CompileSource(StartupOnlySource).Report.UserFunctionCalls;
        var bump = Assert.Single(report.Functions, function => function.Name == "Bump");

        Assert.False(report.HasFrameLoop);
        Assert.Equal(NesUserFunctionPhase.OneShot, bump.Phase);
        Assert.Equal(2, bump.Calls);
        Assert.Equal(0, bump.CallsPerFrame);
        Assert.DoesNotContain(report.Functions, function => function.Phase is NesUserFunctionPhase.Hot);
    }

    [Fact]
    public void A_recursive_call_graph_is_reported_explicitly_instead_of_looping_forever()
    {
        // Emission rejects recursion before accounting sees it, so the guard is exercised directly
        // against a trace whose bodies call each other.
        NesUserFunctionExpansion Expansion(string function, int parent) => new(
            function,
            parent,
            NesRomBuilder.MainInitPlacementUnitName,
            NesPrgPlacementPhase.OneShot,
            LoopDepth: 0,
            EmittedBytes: 8,
            NesUserFunctionArguments.None);

        var exception = Assert.Throws<InvalidOperationException>(() => NesUserFunctionCallAccounting.Create(
            [Expansion("A", -1), Expansion("B", 0), Expansion("A", 1)],
            hasFrameLoop: false));

        Assert.Contains("recursive call cycle", exception.Message);
        Assert.Contains("A -> B -> A", exception.Message);
    }

    [Fact]
    public void Falling_blocks_reports_its_hot_helpers_per_frame_and_their_inline_duplication()
    {
        var build = CompileSample(
            ["samples/falling-blocks/src/rules.rs", "samples/falling-blocks/src/main.rs"],
            "samples/falling-blocks",
            "FallingBlocks",
            "samples/falling-blocks/src");
        var report = build.Report.UserFunctionCalls;

        Assert.True(report.HasFrameLoop);
        var present = Assert.Single(report.Functions, function => function.Name == "Present");
        var preview = Assert.Single(report.Functions, function => function.Name == "PresentPreview");
        var scale = Assert.Single(report.Functions, function => function.Name == "ScaleToPixels");

        Assert.Equal(4, present.CallsPerFrame);
        Assert.Equal(4, preview.CallsPerFrame);
        Assert.Equal(8, scale.CallsPerFrame);
        Assert.All(
            new[] { present, preview, scale },
            function =>
            {
                Assert.Equal(NesUserFunctionPhase.Hot, function.Phase);
                Assert.False(function.RepeatsPerFrame);
                Assert.Equal(function.CallsPerFrame, function.EmittedCopies);
                Assert.Equal(function.TotalEmittedBytes - function.EmittedBodyBytes, function.DuplicatedBytes);
            });

        // ScaleToPixels is reached only through Present and PresentPreview, so per-frame work must
        // stay the sum of its callers even though its body is written once.
        Assert.Equal(
            present.CallsPerFrame + preview.CallsPerFrame,
            report.ForRuntimeWork.Count(call => call.Function == "ScaleToPixels"));
        Assert.All(
            report.ForRuntimeWork.Where(call => call.Function == "ScaleToPixels"),
            call => Assert.Contains(call.Caller, new[] { "Present", "PresentPreview" }));

        // Diagnostic bands, not byte gates: three duplicate copies each of a helper of this size.
        Assert.InRange(present.DuplicatedBytes, 800, 1_500);
        Assert.InRange(preview.DuplicatedBytes, 800, 1_500);
        Assert.InRange(scale.DuplicatedBytes, 250, 600);

        // Helpers that sit inside inner loops must say so instead of claiming an exact per-frame count.
        var locate = Assert.Single(report.Functions, function => function.Name == "Locate");
        Assert.True(locate.RepeatsPerFrame);
    }

    [Fact]
    public void Executable_banking_reports_its_largest_duplication_as_startup_only()
    {
        var build = CompileSample(
            ["samples/executable-banking/executable-banking.rs"],
            "samples/executable-banking",
            rootNamespace: null,
            sourceRoot: null);
        var report = build.Report.UserFunctionCalls;
        var step512 = Assert.Single(report.Functions, function => function.Name == "Step512");

        Assert.False(report.HasFrameLoop);
        Assert.Equal(NesUserFunctionPhase.OneShot, step512.Phase);
        Assert.Equal(0, step512.CallsPerFrame);
        Assert.DoesNotContain(report.Functions, function => function.Phase is NesUserFunctionPhase.Hot);
        Assert.All(
            report.ForRuntimeWork,
            call => Assert.Equal(NesRomBuilder.MainInitPlacementUnitName, call.PlacementUnit));

        Assert.Equal(8, step512.EmittedCopies);
        Assert.Equal(step512.TotalEmittedBytes - step512.EmittedBodyBytes, step512.DuplicatedBytes);
        Assert.InRange(step512.DuplicatedBytes, 20_000, 30_000);
        Assert.True(step512.DuplicatedBytes > build.Report.ProgramR6Bytes / 2);

        // Step512 nests Step64 nests Step8, so inclusive bytes overlap; the report's own duplication
        // total uses the non-overlapping share instead.
        Assert.True(report.DuplicatedBytes < report.Functions.Sum(function => function.DuplicatedBytes));
        Assert.InRange(report.DuplicatedBytes, 25_000, build.Report.ProgramR6Bytes);
    }

    private static NesRomBuildResult CompileSource(string source) =>
        RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            source,
            null,
            SdkLibraryImportMode.ExplicitOnly,
            null,
            [SdkImportResolver.Portable2D],
            null);

    private static NesRomBuildResult CompileSample(
        string[] sourceRelativePaths,
        string baseDirectoryRelativePath,
        string? rootNamespace,
        string? sourceRoot)
    {
        var files = sourceRelativePaths
            .Select(RepositoryFile)
            .Select(path => new PhysicalNamespaceSourceFile(path, File.ReadAllText(path)))
            .ToArray();
        var source = rootNamespace is null
            ? string.Concat(files.Select(file => file.Source + Environment.NewLine))
            : PhysicalNamespaceSourceComposer.Compose(files, rootNamespace, RepositoryDirectory(sourceRoot!));

        return RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            source,
            RepositoryDirectory(baseDirectoryRelativePath),
            SdkLibraryImportMode.ExplicitOnly,
            null,
            [SdkImportResolver.Portable2D],
            null);
    }

    private static string RepositoryFile(string relativePath)
    {
        var path = RepositoryDirectory(relativePath);
        return File.Exists(path)
            ? path
            : throw new InvalidOperationException($"Could not find repository file '{relativePath}'.");
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
}
