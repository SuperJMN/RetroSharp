namespace RetroSharp.GameBoy;

using RetroSharp.Core.Sdk;
using RetroSharp.Parser;

internal sealed class GameBoySdkLoweringContext(
    Action<SdkByteExpression> emitByteExpressionToA,
    Action<SdkWordExpression, bool> emitWordExpressionToA,
    Action<ExpressionSyntax> emitSourceExpressionToA,
    Action<ushort, ushort, int> emitStoreSplitWordImmediate,
    TryGameBoySourceConstant trySourceConstant,
    Func<ExpressionSyntax, string, int> constRuntimeValue)
{
    public void EmitByteExpressionToA(SdkByteExpression expression) => emitByteExpressionToA(expression);

    public void EmitWordExpressionToA(SdkWordExpression expression, bool highByte) =>
        emitWordExpressionToA(expression, highByte);

    public void EmitSourceExpressionToA(ExpressionSyntax expression) => emitSourceExpressionToA(expression);

    public void EmitStoreSplitWordImmediate(ushort lowAddress, ushort highAddress, int value) =>
        emitStoreSplitWordImmediate(lowAddress, highAddress, value);

    public bool TrySourceConstant(ExpressionSyntax expression, out int value) =>
        trySourceConstant(expression, out value);

    public int ConstRuntimeValue(ExpressionSyntax expression, string context) =>
        constRuntimeValue(expression, context);
}

internal delegate bool TryGameBoySourceConstant(ExpressionSyntax expression, out int value);
