namespace RetroSharp.Core.Tests;

using RetroSharp.Core.Sdk;
using Xunit;

public sealed class PlatformAssetPathResolverTests
{
    [Fact]
    public void Resolves_target_variant_for_arbitrary_asset_extensions()
    {
        var directory = CreateTempDirectory();
        var fallback = Path.Combine(directory, "song.vgz");
        var nes = Path.Combine(directory, "song.nes.vgz");
        File.WriteAllText(fallback, "fallback");
        File.WriteAllText(nes, "nes");

        var resolved = PlatformAssetPathResolver.ResolveVariant(fallback, "nes");

        Assert.Equal(nes, resolved);
    }

    [Fact]
    public void Falls_back_to_original_asset_when_target_variant_is_absent()
    {
        var directory = CreateTempDirectory();
        var fallback = Path.Combine(directory, "song.vgm");
        File.WriteAllText(fallback, "fallback");

        var resolved = PlatformAssetPathResolver.ResolveVariant(fallback, "gb");

        Assert.Equal(fallback, resolved);
    }

    [Fact]
    public void Png_variant_resolution_routes_through_generic_resolver()
    {
        var directory = CreateTempDirectory();
        var fallback = Path.Combine(directory, "sprite.png");
        var gameBoy = Path.Combine(directory, "sprite.gb.png");
        File.WriteAllText(fallback, "fallback");
        File.WriteAllText(gameBoy, "gb");

        var resolved = PlatformAssetPathResolver.ResolvePngVariant(fallback, "gb");

        Assert.Equal(gameBoy, resolved);
    }

    [Fact]
    public void Strips_existing_target_suffix_before_looking_for_current_platform()
    {
        var directory = CreateTempDirectory();
        var requested = Path.Combine(directory, "song.gb.vgm");
        var nes = Path.Combine(directory, "song.nes.vgm");
        File.WriteAllText(requested, "gb");
        File.WriteAllText(nes, "nes");

        var resolved = PlatformAssetPathResolver.ResolveVariant(requested, "nes");

        Assert.Equal(nes, resolved);
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "retrosharp-asset-resolver-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
