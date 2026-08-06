namespace RetroSharp.Core.Tests;

using RetroSharp.Core.Sdk;
using Xunit;

// Direct coverage for the shared SdkStreamReader<TItem, TOperation> stack
// machine. Before this reader moved to Core.Sdk, its traversal logic was only
// exercised indirectly through the Game Boy and NES runtime compilers; these
// tests pin the sequential-consumption, subroutine-frame, and error-path
// behavior that every target-owned wrapper (Sdk2DStreamReader,
// SdkAudioStreamReader, NesSdkStreamReader) now relies on.
public sealed class SdkStreamReaderTests
{
    private static readonly SdkStreamReaderDiagnostics NamedStreamDiagnostics = new(
        CallPrefix: "Test SDK",
        OperationNoun: "SDK operation",
        StreamNoun: "SDK stream");

    private static readonly SdkStreamReaderDiagnostics CursorDiagnostics = new(
        CallPrefix: "Test SDK",
        OperationNoun: "SDK operation",
        StreamNoun: "SDK stream",
        DescribeLocationByCursor: true,
        RequireDeclaredSubroutine: true,
        UseOperationWordingAtTopLevel: true);

    [Fact]
    public void Sequential_operations_are_consumed_in_order()
    {
        var reader = CreateReader(
            [
                new Sdk2DStreamItem.Op(new Sdk2DOperation.PollInput()),
                new Sdk2DStreamItem.Op(new Sdk2DOperation.WaitFrame()),
            ]);

        var first = reader.ConsumeOperation("Input.Poll");
        var second = reader.ConsumeOperation("Video.WaitVBlank");

        Assert.IsType<Sdk2DOperation.PollInput>(first);
        Assert.IsType<Sdk2DOperation.WaitFrame>(second);
        reader.EnsureAllConsumed("Test runtime");
    }

    [Fact]
    public void Typed_consume_returns_the_concrete_operation_and_advances_the_cursor()
    {
        var reader = CreateReader(
            [
                new Sdk2DStreamItem.Op(new Sdk2DOperation.PollInput()),
                new Sdk2DStreamItem.Op(new Sdk2DOperation.WaitFrame()),
            ]);

        var first = reader.ConsumeOperation<Sdk2DOperation.PollInput>("Input.Poll");
        var second = reader.ConsumeOperation<Sdk2DOperation.WaitFrame>("Video.WaitVBlank");

        Assert.NotNull(first);
        Assert.NotNull(second);
        reader.EnsureAllConsumed("Test runtime");
    }

    [Fact]
    public void Subroutine_call_marker_enters_and_leaves_its_own_stream_frame()
    {
        var reader = CreateReader(
            main:
            [
                new Sdk2DStreamItem.Op(new Sdk2DOperation.PollInput()),
                new Sdk2DStreamItem.CallSubroutine("tick"),
            ],
            subroutines: new Dictionary<string, IReadOnlyList<Sdk2DStreamItem>>(StringComparer.Ordinal)
            {
                ["tick"] = [new Sdk2DStreamItem.Op(new Sdk2DOperation.WaitFrame())],
            });

        reader.ConsumeOperation("Input.Poll");
        reader.ConsumeSubroutineCall("tick");
        reader.EnterSubroutine("tick");
        reader.ConsumeOperation<Sdk2DOperation.WaitFrame>("Video.WaitVBlank");
        reader.LeaveSubroutine("tick");

        reader.EnsureAllConsumed("Test runtime");
    }

    [Fact]
    public void Subroutine_stream_is_replayed_once_per_call_site()
    {
        var reader = CreateReader(
            main:
            [
                new Sdk2DStreamItem.CallSubroutine("tick"),
                new Sdk2DStreamItem.CallSubroutine("tick"),
            ],
            subroutines: new Dictionary<string, IReadOnlyList<Sdk2DStreamItem>>(StringComparer.Ordinal)
            {
                ["tick"] = [new Sdk2DStreamItem.Op(new Sdk2DOperation.WaitFrame())],
            });

        for (var call = 0; call < 2; call++)
        {
            reader.ConsumeSubroutineCall("tick");
            reader.EnterSubroutine("tick");
            reader.ConsumeOperation<Sdk2DOperation.WaitFrame>("Video.WaitVBlank");
            reader.LeaveSubroutine("tick");
        }

        reader.EnsureAllConsumed("Test runtime");
    }

    [Fact]
    public void Consume_operation_on_an_exhausted_stream_reports_the_call_name_and_location()
    {
        var reader = CreateReader([]);

        var exception = Assert.Throws<InvalidOperationException>(() => reader.ConsumeOperation("Input.Poll"));

        Assert.Equal(
            "Test SDK call 'Input.Poll' has no collected SDK operation in stream 'main'.",
            exception.Message);
    }

    [Fact]
    public void Consume_operation_on_an_exhausted_stream_can_describe_location_by_cursor()
    {
        var reader = CreateReader([], CursorDiagnostics);

        var exception = Assert.Throws<InvalidOperationException>(() => reader.ConsumeOperation("Input.Poll"));

        Assert.Equal(
            "Test SDK call 'Input.Poll' has no collected SDK operation at stream item 0.",
            exception.Message);
    }

    [Fact]
    public void Consume_operation_rejects_an_unexpected_stream_item_type()
    {
        var reader = CreateReader([new Sdk2DStreamItem.CallSubroutine("tick")]);

        var exception = Assert.Throws<InvalidOperationException>(() => reader.ConsumeOperation("Input.Poll"));

        Assert.Equal(
            "Test SDK call 'Input.Poll' expected a collected SDK operation in stream 'main', got CallSubroutine.",
            exception.Message);
    }

    [Fact]
    public void Typed_consume_rejects_a_mismatched_collected_operation()
    {
        var reader = CreateReader([new Sdk2DStreamItem.Op(new Sdk2DOperation.PollInput())], CursorDiagnostics);

        var exception = Assert.Throws<InvalidOperationException>(
            () => reader.ConsumeOperation<Sdk2DOperation.WaitFrame>("Video.WaitVBlank"));

        Assert.Equal(
            "Test SDK call 'Video.WaitVBlank' expected WaitFrame, got PollInput at stream item 0.",
            exception.Message);
    }

    [Fact]
    public void Consume_subroutine_call_rejects_a_name_that_does_not_match_the_next_marker()
    {
        var reader = CreateReader([new Sdk2DStreamItem.CallSubroutine("tick")]);

        var exception = Assert.Throws<InvalidOperationException>(() => reader.ConsumeSubroutineCall("other"));

        Assert.Equal(
            "Test SDK stream expected subroutine call 'tick', got 'other'.",
            exception.Message);
    }

    [Fact]
    public void Ensure_all_consumed_rejects_leftover_items_in_the_main_stream()
    {
        var reader = CreateReader(
            [
                new Sdk2DStreamItem.Op(new Sdk2DOperation.PollInput()),
                new Sdk2DStreamItem.Op(new Sdk2DOperation.WaitFrame()),
            ]);

        reader.ConsumeOperation("Input.Poll");

        var exception = Assert.Throws<InvalidOperationException>(() => reader.EnsureAllConsumed("Test runtime"));

        Assert.Equal(
            "Test runtime consumed 1 of 2 SDK stream item(s) in 'main'; next item is WaitFrame.",
            exception.Message);
    }

    [Fact]
    public void Ensure_all_consumed_can_use_operation_wording_at_the_top_level()
    {
        var reader = CreateReader([new Sdk2DStreamItem.Op(new Sdk2DOperation.PollInput())], CursorDiagnostics);

        var exception = Assert.Throws<InvalidOperationException>(() => reader.EnsureAllConsumed("Test runtime"));

        Assert.Equal(
            "Test runtime consumed 0 of 1 SDK operation(s); next operation is PollInput.",
            exception.Message);
    }

    [Fact]
    public void Ensure_all_consumed_rejects_an_unfinished_subroutine_frame()
    {
        var reader = CreateReader(
            main: [new Sdk2DStreamItem.CallSubroutine("tick")],
            subroutines: new Dictionary<string, IReadOnlyList<Sdk2DStreamItem>>(StringComparer.Ordinal)
            {
                ["tick"] = [new Sdk2DStreamItem.Op(new Sdk2DOperation.WaitFrame())],
            });

        reader.ConsumeSubroutineCall("tick");
        reader.EnterSubroutine("tick");

        var exception = Assert.Throws<InvalidOperationException>(() => reader.EnsureAllConsumed("Test runtime"));

        Assert.Equal(
            "Test runtime finished while SDK stream 'tick' was still active.",
            exception.Message);
    }

    [Fact]
    public void Leave_subroutine_rejects_a_body_that_was_not_fully_consumed()
    {
        var reader = CreateReader(
            main: [new Sdk2DStreamItem.CallSubroutine("tick")],
            subroutines: new Dictionary<string, IReadOnlyList<Sdk2DStreamItem>>(StringComparer.Ordinal)
            {
                ["tick"] =
                [
                    new Sdk2DStreamItem.Op(new Sdk2DOperation.WaitFrame()),
                    new Sdk2DStreamItem.Op(new Sdk2DOperation.PollInput()),
                ],
            });

        reader.ConsumeSubroutineCall("tick");
        reader.EnterSubroutine("tick");
        reader.ConsumeOperation<Sdk2DOperation.WaitFrame>("Video.WaitVBlank");

        var exception = Assert.Throws<InvalidOperationException>(() => reader.LeaveSubroutine("tick"));

        Assert.Equal(
            "Test SDK subroutine 'tick' consumed 1 of 2 SDK stream item(s) in 'tick'; next item is PollInput.",
            exception.Message);
    }

    [Fact]
    public void Entering_an_undeclared_subroutine_falls_back_to_an_empty_stream_when_not_required()
    {
        var reader = CreateReader(
            main: [new Sdk2DStreamItem.CallSubroutine("tick")],
            subroutines: new Dictionary<string, IReadOnlyList<Sdk2DStreamItem>>(StringComparer.Ordinal));

        reader.ConsumeSubroutineCall("tick");
        reader.EnterSubroutine("tick");
        reader.LeaveSubroutine("tick");

        reader.EnsureAllConsumed("Test runtime");
    }

    [Fact]
    public void Entering_an_undeclared_subroutine_throws_when_declaration_is_required()
    {
        var reader = CreateReader(
            main: [new Sdk2DStreamItem.CallSubroutine("tick")],
            subroutines: new Dictionary<string, IReadOnlyList<Sdk2DStreamItem>>(StringComparer.Ordinal),
            diagnostics: CursorDiagnostics);

        var exception = Assert.Throws<InvalidOperationException>(() => reader.EnterSubroutine("tick"));

        Assert.Equal("Test SDK program has no subroutine stream named 'tick'.", exception.Message);
    }

    private static SdkStreamReader<Sdk2DStreamItem, Sdk2DOperation> CreateReader(
        IReadOnlyList<Sdk2DStreamItem> main,
        SdkStreamReaderDiagnostics? diagnostics = null) =>
        CreateReader(main, new Dictionary<string, IReadOnlyList<Sdk2DStreamItem>>(StringComparer.Ordinal), diagnostics);

    private static SdkStreamReader<Sdk2DStreamItem, Sdk2DOperation> CreateReader(
        IReadOnlyList<Sdk2DStreamItem> main,
        IReadOnlyDictionary<string, IReadOnlyList<Sdk2DStreamItem>> subroutines,
        SdkStreamReaderDiagnostics? diagnostics = null) =>
        new(main, subroutines, diagnostics ?? NamedStreamDiagnostics);
}
