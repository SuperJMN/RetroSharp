namespace RetroSharp.GameBoy.Tests;

using Xunit;

public sealed class GameBoyViewportConstantTests
{
    [Fact]
    public void Viewport_constants_reach_generated_code_as_game_boy_screen_pixels()
    {
        const string source = """
                              import RetroSharp.Portable2D;

                              static class Follow
                              {
                                  const i16 Left = Viewport.Width / 4;
                                  const i16 Right = Viewport.Width * 3 / 8;
                              }

                              void Main() {
                                  Video.Init();
                                  u8 width = Viewport.Width / 2;
                                  u8 height = Viewport.Height / 2;
                                  u8 left = Follow.Left;
                                  u8 right = Follow.Right;
                                  while (true) {
                                      Video.WaitVBlank();
                                  }
                              }
                              """;

        var result = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(source);
        var variables = result.Report.UserVariables.ToDictionary(variable => variable.Name, StringComparer.Ordinal);
        var cpu = new GameBoyTestCpu(result.Rom);

        cpu.RunFrames(2);

        Assert.Equal(80, cpu.Wram(variables["width"].Address));
        Assert.Equal(72, cpu.Wram(variables["height"].Address));
        Assert.Equal(40, cpu.Wram(variables["left"].Address));
        Assert.Equal(60, cpu.Wram(variables["right"].Address));
    }
}
