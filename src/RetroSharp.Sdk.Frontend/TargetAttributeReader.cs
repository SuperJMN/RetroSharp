namespace RetroSharp.Sdk;

using RetroSharp.Parser;

public static class TargetAttributeReader
{
    public static string? StringArgument(FunctionSyntax function, string name)
    {
        var attribute = function.Attributes.FirstOrDefault(attr => attr.Name == name);
        if (attribute is null)
        {
            return null;
        }

        if (attribute.Arguments is not [ConstantSyntax constant])
        {
            throw new InvalidOperationException($"Attribute '{name}' on extern function '{function.Name}' expects one string argument.");
        }

        var text = constant.Value.ToString() ?? "";
        if (text.Length >= 2 && text[0] == '"' && text[^1] == '"')
        {
            return text[1..^1];
        }

        throw new InvalidOperationException($"Attribute '{name}' on extern function '{function.Name}' expects one string argument.");
    }
}
