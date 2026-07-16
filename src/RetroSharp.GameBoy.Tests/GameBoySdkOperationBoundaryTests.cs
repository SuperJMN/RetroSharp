namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.GameBoy;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;

public sealed class GameBoySdkOperationBoundaryTests
{


    [Fact]
    public void Runtime_compiler_consumes_the_collected_sdk_operation_stream()
    {
        var builder = new GbBuilder();
        var program = ProgramWithOverriddenSdkOperations(
            """
            void Main() {
                Video.Init();
                Sprite.Asset(player_run, "player.sprite.json");
                Sprite.Draw(player_run, 72, 80, 0);
            }
            """,
            WriteSpriteAsset(),
            [
                new Sdk2DOperation.DrawLogicalSprite(
                    "player_run",
                    X: new SdkByteExpression.Constant(24),
                    Y: new SdkByteExpression.Constant(80),
                    Frame: new SdkByteExpression.Constant(0),
                    FlipX: null,
                    PaletteSlot: 0,
                    StaticTransform: SpriteTransform.None),
            ]);
        var compiler = new GameBoyRuntimeCompiler(builder, program);

        compiler.Emit(program.MainBlock);

        var bytes = builder.Build();
        Assert.True(
            ContainsSequence(bytes, [0x3E, 0x18, 0xC6, 0x08, 0xEA, 0x01, 0xFE]),
            "Runtime emission should use the collected sprite operation operand.");
        Assert.False(
            ContainsSequence(bytes, [0x3E, 0x48, 0xC6, 0x08, 0xEA, 0x01, 0xFE]),
            "Runtime emission should not re-read the sprite operand from the AST call.");
    }

    [Fact]
    public void Runtime_compiler_consumes_an_overridden_value_sdk_operation_through_the_collected_stream()
    {
        var builder = new GbBuilder();
        var program = ProgramWithOverriddenSdkOperations(
            """
            void Main() {
                World.Column(0, 0, 0);
                World.Column(1, 0, 0);
                World.Flags(0, 0, 1);
                World.Flags(1, 1, 0);
                World.Map(2, 11, 2);
                i16 flags = World.TileFlagsAt(0, 8);
            }
            """,
            Directory.GetCurrentDirectory(),
            [new Sdk2DOperation.ReadWorldTileFlags("default", WorldX: 8, WorldY: 8)]);
        builder.Label(GameBoyRomBuilder.MapFlagDataLabel);
        builder.Emit(0, 1, 1, 0);
        var compiler = new GameBoyRuntimeCompiler(builder, program);

        compiler.Emit(program.MainBlock);

        var bytes = builder.Build();
        Assert.True(
            ContainsSequence(bytes, [0x3E, 0x08, 0xCB, 0x3F, 0xCB, 0x3F, 0xCB, 0x3F]),
            "Runtime value emission should use the overridden world X operand from the collected SDK operation.");
        Assert.False(
            ContainsSequence(bytes, [0x3E, 0x00, 0xCB, 0x3F, 0xCB, 0x3F, 0xCB, 0x3F]),
            "Runtime value emission should not re-read the original world X operand from the AST call.");
    }



    internal static GameBoyRuntimeCompiler CreateRuntimeCompiler(GbBuilder builder)
    {
        var program = GameBoyVideoProgram.FromProgram(ParseLoweredProgram("void Main() { }"));
        return new GameBoyRuntimeCompiler(builder, program);
    }

    internal static GameBoyRuntimeCompiler CreateRuntimeCompiler(GbBuilder builder, string source, string baseDirectory)
    {
        var program = GameBoyVideoProgram.FromProgram(ParseLoweredProgram(source), baseDirectory);
        return new GameBoyRuntimeCompiler(builder, program);
    }

    private static GameBoyVideoProgram ProgramWithOverriddenSdkOperations(string source, string baseDirectory, IReadOnlyList<Sdk2DOperation> operations)
    {
        var program = GameBoyVideoProgram.FromProgram(ParseLoweredProgram(source), baseDirectory);
        typeof(GameBoyVideoProgram)
            .GetProperty(nameof(GameBoyVideoProgram.SdkOperations))!
            .SetValue(program, operations);
        return program;
    }

    internal static ProgramSyntax ParseLoweredProgram(string source)
    {
        var merged = SdkLibrarySource.Merge(
            GameBoyTarget.Intrinsics,
            source,
            libraryImportPaths: [SdkImportResolver.Portable2D]);
        var parse = new SomeParser().Parse(merged);
        if (parse.IsFailure)
        {
            throw new InvalidOperationException(parse.Error.ToString());
        }

        var targetProgram = TargetProgramSelector.Select(parse.Value, GameBoyTarget.Intrinsics);
        return SdkSourcePackageFacadeLowerer.Lower(targetProgram);
    }

    [Fact]
    public void Collects_portable_sdk_operations_before_game_boy_lowering()
    {
        const string source = """
                              void tick() {
                                  Input.Poll();
                                  scroll_set(0, 0);
                                  return;
                              }

                              void Main() {
                                  while (true) {
                                      Video.WaitVBlank();
                                      tick();
                                      sprite_set(0, 8, 16, 6, 0);
                                  }
                              }
                              """;

        var operations = GameBoyRomCompiler.CollectSdkOperations(source);

        Assert.Collection(
            operations,
            operation => Assert.IsType<Sdk2DOperation.WaitFrame>(operation),
            operation => Assert.IsType<Sdk2DOperation.PollInput>(operation));

        Assert.Equal(32768, GameBoyRomCompiler.CompileSource(source).Length);
    }

    [Fact]
    public void Collects_byte_operands_as_typed_storage_locations()
    {
        const string source = """
                              struct Actor { u8 x; }

                              void Main() {
                                  Actor actor = { x: 24 };
                                  u8 frames[3] = [0, 1, 2];
                                  Sprite.Draw(player_run, actor.x, frames[2], 0);
                              }
                              """;

        var operation = Assert.Single(GameBoyRomCompiler.CollectSdkOperations(source));
        var draw = Assert.IsType<Sdk2DOperation.DrawLogicalSprite>(operation);

        Assert.Equal(Field(LocalLocation("actor"), "x"), draw.X);
        Assert.Equal(Indexed("frames", 2), draw.Y);
    }

    internal static string WriteSpriteAsset()
    {
        var directory = Path.Combine(Path.GetTempPath(), "retrosharp-gb-sdk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "player.sprite.json"),
            """
            {
              "platforms": {
                "gb": {
                  "frames": [
                    [
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123",
                      "01230123"
                    ]
                  ]
                }
              }
            }
            """);
        return directory;
    }

    internal static bool ContainsSequence(byte[] bytes, byte[] sequence)
    {
        for (var i = 0; i <= bytes.Length - sequence.Length; i++)
        {
            var matched = true;
            for (var j = 0; j < sequence.Length; j++)
            {
                if (bytes[i + j] != sequence[j])
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                return true;
            }
        }

        return false;
    }

    internal static SdkByteExpression.Variable Local(string name)
    {
        return new SdkByteExpression.Variable(LocalLocation(name));
    }

    internal static SdkWordExpression.Variable WordLocal(string name)
    {
        return new SdkWordExpression.Variable(LocalLocation(name));
    }

    internal static SdkByteExpression.Variable Field(SdkStorageLocation target, string fieldName)
    {
        return new SdkByteExpression.Variable(new SdkStorageLocation.Field(target, fieldName));
    }

    internal static SdkByteExpression.Variable Indexed(string baseName, int index)
    {
        return new SdkByteExpression.Variable(new SdkStorageLocation.IndexedElement(baseName, index));
    }

    internal static SdkByteExpression.Variable RuntimeIndexedField(string baseName, string indexName, string fieldName)
    {
        return new SdkByteExpression.Variable(new SdkStorageLocation.RuntimeIndexedField(baseName, Local(indexName), fieldName));
    }

    internal static SdkStorageLocation.Local LocalLocation(string name)
    {
        return new SdkStorageLocation.Local(name);
    }
}
