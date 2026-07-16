namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.NES;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;

public sealed class NesSdkOperationBoundaryTests
{
    internal static NesRuntimeCompiler CreateRuntimeCompiler(PrgBuilder builder)
    {
        var program = NesVideoProgram.FromProgram(ParseLoweredProgram("void Main() { }"));
        return new NesRuntimeCompiler(builder, program);
    }

    internal static ProgramSyntax ParseLoweredProgram(string source)
    {
        var merged = SdkLibrarySource.Merge(
            NesTarget.Intrinsics,
            source,
            libraryImportPaths: [SdkImportResolver.Portable2D]);
        var parse = new SomeParser().Parse(merged);
        if (parse.IsFailure)
        {
            throw new InvalidOperationException(parse.Error.ToString());
        }

        var targetProgram = TargetProgramSelector.Select(parse.Value, NesTarget.Intrinsics);
        return SdkSourcePackageFacadeLowerer.Lower(targetProgram);
    }

    internal static bool ContainsSequence(IReadOnlyList<byte> bytes, IReadOnlyList<byte> sequence)
    {
        for (var index = 0; index <= bytes.Count - sequence.Count; index++)
        {
            if (sequence.Select((value, offset) => bytes[index + offset] == value).All(matches => matches))
            {
                return true;
            }
        }

        return false;
    }

    internal static string Fingerprint(byte[] bytes) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));

    internal static string WriteSpriteAsset(string fileName, string contents)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.NES.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), contents);
        return directory;
    }

    internal static string RepositoryFile(string relativePath)
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

    internal static int CountOccurrences(IReadOnlyList<byte> bytes, IReadOnlyList<byte> sequence)
    {
        var count = 0;
        for (var index = 0; index <= bytes.Count - sequence.Count; index++)
        {
            if (sequence.Select((value, offset) => bytes[index + offset] == value).All(matches => matches))
            {
                count++;
            }
        }

        return count;
    }

    internal static int IndexOfSequence(IReadOnlyList<byte> bytes, IReadOnlyList<byte> sequence)
    {
        for (var index = 0; index <= bytes.Count - sequence.Count; index++)
        {
            if (sequence.Select((value, offset) => bytes[index + offset] == value).All(matches => matches))
            {
                return index;
            }
        }

        return -1;
    }

    internal static string CollisionHitContractSource(int height, int solidRow, string body)
    {
        var visual = string.Join(", ", Enumerable.Repeat("0", height));
        var flags = string.Join(", ", Enumerable.Range(0, height).Select(row => row == solidRow ? "1" : "0"));
        return $$"""
                 void Main() {
                     World.Column(0, {{visual}});
                     World.Flags(0, {{flags}});
                     World.Map(1, 0, {{height}});
                     Camera.Init(1, 0, {{height}});
                     Camera.SetPosition(0, 1);
                     Camera.Apply();
                     {{body}}
                 }
                 """;
    }

    [Fact]
    public void Stream_reader_rejects_a_mismatched_collected_operation()
    {
        var reader = new NesSdkStreamReader([new Sdk2DOperation.PollInput()]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => reader.ConsumeOperation<Sdk2DOperation.WaitFrame>("Video.WaitVBlank"));

        Assert.Equal(
            "NES SDK call 'Video.WaitVBlank' expected WaitFrame, got PollInput at stream item 0.",
            exception.Message);
    }

    [Fact]
    public void Runtime_compiler_emits_the_collected_operation_instead_of_reconstructing_the_ast_call()
    {
        const string source = """
                              void Main() {
                                  World.Column(0, 1, 2);
                                  World.Column(1, 3, 4);
                                  World.Map(2, 10, 2);
                                  Camera.Init(2, 10, 2);
                                  Camera.SetPosition(4, 0);
                              }
                              """;
        var program = NesVideoProgram.FromProgram(ParseLoweredProgram(source));
        typeof(NesVideoProgram)
            .GetProperty(nameof(NesVideoProgram.SdkOperationStream))!
            .SetValue(
                program,
                new Sdk2DOperation[]
                {
                    new Sdk2DOperation.SetCameraPosition(8, 0, ScrollAxes.Horizontal),
                });
        var builder = new PrgBuilder();
        var compiler = new NesRuntimeCompiler(builder, program);

        compiler.Emit(program.MainBlock);

        var bytes = builder.Build();
        Assert.True(
            ContainsSequence(bytes, [0xA9, 0x08, 0x85, 0xE7]),
            "runtime emission should use the collected camera X operand.");
        Assert.False(
            ContainsSequence(bytes, [0xA9, 0x04, 0x85, 0xE7]),
            "runtime emission should not reconstruct camera X from the source call.");
    }

    [Fact]
    public void Stream_reader_rejects_unconsumed_collected_operations()
    {
        var reader = new NesSdkStreamReader([new Sdk2DOperation.PollInput()]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => reader.EnsureAllConsumed("NES runtime"));

        Assert.Equal(
            "NES runtime consumed 0 of 1 SDK operation(s); next operation is PollInput.",
            exception.Message);
    }

    [Theory]
    [InlineData("void Main() { return; Video.WaitVBlank(); }")]
    [InlineData("void Main() { while (false) { Video.WaitVBlank(); } }")]
    [InlineData("void Main() { while (true) { break; Video.WaitVBlank(); } }")]
    [InlineData("void Main() { while (true) { continue; Video.WaitVBlank(); } }")]
    public void Runtime_stream_ignores_sdk_calls_in_unreachable_source(string source)
    {
        var program = NesVideoProgram.FromProgram(ParseLoweredProgram(source));
        var builder = new PrgBuilder();
        var compiler = new NesRuntimeCompiler(builder, program);

        compiler.Emit(program.MainBlock);
    }

    [Fact]
    public void Unreachable_sdk_calls_still_receive_target_capability_validation()
    {
        const string source = """
                              import RetroSharp.Portable2D;

                              void Main() {
                                  Video.Init();
                                  while (false) {
                                      Hud.SetTile(window, 0, 0, 1);
                                  }
                              }
                              """;

        var operations = NesRomCompiler.CollectSdkOperations(source);
        Assert.Contains(operations, operation => operation is Sdk2DOperation.SetHudTile);

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));

        Assert.Equal(
            "Target 'nes' does not support Window HUD. Use disable HUD for this target.",
            exception.Message);
    }
}
