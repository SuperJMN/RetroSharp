namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Sdk.Plugins.Platformer2D;
using Xunit;

public sealed class RealPlatformer2DPluginTests
{
    private const string Source =
        """
        import RetroSharp.Platformer2D;

        void Main()
        {
            Platformer.GroundProbe();
        }
        """;

    [Fact]
    public void Game_boy_compiles_real_platformer2d_plugin_ground_probe_through_hook()
    {
        var registry = SdkPluginRegistry.Empty.Register(Platformer2DPlugin.Create());

        var rom = GameBoyRomCompiler.CompileSource(Source, sdkPluginRegistry: registry);

        Assert.Equal(32 * 1024, rom.Length);
    }

    [Fact]
    public void Nes_rejects_real_platformer2d_plugin_ground_probe_without_target_hook()
    {
        var registry = SdkPluginRegistry.Empty.Register(Platformer2DPlugin.Create());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            NesRomCompiler.CompileSource(Source, sdkPluginRegistry: registry));

        Assert.Equal(
            "Target 'nes' does not support SDK plugin feature 'RetroSharp.Platformer2D.GroundProbe' on extern function 'platformer2d_ground_probe'.",
            exception.Message);
    }

    [Fact]
    public void Ground_probe_facade_is_unavailable_without_registering_the_plugin()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            GameBoyRomCompiler.CompileSource(Source));

        Assert.Contains("RetroSharp.Platformer2D", exception.Message, StringComparison.Ordinal);
    }
}
