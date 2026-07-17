using System.Globalization;
using RetroSharp.Core;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;
using RetroSharp.Sdk;

namespace RetroSharp.NES;

internal sealed partial class NesRuntimeCompiler
{
    private void EmitCall(FunctionCall call)
    {
        if (IsResourceDeclarationCall(call))
        {
            return;
        }

        switch (call.Name)
        {
            case "tilemap_set":
                NesVideoProgram.RequireArity(call, 3);
                break;
            case "tilemap_fill":
                NesVideoProgram.RequireArity(call, 5);
                break;
            case "map_stream_column":
                EmitSdkOperation<Sdk2DOperation.StreamMapColumn>(call.Name);
                break;
            case "map_stream_row":
                EmitSdkOperation<Sdk2DOperation.StreamMapRow>(call.Name);
                break;
            case "hud_set_tile":
                NesVideoProgram.ValidateHudSetTile(call);
                break;
            default:
                if (TryEmitTargetIntrinsic(call))
                {
                    break;
                }

                if (TryEmitUserFunction(call))
                {
                    break;
                }

                throw new InvalidOperationException($"Unsupported NES video API call '{call.Name}'.");
        }
    }

    private bool IsResourceDeclarationCall(FunctionCall call)
    {
        return program.Functions.TryGetValue(call.Name, out var function)
               && SdkResourceDeclarationResolver.TryResolve(function, out _, program.ResourceDeclarations);
    }

    private bool TryEmitUserFunction(FunctionCall call)
    {
        if (!program.Functions.TryGetValue(call.Name, out var function))
        {
            return false;
        }

        if (function.IsExtern)
        {
            return false;
        }

        if (!userFunctionCallStack.Add(function.Name))
        {
            throw new InvalidOperationException($"Recursive NES user function call '{function.Name}' is not supported.");
        }

        try
        {
            PushInlineVariableScope();
            EmitBlock(ParameterSubstitution.Substitute(function, call, "NES"));
        }
        finally
        {
            PopInlineVariableScope();
            userFunctionCallStack.Remove(function.Name);
        }

        return true;
    }

    private bool TryEmitUserValueFunction(FunctionCall call)
    {
        if (TryEmitGeneratedRomTableLookup(call))
        {
            return true;
        }

        if (!program.Functions.TryGetValue(call.Name, out var function))
        {
            return false;
        }

        if (function.IsExtern)
        {
            return false;
        }

        if (!userFunctionCallStack.Add(function.Name))
        {
            throw new InvalidOperationException($"Recursive NES user function call '{function.Name}' is not supported.");
        }

        try
        {
            EmitExpressionToA(ParameterSubstitution.SubstituteReturnExpression(function, call, "NES"));
        }
        finally
        {
            userFunctionCallStack.Remove(function.Name);
        }

        return true;
    }

    private bool TryEmitGeneratedRomTableLookup(FunctionCall call)
    {
        if (!program.GeneratedRomTables.TryGetValue(call.Name, out var table))
        {
            return false;
        }

        NesVideoProgram.RequireArity(call, 1);
        EmitExpressionToA(call.Parameters.Single());
        builder.TransferAToX();
        builder.LdaAbsoluteX(table.Label);
        return true;
    }

    private bool TryEmitWordValueFunctionToStorage(FunctionCall call, byte address, string targetType)
    {
        if (!program.Functions.TryGetValue(call.Name, out var function))
        {
            return false;
        }

        if (function.IsExtern)
        {
            var intrinsic = TargetIntrinsicResolver.Resolve(function, program.TargetIntrinsics);
            if (intrinsic.ReturnKind != TargetIntrinsicReturnKind.I16
                || !TryEmitTargetValueIntrinsic(call))
            {
                return false;
            }

            builder.StoreAZeroPage(address);
            builder.StoreXZeroPage(HighAddress(address));
            return true;
        }

        if (!userFunctionCallStack.Add(function.Name))
        {
            throw new InvalidOperationException($"Recursive NES user function call '{function.Name}' is not supported.");
        }

        try
        {
            EmitWordExpressionToStorage(
                ParameterSubstitution.SubstituteReturnExpression(function, call, "NES"),
                address,
                targetType);
        }
        finally
        {
            userFunctionCallStack.Remove(function.Name);
        }

        return true;
    }

    private void ValidateWorldHitTopNarrowing(ExpressionSyntax expression, string destinationType)
    {
        if (!IsWorldHitTopValue(expression, []))
        {
            return;
        }

        var world = sdkOperationLowerer.WorldMapForFlagQuery("camera_aabb_hit_top");
        if (world.Height <= 32)
        {
            return;
        }

        throw new InvalidOperationException(
            $"World hit-top cannot be stored in byte destination type '{destinationType}' because the active world is {world.Height} hardware rows tall; use an i16 local and compare it with -1.");
    }

    private bool IsWorldHitTopValue(ExpressionSyntax expression, HashSet<string> callStack)
    {
        if (expression is CastSyntax cast)
        {
            return IsWorldHitTopValue(cast.Expression, callStack);
        }

        if (expression is not FunctionCall call
            || !program.Functions.TryGetValue(call.Name, out var function))
        {
            return false;
        }

        if (function.IsExtern)
        {
            return TargetIntrinsicResolver.Resolve(function, program.TargetIntrinsics).Operation
                   == TargetIntrinsicOperation.CameraAabbHitTop;
        }

        if (!callStack.Add(function.Name))
        {
            return false;
        }

        try
        {
            return IsWorldHitTopValue(
                ParameterSubstitution.SubstituteReturnExpression(function, call, "NES"),
                callStack);
        }
        finally
        {
            callStack.Remove(function.Name);
        }
    }

}
