namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.NES;
using RetroSharp.Parser;
using Xunit;
using static NesSdkOperationBoundaryTests;

[Trait("RetroSharp.TestOwnership", "SdkLowering")]
public sealed class NesSdkFrameInputLoweringTests
{
    [Fact]
    public void Lowers_wait_frame_operation_to_existing_nes_vblank_routine()
    {
        var builder = new PrgBuilder();
        var lowerer = CreateLowerer(builder);

        lowerer.Emit(new Sdk2DOperation.WaitFrame());

        Assert.Equal([0x2C, 0x02, 0x20, 0x30, 0xFB, 0x2C, 0x02, 0x20, 0x10, 0xFB], builder.Build());
    }

    private static NesSdkOperationLowerer CreateLowerer(PrgBuilder builder)
    {
        var program = NesVideoProgram.FromProgram(ParseLoweredProgram("void Main() { }"));
        return new NesSdkOperationLowerer(
            builder,
            program,
            new NesSdkLoweringContext(
                _ => throw UnusedContext(),
                NoSourceConstant,
                _ => throw UnusedContext(),
                _ => throw UnusedContext(),
                (_, _) => throw UnusedContext(),
                (_, _) => throw UnusedContext()),
            useFourScreenNametables: false,
            usePackedCamera: false,
            useDirectOamWrites: false);
    }

    private static bool NoSourceConstant(ExpressionSyntax _, out int value)
    {
        value = 0;
        return false;
    }

    private static InvalidOperationException UnusedContext() =>
        new("Wait-frame lowering must not use source operand context.");

    [Fact]
    public void Lowers_poll_input_operation_to_deterministic_nes_bytes()
    {
        var builder = new PrgBuilder();
        var compiler = CreateRuntimeCompiler(builder);

        compiler.SdkOperationLowerer.Emit(new Sdk2DOperation.PollInput());

        var bytes = builder.Build();
        Assert.True(ContainsSequence(bytes, [0xA5, 0xF0, 0x85, 0xF1]), "poll input should preserve the previous controller snapshot.");
        Assert.True(ContainsSequence(bytes, [0xA9, 0x01, 0x8D, 0x16, 0x40, 0xA9, 0x00, 0x8D, 0x16, 0x40, 0x85, 0xF0]), "poll input should strobe and clear the NES controller port before sampling.");
    }

    [Fact]
    public void Compiles_wait_frame_library_helper_over_nes_intrinsic_like_sdk_operation()
    {
        const string sdkSource = "void Main() { Video.WaitVBlank(); }";
        const string intrinsicSource = """
                                       [target("nes")]
                                       [intrinsic("wait_frame")]
                                       extern void nes_wait_frame();
                                       inline void wait_frame() { nes_wait_frame(); }
                                       void Main() { wait_frame(); }
                                       """;

        Assert.Equal(NesRomCompiler.CompileSource(sdkSource), NesRomCompiler.CompileSource(intrinsicSource));
    }

    [Fact]
    public void Compiles_input_poll_library_helper_over_nes_intrinsic_like_sdk_operation()
    {
        const string sdkSource = "void Main() { Input.Poll(); }";
        const string intrinsicSource = """
                                       [target("nes")]
                                       [intrinsic("poll_input")]
                                       extern void nes_poll_input();
                                       inline void poll_input() { nes_poll_input(); }
                                       void Main() { poll_input(); }
                                       """;

        Assert.Equal(NesRomCompiler.CompileSource(sdkSource), NesRomCompiler.CompileSource(intrinsicSource));
    }

    [Fact]
    public void Nes_video_wait_vblank_waits_for_the_next_vblank_edge()
    {
        const string source = """
                       void Main() {
                           Video.Init();
                           while (true) {
                               Video.WaitVBlank();
                           }
                       }
                       """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.True(
            ContainsSequence(prg, [0x2C, 0x02, 0x20, 0x30, 0xFB, 0x2C, 0x02, 0x20, 0x10, 0xFB]),
            "video_wait_vblank should first wait for any active VBlank flag to clear, then wait for the next VBlank edge.");
    }
    [Fact]
    public void Nes_video_wait_vblank_applies_pending_camera_scroll_before_sprite_dma()
    {
        const string source = """
                       void Main() {
                           Video.Init();
                           Palette.Sprite(0, 0, 1, 2, 3);
                           World.Column(0, 1);
                           World.Map(1, 0, 1);
                           Camera.Init(1, 0, 1);
                           Sprite.Asset(marker, "samples/cross-target-camera/marker.json");
                           Camera.SetPosition(0, 0);
                           while (true) {
                               Video.WaitVBlank();
                               Sprite.Draw(marker, 72, 72, 0, false, 0);
                               Camera.Apply();
                           }
                       }
                       """;

        var rom = NesRomCompiler.CompileSource(source, Path.GetDirectoryName(RepositoryFile("RetroSharp.sln")));
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();
        var waitIndex = IndexOfSequence(prg, [0x2C, 0x02, 0x20, 0x30, 0xFB, 0x2C, 0x02, 0x20, 0x10, 0xFB]);
        Assert.True(waitIndex >= 0, "The runtime VBlank wait should use edge polling.");

        var afterWait = prg.Skip(waitIndex).ToArray();
        var scrollIndex = IndexOfSequence(afterWait, [0x8D, 0x05, 0x20]);
        var dmaIndex = IndexOfSequence(afterWait, [0x8D, 0x14, 0x40]);

        Assert.True(scrollIndex >= 0, "The camera scroll restore should write PPUSCROLL.");
        Assert.True(dmaIndex >= 0, "sprite_draw should DMA the OAM shadow page.");
        Assert.True(scrollIndex < dmaIndex, "Pending camera scroll should be restored at the start of VBlank before sprite DMA consumes the window.");
    }
    [Theory]
    [InlineData("Button.A")]
    [InlineData("Button.B")]
    [InlineData("Button.Select")]
    [InlineData("Button.Start")]
    [InlineData("Button.Right")]
    [InlineData("Button.Left")]
    [InlineData("Button.Up")]
    [InlineData("Button.Down")]
    public void Button_enum_members_are_accepted_by_input_facade(string enumMember)
    {
        var source = $$"""
                              void Main() {
                                  Video.Init();
                                  i16 down = 0;
                                  i16 pressed = 0;
                                  i16 released = 0;
                                  i16 held = 0;
                                  while (true) {
                                      Video.WaitVBlank();
                                      Input.Poll();
                                      down = Input.IsDown({{enumMember}});
                                      pressed = Input.WasPressed({{enumMember}});
                                      released = Input.WasReleased({{enumMember}});
                                      held = Input.HoldTicks({{enumMember}});
                                  }
                              }
                              """;

        Assert.Equal(40976, NesRomCompiler.CompileSource(source).Length);
    }
    [Fact]
    public void Bare_button_identifiers_are_rejected_by_input_facade()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Input.Poll();
                                  if (Input.IsDown(a)) {
                                  }
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));
        Assert.Contains("Button enum member", exception.Message, StringComparison.Ordinal);
    }
    [Fact]
    public void Input_facade_predicates_lower_like_explicit_numeric_checks()
    {
        const string explicitComparisonSource = """
                                                void Main() {
                                                    Video.Init();
                                                    i16 w = 0;
                                                    i16 h = 0;
                                                    while (true) {
                                                        Video.WaitVBlank();
                                                        Input.Poll();
                                                        if (Input.WasPressed(Button.A) != 0) { w += 1; }
                                                        if (Input.IsDown(Button.A) != 0) { w += 1; }
                                                        if (Input.WasReleased(Button.A) != 0) { w += 1; }
                                                        h = Input.HoldTicks(Button.A);
                                                    }
                                                }
                                                """;

        const string predicateConditionSource = """
                                               void Main() {
                                                   Video.Init();
                                                   i16 w = 0;
                                                   i16 h = 0;
                                                   while (true) {
                                                       Video.WaitVBlank();
                                                       Input.Poll();
                                                       if (Input.WasPressed(Button.A)) { w += 1; }
                                                       if (Input.IsDown(Button.A)) { w += 1; }
                                                       if (Input.WasReleased(Button.A)) { w += 1; }
                                                       h = Input.HoldTicks(Button.A);
                                                   }
                                               }
                                               """;

        Assert.Equal(NesRomCompiler.CompileSource(explicitComparisonSource), NesRomCompiler.CompileSource(predicateConditionSource));
    }
    [Fact]
    public void Compiles_tick_input_helpers_to_nes_controller_state()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  i16 down = 0;
                                  i16 pressed = 0;
                                  i16 released = 0;
                                  i16 held = 0;
                                  while (true) {
                                      Video.WaitVBlank();
                                      Input.Poll();
                                      down = Input.IsDown(Button.A);
                                      pressed = Input.WasPressed(Button.A);
                                      released = Input.WasReleased(Button.A);
                                      held = Input.HoldTicks(Button.A);
                                  }
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source);
        var prg = rom.Skip(16).Take(32 * 1024).ToArray();

        Assert.Equal(40976, rom.Length);
        Assert.True(ContainsSequence(prg, [0xA5, 0xF0, 0x85, 0xF1]), "input_poll should snapshot previous controller state before reading the current tick.");
        Assert.True(ContainsSequence(prg, [0xA9, 0x01, 0x8D, 0x16, 0x40, 0xA9, 0x00, 0x8D, 0x16, 0x40]), "input_poll should strobe NES controller port $4016.");
        Assert.True(ContainsSequence(prg, [0xAD, 0x16, 0x40, 0x29, 0x01]), "input_poll should read serial button bits from NES controller port $4016.");
        Assert.True(ContainsSequence(prg, [0xA5, 0xF0, 0x29, 0x01]), "Input.IsDown(Button.A) should read the current tick mask.");
        Assert.True(ContainsSequence(prg, [0xA5, 0xF1, 0x29, 0x01]), "edge helpers should read the previous tick mask.");
        Assert.True(ContainsSequence(prg, [0xA5, 0xF2]), "Input.HoldTicks(Button.A) should read the A-button hold counter.");
    }

}
