namespace RetroSharp.Sdk;

using System.Globalization;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;

// Target screen size published to source as compile-time constants, so games can express
// viewport-relative values such as camera dead-zone edges without target-specific numbers.
// They fold to literals like any other constant, so they cost nothing at runtime and can hold
// values the 8-bit intrinsic return channel cannot carry.
internal static class TargetViewportConstants
{
    internal const string WidthName = "Viewport.Width";

    internal const string HeightName = "Viewport.Height";

    internal static ProgramSyntax Inject(ProgramSyntax program, Target2DCapabilities capabilities)
    {
        foreach (var constant in program.Constants)
        {
            if (string.Equals(constant.Name, WidthName, StringComparison.Ordinal)
                || string.Equals(constant.Name, HeightName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"'{constant.Name}' is a reserved constant provided by the target; remove the source declaration.");
            }
        }

        var constants = new List<ConstDeclarationSyntax>
        {
            Constant(WidthName, capabilities.ScreenPixels.Width),
            Constant(HeightName, capabilities.ScreenPixels.Height),
        };

        constants.AddRange(program.Constants);

        return new ProgramSyntax(
            program.Imports,
            program.TypeAliases,
            constants,
            program.Enums,
            program.Structs,
            program.Functions);
    }

    private static ConstDeclarationSyntax Constant(string name, int value)
    {
        return new ConstDeclarationSyntax("i16", name, new ConstantSyntax(value.ToString(CultureInfo.InvariantCulture)));
    }
}
