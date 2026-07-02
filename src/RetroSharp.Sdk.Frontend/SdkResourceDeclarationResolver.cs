namespace RetroSharp.Sdk;

using RetroSharp.Core.Sdk;
using RetroSharp.Parser;

public static class SdkResourceDeclarationResolver
{
    public static bool TryResolve(FunctionSyntax function, out SdkResourceDeclarationDescriptor descriptor)
    {
        var resourceId = TargetAttributeReader.StringArgument(function, "resource");
        if (resourceId is null)
        {
            descriptor = null!;
            return false;
        }

        descriptor = SdkResourceDeclarationDescriptor.Create(resourceId);
        return true;
    }
}
