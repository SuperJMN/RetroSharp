namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using Xunit;
using GbCompiler = RetroSharp.GameBoy.GameBoyRomCompiler;
using NesCompiler = RetroSharp.NES.NesRomCompiler;

public sealed class CrossTargetFrontendPreparationTests
{
    private const string PortableSource = """
        import RetroSharp.Portable2D;

        pure u8 Next(u8 value) => value + 1;

        void Main() {
            Video.Init();
            Audio.Init();
            World.Column(0, 1, 2);
            World.Column(1, 2, 3);
            World.Map(2, 0, 2);
            Camera.Init(2, 0, 2);
            while (true) {
                Video.WaitVBlank();
                Input.Poll();
                let cameraX = Next(Input.HoldTicks(Button.Right));
                Camera.SetPosition(cameraX, 0);
                Camera.Apply();
                Audio.Update();
            }
        }
        """;

    public static TheoryData<string, string> FrontendDiagnostics => new()
    {
        {
            """
            import RetroSharp.Experimental;

            void Main() {
                Actors.Pool(enemies, 0);
                let speed = 2;
                speed = 3;
            }
            """,
            "Unknown import 'RetroSharp.Experimental'."
        },
        {
            """
            void Main() {
                Video.WaitVBlank();
            }
            """,
            "Unknown static or receiver method 'Video.WaitVBlank'."
        },
        {
            """
            import RetroSharp.Portable2D;

            void Main() {
                Actors.Pool(enemies, 0);
                let speed = 2;
                speed = 3;
            }
            """,
            "Actors.Pool for 'enemies' requires a literal capacity from 1 to 255."
        },
        {
            """
            import RetroSharp.Portable2D;

            void Main() {
                u16 wideUnsigned = 300u16;
                i16 wideSigned = -2i16;
                let mixed = wideUnsigned + wideSigned;
            }
            """,
            "Cannot infer type of let 'mixed': expression mixes i16 and u16 word values; add an explicit cast."
        },
        {
            """
            import RetroSharp.Portable2D;

            pure void draw() {
                Video.Init();
            }

            void Main() {
                draw();
            }
            """,
            "pure helper 'draw' contains side-effecting statements; pure helpers must be a single return expression."
        },
    };

    [Fact]
    public void Compile_and_collect_paths_apply_equivalent_frontend_preparation_on_both_targets()
    {
        var gbOperations = GbCompiler.CollectSdkOperations(PortableSource);
        var nesOperations = NesCompiler.CollectSdkOperations(PortableSource);
        var gbAudio = GbCompiler.CollectSdkAudioOperations(PortableSource);
        var nesAudio = NesCompiler.CollectSdkAudioOperations(PortableSource);

        Assert.Equal(
            gbOperations.Select(operation => operation.GetType()),
            nesOperations.Select(operation => operation.GetType()));
        Assert.Contains(gbOperations, operation => operation is Sdk2DOperation.WaitFrame);
        Assert.Contains(gbOperations, operation => operation is Sdk2DOperation.PollInput);
        Assert.Contains(gbOperations, operation => operation is Sdk2DOperation.SetCameraPosition);
        Assert.Contains(gbOperations, operation => operation is Sdk2DOperation.ApplyCamera);
        Assert.Equal(
            gbAudio.Select(operation => operation.GetType()),
            nesAudio.Select(operation => operation.GetType()));
        Assert.Contains(gbAudio, operation => operation is SdkAudioOperation.InitializeAudio);
        Assert.Contains(gbAudio, operation => operation is SdkAudioOperation.UpdateAudio);

        Assert.Equal(32768, GbCompiler.CompileSource(PortableSource).Length);
        Assert.Equal(40976, NesCompiler.CompileSource(PortableSource).Length);
    }

    [Theory]
    [MemberData(nameof(FrontendDiagnostics))]
    public void All_entry_paths_preserve_frontend_diagnostic_precedence(string source, string expectedMessage)
    {
        foreach (var (entryPath, invoke) in EntryPaths())
        {
            var exception = Assert.Throws<InvalidOperationException>(() => invoke(source));
            Assert.True(
                string.Equals(expectedMessage, exception.Message, StringComparison.Ordinal),
                $"{entryPath} produced '{exception.Message}' instead of '{expectedMessage}'.");
        }
    }

    [Fact]
    public void Window_hud_remains_a_target_specific_diagnostic_after_shared_preparation()
    {
        const string source = """
            import RetroSharp.Portable2D;

            void Main() {
                Video.Init();
                Hud.SetTile(window, 0, 0, 1);
            }
            """;
        const string expected = "Target 'nes' does not support Window HUD. Use disable HUD for this target.";

        Assert.Empty(GbCompiler.CollectSdkOperations(source));
        Assert.Empty(GbCompiler.CollectSdkAudioOperations(source));
        Assert.Equal(32768, GbCompiler.CompileSource(source).Length);

        foreach (var (entryPath, invoke) in NesEntryPaths())
        {
            var exception = Assert.Throws<InvalidOperationException>(() => invoke(source));
            Assert.True(
                string.Equals(expected, exception.Message, StringComparison.Ordinal),
                $"{entryPath} produced '{exception.Message}' instead of '{expected}'.");
        }
    }

    private static IReadOnlyList<(string Name, Action<string> Invoke)> EntryPaths()
    {
        return
        [
            ("Game Boy compile", source => _ = GbCompiler.CompileSource(source)),
            ("Game Boy SDK collect", source => _ = GbCompiler.CollectSdkOperations(source)),
            ("Game Boy audio collect", source => _ = GbCompiler.CollectSdkAudioOperations(source)),
            .. NesEntryPaths(),
        ];
    }

    private static IReadOnlyList<(string Name, Action<string> Invoke)> NesEntryPaths()
    {
        return
        [
            ("NES compile", source => _ = NesCompiler.CompileSource(source)),
            ("NES SDK collect", source => _ = NesCompiler.CollectSdkOperations(source)),
            ("NES audio collect", source => _ = NesCompiler.CollectSdkAudioOperations(source)),
        ];
    }
}
