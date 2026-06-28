namespace RetroSharp.GameBoy.Tests;

using System.Collections.Generic;
using System.Linq;
using RetroSharp.Core.Sdk;
using RetroSharp.GameBoy;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;

public sealed class Sdk2DProgramStreamTests
{
    private const string Source = """
        void tick() {
            video.WaitVBlank();
            input.Poll();
        }

        void main() {
            video.Init();
            loop {
                tick();
                tick();
            }
        }
        """;

    private static GameBoyVideoProgram Compile()
    {
        var parse = new SomeParser().Parse(Source);
        Assert.True(parse.IsSuccess, parse.IsFailure ? parse.Error : string.Empty);
        return GameBoyVideoProgram.FromProgram(parse.Value, null);
    }

    [Fact]
    public void Empty_subroutine_set_is_byte_identical_to_legacy_collect()
    {
        var program = Compile();
        var legacy = Sdk2DOperationCollector.Collect(
            program.MainBlock,
            program.Functions,
            "Game Boy",
            GameBoyTarget.Capabilities);
        var streamed = Sdk2DOperationCollector.CollectProgram(
            program.MainBlock,
            program.Functions,
            "Game Boy",
            GameBoyTarget.Capabilities,
            new HashSet<string>());

        Assert.Empty(streamed.Subroutines);
        var flattened = streamed.Main.Select(item => Assert.IsType<Sdk2DStreamItem.Op>(item).Operation).ToList();
        Assert.Equal(legacy, flattened);
    }

    [Fact]
    public void Subroutined_function_body_is_collected_once_and_referenced_by_call_markers()
    {
        var program = Compile();
        var streamed = Sdk2DOperationCollector.CollectProgram(
            program.MainBlock,
            program.Functions,
            "Game Boy",
            GameBoyTarget.Capabilities,
            new HashSet<string> { "tick" });

        // The two tick() calls become CallSubroutine markers in the main stream
        // instead of inlining tick's body twice.
        var markers = streamed.Main.OfType<Sdk2DStreamItem.CallSubroutine>().ToList();
        Assert.Equal(2, markers.Count);
        Assert.All(markers, marker => Assert.Equal("tick", marker.Name));

        // tick's WaitFrame + PollInput ops are collected exactly once.
        Assert.True(streamed.Subroutines.ContainsKey("tick"));
        var tickOps = streamed.Subroutines["tick"]
            .Select(item => Assert.IsType<Sdk2DStreamItem.Op>(item).Operation)
            .ToList();
        Assert.Collection(
            tickOps,
            op => Assert.IsType<Sdk2DOperation.WaitFrame>(op),
            op => Assert.IsType<Sdk2DOperation.PollInput>(op));

        // The main stream itself carries no inlined copy of tick's frame ops.
        Assert.DoesNotContain(
            streamed.Main.OfType<Sdk2DStreamItem.Op>(),
            item => item.Operation is Sdk2DOperation.WaitFrame or Sdk2DOperation.PollInput);
    }

    [Fact]
    public void Audio_subroutined_function_body_is_collected_once_and_referenced_by_call_markers()
    {
        const string source = """
            void tick_audio() {
                audio.Update();
            }

            void main() {
                video.Init();
                audio.Init();
                loop {
                    tick_audio();
                    tick_audio();
                }
            }
            """;
        var parse = new SomeParser().Parse(source);
        Assert.True(parse.IsSuccess, parse.IsFailure ? parse.Error : string.Empty);
        var program = GameBoyVideoProgram.FromProgram(parse.Value, null);

        var streamed = SdkAudioOperationCollector.CollectProgram(
            program.MainBlock,
            program.Functions,
            "Game Boy",
            new HashSet<string> { "tick_audio" });

        var markers = streamed.Main.OfType<SdkAudioStreamItem.CallSubroutine>().ToList();
        Assert.Equal(2, markers.Count);
        Assert.All(markers, marker => Assert.Equal("tick_audio", marker.Name));

        Assert.True(streamed.Subroutines.ContainsKey("tick_audio"));
        var tickOps = streamed.Subroutines["tick_audio"]
            .Select(item => Assert.IsType<SdkAudioStreamItem.Op>(item).Operation)
            .ToList();
        Assert.Collection(tickOps, op => Assert.IsType<SdkAudioOperation.UpdateAudio>(op));

        Assert.Single(streamed.Main.OfType<SdkAudioStreamItem.Op>());
        Assert.IsType<SdkAudioOperation.InitializeAudio>(streamed.Main.OfType<SdkAudioStreamItem.Op>().Single().Operation);
    }
}
