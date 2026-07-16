using System.Globalization;
using RetroSharp.Core;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;
using RetroSharp.Sdk;

namespace RetroSharp.NES;

internal sealed partial class NesRuntimeCompiler
{
    private bool TryEmitTargetIntrinsic(FunctionCall call)
    {
        if (!program.Functions.TryGetValue(call.Name, out var function) || !function.IsExtern)
        {
            return false;
        }

        var intrinsic = TargetIntrinsicResolver.Resolve(function, program.TargetIntrinsics);
        NesVideoProgram.RequireArity(call, intrinsic.Arity);
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
                EmitSdkOperation<Sdk2DOperation.WaitFrame>(call.Name);
                return true;
            case TargetIntrinsicOperation.PollInput:
                EmitSdkOperation<Sdk2DOperation.PollInput>(call.Name);
                return true;
            case TargetIntrinsicOperation.UpdateAudio:
                EmitAudioUpdate();
                return true;
            case TargetIntrinsicOperation.InitializeAudio:
                EmitAudioInit();
                return true;
            case TargetIntrinsicOperation.PlayMusic:
                EmitMusicPlay(call);
                return true;
            case TargetIntrinsicOperation.PlaySoundEffect:
                EmitSoundEffectPlay(call);
                return true;
            case TargetIntrinsicOperation.StopMusic:
                EmitMusicStop();
                return true;
            case TargetIntrinsicOperation.InitializeCamera:
                sdkOperationLowerer.EmitCameraInit(call);
                return true;
            case TargetIntrinsicOperation.SetCameraPosition:
                EmitSdkOperation<Sdk2DOperation.SetCameraPosition>(call.Name);
                return true;
            case TargetIntrinsicOperation.ApplyCamera:
                EmitSdkOperation<Sdk2DOperation.ApplyCamera>(call.Name);
                return true;
            case TargetIntrinsicOperation.CameraVerticalScrollMax:
                sdkOperationLowerer.EmitCameraVerticalScrollMax();
                return true;
            case TargetIntrinsicOperation.DrawLogicalSprite:
                EmitSdkOperation<Sdk2DOperation.DrawLogicalSprite>(call.Name);
                return true;
            default:
                throw new NotSupportedException($"NES intrinsic lowering does not support {intrinsic.Operation} yet.");
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

    private void EmitValueCallToA(FunctionCall call)
    {
        if (sdkOperationLowerer.TryEmitCompatibilityValueCall(call))
        {
            return;
        }

        switch (call.Name)
        {
            default:
                if (TryEmitTargetValueIntrinsic(call))
                {
                    break;
                }

                if (TryEmitUserValueFunction(call))
                {
                    break;
                }

                throw new InvalidOperationException($"Unsupported NES value API call '{call.Name}'.");
        }
    }

    private bool TryEmitTargetValueIntrinsic(FunctionCall call)
    {
        if (!program.Functions.TryGetValue(call.Name, out var function) || !function.IsExtern)
        {
            return false;
        }

        var intrinsic = TargetIntrinsicResolver.Resolve(function, program.TargetIntrinsics);
        if (intrinsic.IsPluginOperation)
        {
            return false;
        }

        var completeWordReturn = false;
        switch (intrinsic.Operation)
        {
            case TargetIntrinsicOperation.CameraAabbTiles:
                NesVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitSdkOperation<Sdk2DOperation.CameraAabbTiles>(call.Name);
                break;
            case TargetIntrinsicOperation.CameraAabbHitTop:
                NesVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitSdkOperation<Sdk2DOperation.CameraAabbHitTop>(call.Name);
                completeWordReturn = true;
                break;
            case TargetIntrinsicOperation.CameraScreenAabbTiles:
                NesVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitSdkOperation<Sdk2DOperation.CameraScreenAabbTiles>(call.Name);
                break;
            case TargetIntrinsicOperation.CameraScreenAabbHitTop:
                NesVideoProgram.RequireArity(call, intrinsic.Arity);
                EmitSdkOperation<Sdk2DOperation.CameraScreenAabbHitTop>(call.Name);
                break;
            case TargetIntrinsicOperation.CameraVerticalScrollMax:
                NesVideoProgram.RequireArity(call, intrinsic.Arity);
                sdkOperationLowerer.EmitCameraVerticalScrollMax();
                break;
            case TargetIntrinsicOperation.ButtonDown:
                NesVideoProgram.RequireArity(call, intrinsic.Arity);
                sdkOperationLowerer.EmitButtonDown(call);
                break;
            case TargetIntrinsicOperation.ButtonJustPressed:
                NesVideoProgram.RequireArity(call, intrinsic.Arity);
                sdkOperationLowerer.EmitButtonJustPressed(call);
                break;
            case TargetIntrinsicOperation.ButtonJustReleased:
                NesVideoProgram.RequireArity(call, intrinsic.Arity);
                sdkOperationLowerer.EmitButtonJustReleased(call);
                break;
            case TargetIntrinsicOperation.ButtonHoldTicks:
                NesVideoProgram.RequireArity(call, intrinsic.Arity);
                sdkOperationLowerer.EmitButtonHoldTicks(call);
                break;
            case TargetIntrinsicOperation.ReadSpriteWidth:
                NesVideoProgram.RequireArity(call, intrinsic.Arity);
                _ = TargetIntrinsicResolver.ResolveCall(function, call, program.TargetIntrinsics);
                sdkOperationLowerer.EmitSpriteWidth(call);
                break;
            case TargetIntrinsicOperation.ReadAnimationFrame:
                NesVideoProgram.RequireArity(call, intrinsic.Arity);
                _ = TargetIntrinsicResolver.ResolveCall(function, call, program.TargetIntrinsics);
                sdkOperationLowerer.EmitAnimationFrame(call);
                break;
            default:
                return false;
        }

        if (intrinsic.ReturnKind == TargetIntrinsicReturnKind.I16 && !completeWordReturn)
        {
            builder.LoadXImmediate(0);
        }

        return true;
    }

}
