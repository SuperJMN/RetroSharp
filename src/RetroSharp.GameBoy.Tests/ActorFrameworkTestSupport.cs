namespace RetroSharp.GameBoy.Tests;

using RetroSharp.GameBoy;
using RetroSharp.Parser;
using RetroSharp.Sdk;

public partial class GameBoyRomCompilerTests
{
    private static ProgramSyntax ParseGameBoySourceWithPortable2D(string source)
    {
        var parse = new SomeParser().Parse(
            SdkLibrarySource.Merge(
                GameBoyTarget.Intrinsics,
                source,
                libraryImportPaths: [SdkImportResolver.Portable2D]));
        if (parse.IsFailure)
        {
            throw new InvalidOperationException(parse.Error);
        }

        return TargetProgramSelector.Select(parse.Value, GameBoyTarget.Intrinsics);
    }
}
