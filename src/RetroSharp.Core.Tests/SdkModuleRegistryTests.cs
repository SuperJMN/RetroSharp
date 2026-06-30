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
