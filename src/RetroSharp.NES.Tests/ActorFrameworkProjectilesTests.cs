namespace RetroSharp.NES.Tests;

using System.Buffers.Binary;
using System.IO.Compression;
using RetroSharp.GameBoy;
using RetroSharp.NES;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;

public partial class NesRomCompilerTests
{
    [Fact]
    public void Compiles_projectile_draw_through_nes_backend()
    {
        var baseDirectory = WriteSpriteAsset(
            "hero-shot.nes.json",
            """
            {
              "platforms": {
                "nes": {
                  "frames": [
                    [
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111",
                      "11111111"
                    ]
                  ]
                }
              }
            }
            """);
        const string source = """
                              void Main() {
                                  Video.Init();
                                  Sprite.Asset(hero_shot, "hero-shot.nes.json");
                                  Projectiles.Pool(shots, hero: 1, enemy: 1, requests: 1, offscreenMargin: 16);
                                  Projectiles.Def(HeroShot, team: Hero, sprite: hero_shot, speedX: 3, speedY: 0, damage: 1, lifetime: 32, hitboxWidth: 4, hitboxHeight: 4);
                                  u8 queued = 0;
                                  shots.Request(HeroShot, 40, 60, 0, queued);
                                  shots.ProcessRequests();
                                  Video.WaitVBlank();
                                  shots.Draw();
                                  return;
                              }
                              """;

        var rom = NesRomCompiler.CompileSource(source, baseDirectory);

        Assert.Equal(40976, rom.Length);
    }

}
