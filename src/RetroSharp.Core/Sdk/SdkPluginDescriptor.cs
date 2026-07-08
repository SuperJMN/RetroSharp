namespace RetroSharp.Core.Sdk;

public sealed record SdkPluginSourcePackageDescriptor(
    string ImportPath,
    Func<TargetIntrinsicCatalog, string> SourceFactory)
{
    public string SourceForTarget(TargetIntrinsicCatalog catalog)
    {
        return SourceFactory(catalog);
    }
}

public sealed record SdkPluginResourceDeclarationDescriptor(string ResourceId);

public enum SdkPluginOperationCallKind
{
    Statement,
    Value,
}

public sealed record SdkPluginOperationDescriptor
{
    public SdkPluginOperationDescriptor(
        string OperationId,
        TargetIntrinsicReturnKind ReturnKind,
        int RuntimeArity,
        SdkPluginOperationCallKind CallKind,
        IReadOnlyList<TargetIntrinsicCompileTimeOperand> CompileTimeOperands,
        IReadOnlyList<string> RequiredCapabilities)
    {
        if (RuntimeArity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(RuntimeArity), "Runtime arity cannot be negative.");
        }

        OperationId = OperationId.Trim();
        this.OperationId = OperationId;
        this.ReturnKind = ReturnKind;
        this.RuntimeArity = RuntimeArity;
        this.CallKind = CallKind;
        this.CompileTimeOperands = CompileTimeOperands.OrderBy(operand => operand.Slot).ToArray();
        this.RequiredCapabilities = RequiredCapabilities
            .Where(capability => !string.IsNullOrWhiteSpace(capability))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var duplicate = this.CompileTimeOperands
            .GroupBy(operand => operand.Slot)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"SDK plugin operation '{OperationId}' declares compile-time operand slot {duplicate.Key + 1} more than once.");
        }
    }

    public string OperationId { get; }

    public TargetIntrinsicReturnKind ReturnKind { get; }

    public int RuntimeArity { get; }

    public SdkPluginOperationCallKind CallKind { get; }

    public IReadOnlyList<TargetIntrinsicCompileTimeOperand> CompileTimeOperands { get; }

    public IReadOnlyList<string> RequiredCapabilities { get; }

    public int Arity => RuntimeArity + CompileTimeOperands.Count;
}

public sealed record SdkPluginCapabilityDescriptor(string CapabilityId);

public sealed record SdkPluginAssetImporterDescriptor(string ResourceId);

public sealed record SdkPluginRuntimeOperand(int Slot);

public sealed record SdkPluginCompileTimeOperand(
    int Slot,
    TargetIntrinsicOperandRole Role,
    string? Identifier,
    int? Constant);

public sealed record SdkPluginValidationContext(
    string TargetId,
    SdkPluginOperationDescriptor Operation,
    IReadOnlyList<SdkPluginRuntimeOperand> RuntimeOperands,
    IReadOnlyList<SdkPluginCompileTimeOperand> CompileTimeOperands);

public sealed record SdkPluginTargetLoweringContext(
    string TargetId,
    SdkPluginOperationDescriptor Operation,
    IReadOnlyList<SdkPluginRuntimeOperand> RuntimeOperands,
    IReadOnlyList<SdkPluginCompileTimeOperand> CompileTimeOperands,
    SdkPluginTargetEmitter Emitter);

public sealed record SdkPluginTargetEmitter(Action<IReadOnlyList<byte>> EmitBytes)
{
    public void Emit(params byte[] bytes)
    {
        EmitBytes(bytes);
    }
}

public sealed record SdkPluginValidatorDescriptor(
    string FeatureId,
    Action<SdkPluginValidationContext> Validate);

public sealed record SdkPluginTargetLoweringDescriptor(
    string TargetId,
    string OperationId,
    Action<SdkPluginTargetLoweringContext> Lower);

public sealed record SdkPluginCompatibilityDescriptor(
    string? MinimumCompilerVersion,
    string? MaximumCompilerVersion,
    IReadOnlyDictionary<string, string> MigrationAliases)
{
    public static SdkPluginCompatibilityDescriptor Unspecified { get; } = new(null, null, new Dictionary<string, string>());
}

public sealed record SdkPluginDescriptor
{
    public const string StaticCompilerAbi = "sdk-plugin-static-v1";

    public SdkPluginDescriptor(
        string Id,
        string Version,
        string RequiredCompilerAbi,
        SdkPluginSourcePackageDescriptor SourcePackage,
        IReadOnlyList<SdkPluginResourceDeclarationDescriptor> ResourceDeclarations,
        IReadOnlyList<SdkPluginOperationDescriptor> Operations,
        IReadOnlyList<SdkPluginCapabilityDescriptor> Capabilities,
        IReadOnlyList<SdkPluginValidatorDescriptor> Validators,
        IReadOnlyList<SdkPluginTargetLoweringDescriptor> TargetLoweringHooks,
        SdkPluginCompatibilityDescriptor Compatibility,
        IReadOnlyList<SdkPluginAssetImporterDescriptor>? AssetImporters = null)
    {
        this.Id = RequireText(Id, nameof(Id));
        this.Version = RequireText(Version, nameof(Version));
        this.RequiredCompilerAbi = RequireText(RequiredCompilerAbi, nameof(RequiredCompilerAbi));
        this.SourcePackage = SourcePackage;
        this.ResourceDeclarations = ResourceDeclarations.ToArray();
        this.Operations = Operations.ToArray();
        this.Capabilities = Capabilities.ToArray();
        this.AssetImporters = (AssetImporters ?? []).ToArray();
        this.Validators = Validators.ToArray();
        this.TargetLoweringHooks = TargetLoweringHooks.ToArray();
        this.Compatibility = Compatibility;

        ValidateNamespacedIds();
        ValidateTargetLoweringHooks();
    }

    public string Id { get; }

    public string Version { get; }

    public string RequiredCompilerAbi { get; }

    public SdkPluginSourcePackageDescriptor SourcePackage { get; }

    public IReadOnlyList<SdkPluginResourceDeclarationDescriptor> ResourceDeclarations { get; }

    public IReadOnlyList<SdkPluginOperationDescriptor> Operations { get; }

    public IReadOnlyList<SdkPluginCapabilityDescriptor> Capabilities { get; }

    public IReadOnlyList<SdkPluginAssetImporterDescriptor> AssetImporters { get; }

    public IReadOnlyList<SdkPluginValidatorDescriptor> Validators { get; }

    public IReadOnlyList<SdkPluginTargetLoweringDescriptor> TargetLoweringHooks { get; }

    public SdkPluginCompatibilityDescriptor Compatibility { get; }

    public bool TryResolveOperation(string operationId, out SdkPluginOperationDescriptor operation)
    {
        operation = Operations.FirstOrDefault(descriptor => descriptor.OperationId == operationId)!;
        return operation is not null;
    }

    public IEnumerable<SdkPluginValidatorDescriptor> ValidatorsFor(string featureId)
    {
        return Validators.Where(validator => validator.FeatureId == featureId);
    }

    private void ValidateNamespacedIds()
    {
        foreach (var resource in ResourceDeclarations)
        {
            RequireNamespaced(resource.ResourceId);
        }

        foreach (var operation in Operations)
        {
            RequireNamespaced(operation.OperationId);
            foreach (var capability in operation.RequiredCapabilities)
            {
                RequireNamespaced(capability);
            }
        }

        foreach (var capability in Capabilities)
        {
            RequireNamespaced(capability.CapabilityId);
        }

        foreach (var assetImporter in AssetImporters)
        {
            RequireNamespaced(assetImporter.ResourceId);
        }

        foreach (var validator in Validators)
        {
            RequireNamespaced(validator.FeatureId);
        }

        foreach (var hook in TargetLoweringHooks)
        {
            RequireNamespaced(hook.OperationId);
        }
    }

    private void ValidateTargetLoweringHooks()
    {
        foreach (var hook in TargetLoweringHooks)
        {
            if (!Operations.Any(operation => operation.OperationId == hook.OperationId))
            {
                throw new InvalidOperationException($"SDK plugin target hook '{hook.OperationId}' does not match an operation in plugin '{Id}'.");
            }
        }
    }

    private void RequireNamespaced(string descriptorId)
    {
        if (!descriptorId.StartsWith(Id + ".", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"SDK plugin descriptor id '{descriptorId}' must be namespaced under plugin id '{Id}'.");
        }
    }

    private static string RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value cannot be empty.", parameterName);
        }

        return value.Trim();
    }
}

public sealed class SdkPluginRegistry
{
    private readonly Dictionary<string, SdkPluginDescriptor> plugins;

    public static SdkPluginRegistry Empty { get; } = new([]);

    public SdkPluginRegistry(IEnumerable<SdkPluginDescriptor> plugins)
    {
        this.plugins = new Dictionary<string, SdkPluginDescriptor>(StringComparer.Ordinal);
        foreach (var plugin in plugins)
        {
            if (!string.Equals(plugin.RequiredCompilerAbi, SdkPluginDescriptor.StaticCompilerAbi, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"SDK plugin '{plugin.Id}' requires compiler ABI '{plugin.RequiredCompilerAbi}', but this compiler supports '{SdkPluginDescriptor.StaticCompilerAbi}'.");
            }

            if (!this.plugins.TryAdd(plugin.Id, plugin))
            {
                throw new InvalidOperationException($"SDK plugin '{plugin.Id}' is registered more than once.");
            }
        }
    }

    public IReadOnlyCollection<SdkPluginDescriptor> Plugins => plugins.Values;

    public SdkPluginRegistry Register(SdkPluginDescriptor plugin)
    {
        return new SdkPluginRegistry(plugins.Values.Append(plugin));
    }

    public bool TryResolve(string pluginId, out SdkPluginDescriptor plugin)
    {
        return plugins.TryGetValue(pluginId, out plugin!);
    }

    public bool TryResolveOperation(string operationId, out SdkPluginDescriptor plugin, out SdkPluginOperationDescriptor operation)
    {
        foreach (var candidate in plugins.Values)
        {
            if (candidate.TryResolveOperation(operationId, out operation!))
            {
                plugin = candidate;
                return true;
            }
        }

        plugin = null!;
        operation = null!;
        return false;
    }

    public IEnumerable<TargetIntrinsicDescriptor> TargetIntrinsicDescriptorsFor(string targetId)
    {
        foreach (var plugin in plugins.Values)
        {
            foreach (var hook in plugin.TargetLoweringHooks.Where(hook => hook.TargetId == targetId))
            {
                if (!plugin.TryResolveOperation(hook.OperationId, out var operation))
                {
                    continue;
                }

                yield return TargetIntrinsicDescriptor.FromPluginOperation(
                    operation,
                    hook,
                    plugin.ValidatorsFor(operation.OperationId));
            }
        }
    }

    public IReadOnlySet<string> UnsupportedOperationIdsFor(string targetId)
    {
        var allOperations = plugins.Values
            .SelectMany(plugin => plugin.Operations)
            .Select(operation => operation.OperationId)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var supported in plugins.Values.SelectMany(plugin => plugin.TargetLoweringHooks).Where(hook => hook.TargetId == targetId))
        {
            allOperations.Remove(supported.OperationId);
        }

        return allOperations;
    }
}
