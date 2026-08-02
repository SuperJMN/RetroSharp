namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Sdk.Tiled;
using RetroSharp.Core.Targeting;
using RetroSharp.GameBoy;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;
using static RetroSharp.GameBoy.Tests.GameBoyTestSupport;

public partial class GameBoyRomCompilerTests
{
    [Fact]
    public void Runtime_tilemap_set_updates_the_requested_background_cell_during_vblank()
    {
        const string source = """
                              import RetroSharp.Portable2D;

                              void Main() {
                                  Video.Init();
                                  u8 x = 3;
                                  u8 y = 2;
                                  u8 tile = 5;
                                  while (true) {
                                      Video.WaitVBlank();
                                      Tilemap.Set(x, y, tile);
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);
        var cpu = new GameBoyTestCpu(rom)
        {
            CycleAccurateLy = true,
            EnforceVblankVramWrites = true,
        };

        cpu.RunFrames(3);

        Assert.Equal(5, cpu.Vram(0x9843));
        Assert.DoesNotContain(cpu.VramWrites, write => write.LcdEnabled && !write.Applied);
    }

    [Fact]
    public void Runtime_struct_array_addressing_does_not_repeat_stride_proportional_addition()
    {
        const string source = """
                              struct Actor {
                                  u16 worldX;
                                  u8 y;
                                  u8 active;
                                  u8 state0;
                                  u8 state1;
                                  u8 state2;
                                  u8 state3;
                                  u8 state4;
                                  u8 state5;
                                  u8 state6;
                                  u8 state7;
                                  u8 kind;
                              }

                              void Main() {
                                  Actor actors[2];
                                  u8 i = 1;
                                  actors[i].active += 1;
                                  actors[i].state7 += 1;
                                  actors[i].y += 1;
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);

        Assert.True(
            ContainsSequence(rom, [0xFA, 0x1A, 0xC0, 0x47, 0x87, 0x80, 0x87, 0x87, 0x80, 0x21, 0x03, 0xC0, 0x5F, 0x16, 0x00, 0x19]),
            "a 13-byte struct stride should use the accepted bounded binary multiply before forming the field address.");
        Assert.False(
            ContainsSequence(rom, [0x47, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80]),
            "a 13-byte struct stride must not emit twelve repeated ADD A,B instructions for every runtime field access.");
    }

    [Fact]
    public void Runtime_struct_array_addressing_preserves_mixed_width_values_and_mutations()
    {
        const string source = """
                              struct Actor {
                                  u16 worldX;
                                  u8 y;
                                  u8 active;
                                  u8 state0;
                                  u8 state1;
                                  u8 state2;
                                  u8 state3;
                                  u8 state4;
                                  u8 state5;
                                  u8 state6;
                                  u8 state7;
                                  u8 kind;
                              }

                              void Main() {
                                  Video.Init();
                                  Actor actors[3];
                                  u8 i = 2;
                                  actors[i].worldX = 0x1234u16;
                                  actors[i].active = 7;
                                  actors[i].active += 5;
                                  u16 wordCopy = actors[i].worldX;
                                  u8 byteCopy = actors[i].active;
                                  while (true) {
                                      Video.WaitVBlank();
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source);
        var cpu = new GameBoyTestCpu(rom);
        cpu.RunFrames(2);

        Assert.Equal(0x1234, cpu.Wram(0xC01A) | cpu.Wram(0xC01B) << 8);
        Assert.Equal(12, cpu.Wram(0xC01D));
        Assert.Equal(0x1234, cpu.Wram(0xC028) | cpu.Wram(0xC029) << 8);
        Assert.Equal(12, cpu.Wram(0xC02A));
    }

    [Fact]
    public void Direct_legacy_resource_declarations_are_rejected()
    {
        const string source = """
                              void Main() {
                                  world_column(0, 1, 2);
                                  world_map(1, 10, 2);
                              }
                              """;

        var exception = Assert.Throws<InvalidOperationException>(() => GameBoyRomCompiler.CompileSource(source));
        Assert.Contains("world_column", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Bool_flags_lower_like_int_flags_with_explicit_comparisons()
    {
        const string intSource = """
                                 type Pixel = i16;
                                 struct S { u8 grounded; u8 moving; Pixel x; }
                                 inline void step(this S s, u8 grounded) {
                                     if (grounded != 0) { s.x += 1; }
                                     if (s.grounded == 0) { s.x += 1; }
                                 }
                                 void Main() {
                                     Video.Init();
                                     S s; s.grounded = 1; s.moving = 0; s.x = 0;
                                     s.step(s.grounded);
                                     Pixel frame = s.grounded switch { 0 => 4, _ => s.moving switch { 0 => 0, _ => 7 } };
                                     i16 sink = frame + s.x;
                                     if (sink != 0) { Video.Present(); }
                                     return;
                                 }
                                 """;

        const string boolSource = """
                                  type Pixel = i16;
                                  struct S { bool grounded; bool moving; Pixel x; }
                                  inline void step(this S s, bool grounded) {
                                      if (grounded) { s.x += 1; }
                                      if (!s.grounded) { s.x += 1; }
                                  }
                                  void Main() {
                                      Video.Init();
                                      S s; s.grounded = true; s.moving = false; s.x = 0;
                                      s.step(s.grounded);
                                      Pixel frame = s.grounded switch { false => 4, _ => s.moving switch { false => 0, _ => 7 } };
                                      i16 sink = frame + s.x;
                                      if (sink != 0) { Video.Present(); }
                                      return;
                                  }
                                  """;

        Assert.Equal(GameBoyRomCompiler.CompileSource(intSource), GameBoyRomCompiler.CompileSource(boolSource));
    }

}
