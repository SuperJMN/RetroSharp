namespace RetroSharp.GameBoy.Tests;

using RetroSharp.GameBoy;
using Xunit;

public sealed class GameBoyLargeWorldCameraTests
{
    [Fact]
    public void Camera_accepts_logical_map_width_above_byte_range()
    {
        var rom = GameBoyRomCompiler.CompileSource(WideCameraSource(256));

        Assert.True(rom.Length >= 32 * 1024);
    }

    [Fact]
    public void Camera_streams_logical_column_256_when_moving_from_255_to_256()
    {
        var cpu = new GameBoyTestCpu(GameBoyRomCompiler.CompileSource(WideCameraSource(1888)))
        {
            CycleAccurateLy = true,
        };

        cpu.RunFrames(130);

        Assert.Equal(0x60, cpu.Wram(0xC0E0));
        Assert.Equal(0x07, cpu.Wram(0xC0E1));
        Assert.Equal(0x01, cpu.Wram(0xC144));
        Assert.Equal(4, cpu.Vram(0x9800));
        Assert.Equal(0x60, cpu.IoRegister(0xFF43));
    }

    [Fact]
    public void Camera_streams_logical_column_256_when_moving_from_256_to_255()
    {
        var source = WideCameraSource(
            requestedX: 2056,
            beforeApply: "if (ticks == 130) { cameraX = 2055; }",
            afterApply: "ticks += 1;",
            declarations: "i16 ticks = 0;");
        var cpu = new GameBoyTestCpu(GameBoyRomCompiler.CompileSource(source))
        {
            CycleAccurateLy = true,
        };

        cpu.RunFrames(136);

        Assert.Equal(0x07, cpu.Wram(0xC0E0));
        Assert.Equal(0x08, cpu.Wram(0xC0E1));
        Assert.Equal(0x00, cpu.Wram(0xC0E3));
        Assert.Equal(0x01, cpu.Wram(0xC143));
        Assert.Equal(4, cpu.Vram(0x9800));
        Assert.Equal(0x07, cpu.IoRegister(0xFF43));
    }

    [Fact]
    public void Vertical_stream_keeps_logical_source_column_256_after_horizontal_camera_crossing()
    {
        var cpu = new GameBoyTestCpu(GameBoyRomCompiler.CompileSource(WideDiagonalCameraSource()))
        {
            CycleAccurateLy = true,
        };

        cpu.RunFrames(145);

        Assert.Equal(0x00, cpu.Wram(0xC0E3));
        Assert.Equal(0x01, cpu.Wram(0xC143));
        Assert.Equal(7, cpu.Vram(0x9A60));
    }

    private static string WideCameraSource(
        int requestedX,
        string beforeApply = "",
        string afterApply = "",
        string declarations = "")
    {
        return $$"""
                 void Main() {
                     Video.Init();
                     World.Column(0, 1);
                     World.Column(256, 4);
                     World.Column(311, 1);
                     World.Map(312, 0, 1);
                     Camera.Init(312, 0, 1);
                     i16 cameraX = {{requestedX}};
                     {{declarations}}
                     while (true) {
                         Video.WaitVBlank();
                         {{beforeApply}}
                         Camera.SetPosition(cameraX, 0);
                         Camera.Apply();
                         {{afterApply}}
                     }
                 }
                 """;
    }

    private static string WideDiagonalCameraSource()
    {
        var ordinaryColumn = string.Join(", ", Enumerable.Repeat("1", 22));
        var markerColumn = string.Join(", ", Enumerable.Repeat("0", 19).Append("7").Concat(Enumerable.Repeat("0", 2)));
        return $$"""
                 void Main() {
                     Video.Init();
                     World.Column(0, {{ordinaryColumn}});
                     World.Column(256, {{markerColumn}});
                     World.Column(311, {{ordinaryColumn}});
                     World.Map(312, 0, 22);
                     Camera.Init(312, 0, 22);
                     i16 cameraX = 2048;
                     i16 cameraY = 0;
                     i16 ticks = 0;
                     while (true) {
                         Video.WaitVBlank();
                         if (ticks == 130) { cameraY = 8; }
                         Camera.SetPosition(cameraX, cameraY);
                         Camera.Apply();
                         ticks += 1;
                     }
                 }
                 """;
    }
}
