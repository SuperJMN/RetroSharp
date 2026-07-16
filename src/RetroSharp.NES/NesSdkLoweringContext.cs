namespace RetroSharp.NES;

using RetroSharp.Core.Sdk;
using RetroSharp.Parser;

internal sealed class NesSdkLoweringContext(
    Action<ExpressionSyntax> emitExpressionToA,
    TryNesSourceConstant trySourceConstant,
    Func<string, string> variableStorageType,
    Func<string, byte> variableAddress,
    Func<string, string, byte> runtimeIndexedMemberBaseAddress,
    Action<string, SdkByteExpression> emitRuntimeMemberIndexToX)
{
    public void EmitExpressionToA(ExpressionSyntax expression) => emitExpressionToA(expression);

    public bool TrySourceConstant(ExpressionSyntax expression, out int value) =>
        trySourceConstant(expression, out value);

    public string VariableStorageType(string name) => variableStorageType(name);

    public byte VariableAddress(string name) => variableAddress(name);

    public byte RuntimeIndexedMemberBaseAddress(string baseName, string fieldName) =>
        runtimeIndexedMemberBaseAddress(baseName, fieldName);

    public void EmitRuntimeMemberIndexToX(string baseName, SdkByteExpression index) =>
        emitRuntimeMemberIndexToX(baseName, index);
}

internal delegate bool TryNesSourceConstant(ExpressionSyntax expression, out int value);
