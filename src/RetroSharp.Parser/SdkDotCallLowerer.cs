namespace RetroSharp.Parser;

using RetroSharp.Core.Sdk;

public static class SdkDotCallLowerer
{
    public static bool IsKnownModule(string module)
    {
        return SdkModuleRegistry.IsKnownModule(module);
    }

    public static FunctionCall Lower(SdkDotCallSyntax call)
    {
        if (!SdkModuleRegistry.TryResolveCallName(call.Module, call.Method, out var callName))
        {
            throw new InvalidOperationException($"Unknown SDK module '{call.Module}'.");
        }

        return new FunctionCall(callName, call.Parameters);
    }
}
