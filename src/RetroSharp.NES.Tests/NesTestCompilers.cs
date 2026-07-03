namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Sdk;

internal static class NesRomCompiler
{
    private static readonly string[] DefaultLibraryImports = [SdkImportResolver.Portable2D];

    public static byte[] CompileSource(
        string source,
        string? baseDirectory = null,
        SdkLibraryImportMode sdkImportMode = SdkLibraryImportMode.ExplicitOnly,
        SdkLibraryRegistry? sdkLibraryRegistry = null,
        IReadOnlyList<string>? sdkLibraryImports = null)
    {
        return RetroSharp.NES.NesRomCompiler.CompileSource(
            source,
            baseDirectory,
            sdkImportMode,
            sdkLibraryRegistry,
            sdkLibraryImports ?? DefaultLibraryImports);
    }

    public static IReadOnlyList<Sdk2DOperation> CollectSdkOperations(
        string source,
        string? baseDirectory = null,
        SdkLibraryImportMode sdkImportMode = SdkLibraryImportMode.ExplicitOnly,
        SdkLibraryRegistry? sdkLibraryRegistry = null,
        IReadOnlyList<string>? sdkLibraryImports = null)
    {
        return RetroSharp.NES.NesRomCompiler.CollectSdkOperations(
            source,
            baseDirectory,
            sdkImportMode,
            sdkLibraryRegistry,
            sdkLibraryImports ?? DefaultLibraryImports);
    }

    public static IReadOnlyList<SdkAudioOperation> CollectSdkAudioOperations(
        string source,
        string? baseDirectory = null,
        SdkLibraryImportMode sdkImportMode = SdkLibraryImportMode.ExplicitOnly,
        SdkLibraryRegistry? sdkLibraryRegistry = null,
        IReadOnlyList<string>? sdkLibraryImports = null)
    {
        return RetroSharp.NES.NesRomCompiler.CollectSdkAudioOperations(
            source,
            baseDirectory,
            sdkImportMode,
            sdkLibraryRegistry,
            sdkLibraryImports ?? DefaultLibraryImports);
    }
}

internal static class GameBoyRomCompiler
{
    private static readonly string[] DefaultLibraryImports = [SdkImportResolver.Portable2D];

    public static byte[] CompileSource(
        string source,
        string? baseDirectory = null,
        SdkLibraryImportMode sdkImportMode = SdkLibraryImportMode.ExplicitOnly,
        SdkLibraryRegistry? sdkLibraryRegistry = null,
        IReadOnlyList<string>? sdkLibraryImports = null)
    {
        return RetroSharp.GameBoy.GameBoyRomCompiler.CompileSource(
            source,
            baseDirectory,
            sdkImportMode,
            sdkLibraryRegistry,
            sdkLibraryImports ?? DefaultLibraryImports);
    }

    public static IReadOnlyList<Sdk2DOperation> CollectSdkOperations(
        string source,
        string? baseDirectory = null,
        SdkLibraryImportMode sdkImportMode = SdkLibraryImportMode.ExplicitOnly,
        SdkLibraryRegistry? sdkLibraryRegistry = null,
        IReadOnlyList<string>? sdkLibraryImports = null)
    {
        return RetroSharp.GameBoy.GameBoyRomCompiler.CollectSdkOperations(
            source,
            baseDirectory,
            sdkImportMode,
            sdkLibraryRegistry,
            sdkLibraryImports ?? DefaultLibraryImports);
    }
}
