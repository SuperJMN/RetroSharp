namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Targeting;
using RetroSharp.GameBoy;
using Xunit;

public sealed class GameBoyCapabilityErrorTests
{
    [Fact]
    public void Hud_capability_check_uses_consistent_error_text()
    {
        TargetCapabilityChecks.RequireHudMode(GameBoyTarget.Capabilities, HudMode.Window);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            TargetCapabilityChecks.RequireHudMode(GameBoyTarget.Capabilities, HudMode.SplitScroll));

        Assert.Equal(
            "Target 'gb' does not support SplitScroll HUD. Use Window HUD, SpriteHud, or disable HUD for this target.",
            exception.Message);
    }
}
