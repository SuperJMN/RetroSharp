using System.Globalization;
using System.Text;
using RetroSharp.Core;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;
using RetroSharp.Sdk;

namespace RetroSharp.GameBoy;

internal sealed partial class GameBoyRuntimeCompiler
{
    private bool TryEmitTargetIntrinsic(FunctionCall call)
    {
        if (!program.Functions.TryGetValue(call.Name, out var function) || !function.IsExtern)
        {
            return false;
        }

        var intrinsic = TargetIntrinsicResolver.Resolve(function, program.TargetIntrinsics);
        GameBoyVideoProgram.RequireArity(call, intrinsic.Arity);
        if (intrinsic.IsPluginOperation)
        {
            EmitSdkPluginOperation(function, call, intrinsic);
            return true;
        }

        switch (intrinsic.Operation)
        {
            case TargetIntrinsicOperation.InitializeVideo:
            case TargetIntrinsicOperation.PresentVideo:
                return true;
            case TargetIntrinsicOperation.WaitFrame:
                EmitNextSdkOperation<Sdk2DOperation.WaitFrame>(call.Name);
                return true;
            case TargetIntrinsicOperation.PollInput:
                EmitNextSdkOperation<Sdk2DOperation.PollInput>(call.Name);
                return true;
            case TargetIntrinsicOperation.UpdateAudio:
                EmitNextSdkAudioOperation<SdkAudioOperation.UpdateAudio>(call.Name);
                return true;
            case TargetIntrinsicOperation.InitializeAudio:
                EmitNextSdkAudioOperation<SdkAudioOperation.InitializeAudio>(call.Name);
                return true;
            case TargetIntrinsicOperation.PlayMusic:
                EmitNextSdkAudioOperation<SdkAudioOperation.PlayMusic>(call.Name);
                return true;
            case TargetIntrinsicOperation.PlaySoundEffect:
                EmitNextSdkAudioOperation<SdkAudioOperation.PlaySoundEffect>(call.Name);
                return true;
            case TargetIntrinsicOperation.StopMusic:
                EmitNextSdkAudioOperation<SdkAudioOperation.StopMusic>(call.Name);
                return true;
            case TargetIntrinsicOperation.InitializeCamera:
                sdkOperationLowerer.EmitCameraInit(call);
                return true;
            case TargetIntrinsicOperation.SetCameraPosition:
                EmitNextSdkOperation<Sdk2DOperation.SetCameraPosition>(call.Name);
                return true;
            case TargetIntrinsicOperation.ApplyCamera:
                EmitNextSdkOperation<Sdk2DOperation.ApplyCamera>(call.Name);
                return true;
            case TargetIntrinsicOperation.CameraVerticalScrollMax:
                GameBoyVideoProgram.RequireArity(call, intrinsic.Arity);
                sdkOperationLowerer.EmitCameraVerticalScrollMax();
                return true;
            case TargetIntrinsicOperation.DrawLogicalSprite:
                EmitNextSdkOperation<Sdk2DOperation.DrawLogicalSprite>(call.Name);
                return true;
            default:
                throw new NotSupportedException($"Game Boy intrinsic lowering does not support {intrinsic.Operation} yet.");
        }
    }

    private void EmitSdkPluginOperation(
        FunctionSyntax function,
        FunctionCall call,
        TargetIntrinsicDescriptor intrinsic)
    {
        if (intrinsic.PluginOperation.CallKind != SdkPluginOperationCallKind.Statement)
        {
            throw new InvalidOperationException($"SDK plugin feature '{intrinsic.PluginOperation.OperationId}' cannot be emitted as a statement.");
        }

        var resolved = TargetIntrinsicResolver.ResolveCall(function, call, program.TargetIntrinsics);
        intrinsic.PluginTargetLowering.Lower(new SdkPluginTargetLoweringContext(
            program.TargetIntrinsics.TargetId,
            intrinsic.PluginOperation,
            resolved.RuntimeOperands.Select(operand => new SdkPluginRuntimeOperand(operand.Slot)).ToArray(),
            resolved.CompileTimeOperands.Select(operand => new SdkPluginCompileTimeOperand(operand.Slot, operand.Role, operand.Identifier, operand.Constant)).ToArray(),
            new SdkPluginTargetEmitter(bytes => builder.Emit(bytes.ToArray()))));
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

        if (program.SubroutineNames.Contains(function.Name))
        {
            EmitUserSubroutineCall(call, function);
            return true;
        }

        if (!userFunctionCallStack.Add(function.Name))
        {
            throw new InvalidOperationException($"Recursive Game Boy user function call '{function.Name}' is not supported.");
        }

        try
        {
            PushInlineVariableScope();
            EmitBlock(ParameterSubstitution.Substitute(function, call, "Game Boy"));
        }
        finally
        {
            PopInlineVariableScope();
            userFunctionCallStack.Remove(function.Name);
        }

        return true;
    }

    private void EmitUserSubroutineCall(FunctionCall call, FunctionSyntax function)
    {
        var arguments = BindCallArguments(function, call);
        foreach (var parameter in function.Parameters)
        {
            if (parameter.IsReceiver)
            {
                throw new InvalidOperationException($"Game Boy subroutine '{function.Name}' cannot use receiver parameter '{parameter.Name}' yet.");
            }

            var slot = EnsureSubroutineParameterSlot(function, parameter);
            EmitExpressionToStorage(arguments[parameter.Name], slot, parameter.Type);
        }

        sdkOperations.ConsumeSubroutineCall(function.Name);
        sdkAudioOperations.ConsumeSubroutineCall(function.Name);
        builder.JumpAbsolute(0xCD, SubroutineEntryLabel(function.Name)); // CALL nn
    }

    private string SubroutineEntryLabel(string functionName)
    {
        return UsesSubroutineTrampolines
            ? SubroutineTrampolineLabel(functionName)
            : SubroutineLabel(functionName);
    }

    private static string SubroutineLabel(string functionName) => $"user_fn_{functionName}";

    private static string SubroutineTrampolineLabel(string functionName) => $"user_fn_{functionName}_trampoline";

    private static bool IsRuntimeReadOnlyDataLabel(string label)
    {
        return label.StartsWith("map_row_", StringComparison.Ordinal)
               || label.StartsWith("background_stream_row_", StringComparison.Ordinal)
               || label.StartsWith("generated_rom_table_", StringComparison.Ordinal);
    }

    private static string ReadOnlyDataByteReaderLabel(string label) => $"read_data_{label}";

    private static string MusicPlayHelperLabel(string themeId) => $"music_play_fixed_{themeId}";

    private static string SoundEffectPlayHelperLabel(string soundId) => $"sfx_play_fixed_{soundId}";

    private const string AudioUpdateHelperLabel = "audio_update_fixed_helper";

    private BlockSyntax SubroutineBody(FunctionSyntax function)
    {
        var slotArguments = function.Parameters
            .Select(parameter => (ExpressionSyntax)new IdentifierSyntax(SubroutineParameterSlotName(function, parameter)))
            .ToArray();
        return ParameterSubstitution.Substitute(function, new FunctionCall(function.Name, slotArguments), "Game Boy");
    }

    private ushort EnsureSubroutineParameterSlot(FunctionSyntax function, ParameterSyntax parameter)
    {
        if (!IsByteBackedLocalType(parameter.Type))
        {
            throw new InvalidOperationException($"Game Boy subroutine '{function.Name}' parameter '{parameter.Name}' has unsupported type '{parameter.Type}'.");
        }

        var name = SubroutineParameterSlotName(function, parameter);
        return variables.TryGetValue(name, out var address)
            ? address
            : DeclareVariable(name, parameter.Type);
    }

    private IReadOnlyList<(string Name, bool HadPrevious, ushort PreviousAddress, bool HadPreviousType, string PreviousType)> PushSubroutineParameterAliases(FunctionSyntax function)
    {
        var aliases = new List<(string Name, bool HadPrevious, ushort PreviousAddress, bool HadPreviousType, string PreviousType)>();
        foreach (var parameter in function.Parameters)
        {
            if (parameter.IsReceiver)
            {
                continue;
            }

            var hadPrevious = variables.TryGetValue(parameter.Name, out var previousAddress);
            var hadPreviousType = variableTypes.TryGetValue(parameter.Name, out var previousType);
            aliases.Add((parameter.Name, hadPrevious, previousAddress, hadPreviousType, previousType ?? string.Empty));
            variables[parameter.Name] = EnsureSubroutineParameterSlot(function, parameter);
            variableTypes[parameter.Name] = parameter.Type;
        }

        return aliases;
    }

    private void PopSubroutineParameterAliases(IReadOnlyList<(string Name, bool HadPrevious, ushort PreviousAddress, bool HadPreviousType, string PreviousType)> aliases)
    {
        for (var index = aliases.Count - 1; index >= 0; index--)
        {
            var alias = aliases[index];
            if (alias.HadPrevious)
            {
                variables[alias.Name] = alias.PreviousAddress;
            }
            else
            {
                variables.Remove(alias.Name);
            }

            if (alias.HadPreviousType)
            {
                variableTypes[alias.Name] = alias.PreviousType;
            }
            else
            {
                variableTypes.Remove(alias.Name);
            }
        }
    }

    private static string SubroutineParameterSlotName(FunctionSyntax function, ParameterSyntax parameter)
    {
        return $"{function.Name}__p_{parameter.Name}";
    }

    private static IReadOnlyDictionary<string, ExpressionSyntax> BindCallArguments(FunctionSyntax function, FunctionCall call)
    {
        var parameters = function.Parameters.ToList();
        var parameterNames = parameters.Select(parameter => parameter.Name).ToHashSet(StringComparer.Ordinal);
        var arguments = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
        var positionalIndex = 0;
        var namedArgumentSeen = false;

        foreach (var argument in call.Parameters)
        {
            if (argument is NamedArgumentSyntax namedArgument)
            {
                namedArgumentSeen = true;
                if (!parameterNames.Contains(namedArgument.Name))
                {
                    throw new InvalidOperationException($"Game Boy target call '{call.Name}' has no parameter named '{namedArgument.Name}'.");
                }

                if (!arguments.TryAdd(namedArgument.Name, namedArgument.Expression))
                {
                    throw new InvalidOperationException($"Game Boy target call '{call.Name}' supplies parameter '{namedArgument.Name}' more than once.");
                }

                continue;
            }

            if (namedArgumentSeen)
            {
                throw new InvalidOperationException($"Game Boy target call '{call.Name}' cannot use positional arguments after named arguments.");
            }

            if (positionalIndex >= parameters.Count)
            {
                throw new InvalidOperationException($"Game Boy target expected at most {parameters.Count} argument(s) for '{call.Name}', but got {call.Parameters.Count()}.");
            }

            arguments.Add(parameters[positionalIndex].Name, argument);
            positionalIndex++;
        }

        var substitutions = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
        foreach (var parameter in parameters)
        {
            if (arguments.TryGetValue(parameter.Name, out var argument))
            {
                substitutions.Add(parameter.Name, argument);
                continue;
            }

            if (!parameter.DefaultValue.HasValue)
            {
                throw new InvalidOperationException($"Game Boy target expected argument for '{call.Name}' because parameter '{parameter.Name}' has no default value.");
            }

            var defaultValue = ConstantFolder.FoldConstants(SubstituteDefaultValue(parameter.DefaultValue.Value, substitutions));
            substitutions.Add(parameter.Name, defaultValue);
        }

        return substitutions;
    }

    private static ExpressionSyntax SubstituteDefaultValue(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, ExpressionSyntax> substitutions)
    {
        return expression switch
        {
            IdentifierSyntax identifier when substitutions.TryGetValue(identifier.Identifier, out var value) => value,
            BinaryExpressionSyntax binary => new BinaryExpressionSyntax(
                SubstituteDefaultValue(binary.Left, substitutions),
                SubstituteDefaultValue(binary.Right, substitutions),
                binary.Operator),
            CastSyntax cast => new CastSyntax(cast.Type, SubstituteDefaultValue(cast.Expression, substitutions)),
            NamedArgumentSyntax namedArgument => new NamedArgumentSyntax(namedArgument.Name, SubstituteDefaultValue(namedArgument.Expression, substitutions)),
            _ => expression,
        };
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
            throw new InvalidOperationException($"Recursive Game Boy user function call '{function.Name}' is not supported.");
        }

        try
        {
            EmitExpressionToA(ParameterSubstitution.SubstituteReturnExpression(function, call, "Game Boy"));
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

        GameBoyVideoProgram.RequireArity(call, 1);
        EmitExpressionToA(call.Parameters.Single());
        if (romLayout.TryReadOnlyDataPlacement(table.Label, out _))
        {
            builder.JumpAbsolute(0xCD, ReadOnlyDataByteReaderLabel(table.Label)); // CALL nn
            return true;
        }

        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        builder.LoadHl(table.Label);
        builder.AddHlDe();
        builder.LoadAFromHl();
        return true;
    }

    private bool TryEmitWordValueFunctionToStorage(FunctionCall call, ushort address, string targetType)
    {
        if (!program.Functions.TryGetValue(call.Name, out var function))
        {
            return false;
        }

        if (function.IsExtern)
        {
            var intrinsic = TargetIntrinsicResolver.Resolve(function, program.TargetIntrinsics);
            if (intrinsic.ReturnKind != TargetIntrinsicReturnKind.I16
                || !TryEmitTargetValueIntrinsic(call, preserveWordReturn: true))
            {
                return false;
            }

            builder.LoadAFromL();
            builder.StoreA(address);
            builder.LoadAFromH();
            builder.StoreA(HighAddress(address));
            return true;
        }

        if (!userFunctionCallStack.Add(function.Name))
        {
            throw new InvalidOperationException($"Recursive Game Boy user function call '{function.Name}' is not supported.");
        }

        try
        {
            EmitWordExpressionToStorage(
                ParameterSubstitution.SubstituteReturnExpression(function, call, "Game Boy"),
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

        var world = program.WorldMap
                    ?? throw new InvalidOperationException("camera_aabb_hit_top requires world_map collision flag data.");
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
                ParameterSubstitution.SubstituteReturnExpression(function, call, "Game Boy"),
                callStack);
        }
        finally
        {
            callStack.Remove(function.Name);
        }
    }

    private void EmitSdkByteExpressionToA(SdkByteExpression expression)
    {
        switch (expression)
        {
            case SdkByteExpression.Constant constant:
                builder.LoadAImmediate(constant.Value);
                break;
            case SdkByteExpression.Variable variable:
                EmitSdkStorageLocationToA(variable.Location);
                break;
            default:
                throw new InvalidOperationException($"Unsupported SDK byte expression '{expression.GetType().Name}'.");
        }
    }

    private void EmitSdkWordExpressionToA(SdkWordExpression expression, bool highByte)
    {
        switch (expression)
        {
            case SdkWordExpression.Constant constant:
                builder.LoadAImmediate(highByte ? (constant.Value >> 8) & 0xFF : constant.Value & 0xFF);
                break;
            case SdkWordExpression.Variable variable:
                EmitSdkWordStorageLocationToA(variable.Location, highByte);
                break;
            default:
                throw new InvalidOperationException($"Unsupported SDK word expression '{expression.GetType().Name}'.");
        }
    }

    private void EmitSdkWordStorageLocationToA(SdkStorageLocation location, bool highByte)
    {
        if (!highByte)
        {
            EmitSdkStorageLocationToA(location);
            return;
        }

        var type = location is SdkStorageLocation.RuntimeIndexedField runtimeIndexed
            ? VariableStorageType(IndexedMemberName(runtimeIndexed.BaseName, 0, runtimeIndexed.FieldName))
            : VariableStorageType(StorageKey(location));
        if (!IsWordBackedType(type))
        {
            builder.LoadAImmediate(0);
            return;
        }

        if (location is SdkStorageLocation.RuntimeIndexedField runtime)
        {
            EmitRuntimeIndexedMemberAddressToHl(runtime.BaseName, runtime.Index, runtime.FieldName);
            builder.IncrementHl();
            builder.LoadAFromHl();
            return;
        }

        builder.LoadA(HighAddress(VariableAddress(StorageKey(location))));
    }

    private void EmitSdkStorageLocationToA(SdkStorageLocation location)
    {
        switch (location)
        {
            case SdkStorageLocation.RuntimeIndexedField runtimeIndexed:
                EmitRuntimeIndexedMemberAddressToHl(runtimeIndexed.BaseName, runtimeIndexed.Index, runtimeIndexed.FieldName);
                builder.LoadAFromHl();
                break;
            default:
                builder.LoadA(VariableAddress(StorageKey(location)));
                break;
        }
    }
}
