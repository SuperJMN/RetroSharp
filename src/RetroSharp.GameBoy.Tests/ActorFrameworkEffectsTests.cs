namespace RetroSharp.GameBoy.Tests;

using System.Buffers.Binary;
using System.IO.Compression;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Sdk.Tiled;
using RetroSharp.Core.Targeting;
using RetroSharp.GameBoy;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;

public partial class GameBoyRomCompilerTests
{
    [Fact]
    public void Effect_framework_lowers_requests_update_and_draw_as_separate_fixed_pool()
    {
        const string source = """
                              void Main() {
                                  Sprite.Asset(spark_sprite, "spark.sprite.json");
                                  Effects.Pool(fx, capacity: 2, requests: 2);
                                  Effects.Def(Spark, sprite: spark_sprite, lifetime: 8);
                                  fx.Request(Spark, 40, 60);
                                  fx.ProcessRequests();
                                  fx.Update();
                                  fx.Draw();
                              }
                              """;

        var program = ParseGameBoySourceWithPortable2D(source);
        var loweredProgram = ActorFrameworkLowerer.Lower(program, GameBoyTarget.Capabilities, supportsUpdate: true, supportsDraw: true);
        var visitor = new PrintNodeVisitor();
        loweredProgram.Accept(visitor);
        var lowered = visitor.ToString();

        Assert.Contains("struct Effect", lowered);
        Assert.Contains("Effect fx[2];", lowered);
        Assert.Contains("EffectSpawnRequest fxRequests[2];", lowered);
        Assert.Contains("fxRequests[__fx_request_call0_i].kind=Spark;", lowered);
        Assert.Contains("fx[__fx_process_Spark_i].active=1;", lowered);
        Assert.Contains("fx[__fx_update_i].age+=1;", lowered);
        Assert.Contains("RetroSharp_Portable2D_portable2d_sprite_draw(spark_sprite, __fx_draw_0_x_Spark, __fx_draw_0_y_Spark, 0, false, 0);", lowered);
        Assert.Contains("RetroSharp_Portable2D_portable2d_sprite_draw(spark_sprite, __fx_draw_1_x_Spark, __fx_draw_1_y_Spark, 0, false, 0);", lowered);
    }

    [Fact]
    public void Effect_framework_draw_hides_inactive_or_offscreen_slots_without_skipping_the_sprite_call()
    {
        const string source = """
                              void Main() {
                                  Sprite.Asset(spark_sprite, "spark.sprite.json");
                                  Effects.Pool(fx, capacity: 1, requests: 1);
                                  Effects.Def(Spark, sprite: spark_sprite, lifetime: 4);
                                  fx[0].kind = Spark;
                                  fx.Draw();
                              }
                              """;

        var program = ParseGameBoySourceWithPortable2D(source);
        var loweredProgram = ActorFrameworkLowerer.Lower(program, GameBoyTarget.Capabilities, supportsUpdate: true, supportsDraw: true);
        var visitor = new PrintNodeVisitor();
        loweredProgram.Accept(visitor);
        var lowered = visitor.ToString();

        Assert.Contains("u8 __fx_draw_0_x_Spark=0;", lowered);
        Assert.Contains("u8 __fx_draw_0_y_Spark=144;", lowered);
        Assert.Contains("__fx_draw_0_x_Spark=__fx_draw_0_screen_x;", lowered);
        Assert.Contains("__fx_draw_0_y_Spark=__fx_draw_0_screen_y;", lowered);
        Assert.Contains("RetroSharp_Portable2D_portable2d_sprite_draw(spark_sprite, __fx_draw_0_x_Spark, __fx_draw_0_y_Spark, 0, false, 0);", lowered);
        Assert.Equal(1, CountOccurrences(lowered, "RetroSharp_Portable2D_portable2d_sprite_draw(spark_sprite"));
    }

    [Fact]
    public void Effect_draw_runtime_hides_expired_slot_in_oam()
    {
        var baseDirectory = WriteSpriteAsset(
            "spark.sprite.json",
            SpriteJson(Rows(8, 16, "11111111")));
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(spark_sprite, "spark.sprite.json");
                                  Effects.Pool(fx, capacity: 1, requests: 1);
                                  Effects.Def(Spark, sprite: spark_sprite, lifetime: 1);
                                  fx.Request(Spark, 40, 60);
                                  fx.ProcessRequests();
                                  while (true) {
                                      Video.WaitVBlank();
                                      fx.Draw();
                                      fx.Update();
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);
        var cpu = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        cpu.RunFrames(4);

        Assert.Contains(cpu.OamWrites, write => write.Address == 0xFE00 && write.Value == 76);
        Assert.Equal(160, cpu.Oam(0xFE00));
    }

    [Fact]
    public void Effect_draw_runtime_uses_distinct_oam_slots_for_pool_slots()
    {
        var baseDirectory = WriteSpriteAsset(
            "spark.sprite.json",
            SpriteJson(Rows(8, 16, "11111111")));
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(spark_sprite, "spark.sprite.json");
                                  Effects.Pool(fx, capacity: 2, requests: 2);
                                  Effects.Def(Spark, sprite: spark_sprite, lifetime: 8);
                                  fx.Request(Spark, 24, 40);
                                  fx.Request(Spark, 64, 60);
                                  fx.ProcessRequests();
                                  while (true) {
                                      Video.WaitVBlank();
                                      fx.Draw();
                                  }
                              }
                              """;

        var rom = GameBoyRomCompiler.CompileSource(source, baseDirectory);
        var cpu = new GameBoyTestCpu(rom) { CycleAccurateLy = true };
        cpu.RunFrames(4);

        Assert.Equal(56, cpu.Oam(0xFE00));
        Assert.Equal(32, cpu.Oam(0xFE01));
        Assert.Equal(76, cpu.Oam(0xFE04));
        Assert.Equal(72, cpu.Oam(0xFE05));
    }

}
