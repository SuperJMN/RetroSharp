namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Sdk;

internal static class GameBoyRomCompiler
{
    private static readonly string[] DefaultLibraryImports = [SdkImportResolver.Portable2D];

    public static byte[] CompileSource(
        string source,
        string? baseDirectory = null,
        SdkLibraryImportMode sdkImportMode = SdkLibraryImportMode.ExplicitOnly,
        SdkLibraryRegistry? sdkLibraryRegistry = null,
        IReadOnlyList<string>? sdkLibraryImports = null,
        SdkPluginRegistry? sdkPluginRegistry = null)
    {
        return RetroSharp.GameBoy.GameBoyRomCompiler.CompileSource(
            source,
            baseDirectory,
            sdkImportMode,
            sdkLibraryRegistry,
            sdkLibraryImports ?? DefaultLibraryImports,
            sdkPluginRegistry);
    }

    public static IReadOnlyList<Sdk2DOperation> CollectSdkOperations(
        string source,
        string? baseDirectory = null,
        SdkLibraryImportMode sdkImportMode = SdkLibraryImportMode.ExplicitOnly,
        SdkLibraryRegistry? sdkLibraryRegistry = null,
        IReadOnlyList<string>? sdkLibraryImports = null,
        SdkPluginRegistry? sdkPluginRegistry = null)
    {
        return RetroSharp.GameBoy.GameBoyRomCompiler.CollectSdkOperations(
            source,
            baseDirectory,
            sdkImportMode,
            sdkLibraryRegistry,
            sdkLibraryImports ?? DefaultLibraryImports,
            sdkPluginRegistry);
    }

    public static IReadOnlyList<SdkAudioOperation> CollectSdkAudioOperations(
        string source,
        string? baseDirectory = null,
        SdkLibraryImportMode sdkImportMode = SdkLibraryImportMode.ExplicitOnly,
        SdkLibraryRegistry? sdkLibraryRegistry = null,
        IReadOnlyList<string>? sdkLibraryImports = null,
        SdkPluginRegistry? sdkPluginRegistry = null)
    {
        return RetroSharp.GameBoy.GameBoyRomCompiler.CollectSdkAudioOperations(
            source,
            baseDirectory,
            sdkImportMode,
            sdkLibraryRegistry,
            sdkLibraryImports ?? DefaultLibraryImports,
            sdkPluginRegistry);
    }
}
