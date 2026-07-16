using System.Globalization;
using RetroSharp.Core;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;
using RetroSharp.Sdk;

namespace RetroSharp.NES;

internal sealed partial class NesRuntimeCompiler
{
    private void EmitAudioInit()
    {
        builder.LoadAImmediate(0x0F);
        builder.StoreAAbsolute(0x4015);
        builder.LoadAImmediate(0x40);
        builder.StoreAAbsolute(0x4017);
        EmitAudioStateInitialization();
    }

    private void EmitMusicPlay(FunctionCall call)
    {
        var themeId = NesVideoProgram.IdentifierArg(call.Parameters.ElementAt(0), "music_play argument 1");
        if (!program.MusicAssets.TryGetValue(themeId, out var asset))
        {
            throw new InvalidOperationException($"Unknown NES music asset '{themeId}'. Declare it with music_asset(...).");
        }

        var label = NesRomBuilder.MusicDataLabel(asset.Name);
        builder.LoadAImmediateLabelLowByte(label, asset.OrderStartOffset);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Audio.MusicOrderPointerLow);
        builder.LoadAImmediateLabelHighByte(label, asset.OrderStartOffset);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Audio.MusicOrderPointerHigh);
        builder.LoadAImmediateLabelLowByte(label, asset.LoopOrderOffset);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Audio.MusicLoopPointerLow);
        builder.LoadAImmediateLabelHighByte(label, asset.LoopOrderOffset);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Audio.MusicLoopPointerHigh);
        builder.LoadAImmediate(0);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Audio.MusicTick);
    }

    private void EmitSoundEffectPlay(FunctionCall call)
    {
        var soundId = NesVideoProgram.IdentifierArg(call.Parameters.ElementAt(0), "sfx_play argument 1");
        if (!program.SoundEffectAssets.TryGetValue(soundId, out var asset))
        {
            throw new InvalidOperationException($"Unknown NES SFX asset '{soundId}'. Declare it with sfx_asset(...).");
        }

        // Arm the SFX engine only: point the cursor at the effect's first frame body and mark it
        // active. The next audio-update tick plays it. This must not touch the music order/body/tick
        // state, otherwise the background music desyncs on every trigger.
        var label = NesRomBuilder.SoundEffectDataLabel(asset.Name);
        builder.LoadAImmediateLabelLowByte(label);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Audio.SfxPointerLow);
        builder.LoadAImmediateLabelHighByte(label);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Audio.SfxPointerHigh);
        builder.LoadAImmediate(asset.LingerFrames);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Audio.SfxLinger);
        builder.LoadAImmediate(1);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Audio.SfxActive);
    }

    private void EmitMusicStop()
    {
        builder.LoadAImmediate(0);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Audio.MusicOrderPointerHigh);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Audio.MusicTick);
        builder.StoreAAbsolute(0x4015);
    }

    private void EmitAudioUpdate()
    {
        if (usePackedCamera)
        {
            builder.IncrementAbsolute(NesRuntimeMemoryLayout.WorldPack.AudioTickCount);
        }
        var hasSfx = program.SoundEffectAssetsInLoadOrder.Count > 0;
        var doneLabel = builder.CreateLabel("nes_audio_done");
        var processOrderLabel = builder.CreateLabel("nes_audio_process_order");
        var hasActiveMusicLabel = builder.CreateLabel("nes_audio_active");
        var tickExpiredLabel = builder.CreateLabel("nes_audio_tick_expired");
        var hasBodyLabel = builder.CreateLabel("nes_audio_has_body");
        var tickNonZeroLabel = builder.CreateLabel("nes_audio_tick_nonzero");

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Audio.MusicOrderPointerHigh);
        builder.BranchRelative(0xD0, hasActiveMusicLabel); // BNE hasActiveMusicLabel (LDA sets Z)
        builder.JumpAbsolute(doneLabel);
        builder.Label(hasActiveMusicLabel);

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Audio.MusicTick);
        builder.BranchRelative(0xF0, processOrderLabel); // BEQ processOrderLabel (LDA sets Z)
        builder.DecrementZeroPage(NesRuntimeMemoryLayout.Audio.MusicTick);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Audio.MusicTick);
        builder.BranchRelative(0xF0, tickExpiredLabel); // BEQ tickExpiredLabel (LDA sets Z)
        builder.JumpAbsolute(doneLabel);
        builder.Label(tickExpiredLabel);

        builder.Label(processOrderLabel);
        builder.LoadYImmediate(0);
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.Audio.MusicOrderPointerLow);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Audio.MusicBodyPointerLow);
        builder.IncrementY();
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.Audio.MusicOrderPointerLow);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Audio.MusicBodyPointerHigh);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Audio.MusicBodyPointerLow);
        builder.OrZeroPage(NesRuntimeMemoryLayout.Audio.MusicBodyPointerHigh);
        builder.BranchRelative(0xD0, hasBodyLabel); // BNE hasBodyLabel (ORA sets Z)
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Audio.MusicLoopPointerLow);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Audio.MusicOrderPointerLow);
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Audio.MusicLoopPointerHigh);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Audio.MusicOrderPointerHigh);
        builder.JumpAbsolute(processOrderLabel);

        builder.Label(hasBodyLabel);
        builder.IncrementY();
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.Audio.MusicOrderPointerLow);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Audio.MusicTick);
        EmitAdvanceMusicOrderPointer();
        EmitPlayApuBody(sfxPath: false);

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Audio.MusicTick);
        builder.BranchRelative(0xD0, tickNonZeroLabel); // BNE tickNonZeroLabel (LDA sets Z)
        builder.JumpAbsolute(processOrderLabel);
        builder.Label(tickNonZeroLabel);

        builder.Label(doneLabel);

        if (hasSfx)
        {
            var endLabel = builder.CreateLabel("nes_audio_end");
            EmitSoundEffectUpdate(endLabel);
            builder.Label(endLabel);
        }
    }

    // Compact one-shot pulse 1 SFX engine, ticked right after the music engine each audio-update tick.
    // It walks one frame body per tick from a zero-page cursor, keeping state fully independent from
    // the music sequencer (it never touches the music order/body/tick pointers, which previously
    // corrupted the background music). A 0xFF marker byte ends the effect.
    private void EmitSoundEffectUpdate(string endLabel)
    {
        var stopLabel = builder.CreateLabel("nes_sfx_stop");
        var playFrameLabel = builder.CreateLabel("nes_sfx_play");
        var noCarryLabel = builder.CreateLabel("nes_sfx_no_carry");

        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Audio.SfxActive);
        builder.BranchRelative(0xF0, endLabel); // BEQ endLabel (no effect playing; LDA sets Z)

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Audio.SfxPointerLow);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Audio.MusicBodyPointerLow);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Audio.SfxPointerHigh);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Audio.MusicBodyPointerHigh);

        builder.LoadYImmediate(0);
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.Audio.MusicBodyPointerLow);
        builder.CompareImmediate(0xFF);
        builder.BranchRelative(0xD0, playFrameLabel); // BNE playFrameLabel (has a frame body)

        // Register frames exhausted: keep pulse 1 owned by the effect (music still muted) for LingerFrames
        // more ticks so the note rings out fully, then release the channel.
        builder.LoadAAbsolute(NesRuntimeMemoryLayout.Audio.SfxLinger);
        builder.BranchRelative(0xF0, stopLabel); // BEQ stopLabel (linger done; LDA sets Z)
        builder.DecrementAbsolute(NesRuntimeMemoryLayout.Audio.SfxLinger);
        builder.JumpAbsolute(endLabel);

        // Play this frame's body on pulse 1 (X = 2, the effect owns and writes the channel directly).
        // The shared body player returns Y = bytes consumed (1 + 2 * writeCount), which advances the
        // cursor to the next frame body.
        builder.Label(playFrameLabel);
        EmitPlayApuBody(sfxPath: true);
        builder.TransferYToA();
        builder.ClearCarry();
        builder.AddZeroPage(NesRuntimeMemoryLayout.Audio.SfxPointerLow);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Audio.SfxPointerLow);
        builder.BranchRelative(0x90, noCarryLabel); // BCC noCarryLabel
        builder.IncrementZeroPage(NesRuntimeMemoryLayout.Audio.SfxPointerHigh);
        builder.Label(noCarryLabel);
        builder.JumpAbsolute(endLabel);

        builder.Label(stopLabel);
        builder.LoadAImmediate(0);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Audio.SfxActive);
        // Release pulse 1 back to the BGM: restore its full shadowed state ($4000-$4003) so the channel
        // no longer carries the effect's duty/volume/sweep/frequency. Restoring only $4001 (sweep) left
        // the effect's $4000 (duty + volume/envelope) on the channel, because the BGM can go dozens of
        // frames without rewriting pulse 1. The four stores all land within this frame, before the next
        // APU frame-sequencer clock re-reads $4000, so the descending order (which writes the $4003
        // length/trigger first) still ends up with the BGM's final register state.
        var restoreLoopLabel = builder.CreateLabel("nes_sfx_restore");
        builder.LoadXImmediate(3);
        builder.Label(restoreLoopLabel);
        builder.LoadAAbsoluteX(NesRuntimeMemoryLayout.Audio.Pulse1ShadowBase);
        builder.StoreAAbsoluteX(0x4000);
        builder.DecrementX();
        builder.BranchRelative(0x10, restoreLoopLabel); // BPL restoreLoopLabel
    }

    // Plays the APU trace body pointed to by MusicBodyPointer via the shared subroutine. X selects the
    // pulse 1 arbitration mode read by the body writer: for the SFX path X = 2 (the effect owns pulse 1
    // and writes it directly), for the music path X = SfxActive (0 = write pulse 1 and shadow it,
    // non-zero = an effect owns pulse 1 so only shadow the BGM's intended writes).
    private void EmitPlayApuBody(bool sfxPath)
    {
        if (sfxPath)
        {
            builder.LoadXImmediate(2);
        }
        else if (program.SoundEffectAssetsInLoadOrder.Count > 0)
        {
            builder.LoadXAbsolute(NesRuntimeMemoryLayout.Audio.SfxActive);
        }
        else
        {
            builder.LoadXImmediate(0);
        }

        builder.CallSubroutine(ApuBodySubroutineLabel);
        apuBodySubroutineReferenced = true;
    }

    public void EmitReferencedSubroutines()
    {
        if (apuBodySubroutineReferenced)
        {
            EmitApuBodySubroutine();
        }

        sdkOperationLowerer.EmitReferencedSubroutines();
    }

    private void EmitApuBodySubroutine()
    {
        var muteSfxChannel = program.SoundEffectAssetsInLoadOrder.Count > 0;
        var commandLoopLabel = builder.CreateLabel("nes_apu_body_loop");
        var afterBodyLabel = builder.CreateLabel("nes_apu_body_after");

        builder.Label(ApuBodySubroutineLabel);
        builder.LoadYImmediate(0);
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.Audio.MusicBodyPointerLow);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Audio.MusicCommandCount);
        builder.IncrementY();
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Audio.MusicCommandCount);
        builder.BranchRelative(0xF0, afterBodyLabel); // BEQ afterBodyLabel (empty frame body)

        builder.Label(commandLoopLabel);
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.Audio.MusicBodyPointerLow);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        builder.IncrementY();
        builder.LoadAIndirectY(NesRuntimeMemoryLayout.Audio.MusicBodyPointerLow);
        builder.IncrementY();
        EmitNesApuRegisterWrite(muteSfxChannel);
        builder.DecrementZeroPage(NesRuntimeMemoryLayout.Audio.MusicCommandCount);
        builder.BranchRelative(0xD0, commandLoopLabel); // BNE commandLoopLabel

        builder.Label(afterBodyLabel);
        builder.Return();
    }

    private void EmitAdvanceMusicOrderPointer()
    {
        var noCarryLabel = builder.CreateLabel("nes_audio_order_no_carry");
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Audio.MusicOrderPointerLow);
        builder.ClearCarry();
        builder.AddImmediate(3);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Audio.MusicOrderPointerLow);
        builder.BranchRelative(0x90, noCarryLabel); // BCC noCarryLabel
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Audio.MusicOrderPointerHigh);
        builder.ClearCarry();
        builder.AddImmediate(1);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Audio.MusicOrderPointerHigh);
        builder.Label(noCarryLabel);
    }

    private void EmitNesApuRegisterWrite(bool muteSfxChannel)
    {
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionColumnScratch);
        builder.StoreYZeroPage(NesRuntimeMemoryLayout.Runtime.SpriteFrameScratch);

        string? skipWriteLabel = null;
        if (muteSfxChannel)
        {
            var writeHwLabel = builder.CreateLabel("nes_apu_write");
            skipWriteLabel = builder.CreateLabel("nes_apu_skip_write");
            // Pulse 1 arbitration keyed on X: 2 = the effect writing its own channel (write hardware,
            // do not shadow), 0 = BGM with no active effect (write hardware and shadow), 1 = BGM while
            // an effect owns pulse 1 (shadow only, hardware suppressed). Non-pulse-1 registers ($04+)
            // always write hardware. Shadowing the BGM's intended pulse 1 values lets the channel be
            // restored to the BGM when the effect ends, so no effect residue is left behind.
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
            builder.CompareImmediate(0x04);
            builder.BranchRelative(0xB0, writeHwLabel);   // BCS writeHwLabel (offset >= 4, other channel)
            builder.CompareXImmediate(2);
            builder.BranchRelative(0xF0, writeHwLabel);   // BEQ writeHwLabel (X == 2, effect writes pulse 1)
            builder.LoadYZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
            builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionColumnScratch);
            builder.StoreAAbsoluteY(NesRuntimeMemoryLayout.Audio.Pulse1ShadowBase); // shadow[offset] = BGM's intended value
            builder.CompareXImmediate(1);
            builder.BranchRelative(0xF0, skipWriteLabel);  // BEQ skipWriteLabel (X == 1, effect owns pulse 1)
            builder.Label(writeHwLabel);
        }

        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.IndexScratch);
        builder.LoadAImmediate(0x40);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        builder.LoadYImmediate(0);
        builder.LoadAZeroPage(NesRuntimeMemoryLayout.Runtime.CollisionColumnScratch);
        builder.StoreAIndirectY(NesRuntimeMemoryLayout.Runtime.IndexScratch);

        if (skipWriteLabel is not null)
        {
            builder.Label(skipWriteLabel);
        }

        builder.LoadYZeroPage(NesRuntimeMemoryLayout.Runtime.SpriteFrameScratch);
    }

}
