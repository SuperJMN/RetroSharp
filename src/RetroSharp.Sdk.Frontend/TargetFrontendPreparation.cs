namespace RetroSharp.Sdk;

using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;

internal sealed record TargetFrontendPreparationOptions(
    string Source,
    TargetIntrinsicCatalog BaseTargetIntrinsics,
    Target2DCapabilities Capabilities)
{
    public string? BaseDirectory { get; init; }

    public SdkLibraryImportMode LibraryImportMode { get; init; } = SdkLibraryImportMode.ExplicitOnly;

    public SdkLibraryRegistry? BaseLibraryRegistry { get; init; }

    public IReadOnlyList<string>? LibraryImports { get; init; }

    public SdkPluginRegistry? PluginRegistry { get; init; }

}

internal sealed class PreparedTargetProgram
{
    private readonly ProgramSyntax selectedPreActorProgram;

    internal PreparedTargetProgram(
        ProgramSyntax selectedPreActorProgram,
        ProgramSyntax loweredProgram,
        TargetIntrinsicCatalog targetIntrinsics,
        SdkResourceDeclarationRegistry resourceDeclarations,
        Target2DCapabilities capabilities,
        string? baseDirectory)
    {
        this.selectedPreActorProgram = selectedPreActorProgram;
        LoweredProgram = loweredProgram;
        TargetIntrinsics = targetIntrinsics;
        ResourceDeclarations = resourceDeclarations;
        Capabilities = capabilities;
        BaseDirectory = baseDirectory;
    }

    public ProgramSyntax LoweredProgram { get; }

    public TargetIntrinsicCatalog TargetIntrinsics { get; }

    public SdkResourceDeclarationRegistry ResourceDeclarations { get; }

    public Target2DCapabilities Capabilities { get; }

    public string? BaseDirectory { get; }

    public void ValidateActorPoolSpriteBudgets(Func<string, ActorMetaspriteGeometry> metaspriteGeometry)
    {
        ActorFrameworkLowerer.ValidatePoolSpriteBudgets(
            selectedPreActorProgram,
            Capabilities,
            metaspriteGeometry,
            BaseDirectory);
    }
}

internal static class TargetFrontendPreparation
{
    internal static PreparedTargetProgram Prepare(TargetFrontendPreparationOptions options)
    {
        var pluginRegistry = options.PluginRegistry ?? SdkPluginRegistry.Empty;
        var targetIntrinsics = options.BaseTargetIntrinsics.WithSdkPlugins(pluginRegistry);
        var libraryRegistry = (options.BaseLibraryRegistry ?? SdkLibraryRegistry.Default).WithSdkPlugins(pluginRegistry);
        var resourceDeclarations = AddPluginResources(SdkResourceDeclarationRegistry.Default, pluginRegistry);
        var mergedSource = SdkLibrarySource.Merge(
            targetIntrinsics,
            options.Source,
            options.LibraryImportMode,
            libraryRegistry,
            options.LibraryImports);
        var parse = new SomeParser().Parse(mergedSource);
        if (parse.IsFailure)
        {
            throw new InvalidOperationException(parse.Error);
        }

        var targetProgram = TargetProgramSelector.Select(parse.Value, targetIntrinsics);
        SdkImportResolver.ValidateImports(targetProgram, libraryRegistry);
        var actorProgram = ActorFrameworkLowerer.Lower(
            targetProgram,
            options.Capabilities,
            supportsUpdate: true,
            supportsDraw: true,
            options.BaseDirectory);
        var loweredProgram = SdkSourcePackageFacadeLowerer.Lower(actorProgram);
        loweredProgram = LetTypeInference.ResolveOrThrow(loweredProgram);
        var contractErrors = FunctionContractValidator.ValidateProgram(loweredProgram).ToList();
        if (contractErrors.Count != 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, contractErrors));
        }

        return new PreparedTargetProgram(
            targetProgram,
            loweredProgram,
            targetIntrinsics,
            resourceDeclarations,
            options.Capabilities,
            options.BaseDirectory);
    }

    private static SdkResourceDeclarationRegistry AddPluginResources(
        SdkResourceDeclarationRegistry resourceDeclarations,
        SdkPluginRegistry pluginRegistry)
    {
        foreach (var plugin in pluginRegistry.Plugins)
        {
            resourceDeclarations = resourceDeclarations.Register(plugin);
        }

        return resourceDeclarations;
    }
}
