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
    private void EmitCall(FunctionCall call)
    {
        if (IsResourceDeclarationCall(call))
        {
            return;
        }

        switch (call.Name)
        {
            case "hud_set_tile":
                _ = ConsumeSdkOperation<Sdk2DOperation.SetHudTile>(call.Name);
                break;
            case "tilemap_fill_column":
                sdkOperationLowerer.EmitTilemapFillColumn(call);
                break;
            case "map_stream_column":
                EmitNextSdkOperation<Sdk2DOperation.StreamMapColumn>(call.Name);
                break;
            case "camera_move_right":
                sdkOperationLowerer.EmitCameraMoveRight(call);
                break;
            case "camera_move_left":
                sdkOperationLowerer.EmitCameraMoveLeft(call);
                break;
            case "sprite_set":
                sdkOperationLowerer.EmitSpriteSet(call);
                break;
            case "scroll_set":
                sdkOperationLowerer.EmitScrollSet(call);
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

                throw new InvalidOperationException($"Unsupported Game Boy video API call '{call.Name}'.");
        }
    }

    private bool IsResourceDeclarationCall(FunctionCall call)
    {
        return program.Functions.TryGetValue(call.Name, out var function)
               && SdkResourceDeclarationResolver.TryResolve(function, out _, program.ResourceDeclarations);
    }

    private void EmitSdkOperation(Sdk2DOperation operation)
    {
        sdkOperationLowerer.Emit(operation);
    }

    private void BeginRuntimeIndexedAddressReuse(GameBoyRuntimeIndexedAddressReuse reuse)
    {
        if (runtimeIndexedAddressReuse is not null)
        {
            throw new InvalidOperationException("Game Boy runtime indexed address reuse scopes cannot be nested.");
        }

        EmitRuntimeIndexedAddressReuseOffset(reuse);
        runtimeIndexedAddressReuse = reuse;
    }

    private void EndRuntimeIndexedAddressReuse()
    {
        runtimeIndexedAddressReuse = null;
    }

    private void EmitSdkAudioOperation(SdkAudioOperation operation)
    {
        GameBoySdkAudioOperationLowerer.Emit(this, operation);
    }

    private void EmitNextSdkOperation<T>(string callName)
        where T : Sdk2DOperation
    {
        EmitSdkOperation(ConsumeSdkOperation<T>(callName));
    }

    private T ConsumeSdkOperation<T>(string callName)
        where T : Sdk2DOperation
    {
        var operation = sdkOperations.ConsumeOperation(callName);
        if (operation is T typed)
        {
            return typed;
        }

        throw new InvalidOperationException(
            $"Game Boy SDK call '{callName}' expected collected operation {typeof(T).Name}, got {operation.GetType().Name}.");
    }

    private void EmitNextSdkAudioOperation<T>(string callName)
        where T : SdkAudioOperation
    {
        var operation = ConsumeSdkAudioOperation<T>(callName);
        if (operation is SdkAudioOperation.PlayMusic play && UsesMusicPlayHelpers)
        {
            builder.JumpAbsolute(0xCD, MusicPlayHelperLabel(play.ThemeId)); // CALL nn
            return;
        }

        if (operation is SdkAudioOperation.PlaySoundEffect sfx && UsesSoundEffectPlayHelpers)
        {
            builder.JumpAbsolute(0xCD, SoundEffectPlayHelperLabel(sfx.SoundId)); // CALL nn
            return;
        }

        if (operation is SdkAudioOperation.UpdateAudio && UsesPackedCameraRuntime)
        {
            builder.JumpAbsolute(0xCD, GameBoyRomBuilder.WorldPackWaitAudioTickLabel);
            return;
        }

        if (operation is SdkAudioOperation.UpdateAudio && UsesAudioUpdateHelper)
        {
            builder.JumpAbsolute(0xCD, AudioUpdateHelperLabel); // CALL nn
            return;
        }

        EmitSdkAudioOperation(operation);
        EmitRestoreProgramTailBank();
    }

    private T ConsumeSdkAudioOperation<T>(string callName)
        where T : SdkAudioOperation
    {
        var operation = sdkAudioOperations.ConsumeOperation(callName);
        if (operation is T typed)
        {
            return typed;
        }

        throw new InvalidOperationException(
            $"Game Boy SDK audio call '{callName}' expected collected operation {typeof(T).Name}, got {operation.GetType().Name}.");
    }

    private void EnsureAllSdkOperationsConsumed()
    {
        sdkOperations.EnsureAllConsumed("Game Boy runtime");
    }

    private void EnsureAllSdkAudioOperationsConsumed()
    {
        sdkAudioOperations.EnsureAllConsumed("Game Boy runtime");
    }

    private void EmitRestoreProgramTailBank()
    {
        if (romLayout.ProgramTailBankCount == 0)
        {
            return;
        }

        builder.LoadA(GameBoyRuntimeMemoryLayout.Banking.ProgramCurrentBank);
        EmitSelectRomBankFromA();
    }
}
