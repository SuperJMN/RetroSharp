namespace RetroSharp.Core.Tests;

using Xunit;

public sealed class SdkV1ReferenceTests
{
    [Fact]
    public void Readme_links_to_the_portable_sdk_v1_reference()
    {
        var readme = File.ReadAllText(RepositoryFile("README.md"));

        Assert.Contains("docs/Portable2DSdkV1.md", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void Portable_sdk_v1_reference_documents_the_public_contract()
    {
        var reference = File.ReadAllText(RepositoryFile("docs/Portable2DSdkV1.md"));

        Assert.Contains("# Portable 2D SDK v1 Reference", reference, StringComparison.Ordinal);
        Assert.Contains("## SDK v1 Surface", reference, StringComparison.Ordinal);
        Assert.Contains("## Capability Requirements", reference, StringComparison.Ordinal);
        Assert.Contains("## Target Support", reference, StringComparison.Ordinal);
        Assert.Contains("## Failure Modes", reference, StringComparison.Ordinal);
        Assert.Contains("## Minimal Game Boy/NES Example", reference, StringComparison.Ordinal);

        foreach (var signature in RequiredSignatures)
        {
            Assert.Contains(signature, reference, StringComparison.Ordinal);
        }

        Assert.Contains("Target 'nes' supports only horizontal camera_set_position(x, 0) in the current camera spike.", reference, StringComparison.Ordinal);
        Assert.Contains("Target 'nes' does not support Window HUD", reference, StringComparison.Ordinal);
    }

    private static readonly string[] RequiredSignatures =
    [
        "video_init()",
        "video_wait_vblank()",
        "input_poll()",
        "button_down(button)",
        "button_just_pressed(button)",
        "button_just_released(button)",
        "button_hold_ticks(button)",
        "world_column(index, tile0, tile1, ...)",
        "world_flags(index, flags0, flags1, ...)",
        "world_map(width, streamY, height)",
        "camera_init(mapWidth, streamY, streamHeight)",
        "camera_set_position(x, y)",
        "camera_apply()",
        "sprite_asset(name, path[, frameWidth, frameHeight])",
        "sprite_width(name)",
        "sprite_draw(name, x, y, frame[, flipX[, paletteSlot]])",
        "animation_clip(name, firstFrame, duration...)",
        "animation_frame(name, tick)",
        "world_tile_flags_at(worldX, worldY)",
        "collision_aabb_tiles(x, y, width, height, flags)",
        "hud_set_tile(mode, x, y, tile)",
    ];

    private static string RepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not find repository file '{relativePath}'.");
    }
}
