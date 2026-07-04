namespace RetroSharp.Core.Tests;

using RetroSharp.Core.Sdk;
using Xunit;

public sealed class TargetIntrinsicCatalogTests
{
    [Fact]
    public void Target_intrinsic_catalog_exposes_declared_intrinsics()
    {
        var catalog = new TargetIntrinsicCatalog(
            "gb",
            "Game Boy",
            [
                TargetIntrinsicDescriptor.WaitFrame("wait_frame", arity: 0),
                TargetIntrinsicDescriptor.PresentVideo("video_present", arity: 0),
                TargetIntrinsicDescriptor.PollInput("poll_input", arity: 0),
            ]);

        Assert.True(catalog.TryResolve("wait_frame", out var waitFrame));
        Assert.Equal("wait_frame", waitFrame.IntrinsicId);
        Assert.Equal(TargetIntrinsicOperation.WaitFrame, waitFrame.Operation);
        Assert.Equal(TargetIntrinsicReturnKind.Void, waitFrame.ReturnKind);
        Assert.Equal(0, waitFrame.Arity);
        Assert.Empty(waitFrame.RequiredCapabilities);

        Assert.True(catalog.TryResolve("poll_input", out var pollInput));
        Assert.Equal(TargetIntrinsicOperation.PollInput, pollInput.Operation);
        Assert.Equal(0, pollInput.Arity);

        Assert.True(catalog.TryResolve("video_present", out var present));
        Assert.Equal(TargetIntrinsicOperation.PresentVideo, present.Operation);
        Assert.Equal(TargetIntrinsicReturnKind.Void, present.ReturnKind);
        Assert.Equal(0, present.Arity);
    }

    [Fact]
    public void Input_value_intrinsics_are_described_without_sdk_facade_names()
    {
        var buttonDown = TargetIntrinsicDescriptor.ButtonDown("button_down", arity: 1);
        Assert.Equal(TargetIntrinsicOperation.ButtonDown, buttonDown.Operation);
        Assert.Equal(TargetIntrinsicReturnKind.Bool, buttonDown.ReturnKind);
        Assert.Equal(1, buttonDown.RuntimeArity);
        Assert.Empty(buttonDown.CompileTimeOperands);

        var holdTicks = TargetIntrinsicDescriptor.ButtonHoldTicks("button_hold_ticks", arity: 1);
        Assert.Equal(TargetIntrinsicOperation.ButtonHoldTicks, holdTicks.Operation);
        Assert.Equal(TargetIntrinsicReturnKind.I16, holdTicks.ReturnKind);
        Assert.Equal(1, holdTicks.RuntimeArity);
        Assert.Empty(holdTicks.CompileTimeOperands);
    }

    [Fact]
    public void Intrinsic_extern_can_declare_compile_time_operand()
    {
        var descriptor = TargetIntrinsicDescriptor.ReadWorldTileFlags(
            "world_tile_flags_for_world",
            runtimeArity: 2,
            compileTimeOperands: [new TargetIntrinsicCompileTimeOperand(0, TargetIntrinsicOperandRole.WorldId)]);

        Assert.Equal("world_tile_flags_for_world", descriptor.Name);
        Assert.Equal("world_tile_flags_for_world", descriptor.IntrinsicId);
        Assert.Equal(TargetIntrinsicOperation.ReadWorldTileFlags, descriptor.Operation);
        Assert.Equal(TargetIntrinsicReturnKind.I16, descriptor.ReturnKind);
        Assert.Equal(2, descriptor.RuntimeArity);
        Assert.Equal(3, descriptor.Arity);
        Assert.Contains(TargetIntrinsicCapabilityRequirement.WorldTileFlags, descriptor.RequiredCapabilities);
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
        Assert.Equal(TargetIntrinsicReturnKind.Void, descriptor.ReturnKind);
        Assert.Contains(TargetIntrinsicCapabilityRequirement.LogicalSprites, descriptor.RequiredCapabilities);
        Assert.Contains(descriptor.CompileTimeOperands, operand => operand.Role == TargetIntrinsicOperandRole.AssetRef);
        Assert.Contains(descriptor.CompileTimeOperands, operand => operand.Role == TargetIntrinsicOperandRole.ConstPaletteSlot);
    }

    [Theory]
    [InlineData("sprite_width", TargetIntrinsicOperation.ReadSpriteWidth, TargetIntrinsicReturnKind.I16, 0)]
    [InlineData("animation_frame", TargetIntrinsicOperation.ReadAnimationFrame, TargetIntrinsicReturnKind.I16, 1)]
    public void Complex_value_intrinsics_can_be_declared_with_compile_time_asset_operands(
        string intrinsicId,
        TargetIntrinsicOperation operation,
        TargetIntrinsicReturnKind returnKind,
        int runtimeArity)
    {
        var descriptor = operation switch
        {
            TargetIntrinsicOperation.ReadSpriteWidth => TargetIntrinsicDescriptor.ReadSpriteWidth(
                intrinsicId,
                runtimeArity,
                [new TargetIntrinsicCompileTimeOperand(0, TargetIntrinsicOperandRole.AssetRef)]),
            TargetIntrinsicOperation.ReadAnimationFrame => TargetIntrinsicDescriptor.ReadAnimationFrame(
                intrinsicId,
                runtimeArity,
                [new TargetIntrinsicCompileTimeOperand(0, TargetIntrinsicOperandRole.AssetRef)]),
            _ => throw new InvalidOperationException()
        };

        Assert.Equal(operation, descriptor.Operation);
        Assert.Equal(returnKind, descriptor.ReturnKind);
        Assert.Equal(runtimeArity, descriptor.RuntimeArity);
        var operand = Assert.Single(descriptor.CompileTimeOperands);
        Assert.Equal(0, operand.Slot);
        Assert.Equal(TargetIntrinsicOperandRole.AssetRef, operand.Role);
    }

    [Theory]
    [InlineData("sprite_asset", SdkResourceDeclarationKind.SpriteAsset)]
    [InlineData("world_load", SdkResourceDeclarationKind.WorldLoad)]
    [InlineData("music_asset", SdkResourceDeclarationKind.MusicAsset)]
    [InlineData("sfx_asset", SdkResourceDeclarationKind.SoundEffectAsset)]
    [InlineData("palette_background", SdkResourceDeclarationKind.BackgroundPalette)]
    [InlineData("palette_sprite", SdkResourceDeclarationKind.SpritePalette)]
    [InlineData("animation_clip", SdkResourceDeclarationKind.AnimationClip)]
    public void Resource_declaration_descriptors_map_package_contract_ids(string resourceId, SdkResourceDeclarationKind kind)
    {
        Assert.True(SdkResourceDeclarationDescriptor.TryCreate(resourceId, out var descriptor));
        Assert.Equal(resourceId, descriptor.ResourceId);
        Assert.Equal(kind, descriptor.Kind);
    }

    [Fact]
    public void Sfx_play_intrinsic_carries_compile_time_asset_operand()
    {
        var descriptor = TargetIntrinsicDescriptor.PlaySoundEffect(
            "sfx_play",
            runtimeArity: 0,
            [new TargetIntrinsicCompileTimeOperand(0, TargetIntrinsicOperandRole.AssetRef)]);

        Assert.Equal(TargetIntrinsicOperation.PlaySoundEffect, descriptor.Operation);
        Assert.Equal(TargetIntrinsicReturnKind.Void, descriptor.ReturnKind);
        Assert.Contains(TargetIntrinsicCapabilityRequirement.SoundEffects, descriptor.RequiredCapabilities);
        var operand = Assert.Single(descriptor.CompileTimeOperands);
        Assert.Equal(0, operand.Slot);
        Assert.Equal(TargetIntrinsicOperandRole.AssetRef, operand.Role);
    }
}
