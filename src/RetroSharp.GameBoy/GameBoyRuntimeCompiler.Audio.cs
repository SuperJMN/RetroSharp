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
    internal void EmitInitializeAudio()
    {
        builder.LoadAImmediate(0x80);
        builder.StoreHighRamA(0x26);                // NR52: enable APU
        builder.LoadAImmediate(0xFF);
        builder.StoreHighRamA(0x25);                // NR51: route channels
        builder.LoadAImmediate(0x77);
        builder.StoreHighRamA(0x24);                // NR50: balanced master volume

        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicActive);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicRow);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicTick);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicDataPointerLow);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicDataPointerHigh);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicCurrentPointerLow);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicCurrentPointerHigh);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicScratchPointerLow);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicScratchPointerHigh);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicTicksPerRow);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicRowHigh);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicDataBank);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicCurrentBank);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicScratchBank);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxActive);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxTick);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxDataPointerLow);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxDataPointerHigh);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxCurrentPointerLow);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxCurrentPointerHigh);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxDataBank);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxCurrentBank);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.Channel1ShadowNr10);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.Channel1ShadowNr11);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.Channel1ShadowNr12);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.Channel1ShadowNr13);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.Channel1ShadowNr14);
        if (romLayout.UsesBankedMusic)
        {
            builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicDataCursorBank);
            builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxDataCursorBank);
        }

        EmitClearMusicRowCache();
    }

    internal void EmitPlayMusic(SdkAudioOperation.PlayMusic operation)
    {
        if (!program.MusicAssets.TryGetValue(operation.ThemeId, out var asset))
        {
            throw new InvalidOperationException($"Unknown Game Boy music asset '{operation.ThemeId}'. Declare it before playback.");
        }

        if (romLayout.UsesBankedMusic)
        {
            var placement = romLayout.MusicPlacement(operation.ThemeId);
            builder.LoadHl(placement.Address);
            builder.Emit(0x7D);                         // LD A,L
            builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicDataPointerLow);
            builder.Emit(0x7C);                         // LD A,H
            builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicDataPointerHigh);
            builder.LoadAImmediate(placement.Bank);
            builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicDataBank);
            builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicCurrentBank);
            EmitSelectRomBankFromA();
        }
        else
        {
            builder.LoadHl(GameBoyRomBuilder.MusicLabel(operation.ThemeId));
            builder.Emit(0x7D);                         // LD A,L
            builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicDataPointerLow);
            builder.Emit(0x7C);                         // LD A,H
            builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicDataPointerHigh);
        }

        builder.LoadAImmediate(asset.Kind == GameBoyMusicAssetKind.ApuTrace ? MusicActiveApuTrace : MusicActiveUgeRows);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicActive);
        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicRow);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicRowHigh);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicTick);
        if (asset.Kind == GameBoyMusicAssetKind.ApuTrace)
        {
            EmitResetApuTracePointerToStart();
        }
        else
        {
            EmitResetMusicRowPointer();
        }
    }

    internal void EmitPlaySoundEffect(SdkAudioOperation.PlaySoundEffect operation)
    {
        if (!program.SoundEffectAssets.TryGetValue(operation.SoundId, out _))
        {
            throw new InvalidOperationException($"Unknown Game Boy SFX asset '{operation.SoundId}'. Declare it before playback.");
        }

        if (romLayout.UsesBankedMusic)
        {
            var placement = romLayout.SoundEffectPlacement(operation.SoundId);
            builder.LoadHl(placement.Address);
            builder.Emit(0x7D);                         // LD A,L
            builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxDataPointerLow);
            builder.Emit(0x7C);                         // LD A,H
            builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxDataPointerHigh);
            builder.LoadAImmediate(placement.Bank);
            builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxDataBank);
            builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxCurrentBank);
            EmitSelectRomBankFromA();
        }
        else
        {
            builder.LoadHl(GameBoyRomBuilder.SoundEffectLabel(operation.SoundId));
            builder.Emit(0x7D);                         // LD A,L
            builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxDataPointerLow);
            builder.Emit(0x7C);                         // LD A,H
            builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxDataPointerHigh);
        }

        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxActive);
        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxTick);
        EmitResetSfxApuTracePointerToStart();
    }

    internal void EmitStopMusic()
    {
        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicActive);
        builder.StoreHighRamA(0x12);                // NR12: silence CH1 envelope
        builder.StoreHighRamA(0x17);                // NR22: silence CH2 envelope
        builder.StoreHighRamA(0x1A);                // NR30: disable CH3
        builder.StoreHighRamA(0x21);                // NR42: silence CH4 envelope
    }

    internal void EmitUpdateAudio()
    {
        var endLabel = builder.CreateLabel("audio_update_end");
        var sfxLabel = builder.CreateLabel("audio_update_sfx");
        var apuTraceLabel = builder.CreateLabel("audio_update_apu_trace");
        var loadRowLabel = builder.CreateLabel("audio_update_load_row");
        var rowReadyLabel = builder.CreateLabel("audio_update_row_ready");
        var rowHighMatchesLabel = builder.CreateLabel("audio_update_row_high_matches");
        var resetRowLabel = builder.CreateLabel("audio_update_reset_row");
        var rowIncrementDoneLabel = builder.CreateLabel("audio_update_row_increment_done");

        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.MusicActive);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, sfxLabel);       // JP Z,sfxLabel
        builder.CompareImmediate(MusicActiveApuTrace);
        builder.JumpAbsolute(0xCA, apuTraceLabel);  // JP Z,apuTraceLabel

        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.MusicTick);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, loadRowLabel);   // JP Z,loadRowLabel
        builder.SubtractAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicTick);
        builder.JumpAbsolute(sfxLabel);

        builder.Label(loadRowLabel);
        EmitLoadMusicPointerToHl(GameBoyRuntimeMemoryLayout.Audio.MusicDataCursorBank);
        builder.LoadAFromHl();                      // ticks per row
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicTicksPerRow);
        EmitAdvanceMusicHl(GameBoyRuntimeMemoryLayout.Audio.MusicDataCursorBank);
        builder.LoadAFromHl();                      // row count low
        builder.LoadCFromA();
        EmitAdvanceMusicHl(GameBoyRuntimeMemoryLayout.Audio.MusicDataCursorBank);
        builder.LoadAFromHl();                      // row count high
        builder.LoadBFromA();

        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.MusicRowHigh);
        builder.CompareB();
        builder.JumpAbsolute(0xDA, rowReadyLabel);  // JP C,rowReadyLabel
        builder.JumpAbsolute(0xCA, rowHighMatchesLabel); // JP Z,rowHighMatchesLabel
        builder.JumpAbsolute(resetRowLabel);

        builder.Label(rowHighMatchesLabel);
        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.MusicRow);
        builder.Emit(0xB9);                         // CP C
        builder.JumpAbsolute(0xDA, rowReadyLabel);  // JP C,rowReadyLabel
        builder.Label(resetRowLabel);
        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicRow);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicRowHigh);
        EmitResetMusicRowPointer();

        builder.Label(rowReadyLabel);
        EmitLoadMusicRowEventsToCache();
        builder.LoadHl(GameBoyRuntimeMemoryLayout.Audio.MusicRowCacheStart);

        EmitWriteMusicRegister(0x11);
        EmitWriteMusicRegister(0x12);
        EmitWriteMusicRegister(0x13);
        EmitWriteMusicRegister(0x14);
        EmitWriteMusicRegister(0x16);
        EmitWriteMusicRegister(0x17);
        EmitWriteMusicRegister(0x18);
        EmitWriteMusicRegister(0x19);
        EmitWriteWaveChannelFromRow();
        EmitWriteMusicRegister(0x21);
        EmitWriteMusicRegister(0x22);
        EmitWriteMusicRegister(0x23);

        EmitClearMusicRowTriggerBits();

        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.MusicRow);
        builder.AddAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicRow);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, rowIncrementDoneLabel); // JP NZ,rowIncrementDoneLabel
        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.MusicRowHigh);
        builder.AddAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicRowHigh);
        builder.Label(rowIncrementDoneLabel);

        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.MusicTicksPerRow);
        builder.SubtractAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicTick);
        builder.JumpAbsolute(sfxLabel);

        builder.Label(apuTraceLabel);
        EmitUpdateApuTrace(sfxLabel);

        builder.Label(sfxLabel);
        EmitUpdateSoundEffectApuTrace(endLabel);
        builder.Label(endLabel);
    }

    private void EmitUpdateApuTrace(string endLabel)
    {
        var processLabel = builder.CreateLabel("audio_update_apu_process");
        var commandLoopLabel = builder.CreateLabel("audio_update_apu_command_loop");
        var waveBlockLabel = builder.CreateLabel("audio_update_apu_wave_block");
        var commandDoneLabel = builder.CreateLabel("audio_update_apu_command_done");
        var loopLabel = builder.CreateLabel("audio_update_apu_loop");

        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.MusicTick);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, processLabel);   // JP Z,processLabel
        builder.SubtractAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicTick);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, endLabel);       // JP NZ,endLabel

        // Read the next order entry: { bodyOffset (u16 from data start), waitAfter }.
        builder.Label(processLabel);
        EmitLoadMusicCurrentPointerToHl();          // HL -> order entry
        builder.LoadAFromHl();                      // body offset low
        builder.LoadEFromA();
        EmitAdvanceMusicHl();
        builder.LoadAFromHl();                      // body offset high
        builder.LoadDFromA();
        builder.Emit(0xB3);                         // OR E: body offset zero is the loop sentinel
        builder.JumpAbsolute(0xCA, loopLabel);      // JP Z,loopLabel
        EmitAdvanceMusicHl();                       // HL -> waitAfter
        builder.LoadAFromHl();
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicTick);
        EmitAdvanceMusicHl();                       // HL -> next order entry
        EmitStoreHlToMusicCurrentPointer();

        // Resolve the pooled group body via a transient cursor bank, leaving the order-stream bank
        // (GameBoyRuntimeMemoryLayout.Audio.MusicCurrentBank) untouched so the next entry is still read from the right bank.
        EmitLoadMusicDataOffsetToHl(GameBoyRuntimeMemoryLayout.Audio.MusicDataCursorBank);
        builder.LoadAFromHl();                      // command count
        builder.LoadBFromA();
        EmitAdvanceMusicHl(GameBoyRuntimeMemoryLayout.Audio.MusicDataCursorBank); // HL -> first command

        builder.Label(commandLoopLabel);
        builder.LoadAFromHl();                      // register offset or wave RAM block command
        builder.CompareImmediate(ApuTraceWaveRamBlockCommand);
        builder.JumpAbsolute(0xCA, waveBlockLabel); // JP Z,waveBlockLabel
        builder.LoadCFromA();
        EmitAdvanceMusicHl(GameBoyRuntimeMemoryLayout.Audio.MusicDataCursorBank);
        builder.LoadAFromHl();                      // value
        EmitMusicApuRegisterWrite();                // LDH (C),A, muting/shadowing channel 1 for SFX
        EmitAdvanceMusicHl(GameBoyRuntimeMemoryLayout.Audio.MusicDataCursorBank);
        builder.JumpAbsolute(commandDoneLabel);

        builder.Label(waveBlockLabel);
        EmitAdvanceMusicHl(GameBoyRuntimeMemoryLayout.Audio.MusicDataCursorBank);
        for (var i = 0; i < 16; i++)
        {
            builder.LoadAFromHl();
            builder.StoreHighRamA((byte)(0x30 + i));
            EmitAdvanceMusicHl(GameBoyRuntimeMemoryLayout.Audio.MusicDataCursorBank);
        }

        builder.Label(commandDoneLabel);
        builder.Emit(0x05);                         // DEC B
        builder.LoadAFromB();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, commandLoopLabel); // JP NZ,commandLoopLabel

        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.MusicTick);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, processLabel);   // JP Z,processLabel: drain zero-wait order entries
        builder.JumpAbsolute(endLabel);

        builder.Label(loopLabel);
        EmitResetApuTracePointerToLoop();
        builder.JumpAbsolute(endLabel);
    }

    // Writes the music's APU register value (A) to register offset (C). Without SFX assets this is a
    // plain LDH (C),A. With SFX, channel 1 ($FF10-$FF14) gets priority for effects: the music always
    // shadows its full channel 1 state (so the channel can be restored when an effect ends) and, while
    // an effect owns channel 1 (SfxActive != 0), the music's channel 1 writes are suppressed so the
    // effect note is not stomped. Every other register (channels 2-4 and the globals NR50/NR51/NR52) is
    // written normally. Uses D as a scratch for the value; D is free inside the music command loop
    // (only B/C/HL are live there), and HL (the data cursor) is preserved across the shadow store.
    private void EmitMusicApuRegisterWrite()
    {
        if (program.SoundEffectAssetsInLoadOrder.Count == 0)
        {
            builder.StoreHighRamCFromA();           // LDH (C),A
            return;
        }

        var writeHwLabel = builder.CreateLabel("music_apu_write_hw");
        var skipLabel = builder.CreateLabel("music_apu_skip");

        builder.LoadDFromA();                       // D = value (A/flags get clobbered)
        builder.LoadAFromC();                       // A = register offset
        builder.SubtractAImmediate(0x10);           // A = offset - $10
        builder.CompareImmediate(0x05);             // carry set if A < 5 (channel 1: offsets $10..$14)
        builder.JumpRelative(0x30, writeHwLabel);   // JR NC -> not channel 1, write hardware

        // Channel 1: shadow the value at $C200 + offset (== $C200 + C). The shadow page low byte equals
        // the register offset, so the whole NR10-NR14 state is captured with no per-register branching.
        builder.PushHl();                           // preserve the music data cursor
        builder.Emit(0x26, GameBoyRuntimeMemoryLayout.Audio.Channel1ShadowPageHigh); // LD H, $C2
        builder.Emit(0x69);                         // LD L, C
        builder.Emit(0x72);                         // LD (HL), D  -> shadow[$C200+offset] = value
        builder.PopHl();

        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.SfxActive);
        builder.CompareImmediate(0x00);
        builder.JumpRelative(0x20, skipLabel);      // JR NZ -> SFX owns channel 1, skip hardware write

        builder.Label(writeHwLabel);
        builder.LoadAFromD();                       // A = value
        builder.StoreHighRamCFromA();               // LDH (C),A

        builder.Label(skipLabel);
    }

    private void EmitUpdateSoundEffectApuTrace(string endLabel)
    {
        var processLabel = builder.CreateLabel("sfx_update_apu_process");
        var commandLoopLabel = builder.CreateLabel("sfx_update_apu_command_loop");
        var waveBlockLabel = builder.CreateLabel("sfx_update_apu_wave_block");
        var commandDoneLabel = builder.CreateLabel("sfx_update_apu_command_done");
        var stopLabel = builder.CreateLabel("sfx_update_stop");

        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.SfxActive);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, endLabel);       // JP Z,endLabel

        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.SfxTick);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, processLabel);   // JP Z,processLabel
        builder.SubtractAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxTick);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, endLabel);       // JP NZ,endLabel

        builder.Label(processLabel);
        EmitLoadSfxCurrentPointerToHl();            // HL -> order entry
        builder.LoadAFromHl();                      // body offset low
        builder.LoadEFromA();
        EmitAdvanceSfxHl();
        builder.LoadAFromHl();                      // body offset high
        builder.LoadDFromA();
        builder.Emit(0xB3);                         // OR E: body offset zero is the one-shot sentinel
        builder.JumpAbsolute(0xCA, stopLabel);      // JP Z,stopLabel
        EmitAdvanceSfxHl();                         // HL -> waitAfter
        builder.LoadAFromHl();
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxTick);
        EmitAdvanceSfxHl();                         // HL -> next order entry
        EmitStoreHlToSfxCurrentPointer();

        EmitLoadSfxDataOffsetToHl(GameBoyRuntimeMemoryLayout.Audio.SfxDataCursorBank);
        builder.LoadAFromHl();                      // command count
        builder.LoadBFromA();
        EmitAdvanceSfxHl(GameBoyRuntimeMemoryLayout.Audio.SfxDataCursorBank); // HL -> first command

        builder.Label(commandLoopLabel);
        builder.LoadAFromHl();                      // register offset or wave RAM block command
        builder.CompareImmediate(ApuTraceWaveRamBlockCommand);
        builder.JumpAbsolute(0xCA, waveBlockLabel); // JP Z,waveBlockLabel
        builder.LoadCFromA();
        EmitAdvanceSfxHl(GameBoyRuntimeMemoryLayout.Audio.SfxDataCursorBank);
        builder.LoadAFromHl();                      // value
        builder.StoreHighRamCFromA();               // LDH (C),A
        EmitAdvanceSfxHl(GameBoyRuntimeMemoryLayout.Audio.SfxDataCursorBank);
        builder.JumpAbsolute(commandDoneLabel);

        builder.Label(waveBlockLabel);
        EmitAdvanceSfxHl(GameBoyRuntimeMemoryLayout.Audio.SfxDataCursorBank);
        for (var i = 0; i < 16; i++)
        {
            builder.LoadAFromHl();
            builder.StoreHighRamA((byte)(0x30 + i));
            EmitAdvanceSfxHl(GameBoyRuntimeMemoryLayout.Audio.SfxDataCursorBank);
        }

        builder.Label(commandDoneLabel);
        builder.Emit(0x05);                         // DEC B
        builder.LoadAFromB();
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xC2, commandLoopLabel); // JP NZ,commandLoopLabel

        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.SfxTick);
        builder.CompareImmediate(0);
        builder.JumpAbsolute(0xCA, processLabel);   // JP Z,processLabel: drain zero-wait order entries
        builder.JumpAbsolute(endLabel);

        builder.Label(stopLabel);
        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxActive);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxTick);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxCurrentPointerLow);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxCurrentPointerHigh);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxCurrentBank);
        // Release channel 1 back to the BGM: restore its full shadowed state (NR10-NR14) so the melody
        // is not left carrying the effect's sweep/duty/envelope. NR14 is restored with the shadowed
        // trigger bit, which reloads NR12's envelope, fully re-establishing the BGM's channel 1; the
        // BGM's next note re-writes them anyway.
        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.Channel1ShadowNr10);
        builder.StoreHighRamA(0x10);                // NR10 (sweep)
        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.Channel1ShadowNr11);
        builder.StoreHighRamA(0x11);                // NR11 (duty + length)
        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.Channel1ShadowNr12);
        builder.StoreHighRamA(0x12);                // NR12 (envelope)
        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.Channel1ShadowNr13);
        builder.StoreHighRamA(0x13);                // NR13 (frequency low)
        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.Channel1ShadowNr14);
        builder.StoreHighRamA(0x14);                // NR14 (frequency high + trigger)
        builder.JumpAbsolute(endLabel);
    }

    private void EmitLoadMusicRowEventsToCache()
    {
        EmitLoadMusicCurrentPointerToHl();
        builder.LoadAFromHl();                      // row event mask
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicRowMask);
        EmitAdvanceMusicHl();

        EmitCopyMusicChannelEventToCache(0x01, GameBoyRuntimeMemoryLayout.Audio.MusicRowCacheStart, 4);
        EmitCopyMusicChannelEventToCache(0x02, (ushort)(GameBoyRuntimeMemoryLayout.Audio.MusicRowCacheStart + 4), 4);
        EmitCopyMusicChannelEventToCache(0x04, (ushort)(GameBoyRuntimeMemoryLayout.Audio.MusicRowCacheStart + 8), 4);
        EmitCopyMusicChannelEventToCache(0x08, (ushort)(GameBoyRuntimeMemoryLayout.Audio.MusicRowCacheStart + 12), 3);

        EmitStoreHlToMusicCurrentPointer();
    }

    private void EmitCopyMusicChannelEventToCache(byte mask, ushort cacheAddress, int byteCount)
    {
        var skipLabel = builder.CreateLabel("audio_row_event_skip");
        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.MusicRowMask);
        builder.AndImmediate(mask);
        builder.JumpAbsolute(0xCA, skipLabel);      // JP Z,skipLabel
        for (var i = 0; i < byteCount; i++)
        {
            builder.LoadAFromHl();
            builder.StoreA((ushort)(cacheAddress + i));
            EmitAdvanceMusicHl();
        }

        builder.Label(skipLabel);
    }

    private void EmitClearMusicRowTriggerBits()
    {
        EmitClearMusicTriggerBit((ushort)(GameBoyRuntimeMemoryLayout.Audio.MusicRowCacheStart + 3));
        EmitClearMusicTriggerBit((ushort)(GameBoyRuntimeMemoryLayout.Audio.MusicRowCacheStart + 7));
        EmitClearMusicTriggerBit((ushort)(GameBoyRuntimeMemoryLayout.Audio.MusicRowCacheStart + 11));
        EmitClearMusicTriggerBit((ushort)(GameBoyRuntimeMemoryLayout.Audio.MusicRowCacheStart + 14));
    }

    private void EmitClearMusicTriggerBit(ushort address)
    {
        builder.LoadA(address);
        builder.AndImmediate(0x7F);
        builder.StoreA(address);
    }

    private void EmitClearMusicRowCache()
    {
        builder.LoadAImmediate(0);
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicRowMask);
        for (var i = 0; i < MusicRowCacheLength; i++)
        {
            builder.StoreA((ushort)(GameBoyRuntimeMemoryLayout.Audio.MusicRowCacheStart + i));
        }
    }

    private void EmitLoadMusicPointerToHl(ushort cursorBankAddress)
    {
        if (romLayout.UsesBankedMusic)
        {
            builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.MusicDataBank);
            builder.StoreA(cursorBankAddress);
            EmitSelectRomBankFromA();
        }

        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.MusicDataPointerLow);
        builder.LoadLFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.MusicDataPointerHigh);
        builder.Emit(0x67);                         // LD H,A
    }

    private void EmitLoadMusicCurrentPointerToHl()
    {
        if (romLayout.UsesBankedMusic)
        {
            builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.MusicCurrentBank);
            EmitSelectRomBankFromA();
        }

        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.MusicCurrentPointerLow);
        builder.LoadLFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.MusicCurrentPointerHigh);
        builder.Emit(0x67);                         // LD H,A
    }

    private void EmitResetMusicRowPointer()
    {
        builder.LoadDe((ushort)(MusicHeaderLength + MusicWaveTableBytes));
        EmitLoadMusicDataOffsetToHl(GameBoyRuntimeMemoryLayout.Audio.MusicCurrentBank);
        EmitStoreHlToMusicCurrentPointer();
        EmitClearMusicRowCache();
    }

    private void EmitResetApuTracePointerToStart()
    {
        // Order stream pointer = dataPointer + orderStartOffset (header bytes 1..2).
        EmitLoadMusicPointerToHl(GameBoyRuntimeMemoryLayout.Audio.MusicDataCursorBank);
        EmitAdvanceMusicHl(GameBoyRuntimeMemoryLayout.Audio.MusicDataCursorBank); // HL -> orderStart low
        builder.LoadAFromHl();
        builder.LoadEFromA();
        EmitAdvanceMusicHl(GameBoyRuntimeMemoryLayout.Audio.MusicDataCursorBank); // HL -> orderStart high
        builder.LoadAFromHl();
        builder.LoadDFromA();

        EmitLoadMusicDataOffsetToHl(GameBoyRuntimeMemoryLayout.Audio.MusicCurrentBank);
        EmitStoreHlToMusicCurrentPointer();
        EmitClearMusicRowCache();
    }

    private void EmitResetSfxApuTracePointerToStart()
    {
        // Order stream pointer = dataPointer + orderStartOffset (header bytes 1..2).
        EmitLoadSfxPointerToHl(GameBoyRuntimeMemoryLayout.Audio.SfxDataCursorBank);
        EmitAdvanceSfxHl(GameBoyRuntimeMemoryLayout.Audio.SfxDataCursorBank); // HL -> orderStart low
        builder.LoadAFromHl();
        builder.LoadEFromA();
        EmitAdvanceSfxHl(GameBoyRuntimeMemoryLayout.Audio.SfxDataCursorBank); // HL -> orderStart high
        builder.LoadAFromHl();
        builder.LoadDFromA();

        EmitLoadSfxDataOffsetToHl(GameBoyRuntimeMemoryLayout.Audio.SfxCurrentBank);
        EmitStoreHlToSfxCurrentPointer();
    }

    private void EmitResetApuTracePointerToLoop()
    {
        // Order stream pointer = dataPointer + loopOrderOffset (header bytes 3..4).
        EmitLoadMusicPointerToHl(GameBoyRuntimeMemoryLayout.Audio.MusicDataCursorBank);
        EmitAdvanceMusicHl(GameBoyRuntimeMemoryLayout.Audio.MusicDataCursorBank); // HL -> byte 1
        EmitAdvanceMusicHl(GameBoyRuntimeMemoryLayout.Audio.MusicDataCursorBank); // HL -> byte 2
        EmitAdvanceMusicHl(GameBoyRuntimeMemoryLayout.Audio.MusicDataCursorBank); // HL -> loopOrder low (byte 3)
        builder.LoadAFromHl();
        builder.LoadEFromA();
        EmitAdvanceMusicHl(GameBoyRuntimeMemoryLayout.Audio.MusicDataCursorBank); // HL -> loopOrder high (byte 4)
        builder.LoadAFromHl();
        builder.LoadDFromA();

        EmitLoadMusicDataOffsetToHl(GameBoyRuntimeMemoryLayout.Audio.MusicCurrentBank);
        EmitStoreHlToMusicCurrentPointer();
    }

    private void EmitStoreHlToMusicCurrentPointer()
    {
        builder.Emit(0x7D);                         // LD A,L
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicCurrentPointerLow);
        builder.Emit(0x7C);                         // LD A,H
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicCurrentPointerHigh);
    }

    private void EmitStoreHlToMusicScratchPointer()
    {
        builder.Emit(0x7D);                         // LD A,L
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicScratchPointerLow);
        builder.Emit(0x7C);                         // LD A,H
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicScratchPointerHigh);
        if (romLayout.UsesBankedMusic)
        {
            builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.MusicCurrentBank);
            builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.MusicScratchBank);
        }
    }

    private void EmitLoadMusicScratchPointerToHl()
    {
        if (romLayout.UsesBankedMusic)
        {
            builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.MusicScratchBank);
            EmitSelectRomBankFromA();
        }

        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.MusicScratchPointerLow);
        builder.LoadLFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.MusicScratchPointerHigh);
        builder.Emit(0x67);                         // LD H,A
    }

    private void EmitLoadSfxPointerToHl(ushort cursorBankAddress)
    {
        if (romLayout.UsesBankedMusic)
        {
            builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.SfxDataBank);
            builder.StoreA(cursorBankAddress);
            EmitSelectRomBankFromA();
        }

        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.SfxDataPointerLow);
        builder.LoadLFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.SfxDataPointerHigh);
        builder.Emit(0x67);                         // LD H,A
    }

    private void EmitLoadSfxCurrentPointerToHl()
    {
        if (romLayout.UsesBankedMusic)
        {
            builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.SfxCurrentBank);
            EmitSelectRomBankFromA();
        }

        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.SfxCurrentPointerLow);
        builder.LoadLFromA();
        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.SfxCurrentPointerHigh);
        builder.Emit(0x67);                         // LD H,A
    }

    private void EmitStoreHlToSfxCurrentPointer()
    {
        builder.Emit(0x7D);                         // LD A,L
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxCurrentPointerLow);
        builder.Emit(0x7C);                         // LD A,H
        builder.StoreA(GameBoyRuntimeMemoryLayout.Audio.SfxCurrentPointerHigh);
    }

    private void EmitLoadMusicDataOffsetToHl(ushort targetBankAddress)
    {
        if (!romLayout.UsesBankedMusic)
        {
            EmitLoadMusicPointerToHl(targetBankAddress);
            builder.AddHlDe();
            return;
        }

        // Resolve a base-relative offset (DE) into the banked window: the absolute ROM bank is
        // dataBank + (DE >> 14) and the in-window address is 0x4000 | (DE & 0x3FFF). The result bank
        // is written to targetBankAddress so the caller's own cursor bank is never disturbed.
        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.MusicDataBank);
        builder.LoadCFromA();
        builder.LoadAFromD();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.AddAFromC();
        builder.StoreA(targetBankAddress);
        EmitSelectRomBankFromA();
        builder.LoadAFromD();
        builder.AndImmediate(0x3F);
        builder.OrImmediate(0x40);
        builder.Emit(0x67);                         // LD H,A
        builder.LoadLFromE();
    }

    private void EmitLoadSfxDataOffsetToHl(ushort targetBankAddress)
    {
        if (!romLayout.UsesBankedMusic)
        {
            EmitLoadSfxPointerToHl(targetBankAddress);
            builder.AddHlDe();
            return;
        }

        builder.LoadA(GameBoyRuntimeMemoryLayout.Audio.SfxDataBank);
        builder.LoadCFromA();
        builder.LoadAFromD();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.ShiftRightLogicalA();
        builder.AddAFromC();
        builder.StoreA(targetBankAddress);
        EmitSelectRomBankFromA();
        builder.LoadAFromD();
        builder.AndImmediate(0x3F);
        builder.OrImmediate(0x40);
        builder.Emit(0x67);                         // LD H,A
        builder.LoadLFromE();
    }

    // Advances the persistent current/order/row cursor (HL) and its bank (GameBoyRuntimeMemoryLayout.Audio.MusicCurrentBank).
    private void EmitAdvanceMusicHl() => EmitAdvanceMusicHl(GameBoyRuntimeMemoryLayout.Audio.MusicCurrentBank);

    private void EmitAdvanceMusicHl(ushort cursorBankAddress)
    {
        builder.Emit(0x23);                         // INC HL
        if (!romLayout.UsesBankedMusic)
        {
            return;
        }

        // Crossing 0x8000 means the cursor walked past its 16 KiB window: rewind HL to 0x4000 and
        // advance the cursor's own bank so sequential reads continue transparently.
        var endLabel = builder.CreateLabel("music_bank_advance_end");
        builder.Emit(0x7C);                         // LD A,H
        builder.CompareImmediate(0x80);
        builder.JumpAbsolute(0xC2, endLabel);       // JP NZ,endLabel
        builder.LoadHImmediate(0x40);
        builder.LoadA(cursorBankAddress);
        builder.AddAImmediate(1);
        builder.StoreA(cursorBankAddress);
        EmitSelectRomBankFromA();
        builder.Label(endLabel);
    }

    private void EmitAdvanceSfxHl() => EmitAdvanceSfxHl(GameBoyRuntimeMemoryLayout.Audio.SfxCurrentBank);

    private void EmitAdvanceSfxHl(ushort cursorBankAddress)
    {
        builder.Emit(0x23);                         // INC HL
        if (!romLayout.UsesBankedMusic)
        {
            return;
        }

        var endLabel = builder.CreateLabel("sfx_bank_advance_end");
        builder.Emit(0x7C);                         // LD A,H
        builder.CompareImmediate(0x80);
        builder.JumpAbsolute(0xC2, endLabel);       // JP NZ,endLabel
        builder.LoadHImmediate(0x40);
        builder.LoadA(cursorBankAddress);
        builder.AddAImmediate(1);
        builder.StoreA(cursorBankAddress);
        EmitSelectRomBankFromA();
        builder.Label(endLabel);
    }

    private void EmitSelectRomBankFromA()
    {
        GameBoyRomBuilder.EmitSelectRomBankFromA(builder);
    }

    private void EmitWriteMusicRegister(byte register)
    {
        builder.LoadAFromHl();
        builder.StoreHighRamA(register);
        builder.Emit(0x23);                         // INC HL
    }

    private void EmitWriteWaveChannelFromRow()
    {
        builder.LoadAFromHl();                      // wave index
        builder.LoadBFromA();
        builder.Emit(0x23);                         // INC HL
        EmitStoreHlToMusicScratchPointer();

        builder.LoadAImmediate(0);
        builder.StoreHighRamA(0x1A);                // NR30: disable CH3 before wave RAM writes
        builder.LoadAFromB();
        builder.SwapA();
        builder.AndImmediate(0xF0);
        builder.AddAImmediate(MusicHeaderLength);
        builder.LoadEFromA();
        builder.LoadDImmediate(0);
        EmitLoadMusicDataOffsetToHl(GameBoyRuntimeMemoryLayout.Audio.MusicDataCursorBank);
        for (var i = 0; i < 16; i++)
        {
            builder.LoadAFromHl();
            builder.StoreHighRamA((byte)(0x30 + i));
            EmitAdvanceMusicHl(GameBoyRuntimeMemoryLayout.Audio.MusicDataCursorBank);
        }

        builder.LoadAImmediate(0x80);
        builder.StoreHighRamA(0x1A);                // NR30: enable CH3
        builder.LoadAImmediate(0);
        builder.StoreHighRamA(0x1B);                // NR31: full length

        EmitLoadMusicScratchPointerToHl();
        EmitWriteMusicRegister(0x1C);
        EmitWriteMusicRegister(0x1D);
        EmitWriteMusicRegister(0x1E);
    }
}
