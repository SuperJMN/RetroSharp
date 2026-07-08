namespace RetroSharp.Core.Sdk;

public enum TargetIntrinsicOperation
{
    InitializeVideo,
    PresentVideo,
    WaitFrame,
    PollInput,
    ButtonDown,
    ButtonJustPressed,
    ButtonJustReleased,
    ButtonHoldTicks,
    InitializeAudio,
    UpdateAudio,
    PlayMusic,
    PlaySoundEffect,
    StopMusic,
    ReadWorldTileFlags,
    InitializeCamera,
    CameraAabbTiles,
    CameraAabbHitTop,
    CameraScreenAabbTiles,
    CameraScreenAabbHitTop,
    SetCameraPosition,
    ApplyCamera,
    DrawLogicalSprite,
    ReadSpriteWidth,
    ReadAnimationFrame,
}

public enum TargetIntrinsicReturnKind
{
    Void,
    Bool,
    I16,
}

public enum TargetIntrinsicOperandRole
{
    AssetRef,
    ConstPaletteSlot,
    EnumFlags,
    WorldId,
}

public enum TargetIntrinsicCapabilityRequirement
{
    BackgroundMusic,
    SoundEffects,
    LogicalSprites,
    WorldTileFlags,
    CameraRelativeAabb,
    CameraRelativeAabbHitTop,
}

public sealed record TargetIntrinsicCompileTimeOperand(int Slot, TargetIntrinsicOperandRole Role);

public sealed record TargetIntrinsicDescriptor
{
    public TargetIntrinsicDescriptor(
        string intrinsicId,
        TargetIntrinsicOperation operation,
        TargetIntrinsicReturnKind returnKind,
        int runtimeArity,
        IEnumerable<TargetIntrinsicCompileTimeOperand>? compileTimeOperands = null,
        IEnumerable<TargetIntrinsicCapabilityRequirement>? requiredCapabilities = null)
    {
        if (runtimeArity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(runtimeArity), "Runtime arity cannot be negative.");
        }

        IntrinsicId = intrinsicId;
        Name = intrinsicId;
        BuiltInOperation = operation;
        ReturnKind = returnKind;
        RuntimeArity = runtimeArity;
        CompileTimeOperands = (compileTimeOperands ?? []).OrderBy(operand => operand.Slot).ToArray();
        RequiredCapabilities = (requiredCapabilities ?? []).Distinct().Order().ToArray();
        RequiredPluginCapabilities = [];
        PluginValidators = [];

        var duplicate = CompileTimeOperands
            .GroupBy(operand => operand.Slot)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Intrinsic '{intrinsicId}' declares compile-time operand slot {duplicate.Key + 1} more than once.");
        }
    }

    private TargetIntrinsicDescriptor(
        SdkPluginOperationDescriptor operation,
        SdkPluginTargetLoweringDescriptor loweringHook,
        IEnumerable<SdkPluginValidatorDescriptor> validators)
    {
        IntrinsicId = operation.OperationId;
        Name = operation.OperationId;
        ReturnKind = operation.ReturnKind;
        RuntimeArity = operation.RuntimeArity;
        CompileTimeOperands = operation.CompileTimeOperands;
        RequiredCapabilities = [];
        RequiredPluginCapabilities = operation.RequiredCapabilities;
        PluginOperation = operation;
        PluginTargetLowering = loweringHook;
        PluginValidators = validators.ToArray();
    }

    public string IntrinsicId { get; }
    public string Name { get; }
    public TargetIntrinsicOperation Operation =>
        BuiltInOperation ?? throw new InvalidOperationException($"SDK plugin intrinsic '{IntrinsicId}' has no built-in target intrinsic operation.");

    public TargetIntrinsicOperation? BuiltInOperation { get; }
    public TargetIntrinsicReturnKind ReturnKind { get; }
    public int RuntimeArity { get; }
    public IReadOnlyList<TargetIntrinsicCompileTimeOperand> CompileTimeOperands { get; }
    public IReadOnlyList<TargetIntrinsicCapabilityRequirement> RequiredCapabilities { get; }
    public IReadOnlyList<string> RequiredPluginCapabilities { get; }
    public SdkPluginOperationDescriptor PluginOperation { get; } = null!;
    public SdkPluginTargetLoweringDescriptor PluginTargetLowering { get; } = null!;
    public IReadOnlyList<SdkPluginValidatorDescriptor> PluginValidators { get; }
    public bool IsPluginOperation => PluginOperation is not null;
    public int Arity => RuntimeArity + CompileTimeOperands.Count;

    public static TargetIntrinsicDescriptor FromPluginOperation(
        SdkPluginOperationDescriptor operation,
        SdkPluginTargetLoweringDescriptor loweringHook,
        IEnumerable<SdkPluginValidatorDescriptor> validators)
    {
        return new TargetIntrinsicDescriptor(operation, loweringHook, validators);
    }

    public static TargetIntrinsicDescriptor WaitFrame(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.WaitFrame, TargetIntrinsicReturnKind.Void, arity);
    }

    public static TargetIntrinsicDescriptor InitializeVideo(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.InitializeVideo, TargetIntrinsicReturnKind.Void, arity);
    }

    public static TargetIntrinsicDescriptor PresentVideo(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.PresentVideo, TargetIntrinsicReturnKind.Void, arity);
    }

    public static TargetIntrinsicDescriptor PollInput(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.PollInput, TargetIntrinsicReturnKind.Void, arity);
    }

    public static TargetIntrinsicDescriptor ButtonDown(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.ButtonDown, TargetIntrinsicReturnKind.Bool, arity);
    }

    public static TargetIntrinsicDescriptor ButtonJustPressed(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.ButtonJustPressed, TargetIntrinsicReturnKind.Bool, arity);
    }

    public static TargetIntrinsicDescriptor ButtonJustReleased(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.ButtonJustReleased, TargetIntrinsicReturnKind.Bool, arity);
    }

    public static TargetIntrinsicDescriptor ButtonHoldTicks(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.ButtonHoldTicks, TargetIntrinsicReturnKind.I16, arity);
    }

    public static TargetIntrinsicDescriptor InitializeAudio(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(
            name,
            TargetIntrinsicOperation.InitializeAudio,
            TargetIntrinsicReturnKind.Void,
            arity,
            requiredCapabilities: [TargetIntrinsicCapabilityRequirement.BackgroundMusic]);
    }

    public static TargetIntrinsicDescriptor UpdateAudio(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(
            name,
            TargetIntrinsicOperation.UpdateAudio,
            TargetIntrinsicReturnKind.Void,
            arity,
            requiredCapabilities: [TargetIntrinsicCapabilityRequirement.BackgroundMusic]);
    }

    public static TargetIntrinsicDescriptor PlayMusic(
        string name,
        int runtimeArity,
        IEnumerable<TargetIntrinsicCompileTimeOperand> compileTimeOperands)
    {
        return new TargetIntrinsicDescriptor(
            name,
            TargetIntrinsicOperation.PlayMusic,
            TargetIntrinsicReturnKind.Void,
            runtimeArity,
            compileTimeOperands,
            [TargetIntrinsicCapabilityRequirement.BackgroundMusic]);
    }

    public static TargetIntrinsicDescriptor PlaySoundEffect(
        string name,
        int runtimeArity,
        IEnumerable<TargetIntrinsicCompileTimeOperand> compileTimeOperands)
    {
        return new TargetIntrinsicDescriptor(
            name,
            TargetIntrinsicOperation.PlaySoundEffect,
            TargetIntrinsicReturnKind.Void,
            runtimeArity,
            compileTimeOperands,
            [TargetIntrinsicCapabilityRequirement.SoundEffects]);
    }

    public static TargetIntrinsicDescriptor StopMusic(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(
            name,
            TargetIntrinsicOperation.StopMusic,
            TargetIntrinsicReturnKind.Void,
            arity,
            requiredCapabilities: [TargetIntrinsicCapabilityRequirement.BackgroundMusic]);
    }

    public static TargetIntrinsicDescriptor ReadWorldTileFlags(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(
            name,
            TargetIntrinsicOperation.ReadWorldTileFlags,
            TargetIntrinsicReturnKind.I16,
            arity,
            requiredCapabilities: [TargetIntrinsicCapabilityRequirement.WorldTileFlags]);
    }

    public static TargetIntrinsicDescriptor ReadWorldTileFlags(
        string name,
        int runtimeArity,
        IEnumerable<TargetIntrinsicCompileTimeOperand> compileTimeOperands)
    {
        return new TargetIntrinsicDescriptor(
            name,
            TargetIntrinsicOperation.ReadWorldTileFlags,
            TargetIntrinsicReturnKind.I16,
            runtimeArity,
            compileTimeOperands,
            [TargetIntrinsicCapabilityRequirement.WorldTileFlags]);
    }

    public static TargetIntrinsicDescriptor SetCameraPosition(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.SetCameraPosition, TargetIntrinsicReturnKind.Void, arity);
    }

    public static TargetIntrinsicDescriptor InitializeCamera(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.InitializeCamera, TargetIntrinsicReturnKind.Void, arity);
    }

    public static TargetIntrinsicDescriptor CameraAabbTiles(
        string name,
        int runtimeArity,
        IEnumerable<TargetIntrinsicCompileTimeOperand> compileTimeOperands)
    {
        return new TargetIntrinsicDescriptor(
            name,
            TargetIntrinsicOperation.CameraAabbTiles,
            TargetIntrinsicReturnKind.I16,
            runtimeArity,
            compileTimeOperands,
            [TargetIntrinsicCapabilityRequirement.CameraRelativeAabb]);
    }

    public static TargetIntrinsicDescriptor CameraAabbHitTop(
        string name,
        int runtimeArity,
        IEnumerable<TargetIntrinsicCompileTimeOperand> compileTimeOperands)
    {
        return new TargetIntrinsicDescriptor(
            name,
            TargetIntrinsicOperation.CameraAabbHitTop,
            TargetIntrinsicReturnKind.I16,
            runtimeArity,
            compileTimeOperands,
            [TargetIntrinsicCapabilityRequirement.CameraRelativeAabbHitTop]);
    }

    public static TargetIntrinsicDescriptor CameraScreenAabbTiles(
        string name,
        int runtimeArity,
        IEnumerable<TargetIntrinsicCompileTimeOperand> compileTimeOperands)
    {
        return new TargetIntrinsicDescriptor(
            name,
            TargetIntrinsicOperation.CameraScreenAabbTiles,
            TargetIntrinsicReturnKind.I16,
            runtimeArity,
            compileTimeOperands,
            [TargetIntrinsicCapabilityRequirement.CameraRelativeAabb]);
    }

    public static TargetIntrinsicDescriptor CameraScreenAabbHitTop(
        string name,
        int runtimeArity,
        IEnumerable<TargetIntrinsicCompileTimeOperand> compileTimeOperands)
    {
        return new TargetIntrinsicDescriptor(
            name,
            TargetIntrinsicOperation.CameraScreenAabbHitTop,
            TargetIntrinsicReturnKind.I16,
            runtimeArity,
            compileTimeOperands,
            [TargetIntrinsicCapabilityRequirement.CameraRelativeAabbHitTop]);
    }

    public static TargetIntrinsicDescriptor ApplyCamera(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.ApplyCamera, TargetIntrinsicReturnKind.Void, arity);
    }

    public static TargetIntrinsicDescriptor DrawLogicalSprite(
        string name,
        int runtimeArity,
        IEnumerable<TargetIntrinsicCompileTimeOperand> compileTimeOperands)
    {
        return new TargetIntrinsicDescriptor(
            name,
            TargetIntrinsicOperation.DrawLogicalSprite,
            TargetIntrinsicReturnKind.Void,
            runtimeArity,
            compileTimeOperands,
            [TargetIntrinsicCapabilityRequirement.LogicalSprites]);
    }

    public static TargetIntrinsicDescriptor ReadSpriteWidth(
        string name,
        int runtimeArity,
        IEnumerable<TargetIntrinsicCompileTimeOperand> compileTimeOperands)
    {
        return new TargetIntrinsicDescriptor(
            name,
            TargetIntrinsicOperation.ReadSpriteWidth,
            TargetIntrinsicReturnKind.I16,
            runtimeArity,
            compileTimeOperands,
            [TargetIntrinsicCapabilityRequirement.LogicalSprites]);
    }

    public static TargetIntrinsicDescriptor ReadAnimationFrame(
        string name,
        int runtimeArity,
        IEnumerable<TargetIntrinsicCompileTimeOperand> compileTimeOperands)
    {
        return new TargetIntrinsicDescriptor(
            name,
            TargetIntrinsicOperation.ReadAnimationFrame,
            TargetIntrinsicReturnKind.I16,
            runtimeArity,
            compileTimeOperands);
    }
}

public sealed class TargetIntrinsicCatalog
{
    private readonly Dictionary<string, TargetIntrinsicDescriptor> intrinsics;
    private readonly IReadOnlySet<string> unsupportedPluginOperationIds;

    public TargetIntrinsicCatalog(string targetId, string targetName, IEnumerable<TargetIntrinsicDescriptor> intrinsics)
        : this(targetId, targetName, intrinsics, new HashSet<string>(StringComparer.Ordinal))
    {
    }

    private TargetIntrinsicCatalog(
        string targetId,
        string targetName,
        IEnumerable<TargetIntrinsicDescriptor> intrinsics,
        IReadOnlySet<string> unsupportedPluginOperationIds)
    {
        TargetId = targetId;
        TargetName = targetName;
        this.intrinsics = intrinsics.ToDictionary(intrinsic => intrinsic.Name, StringComparer.Ordinal);
        this.unsupportedPluginOperationIds = unsupportedPluginOperationIds;
    }

    public string TargetId { get; }
    public string TargetName { get; }

    public IReadOnlyCollection<TargetIntrinsicDescriptor> Intrinsics => intrinsics.Values;

    public TargetIntrinsicCatalog WithSdkPlugins(SdkPluginRegistry registry)
    {
        return new TargetIntrinsicCatalog(
            TargetId,
            TargetName,
            intrinsics.Values.Concat(registry.TargetIntrinsicDescriptorsFor(TargetId)),
            registry.UnsupportedOperationIdsFor(TargetId));
    }

    public bool TryResolve(string name, out TargetIntrinsicDescriptor descriptor)
    {
        return intrinsics.TryGetValue(name, out descriptor!);
    }

    public TargetIntrinsicDescriptor Resolve(string name, string functionName)
    {
        if (TryResolve(name, out var descriptor))
        {
            return descriptor;
        }

        if (unsupportedPluginOperationIds.Contains(name))
        {
            throw new InvalidOperationException(
                $"Target '{TargetId}' does not support SDK plugin feature '{name}' on extern function '{functionName}'.");
        }

        throw new InvalidOperationException(
            $"Target '{TargetId}' does not support intrinsic '{name}' on extern function '{functionName}'.");
    }
}
