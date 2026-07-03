namespace RetroSharp.Sdk;

using RetroSharp.Parser;

public static class SdkSourcePackageFacadeLowerer
{
    public static ProgramSyntax Lower(ProgramSyntax program)
    {
        var staticMethods = DeclaredStaticMethodIndex.Build(program);
        return staticMethods.Count == 0
            ? program
            : StaticClassLowerer.LowerStaticCalls(program, staticMethods);
    }
}
