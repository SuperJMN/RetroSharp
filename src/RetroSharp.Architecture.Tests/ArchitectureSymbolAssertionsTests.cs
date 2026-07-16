using RetroSharp.Core.Sdk;

namespace RetroSharp.Architecture.Tests;

public sealed class ArchitectureSymbolAssertionsTests
{
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
}
