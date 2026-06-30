namespace RetroSharp.Core.Tests;

using RetroSharp.Core.Sdk;
using Xunit;

public sealed class SdkModuleRegistryTests
{
    [Fact]
    public void Known_sdk_modules_are_declared_as_library_modules()
    {
        var video = SdkModuleRegistry.FindModule("video");

        Assert.NotNull(video);
        Assert.Equal(SdkModuleKind.Library, video.Kind);
        Assert.Equal("video", video.CallPrefix);
        Assert.Equal("video_wait_vblank", video.ResolveCallName("WaitVBlank"));
    }

    [Theory]
    [InlineData("Video", "video")]
    [InlineData("Input", "input")]
    [InlineData("Camera", "camera")]
    [InlineData("Sprite", "sprite")]
    [InlineData("Palette", "palette")]
    [InlineData("Tilemap", "tilemap")]
    [InlineData("Map", "map")]
    [InlineData("World", "world")]
    [InlineData("Hud", "hud")]
    [InlineData("Scroll", "scroll")]
    [InlineData("Animation", "animation")]
    [InlineData("Audio", "audio")]
    [InlineData("Music", "music")]
    public void Pascalcase_facade_is_alias_of_lowercase_module(string pascalCase, string lowercase)
    {
        var facade = SdkModuleRegistry.FindModule(pascalCase);

        Assert.NotNull(facade);
        Assert.Equal(SdkModuleKind.Library, facade.Kind);

        var lower = SdkModuleRegistry.FindModule(lowercase);
        Assert.NotNull(lower);

        Assert.Equal(lower.CallPrefix, facade.CallPrefix);
        Assert.Equal(lower.ResolveCallName("Draw"), facade.ResolveCallName("Draw"));
    }

    [Fact]
    public void Target_intrinsic_catalog_exposes_declared_intrinsics()
    {
        var catalog = new TargetIntrinsicCatalog(
            "gb",
            "Game Boy",
            [
                TargetIntrinsicDescriptor.WaitFrame("wait_frame", arity: 0),
                TargetIntrinsicDescriptor.PollInput("poll_input", arity: 0),
            ]);

        Assert.True(catalog.TryResolve("wait_frame", out var waitFrame));
        Assert.Equal(TargetIntrinsicOperation.WaitFrame, waitFrame.Operation);
        Assert.Equal(0, waitFrame.Arity);

        Assert.True(catalog.TryResolve("poll_input", out var pollInput));
        Assert.Equal(TargetIntrinsicOperation.PollInput, pollInput.Operation);
        Assert.Equal(0, pollInput.Arity);
    }
}
