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
        if (TryEmitResourceDeclarationCall(call))
        {
            return;
        }

        switch (call.Name)
        {
            case "tilemap_set":
                EmitRuntimeTilemapSet(call);
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

    private bool TryEmitResourceDeclarationCall(FunctionCall call)
    {
        if (!program.Functions.TryGetValue(call.Name, out var function)
            || !SdkResourceDeclarationResolver.TryResolve(function, out var descriptor, program.ResourceDeclarations))
        {
            return false;
        }

        if (descriptor.Kind == SdkResourceDeclarationKind.TilemapSet
            && call.Parameters.Any(parameter => !TryConst(parameter, out _)))
        {
            EmitRuntimeTilemapSet(call);
        }

        return true;
    }

    private void EmitRuntimeTilemapSet(FunctionCall call)
    {
        NesVideoProgram.RequireArity(call, 3);
        var args = call.Parameters.ToList();
        ValidateRuntimeTilemapArgument(args[0], 31, "x");
        ValidateRuntimeTilemapArgument(args[1], 29, "y");
        ValidateRuntimeTilemapArgument(args[2], 255, "tile");

        EmitExpressionToA(args[2]);
        builder.PushA();
        EmitExpressionToA(args[0]);
        builder.PushA();

        EmitExpressionToA(args[1]);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.ShiftRightA();
        builder.ClearCarry();
        builder.AddImmediate(0x20);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
        for (var shift = 0; shift < 5; shift++)
        {
            builder.ShiftLeftA();
        }

        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
        builder.PullA();
        builder.ClearCarry();
        builder.AddZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);

        builder.LoadAAbsolute(0x2002); // reset the PPU address/scroll latch
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        builder.StoreAAbsolute(0x2006);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
        builder.StoreAAbsolute(0x2006);
        builder.PullA();
        builder.StoreAAbsolute(0x2007);

        // The runtime fixed-screen tile write is an escape hatch. Restore a zero scroll after
        // touching PPUADDR so the following visible frame starts with a coherent latch.
        builder.LoadAAbsolute(0x2002);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(0x2005);
        builder.StoreAAbsolute(0x2005);
    }

    private void ValidateRuntimeTilemapArgument(ExpressionSyntax expression, int max, string name)
    {
        if (TryConst(expression, out var value) && (value < 0 || value > max))
        {
            throw new InvalidOperationException(
                $"NES runtime tilemap_set {name} must be between 0 and {max}, got {value}.");
        }
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
            using (callRecorder.EnterCall(function.Name, loopTargets.Count, ClassifyCallArguments(function, call)))
            {
                try
                {
                    PushInlineVariableScope();
                    EmitBlock(ParameterSubstitution.Substitute(function, call, "NES"));
                }
                finally
                {
                    PopInlineVariableScope();
                }
            }
        }
        finally
        {
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
            using (callRecorder.EnterCall(function.Name, loopTargets.Count, ClassifyCallArguments(function, call)))
            {
                EmitExpressionToA(ParameterSubstitution.SubstituteReturnExpression(function, call, "NES"));
            }
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
