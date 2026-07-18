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
    private sealed record StructArrayLayout(int Stride, IReadOnlyDictionary<string, int> FieldOffsets);

    private const byte MusicActiveUgeRows = 1;
    private const byte MusicActiveApuTrace = 2;
    private const int MusicHeaderLength = 3;
    private const int MusicWaveTableBytes = 16 * 16;
    private const int MusicRowCacheLength = 15;
    private const byte ApuTraceWaveRamBlockCommand = 0xFF;

    private readonly GbBuilder builder;
    private readonly GameBoyVideoProgram program;
    private readonly GameBoyRomLayout romLayout;
    private readonly bool usesPackedCameraRuntime;
    private readonly bool usesShadowOam;
    private readonly Sdk2DStreamReader sdkOperations;
    private readonly SdkAudioStreamReader sdkAudioOperations;
    private readonly Dictionary<string, ushort> variables = [];
    private readonly Dictionary<string, string> variableTypes = [];
    private readonly Dictionary<string, StructArrayLayout> structArrays = [];
    private readonly HashSet<string> declaredVariables = [];
    private readonly HashSet<string> signedByteLocations = [];
    private readonly HashSet<string> immutableVariables = [];
    private readonly HashSet<string> userFunctionCallStack = [];
    private readonly Stack<InlineVariableScope> inlineVariableScopes = [];
    private readonly Stack<LoopTarget> loopTargets = [];
    private readonly GameBoySdkLoweringState sdkLoweringState = new();
    private readonly GameBoySdkOperationLowerer sdkOperationLowerer;
    private GameBoyRuntimeIndexedAddressReuse? runtimeIndexedAddressReuse;
    private int nextInlineVariableScopeId;
    private ushort nextVariableAddress = GameBoyRuntimeMemoryLayout.UserLocals.Start;

    public GameBoyRuntimeCompiler(
        GbBuilder builder,
        GameBoyVideoProgram program,
        GameBoyRomLayout? romLayout = null,
        GameBoyWorldPackRuntimeLayout? packedWorldRuntimeLayout = null,
        bool usesPackedCameraRuntime = false)
        : this(
            builder,
            program,
            romLayout ?? GameBoyRomLayout.RomOnly,
            packedWorldRuntimeLayout,
            GameBoyFramePlan.Create(
                program,
                romLayout ?? GameBoyRomLayout.RomOnly,
                usesPackedCameraRuntime))
    {
    }

    internal GameBoyRuntimeCompiler(
        GbBuilder builder,
        GameBoyVideoProgram program,
        GameBoyRomLayout romLayout,
        GameBoyWorldPackRuntimeLayout? packedWorldRuntimeLayout,
        GameBoyFramePlan framePlan)
    {
        ArgumentNullException.ThrowIfNull(framePlan);
        GameBoyRuntimeMemoryLayout.Validate();
        this.builder = builder;
        this.program = program;
        this.romLayout = romLayout;
        usesPackedCameraRuntime = framePlan.UsesPackedCameraRuntime;
        usesShadowOam = framePlan.UsesRetainedOam;
        sdkOperations = Sdk2DStreamReader.ForProgram(program);
        sdkAudioOperations = SdkAudioStreamReader.ForProgram(program);
        sdkOperationLowerer = new GameBoySdkOperationLowerer(
            builder,
            program,
            sdkLoweringState,
            new GameBoySdkLoweringContext(
                EmitSdkByteExpressionToA,
                EmitSdkWordExpressionToA,
                EmitExpressionToA,
                EmitStoreSplitWordImmediate,
                TryConst,
                ConstRuntimeValue,
                BeginRuntimeIndexedAddressReuse,
                EndRuntimeIndexedAddressReuse),
            this.romLayout,
            packedWorldRuntimeLayout,
            framePlan);
    }

    public void Emit(BlockSyntax block)
    {
        EmitMain(block);
        EmitSubroutines();
        EnsureAllStreamsConsumed();
    }

    public void EmitMain(BlockSyntax block)
    {
        sdkOperationLowerer.EmitInputStateInitialization();
        EmitBlock(block);
    }

    public bool UsesSubroutineTrampolines => romLayout.ProgramTailBankCount > 0 && program.SubroutineNames.Count != 0;

    internal GameBoySdkOperationLowerer SdkOperationLowerer => sdkOperationLowerer;

    private bool UsesPackedCameraRuntime => usesPackedCameraRuntime;

    public bool UsesReadOnlyDataHelpers => romLayout.ReadOnlyDataPlacements.Keys.Any(IsRuntimeReadOnlyDataLabel);

    public bool UsesAudioUpdateHelper =>
        romLayout.ProgramTailBankCount > 0
        && romLayout.UsesBankedMusic
        && program.SdkAudioOperations.Any(operation => operation is SdkAudioOperation.UpdateAudio);

    public bool UsesMusicPlayHelpers =>
        romLayout.ProgramTailBankCount > 0
        && romLayout.UsesBankedMusic
        && program.SdkAudioOperations.Any(operation => operation is SdkAudioOperation.PlayMusic);

    public bool UsesSoundEffectPlayHelpers =>
        romLayout.ProgramTailBankCount > 0
        && romLayout.UsesBankedMusic
        && program.SdkAudioOperations.Any(operation => operation is SdkAudioOperation.PlaySoundEffect);

    public bool UsesAudioHelpers => UsesAudioUpdateHelper || UsesMusicPlayHelpers || UsesSoundEffectPlayHelpers;

    public void EmitProgramBankInitialization(bool usesMbc1Foundation)
    {
        if (UsesPackedCameraRuntime)
        {
            builder.XorA();
            builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.AudioTickCount);
        }

        if (!usesMbc1Foundation)
        {
            return;
        }

        builder.LoadAImmediate(1);
        if (romLayout.ProgramTailBankCount > 0)
        {
            builder.StoreA(GameBoyRuntimeMemoryLayout.Banking.ProgramCurrentBank);
        }

        EmitSelectRomBankFromA();
    }

    public void EmitReadOnlyDataHelpers()
    {
        if (!UsesReadOnlyDataHelpers)
        {
            return;
        }

        foreach (var placement in romLayout.ReadOnlyDataPlacements.Values)
        {
            if (!IsRuntimeReadOnlyDataLabel(placement.Label))
            {
                continue;
            }

            builder.Label(ReadOnlyDataByteReaderLabel(placement.Label));
            builder.LoadEFromA();
            builder.LoadDImmediate(0);
            builder.LoadA(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank);
            builder.LoadCFromA();
            builder.Emit(0xC5); // PUSH BC
            builder.LoadAImmediate(placement.Bank);
            EmitSelectRomBankFromA();
            builder.LoadHl(placement.Address);
            builder.AddHlDe();
            builder.LoadAFromHl();
            builder.LoadDFromA();
            builder.Emit(0xC1); // POP BC
            builder.LoadAFromC();
            EmitSelectRomBankFromA();
            builder.LoadAFromD();

            builder.Emit(0xC9); // RET
        }
    }

    public void EmitAudioHelpers()
    {
        EmitMusicPlayHelpers();
        EmitSoundEffectPlayHelpers();
        EmitAudioUpdateHelper();
        EmitPackedCameraAudioTickHelper();
    }

    private void EmitPackedCameraAudioTickHelper()
    {
        if (!UsesPackedCameraRuntime)
        {
            return;
        }

        builder.Label(GameBoyRomBuilder.WorldPackWaitAudioTickLabel);
        if (!program.SdkAudioOperations.Any(operation => operation is SdkAudioOperation.UpdateAudio))
        {
            builder.Emit(0xC9); // RET; the wait flag remains disabled.
            return;
        }

        builder.Emit(0xC5); // PUSH BC
        builder.Emit(0xD5); // PUSH DE
        builder.PushHl();
        var restoresBank = romLayout.ProgramTailBankCount > 0
                           || romLayout.UsesBankedReadOnlyData
                           || romLayout.UsesBankedMusic
                           || romLayout.UsesBankedWorldPack;
        if (restoresBank)
        {
            builder.LoadA(GameBoyRuntimeMemoryLayout.Banking.ActualVisibleBank);
            builder.Emit(0xF5); // PUSH AF
        }

        builder.LoadAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.WaitAudioEnabled);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.WaitAudioTicked);
        EmitUpdateAudio();
        builder.LoadA(GameBoyRuntimeMemoryLayout.PackedCamera.AudioTickCount);
        builder.AddAImmediate(1);
        builder.StoreA(GameBoyRuntimeMemoryLayout.PackedCamera.AudioTickCount);
        if (restoresBank)
        {
            builder.Emit(0xF1); // POP AF
            EmitSelectRomBankFromA();
        }

        builder.PopHl();
        builder.Emit(0xD1); // POP DE
        builder.Emit(0xC1); // POP BC
        builder.Emit(0xC9); // RET
    }

    private void EmitMusicPlayHelpers()
    {
        if (!UsesMusicPlayHelpers)
        {
            return;
        }

        foreach (var themeId in program.SdkAudioOperations.OfType<SdkAudioOperation.PlayMusic>().Select(play => play.ThemeId).Distinct())
        {
            builder.Label(MusicPlayHelperLabel(themeId));
            EmitPlayMusic(new SdkAudioOperation.PlayMusic(themeId));
            EmitRestoreProgramTailBank();
            builder.Emit(0xC9); // RET
        }
    }

    private void EmitSoundEffectPlayHelpers()
    {
        if (!UsesSoundEffectPlayHelpers)
        {
            return;
        }

        foreach (var soundId in program.SdkAudioOperations.OfType<SdkAudioOperation.PlaySoundEffect>().Select(play => play.SoundId).Distinct())
        {
            builder.Label(SoundEffectPlayHelperLabel(soundId));
            EmitPlaySoundEffect(new SdkAudioOperation.PlaySoundEffect(soundId));
            EmitRestoreProgramTailBank();
            builder.Emit(0xC9); // RET
        }
    }

    private void EmitAudioUpdateHelper()
    {
        if (!UsesAudioUpdateHelper || UsesPackedCameraRuntime)
        {
            return;
        }

        builder.Label(AudioUpdateHelperLabel);
        EmitUpdateAudio();
        EmitRestoreProgramTailBank();
        builder.Emit(0xC9); // RET
    }

    public void EmitSubroutineTrampolines()
    {
        if (!UsesSubroutineTrampolines)
        {
            return;
        }

        foreach (var name in program.SubroutineNames)
        {
            builder.Label(SubroutineTrampolineLabel(name));
            builder.LoadA(GameBoyRuntimeMemoryLayout.Banking.ProgramCurrentBank);
            builder.Emit(0xF5); // PUSH AF
            builder.LoadAImmediateBankOf(SubroutineLabel(name));
            builder.StoreA(GameBoyRuntimeMemoryLayout.Banking.ProgramCurrentBank);
            EmitSelectRomBankFromA();
            builder.JumpAbsolute(0xCD, SubroutineLabel(name)); // CALL nn
            builder.Emit(0xF1); // POP AF
            builder.StoreA(GameBoyRuntimeMemoryLayout.Banking.ProgramCurrentBank);
            EmitSelectRomBankFromA();
            builder.Emit(0xC9); // RET
        }
    }

    public void EmitSubroutines()
    {
        foreach (var name in program.SubroutineNames)
        {
            if (!program.Functions.TryGetValue(name, out var function))
            {
                continue;
            }

            builder.Label(SubroutineLabel(name));
            sdkOperations.EnterSubroutine(name);
            sdkAudioOperations.EnterSubroutine(name);
            var aliases = PushSubroutineParameterAliases(function);
            try
            {
                EmitBlock(SubroutineBody(function));
                sdkOperations.LeaveSubroutine(name);
                sdkAudioOperations.LeaveSubroutine(name);
                builder.Emit(0xC9); // RET
            }
            finally
            {
                PopSubroutineParameterAliases(aliases);
            }
        }
    }

    public void EnsureAllStreamsConsumed()
    {
        EnsureAllSdkOperationsConsumed();
        EnsureAllSdkAudioOperationsConsumed();
    }


}
