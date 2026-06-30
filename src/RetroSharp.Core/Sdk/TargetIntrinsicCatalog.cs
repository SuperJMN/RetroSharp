namespace RetroSharp.Core.Sdk;

public enum TargetIntrinsicOperation
{
    WaitFrame,
    PollInput,
    UpdateAudio,
    ReadWorldTileFlags,
    SetCameraPosition,
    ApplyCamera,
}

public sealed record TargetIntrinsicDescriptor(string Name, TargetIntrinsicOperation Operation, int Arity)
{
    public static TargetIntrinsicDescriptor WaitFrame(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.WaitFrame, arity);
    }

    public static TargetIntrinsicDescriptor PollInput(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.PollInput, arity);
    }

    public static TargetIntrinsicDescriptor UpdateAudio(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.UpdateAudio, arity);
    }

    public static TargetIntrinsicDescriptor ReadWorldTileFlags(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.ReadWorldTileFlags, arity);
    }

    public static TargetIntrinsicDescriptor SetCameraPosition(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.SetCameraPosition, arity);
    }

    public static TargetIntrinsicDescriptor ApplyCamera(string name, int arity)
    {
        return new TargetIntrinsicDescriptor(name, TargetIntrinsicOperation.ApplyCamera, arity);
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
