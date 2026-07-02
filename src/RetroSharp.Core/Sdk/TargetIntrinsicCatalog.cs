namespace RetroSharp.Core.Sdk;

public enum TargetIntrinsicOperation
{
    WaitFrame,
    PollInput,
    InitializeAudio,
    UpdateAudio,
    PlayMusic,
    StopMusic,
    ReadWorldTileFlags,
    CameraAabbTiles,
    CameraAabbHitTop,
    CameraScreenAabbTiles,
    CameraScreenAabbHitTop,
    SetCameraPosition,
    ApplyCamera,
    DrawLogicalSprite,
}

public enum TargetIntrinsicReturnKind
{
    Void,
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
        Operation = operation;
        ReturnKind = returnKind;
        RuntimeArity = runtimeArity;
        CompileTimeOperands = (compileTimeOperands ?? []).OrderBy(operand => operand.Slot).ToArray();
        RequiredCapabilities = (requiredCapabilities ?? []).Distinct().Order().ToArray();

        var duplicate = CompileTimeOperands
            .GroupBy(operand => operand.Slot)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Intrinsic '{intrinsicId}' declares compile-time operand slot {duplicate.Key + 1} more than once.");
        }
    }

    public string IntrinsicId { get; }
    public string Name { get; }
    public TargetIntrinsicOperation Operation { get; }
    public TargetIntrinsicReturnKind ReturnKind { get; }
    public int RuntimeArity { get; }
    public IReadOnlyList<TargetIntrinsicCompileTimeOperand> CompileTimeOperands { get; }
    public IReadOnlyList<TargetIntrinsicCapabilityRequirement> RequiredCapabilities { get; }
    public int Arity => RuntimeArity + CompileTimeOperands.Count;

    public static TargetIntrinsicDescriptor WaitFrame(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.WaitFrame, TargetIntrinsicReturnKind.Void, arity);
    }

    public static TargetIntrinsicDescriptor PollInput(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.PollInput, TargetIntrinsicReturnKind.Void, arity);
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
}

public sealed class TargetIntrinsicCatalog
{
    private readonly Dictionary<string, TargetIntrinsicDescriptor> intrinsics;

    public TargetIntrinsicCatalog(string targetId, string targetName, IEnumerable<TargetIntrinsicDescriptor> intrinsics)
    {
        TargetId = targetId;
        TargetName = targetName;
        this.intrinsics = intrinsics.ToDictionary(intrinsic => intrinsic.Name, StringComparer.Ordinal);
    }

    public string TargetId { get; }
    public string TargetName { get; }

    public IReadOnlyCollection<TargetIntrinsicDescriptor> Intrinsics => intrinsics.Values;

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

        throw new InvalidOperationException(
            $"Target '{TargetId}' does not support intrinsic '{name}' on extern function '{functionName}'.");
    }
}
