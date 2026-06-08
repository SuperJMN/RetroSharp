namespace RetroSharp.Core.Targeting;

public static class TargetCapabilityChecks
{
    public static void RequireScrollAxis(Target2DCapabilities capabilities, ScrollAxes requestedAxis)
    {
        if (capabilities.SupportsScrollAxis(requestedAxis))
        {
            return;
        }

        throw new InvalidOperationException(
            TargetCapabilityErrorFormatter.UnsupportedFeature(
                capabilities,
                FormatScrollAxis(requestedAxis),
                []));
    }

    public static void RequireSpriteTransform(Target2DCapabilities capabilities, SpriteTransform requestedTransform)
    {
        if (requestedTransform == SpriteTransform.None || capabilities.SupportsSpriteTransform(requestedTransform))
        {
            return;
        }

        throw new InvalidOperationException(
            TargetCapabilityErrorFormatter.UnsupportedFeature(
                capabilities,
                FormatSpriteTransform(requestedTransform),
                []));
    }

    public static void RequireHudMode(Target2DCapabilities capabilities, HudMode requestedMode)
    {
        if (capabilities.SupportsHudMode(requestedMode))
        {
            return;
        }

        var alternatives = SupportedHudModeNames(capabilities.HudModes)
            .Where(mode => mode.Mode != requestedMode)
            .Select(mode => mode.Name)
            .Append("disable HUD");

        throw new InvalidOperationException(
            TargetCapabilityErrorFormatter.UnsupportedFeature(
                capabilities,
                FormatHudMode(requestedMode),
                alternatives));
    }

    private static IEnumerable<(HudMode Mode, string Name)> SupportedHudModeNames(HudMode supportedModes)
    {
        if (supportedModes.HasFlag(HudMode.Window))
        {
            yield return (HudMode.Window, FormatHudMode(HudMode.Window));
        }

        if (supportedModes.HasFlag(HudMode.SplitScroll))
        {
            yield return (HudMode.SplitScroll, FormatHudMode(HudMode.SplitScroll));
        }

        if (supportedModes.HasFlag(HudMode.Sprite))
        {
            yield return (HudMode.Sprite, FormatHudMode(HudMode.Sprite));
        }
    }

    private static string FormatHudMode(HudMode mode)
    {
        return mode switch
        {
            HudMode.Window => "Window HUD",
            HudMode.SplitScroll => "SplitScroll HUD",
            HudMode.Sprite => "SpriteHud",
            _ => mode.ToString(),
        };
    }

    private static string FormatScrollAxis(ScrollAxes axis)
    {
        return axis switch
        {
            ScrollAxes.Horizontal => "horizontal scrolling",
            ScrollAxes.Vertical => "vertical scrolling",
            _ => $"{axis} scrolling",
        };
    }

    private static string FormatSpriteTransform(SpriteTransform transform)
    {
        return transform switch
        {
            SpriteTransform.FlipX => "sprite FlipX",
            SpriteTransform.FlipY => "sprite FlipY",
            _ => $"sprite transform {transform}",
        };
    }
}
