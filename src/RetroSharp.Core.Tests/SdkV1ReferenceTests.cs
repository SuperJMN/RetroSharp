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

        Assert.Contains("Target 'nes': vertical camera movement is not supported on NES yet; see docs/CameraVerticalScrollRoadmap.md before enabling NES vertical scroll.", reference, StringComparison.Ordinal);
        Assert.Contains("Target 'nes' does not support Window HUD", reference, StringComparison.Ordinal);
    }

    private static readonly string[] RequiredSignatures =
    [
        "video.Init()",
        "video.WaitVBlank()",
        "input.Poll()",
        "button_down(button)",
        "button_just_pressed(button)",
        "button_just_released(button)",
        "button_hold_ticks(button)",
        "audio.Init()",
        "music.Asset(name, path)",
        "music.Play(name)",
        "music.Stop()",
        "audio.Update()",
        "palette.Background(slot, c0, c1, c2, c3)",
        "palette.Sprite(slot, c0, c1, c2, c3)",
        "world.Column(index, tile0, tile1, ...)",
        "world.Flags(index, flags0, flags1, ...)",
        "world.Map(width, streamY, height)",
        "camera.Init(mapWidth, streamY, streamHeight)",
        "camera.SetPosition(x, y)",
        "camera.Apply()",
        "sprite.Asset(name, path[, frameWidth, frameHeight])",
        "sprite_width(name)",
        "sprite.Draw(name, x, y, frame[, flipX[, paletteSlot]])",
        "animation.Clip(name, firstFrame, duration...)",
        "animation.Frame(name, tick)",
        "world_tile_flags_at(worldX, worldY)",
        "collision_aabb_tiles(x, y, width, height, flags)",
        "camera.AabbTiles(screenX, worldY, width, height, flags)",
        "camera.AabbHitTop(screenX, worldY, width, height, flags)",
        "hud.SetTile(mode, x, y, tile)",
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
