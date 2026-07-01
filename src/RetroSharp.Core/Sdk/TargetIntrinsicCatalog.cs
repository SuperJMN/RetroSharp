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

public enum TargetIntrinsicOperandRole
{
    AssetRef,
    ConstPaletteSlot,
    EnumFlags,
    WorldId,
}

public sealed record TargetIntrinsicCompileTimeOperand(int Slot, TargetIntrinsicOperandRole Role);

public sealed record TargetIntrinsicDescriptor
{
    public TargetIntrinsicDescriptor(
        string name,
        TargetIntrinsicOperation operation,
        int runtimeArity,
        IEnumerable<TargetIntrinsicCompileTimeOperand>? compileTimeOperands = null)
    {
        if (runtimeArity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(runtimeArity), "Runtime arity cannot be negative.");
        }

        Name = name;
        Operation = operation;
        RuntimeArity = runtimeArity;
        CompileTimeOperands = (compileTimeOperands ?? []).OrderBy(operand => operand.Slot).ToArray();

        var duplicate = CompileTimeOperands
            .GroupBy(operand => operand.Slot)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Intrinsic '{name}' declares compile-time operand slot {duplicate.Key + 1} more than once.");
        }
    }

    public string Name { get; }
    public TargetIntrinsicOperation Operation { get; }
    public int RuntimeArity { get; }
    public IReadOnlyList<TargetIntrinsicCompileTimeOperand> CompileTimeOperands { get; }
    public int Arity => RuntimeArity + CompileTimeOperands.Count;

    public static TargetIntrinsicDescriptor WaitFrame(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.WaitFrame, arity);
    }

    public static TargetIntrinsicDescriptor PollInput(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.PollInput, arity);
    }

    public static TargetIntrinsicDescriptor InitializeAudio(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.InitializeAudio, arity);
    }

    public static TargetIntrinsicDescriptor UpdateAudio(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.UpdateAudio, arity);
    }

    public static TargetIntrinsicDescriptor PlayMusic(
        string name,
        int runtimeArity,
        IEnumerable<TargetIntrinsicCompileTimeOperand> compileTimeOperands)
    {
        return new TargetIntrinsicDescriptor(
            name,
            TargetIntrinsicOperation.PlayMusic,
            runtimeArity,
            compileTimeOperands);
    }

    public static TargetIntrinsicDescriptor StopMusic(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.StopMusic, arity);
    }

    public static TargetIntrinsicDescriptor ReadWorldTileFlags(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.ReadWorldTileFlags, arity);
    }

    public static TargetIntrinsicDescriptor ReadWorldTileFlags(
        string name,
        int runtimeArity,
        IEnumerable<TargetIntrinsicCompileTimeOperand> compileTimeOperands)
    {
        return new TargetIntrinsicDescriptor(
            name,
            TargetIntrinsicOperation.ReadWorldTileFlags,
            runtimeArity,
            compileTimeOperands);
    }

    public static TargetIntrinsicDescriptor SetCameraPosition(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.SetCameraPosition, arity);
    }

    public static TargetIntrinsicDescriptor CameraAabbTiles(
        string name,
        int runtimeArity,
        IEnumerable<TargetIntrinsicCompileTimeOperand> compileTimeOperands)
    {
        return new TargetIntrinsicDescriptor(
            name,
            TargetIntrinsicOperation.CameraAabbTiles,
            runtimeArity,
            compileTimeOperands);
    }

    public static TargetIntrinsicDescriptor CameraAabbHitTop(
        string name,
        int runtimeArity,
        IEnumerable<TargetIntrinsicCompileTimeOperand> compileTimeOperands)
    {
        return new TargetIntrinsicDescriptor(
            name,
            TargetIntrinsicOperation.CameraAabbHitTop,
            runtimeArity,
            compileTimeOperands);
    }

    public static TargetIntrinsicDescriptor CameraScreenAabbTiles(
        string name,
        int runtimeArity,
        IEnumerable<TargetIntrinsicCompileTimeOperand> compileTimeOperands)
    {
        return new TargetIntrinsicDescriptor(
            name,
            TargetIntrinsicOperation.CameraScreenAabbTiles,
            runtimeArity,
            compileTimeOperands);
    }

    public static TargetIntrinsicDescriptor CameraScreenAabbHitTop(
        string name,
        int runtimeArity,
        IEnumerable<TargetIntrinsicCompileTimeOperand> compileTimeOperands)
    {
        return new TargetIntrinsicDescriptor(
            name,
            TargetIntrinsicOperation.CameraScreenAabbHitTop,
            runtimeArity,
            compileTimeOperands);
    }

    public static TargetIntrinsicDescriptor ApplyCamera(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.ApplyCamera, arity);
    }

    public static TargetIntrinsicDescriptor DrawLogicalSprite(
        string name,
        int runtimeArity,
        IEnumerable<TargetIntrinsicCompileTimeOperand> compileTimeOperands)
    {
        return new TargetIntrinsicDescriptor(
            name,
            TargetIntrinsicOperation.DrawLogicalSprite,
            runtimeArity,
            compileTimeOperands);
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
