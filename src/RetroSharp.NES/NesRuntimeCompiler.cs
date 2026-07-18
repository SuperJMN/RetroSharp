using System.Globalization;
using RetroSharp.Core;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;
using RetroSharp.Sdk;

namespace RetroSharp.NES;

internal sealed partial class NesRuntimeCompiler
{
    private sealed record StructArrayLayout(int Stride, IReadOnlyDictionary<string, int> FieldOffsets);

    private readonly PrgBuilder builder;
    private readonly NesVideoProgram program;
    private readonly Dictionary<string, byte> variables = [];
    private readonly Dictionary<string, string> variableTypes = [];
    private readonly Dictionary<string, StructArrayLayout> structArrays = [];
    private readonly HashSet<string> declaredVariables = [];
    private readonly HashSet<string> signedByteLocations = [];
    private readonly HashSet<string> immutableVariables = [];
    private readonly HashSet<string> userFunctionCallStack = [];
    private readonly Stack<InlineVariableScope> inlineVariableScopes = [];
    private readonly Stack<LoopTarget> loopTargets = [];
    private readonly IReadOnlySet<int> longForLoopIds;
    private readonly IReadOnlySet<int> longWhileLoopIds;
    private readonly bool usePackedCamera;
    private readonly NesSdkStreamReader sdkOperations;
    private readonly NesSdkOperationLowerer sdkOperationLowerer;
    private byte nextVariableAddress = checked((byte)NesRuntimeMemoryLayout.UserLocals.Start);
    private int nextForLoopId;
    private int nextWhileLoopId;
    private int nextInlineVariableScopeId;
    private bool apuBodySubroutineReferenced;

    private const string ApuBodySubroutineLabel = "nes_apu_body";

    public NesRuntimeCompiler(
        PrgBuilder builder,
        NesVideoProgram program,
        IReadOnlySet<int>? longForLoopIds = null,
        IReadOnlySet<int>? longWhileLoopIds = null,
        bool useFourScreenNametables = false,
        bool usePackedCamera = false,
        bool useSequentialOamPublication = false)
        : this(
            builder,
            program,
            longForLoopIds,
            longWhileLoopIds,
            NesPhysicalFrameScheduler.Create(
                builder,
                program,
                useFourScreenNametables,
                usePackedCamera,
                useSequentialOamPublication))
    {
    }

    internal NesRuntimeCompiler(
        PrgBuilder builder,
        NesVideoProgram program,
        IReadOnlySet<int>? longForLoopIds,
        IReadOnlySet<int>? longWhileLoopIds,
        NesPhysicalFrameScheduler frameScheduler)
    {
        ArgumentNullException.ThrowIfNull(frameScheduler);
        NesRuntimeMemoryLayout.Validate();
        this.builder = builder;
        this.program = program;
        this.longForLoopIds = longForLoopIds ?? new HashSet<int>();
        this.longWhileLoopIds = longWhileLoopIds ?? new HashSet<int>();
        usePackedCamera = frameScheduler.UsesPackedCameraRuntime;
        sdkOperations = new NesSdkStreamReader(program.SdkOperationStream);
        sdkOperationLowerer = new NesSdkOperationLowerer(
            builder,
            program,
            new NesSdkLoweringContext(
                EmitExpressionToA,
                TryConst,
                VariableStorageType,
                VariableAddress,
                RuntimeIndexedMemberBaseAddress,
                EmitRuntimeMemberIndexToX),
            frameScheduler);
    }

    public void EmitInitialization()
    {
        sdkOperationLowerer.EmitCameraStateInitialization();
        sdkOperationLowerer.EmitInputStateInitialization();
        if (program.MusicAssetsInLoadOrder.Count > 0 || program.SoundEffectAssetsInLoadOrder.Count > 0)
        {
            EmitAudioStateInitialization();
        }

        sdkOperationLowerer.EmitOamShadowClear();
    }

    public void Emit(BlockSyntax block)
    {
        EmitBlock(block);
        sdkOperations.EnsureAllConsumed("NES runtime");
    }

    internal NesSdkOperationLowerer SdkOperationLowerer => sdkOperationLowerer;

    private void EmitAudioStateInitialization()
    {
        builder.LoadAImmediate(0);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Audio.MusicOrderPointerLow);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Audio.MusicOrderPointerHigh);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Audio.MusicTick);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Audio.MusicLoopPointerLow);
        builder.StoreAAbsolute(NesRuntimeMemoryLayout.Audio.MusicLoopPointerHigh);
        if (program.SoundEffectAssetsInLoadOrder.Count > 0)
        {
            builder.StoreAAbsolute(NesRuntimeMemoryLayout.Audio.SfxActive);
            // Seed the pulse 1 sweep shadow ($4001) with "no sweep" so an early effect restores a
            // sweep-free channel before the BGM has written $4001 (which it rarely does). The BGM
            // writes $4000/$4002/$4003 constantly, so those shadow slots are populated before any SFX.
            builder.StoreAAbsolute((ushort)(NesRuntimeMemoryLayout.Audio.Pulse1ShadowBase + 1));
        }
    }

    private bool EmitBlock(BlockSyntax block)
    {
        foreach (var statement in block.Statements)
        {
            if (EmitStatement(statement))
            {
                return true;
            }
        }

        return false;
    }

    private bool EmitStatement(StatementSyntax statement)
    {
        switch (statement)
        {
            case DeclarationSyntax declaration:
                EmitDeclaration(declaration);
                return false;
            case ExpressionStatementSyntax expressionStatement:
                EmitExpressionStatement(expressionStatement);
                return false;
            case WhileSyntax whileSyntax:
                EmitWhile(whileSyntax);
                return false;
            case DoWhileSyntax doWhileSyntax:
                EmitDoWhile(doWhileSyntax);
                return false;
            case RangeForSyntax rangeForSyntax:
                EmitFor(RangeForLowerer.Lower(rangeForSyntax));
                return false;
            case ForSyntax forSyntax:
                EmitFor(forSyntax);
                return false;
            case IfElseSyntax ifElseSyntax:
                return EmitIf(ifElseSyntax);
            case BreakSyntax:
                EmitBreak();
                return true;
            case ContinueSyntax:
                EmitContinue();
                return true;
            case ReturnSyntax:
                return true;
            default:
                throw new InvalidOperationException($"Unsupported NES statement '{statement.GetType().Name}'.");
        }
    }
}
