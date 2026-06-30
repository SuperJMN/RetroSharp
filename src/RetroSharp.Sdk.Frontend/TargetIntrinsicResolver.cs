namespace RetroSharp.Sdk;

using RetroSharp.Core.Sdk;
using RetroSharp.Parser;

public static class TargetIntrinsicResolver
{
    public static TargetIntrinsicDescriptor Resolve(FunctionSyntax function, TargetIntrinsicCatalog catalog)
    {
        var intrinsic = TargetAttributeReader.StringArgument(function, "intrinsic")
                        ?? throw new InvalidOperationException($"Extern function '{function.Name}' must declare an intrinsic attribute.");
        var target = TargetAttributeReader.StringArgument(function, "target")
                     ?? throw new InvalidOperationException($"Extern intrinsic '{function.Name}' must declare a target attribute.");
        if (!string.Equals(target, catalog.TargetId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Extern intrinsic '{function.Name}' targets '{target}', not {catalog.TargetName}.");
        }

        return catalog.Resolve(intrinsic, function.Name);
    }
}
