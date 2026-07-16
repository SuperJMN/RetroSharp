using RetroSharp.Core.Sdk;

namespace RetroSharp.Architecture.Tests;

public sealed class ArchitectureSymbolAssertionsTests
{
    [Fact]
    public void Runtime_memory_guard_rejects_a_reserved_address_declared_outside_the_layout()
    {
        Assert.ThrowsAny<Exception>(() => ArchitectureSymbolAssertions.AssertRuntimeMemoryOwnership(
            typeof(ArchitectureSymbolAssertionsTests).Assembly,
            typeof(FixtureRuntimeMemoryLayout).FullName!));
    }

    [Fact]
    public void Runtime_memory_guard_rejects_a_renamed_byte_address_declared_outside_the_layout()
    {
        Assert.ThrowsAny<Exception>(() => ArchitectureSymbolAssertions.AssertRuntimeMemoryOwnership(
            typeof(ArchitectureSymbolAssertionsTests).Assembly,
            typeof(FixtureByteRuntimeMemoryLayout).FullName!));
    }

    [Fact]
    public void Frontend_guard_rejects_a_target_that_repeats_a_preparation_stage()
    {
        Assert.ThrowsAny<Exception>(() => ArchitectureSymbolAssertions.AssertExclusiveFrontendPreparation(
            typeof(FixtureFrontendPreparation),
            [typeof(FixtureParser)],
            typeof(LeakingTargetCompiler)));
    }

    [Fact]
    public void Sdk_ownership_guard_rejects_a_runtime_compiler_backedge_from_a_type_initializer()
    {
        Assert.ThrowsAny<Exception>(() => ArchitectureSymbolAssertions.AssertSdkOperationOwnership(
            typeof(ArchitectureSymbolAssertionsTests).Assembly,
            typeof(LeakingSdkOwner).FullName!,
            typeof(LeakingRuntimeCompiler).FullName!));
    }

    [Fact]
    public void Domain_state_guard_rejects_feature_generation_in_the_root_lowerer()
    {
        Assert.ThrowsAny<Exception>(() => ArchitectureSymbolAssertions.AssertDomainStateOwnership(
            typeof(LeakingRootLowerer),
            typeof(FixtureRootState),
            [typeof(FixtureActorState)]));
    }

    private sealed class LeakingSdkOwner
    {
        static LeakingSdkOwner()
        {
            new LeakingRuntimeCompiler().Initialize();
        }

        public static void Emit(Sdk2DOperation operation)
        {
        }
    }

    private sealed class LeakingRuntimeCompiler
    {
        public void EmitActorSdkOperation(Sdk2DOperation operation)
        {
            LeakingSdkOwner.Emit(operation);
        }

        public void Initialize()
        {
        }
    }

    private sealed class ActorFact;

    private sealed class FixtureActorState
    {
        private readonly List<ActorFact> facts = [];

        public void Add(ActorFact fact)
        {
            facts.Add(fact);
        }
    }

    private sealed class FixtureRootState
    {
        public FixtureActorState Actors { get; } = new();
    }

    private sealed class LeakingRootLowerer
    {
        private static void GenerateActor(FixtureRootState state)
        {
            state.Actors.Add(new ActorFact());
        }
    }

    private readonly record struct FixtureRuntimeRange(ushort Start, int Length)
    {
        public int EndExclusive => Start + Length;
    }

    private sealed record FixtureRuntimeAddress(string Name, ushort Address);

    private static class FixtureRuntimeMemoryLayout
    {
        public static IReadOnlyList<FixtureRuntimeRange> ReservedRanges { get; } =
        [
            new(0x0200, 0x0100),
        ];

        public static IReadOnlyList<FixtureRuntimeAddress> NamedAddresses { get; } =
        [
            new("Cursor", 0x0200),
        ];

        public static class Runtime
        {
            public const ushort Cursor = 0x0200;
        }
    }

    private sealed class LeakingRuntimeMemoryMap
    {
        private const ushort RenamedCursor = 0x0201;
    }

    private static class FixtureByteRuntimeMemoryLayout
    {
        public static IReadOnlyList<FixtureRuntimeRange> ReservedRanges { get; } =
        [
            new(0x00F0, 0x0001),
        ];

        public static IReadOnlyList<FixtureRuntimeAddress> NamedAddresses { get; } =
        [
            new("Current", 0x00F0),
        ];

        public static class Input
        {
            public const byte Current = 0xF0;
        }
    }

    private sealed class LeakingByteRuntimeMemoryMap
    {
        private const byte RenamedInput = 0xF0;
    }

    private static class FixtureParser
    {
        public static void Parse()
        {
        }
    }

    private static class FixtureFrontendPreparation
    {
        public static void Prepare()
        {
            FixtureParser.Parse();
        }
    }

    private static class LeakingTargetCompiler
    {
        public static void Compile()
        {
            FixtureFrontendPreparation.Prepare();
            FixtureParser.Parse();
        }
    }
}
