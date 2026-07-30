namespace RetroSharp.NES.Tests;

using RetroSharp.GameBoy;
using RetroSharp.NES;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;
using static RetroSharp.NES.Tests.NesTestAssets;

public partial class NesRomCompilerTests
{
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

        Assert.Equal(NesRomCompiler.CompileSource(intSource), NesRomCompiler.CompileSource(boolSource));
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

        var exception = Assert.Throws<InvalidOperationException>(() => NesRomCompiler.CompileSource(source));
        Assert.Contains("world_column", exception.Message, StringComparison.Ordinal);
    }



    [Fact]
    public void Runtime_struct_array_addressing_preserves_mixed_width_values_and_mutations()
    {
        const string source = """
                              struct Entry {
                                  u8 tag;
                                  u16 worldX;
                                  u8 y;
                                  bool active;
                              }

                              void Main() {
                                  Entry entries[4];
                                  u8 i = 3;
                                  entries[i].tag = 0xA5;
                                  entries[i].worldX = 0x1234u16;
                                  entries[i].y = 7;
                                  entries[i].y += 2;
                                  entries[i].active = true;
                                  u8 tagCopy = entries[i].tag;
                                  u16 worldXCopy = entries[i].worldX;
                                  u8 yCopy = entries[i].y;
                                  bool activeCopy = entries[i].active;
                              }
                              """;

        var result = RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(source);
        var variables = result.Report.UserVariables.ToDictionary(variable => variable.Name, StringComparer.Ordinal);
        var cpu = new NesTestCpu(result.Rom);

        cpu.RunFrames(6);

        Assert.Equal(0xA5, cpu.Ram(variables["tagCopy"].Address));
        Assert.Equal(0x34, cpu.Ram(variables["worldXCopy"].Address));
        Assert.Equal(0x12, cpu.Ram((ushort)(variables["worldXCopy"].Address + 1)));
        Assert.Equal(9, cpu.Ram(variables["yCopy"].Address));
        Assert.Equal(1, cpu.Ram(variables["activeCopy"].Address));
    }

}
