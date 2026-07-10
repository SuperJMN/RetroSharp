namespace RetroSharp.NES.Tests;

using RetroSharp.NES;
using Xunit;

public sealed class NesLargeWorldCameraTests
{
    [Fact]
    public void Camera_accepts_logical_map_width_above_byte_range()
    {
        var rom = NesRomCompiler.CompileSource(WideCameraSource(256));

        Assert.Equal(40976, rom.Length);
    }

    [Fact]
    public void Camera_emits_word_movement_and_column_256_addressing_from_255_to_256()
    {
        var prg = Prg(NesRomCompiler.CompileSource(WideCameraSource(1888)));

        Assert.True(ContainsSequence(prg, [0xA9, 0x60, 0x85, 0x00, 0xA9, 0x07, 0x85, 0x01]));
        Assert.True(ContainsSequence(prg, [0xAD, 0x18, 0x03, 0xC5, 0xE9]));
        Assert.True(ContainsSequence(prg, [0xAD, 0x1A, 0x03, 0x8D, 0x1E, 0x03]));
        Assert.True(ContainsSequence(prg, [0xAD, 0x1E, 0x03, 0x8D, 0x21, 0x03]));
        Assert.True(ContainsSequence(prg, [0x6D, 0x1E, 0x03, 0x85, 0xE9, 0xA0, 0x00, 0xB1, 0xE8]));
        Assert.True(ContainsSequence(prg, [0xA5, 0xE0, 0x8D, 0x05, 0x20]));
    }

    [Fact]
    public void Camera_emits_word_movement_and_column_256_addressing_from_256_to_255()
    {
        var prg = Prg(NesRomCompiler.CompileSource(WideCameraSource(2055)));

        Assert.True(ContainsSequence(prg, [0xA9, 0x07, 0x85, 0x00, 0xA9, 0x08, 0x85, 0x01]));
        Assert.True(ContainsSequence(prg, [0xAD, 0x1A, 0x03, 0x38, 0xE9, 0x01, 0x8D, 0x1A, 0x03, 0xC6, 0xE1]));
        Assert.True(ContainsSequence(prg, [0xAD, 0x1A, 0x03, 0x8D, 0x1E, 0x03]));
        Assert.True(ContainsSequence(prg, [0xAD, 0x1E, 0x03, 0x8D, 0x21, 0x03]));
        Assert.True(ContainsSequence(prg, [0x6D, 0x1E, 0x03, 0x85, 0xE9, 0xA0, 0x00, 0xB1, 0xE8]));
        Assert.True(ContainsSequence(prg, [0xA5, 0xE0, 0x8D, 0x05, 0x20]));
    }

    private static string WideCameraSource(int requestedX)
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
                     Camera.SetPosition(cameraX, 0);
                     Camera.Apply();
                 }
                 """;
    }

    private static byte[] Prg(byte[] rom) => rom.Skip(16).Take(32 * 1024).ToArray();

    private static bool ContainsSequence(byte[] bytes, byte[] sequence)
    {
        for (var i = 0; i <= bytes.Length - sequence.Length; i++)
        {
            if (bytes.AsSpan(i, sequence.Length).SequenceEqual(sequence))
            {
                return true;
            }
        }

        return false;
    }
}
