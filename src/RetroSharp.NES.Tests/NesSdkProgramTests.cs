namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using Xunit;

public sealed class NesSdkProgramTests
{
    [Fact]
    public void Named_subroutine_stream_is_consumed_once_for_each_call()
    {
        var program = TickProgram();
        var reader = new NesSdkStreamReader(program);

        reader.ConsumeOperation<Sdk2DOperation.PollInput>("Input.Poll");
        ConsumeTick(reader);
        ConsumeTick(reader);

        reader.EnsureAllConsumed("NES runtime");
    }

    [Fact]
    public void Runtime_work_counts_each_call_while_the_body_is_collected_once()
    {
        var program = TickProgram();

        var collected = NesSdkProgramOperations.Collected(program);
        var runtimeWork = NesSdkProgramOperations.ForRuntimeWork(program);

        Assert.Collection(
            collected,
            operation => Assert.IsType<Sdk2DOperation.PollInput>(operation),
            operation => Assert.IsType<Sdk2DOperation.WaitFrame>(operation));
        Assert.Collection(
            runtimeWork,
            operation => Assert.IsType<Sdk2DOperation.PollInput>(operation),
            operation => Assert.IsType<Sdk2DOperation.WaitFrame>(operation),
            operation => Assert.IsType<Sdk2DOperation.WaitFrame>(operation));
    }

    private static Sdk2DProgram TickProgram() =>
        new(
            [
                new Sdk2DStreamItem.Op(new Sdk2DOperation.PollInput()),
                new Sdk2DStreamItem.CallSubroutine("tick"),
                new Sdk2DStreamItem.CallSubroutine("tick"),
            ],
            new Dictionary<string, IReadOnlyList<Sdk2DStreamItem>>(StringComparer.Ordinal)
            {
                ["tick"] = [new Sdk2DStreamItem.Op(new Sdk2DOperation.WaitFrame())],
            });

    private static void ConsumeTick(NesSdkStreamReader reader)
    {
        reader.ConsumeSubroutineCall("tick");
        reader.EnterSubroutine("tick");
        reader.ConsumeOperation<Sdk2DOperation.WaitFrame>("Video.WaitVBlank");
        reader.LeaveSubroutine("tick");
    }
}
