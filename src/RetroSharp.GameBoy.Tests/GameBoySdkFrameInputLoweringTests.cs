namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.GameBoy;
using Xunit;
using static RetroSharp.GameBoy.Tests.GameBoySdkOperationBoundaryTests;

public sealed class GameBoySdkFrameInputLoweringTests
{
    [Fact]
    public void Lowers_wait_frame_operation_to_existing_game_boy_vblank_routine()
    {
        var builder = new GbBuilder();
        var compiler = CreateRuntimeCompiler(builder);

        compiler.SdkOperationLowerer.Emit(new Sdk2DOperation.WaitFrame());

        Assert.Equal(
            [0xF0, 0x44, 0xFE, 0x90, 0x30, 0xFA, 0xF0, 0x44, 0xFE, 0x90, 0x38, 0xFA],
            builder.Build());
    }

    [Fact]
    public void Lowers_poll_input_operation_to_deterministic_game_boy_bytes()
    {
        var builder = new GbBuilder();
        CreateRuntimeCompiler(builder).SdkOperationLowerer.Emit(new Sdk2DOperation.PollInput());

        var bytes = builder.Build();
        Assert.True(ContainsSequence(bytes, [0xFA, 0xF0, 0xC0, 0xEA, 0xF1, 0xC0]), "poll input should preserve the previous joypad snapshot.");
        Assert.True(ContainsSequence(bytes, [0x3E, 0x10, 0xE0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0x2F, 0xE6, 0x0F]), "poll input should select and settle the Game Boy button row.");
        Assert.True(ContainsSequence(bytes, [0x3E, 0x20, 0xE0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0x2F, 0xE6, 0x0F, 0xCB, 0x37, 0xB0, 0xEA, 0xF0, 0xC0]), "poll input should select, settle, combine, and publish the Game Boy direction row.");
        Assert.True(ContainsSequence(bytes, [0x3E, 0x30, 0xE0, 0x00]), "poll input should deselect both joypad rows after sampling.");
    }

    [Fact]
    public void Direct_button_read_and_bare_button_identifiers_are_rejected()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Input.Poll();
                                  if (button_pressed(a) != 0) {
                                  }
                                  if (Input.IsDown(Button.A)) {
                                  }
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));
        Assert.Contains("button_pressed", exception.Message, StringComparison.Ordinal);
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

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));
        Assert.Contains("Button enum member", exception.Message, StringComparison.Ordinal);
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
                                  i16 held = 0;
                                  i16 ticks = 0;
                                  while (true) {
                                      Video.WaitVBlank();
                                      Input.Poll();
                                      if (Input.WasPressed({{enumMember}}) != 0) {
                                          held = 1;
                                      }
                                      if (Input.IsDown({{enumMember}}) != 0) {
                                          held = 2;
                                      }
                                      if (Input.WasReleased({{enumMember}}) != 0) {
                                          held = 0;
                                      }
                                      ticks = Input.HoldTicks({{enumMember}});
                                  }
                              }
                              """;

        Assert.Equal(32768, GameBoyRomCompiler.CompileSource(source).Length);
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

        Assert.Equal(GameBoyRomCompiler.CompileSource(explicitComparisonSource), GameBoyRomCompiler.CompileSource(predicateConditionSource));
    }

    [Fact]
    public void Compiles_tick_input_api_for_variable_jump()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  i16 grounded = 1;
                                  i16 velocityY = 0;
                                  i16 jumping = 0;
                                  i16 jumpTicks = 0;
                                  while (true) {
                                      Video.WaitVBlank();
                                      Input.Poll();
                                      if (Input.WasPressed(Button.A) != 0) {
                                          if (grounded != 0) {
                                              velocityY = 252;
                                              jumping = 1;
                                          }
                                      }
                                      if (jumping != 0) {
                                          jumpTicks = Input.HoldTicks(Button.A);
                                          if (Input.IsDown(Button.A) != 0) {
                                              if (jumpTicks < 12) {
                                                  velocityY = velocityY - 1;
                                              }
                                          }
                                          if (Input.WasReleased(Button.A) != 0) {
                                              jumping = 0;
                                          }
                                      }
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0xFA, 0xF0, 0xC0, 0xEA, 0xF1, 0xC0]), "input_poll should snapshot the previous button mask before reading the current tick.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x10, 0xE0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0x2F, 0xE6, 0x0F, 0x47]), "input_poll should read the settled action-button group into the current tick mask.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x20, 0xE0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0x2F, 0xE6, 0x0F, 0xCB, 0x37, 0xB0, 0xEA, 0xF0, 0xC0]), "input_poll should read the settled direction-button group and store a combined button mask.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xF2, 0xC0, 0x6F, 0x26, 0x00, 0x7D, 0xEA, 0x06, 0xC0, 0x7C, 0xEA, 0x07, 0xC0]), "Input.HoldTicks(Button.A) should return a complete zero-extended I16 value into a game variable.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xF0, 0xC0, 0xE6, 0x01, 0xFE, 0x00, 0xCA]), "Input.IsDown(Button.A) and Input.WasPressed(Button.A) should test the current tick mask.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xF1, 0xC0, 0xE6, 0x01, 0xFE, 0x00, 0xC2]), "Input.WasPressed(Button.A) should reject buttons that were already down in the previous tick.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xF0, 0xC0, 0xE6, 0x01, 0xFE, 0x00, 0xC2]), "Input.WasReleased(Button.A) should require the button to be up in the current tick.");
        Assert.True(ContainsSequence(rom, [0xFA, 0xF1, 0xC0, 0xE6, 0x01, 0xFE, 0x00, 0xCA]), "Input.WasReleased(Button.A) should require the button to have been down in the previous tick.");
    }

    [Fact]
    public void Input_poll_settles_joypad_rows_before_latching_buttons()
    {
        const string source = """
                              void Main() {
                                  Video.Init();
                                  while (true) {
                                      Video.WaitVBlank();
                                      Input.Poll();
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.Equal(32768, rom.Length);
        Assert.True(ContainsSequence(rom, [0x3E, 0x10, 0xE0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0x2F, 0xE6, 0x0F, 0x47]), "input_poll should discard early action-row reads so DMG hardware has time to settle.");
        Assert.True(ContainsSequence(rom, [0x3E, 0x20, 0xE0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0xF0, 0x00, 0x2F, 0xE6, 0x0F, 0xCB, 0x37]), "input_poll should discard early d-pad-row reads so DMG hardware has time to settle.");
        Assert.True(ContainsSequence(rom, [0xEA, 0xF0, 0xC0, 0x3E, 0x30, 0xE0, 0x00]), "input_poll should deselect both joypad rows after latching the snapshot.");
    }
}
