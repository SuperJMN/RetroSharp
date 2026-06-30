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
    [InlineData("Input", "IsDown", "button_down")]
    [InlineData("Input", "WasPressed", "button_just_pressed")]
    [InlineData("Input", "WasReleased", "button_just_released")]
    [InlineData("Input", "HoldTicks", "button_hold_ticks")]
    [InlineData("input", "IsDown", "button_down")]
    [InlineData("input", "WasReleased", "button_just_released")]
    public void Input_predicate_methods_resolve_to_button_builtins(string module, string method, string expected)
    {
        Assert.True(SdkModuleRegistry.TryResolveCallName(module, method, out var callName));
        Assert.Equal(expected, callName);
    }

    [Theory]
    [InlineData("Sprite", "Width", "sprite_width")]
    [InlineData("sprite", "Width", "sprite_width")]
    public void Sprite_width_resolves_to_sprite_width_builtin(string module, string method, string expected)
    {
        Assert.True(SdkModuleRegistry.TryResolveCallName(module, method, out var callName));
        Assert.Equal(expected, callName);
    }

    [Fact]
    public void Input_poll_still_resolves_through_the_facade_prefix()
    {
        Assert.True(SdkModuleRegistry.TryResolveCallName("Input", "Poll", out var callName));
        Assert.Equal("input_poll", callName);
    }

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

    [Fact]
    public void Intrinsic_extern_can_declare_compile_time_operand()
    {
        var descriptor = TargetIntrinsicDescriptor.ReadWorldTileFlags(
            "world_tile_flags_for_world",
            runtimeArity: 2,
            compileTimeOperands: [new TargetIntrinsicCompileTimeOperand(0, TargetIntrinsicOperandRole.WorldId)]);

        Assert.Equal("world_tile_flags_for_world", descriptor.Name);
        Assert.Equal(TargetIntrinsicOperation.ReadWorldTileFlags, descriptor.Operation);
        Assert.Equal(2, descriptor.RuntimeArity);
        Assert.Equal(3, descriptor.Arity);
        var operand = Assert.Single(descriptor.CompileTimeOperands);
        Assert.Equal(0, operand.Slot);
        Assert.Equal(TargetIntrinsicOperandRole.WorldId, operand.Role);
    }

    [Fact]
    public void Compile_time_operand_is_modeled_as_descriptor_role_not_language_generic()
    {
        var descriptor = TargetIntrinsicDescriptor.DrawLogicalSprite(
            "sprite_draw",
            runtimeArity: 4,
            compileTimeOperands:
            [
                new TargetIntrinsicCompileTimeOperand(0, TargetIntrinsicOperandRole.AssetRef),
                new TargetIntrinsicCompileTimeOperand(5, TargetIntrinsicOperandRole.ConstPaletteSlot),
            ]);

        Assert.Equal(TargetIntrinsicOperation.DrawLogicalSprite, descriptor.Operation);
        Assert.Contains(descriptor.CompileTimeOperands, operand => operand.Role == TargetIntrinsicOperandRole.AssetRef);
        Assert.Contains(descriptor.CompileTimeOperands, operand => operand.Role == TargetIntrinsicOperandRole.ConstPaletteSlot);
    }
}
