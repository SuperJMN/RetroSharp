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

        Assert.Contains("larger source-authored worlds stream one exposed column or row per VBlank", reference, StringComparison.Ordinal);
        Assert.Contains("Target 'nes' does not support Window HUD", reference, StringComparison.Ordinal);
    }

    [Fact]
    public void Sdk_architecture_doc_names_public_package_and_internal_compiler_model()
    {
        var architecture = File.ReadAllText(RepositoryFile("docs/SdkArchitecture.md"));

        Assert.Contains("# RetroSharp SDK Architecture", architecture, StringComparison.Ordinal);
        Assert.Contains("`sdk/RetroSharp.Portable2D`", architecture, StringComparison.Ordinal);
        Assert.Contains("source-only package", architecture, StringComparison.Ordinal);
        Assert.Contains("`RetroSharp.Sdk.Frontend`", architecture, StringComparison.Ordinal);
        Assert.Contains("`RetroSharp.Core.Sdk`", architecture, StringComparison.Ordinal);
        Assert.Contains("internal compiler model", architecture, StringComparison.Ordinal);
        Assert.Contains("not a compiler plugin ABI", architecture, StringComparison.Ordinal);
        Assert.Contains("does not add new `Sdk2DOperation` records", architecture, StringComparison.Ordinal);
    }

    [Fact]
    public void Portable2d_package_readme_documents_source_only_library_contract()
    {
        var readme = File.ReadAllText(RepositoryFile("sdk/RetroSharp.Portable2D/README.md"));

        Assert.Contains("# RetroSharp.Portable2D", readme, StringComparison.Ordinal);
        Assert.Contains("source-only", readme, StringComparison.Ordinal);
        Assert.Contains("retrosharp-library.json", readme, StringComparison.Ordinal);
        Assert.Contains("`libraries`", readme, StringComparison.Ordinal);
        Assert.Contains("`--lib-path`", readme, StringComparison.Ordinal);
        Assert.Contains("does not ship a binary ABI", readme, StringComparison.Ordinal);
        Assert.Contains("docs/SdkArchitecture.md", readme, StringComparison.Ordinal);
    }

    private static readonly string[] RequiredSignatures =
    [
        "Video.Init()",
        "Video.WaitVBlank()",
        "Input.Poll()",
        "Input.IsDown(button)",
        "Input.WasPressed(button)",
        "Input.WasReleased(button)",
        "Input.HoldTicks(button)",
        "Audio.Init()",
        "Music.Asset(name, path)",
        "Music.Play(name)",
        "Music.Stop()",
        "Audio.Update()",
        "Palette.Background(slot, c0, c1, c2, c3)",
        "Palette.Sprite(slot, c0, c1, c2, c3)",
        "World.Column(index, tile0, tile1, ...)",
        "World.Flags(index, flags0, flags1, ...)",
        "World.Map(width, streamY, height)",
        "Camera.Init(mapWidth, streamY, streamHeight)",
        "Camera.SetPosition(x, y)",
        "Camera.Apply()",
        "Sprite.Asset(name, path[, frameWidth, frameHeight])",
        "Sprite.Width(name)",
        "Sprite.Draw(name, x, y, frame[, flipX[, paletteSlot]])",
        "Animation.Clip(name, firstFrame, duration...)",
        "Animation.Frame(name, tick)",
        "Projectiles.Pool(name, hero: n, enemy: n, requests: n, offscreenMargin: n)",
        "Projectiles.Def(name, team: Hero|Enemy, sprite: asset, speedX: n, speedY: n, damage: n, lifetime: n, hitboxWidth: n, hitboxHeight: n[, behavior: Linear|GravityArc])",
        "pool.Request(kind, x, y, direction[, result[, owner]])",
        "pool.ProcessRequests()",
        "pool.TouchActors(actorPool)",
        "pool.TouchHero(playerX, playerY, playerWidth, playerHeight, damageTarget)",
        "world_tile_flags_at(worldX, worldY)",
        "collision_aabb_tiles(x, y, width, height, flags)",
        "Camera.AabbTiles(screenX, worldY, width, height, flags)",
        "Camera.AabbHitTop(screenX, worldY, width, height, flags)",
        "Hud.SetTile(mode, x, y, tile)",
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
