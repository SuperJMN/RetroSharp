namespace RetroSharp.Core.Sdk;

public enum SdkModuleKind
{
    Library,
}

public sealed record SdkModuleDescriptor(string Name, string CallPrefix, SdkModuleKind Kind)
{
    public string ResolveCallName(string method)
    {
        return $"{CallPrefix}_{SdkModuleRegistry.MethodName(method)}";
    }
}
