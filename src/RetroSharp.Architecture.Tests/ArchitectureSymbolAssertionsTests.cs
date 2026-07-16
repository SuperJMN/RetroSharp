using RetroSharp.Core.Sdk;

namespace RetroSharp.Architecture.Tests;

public sealed class ArchitectureSymbolAssertionsTests
{
    [Fact]
    public void Sdk_ownership_guard_rejects_a_runtime_compiler_backedge()
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
        public void Emit(Sdk2DOperation operation)
        {
            new LeakingRuntimeCompiler().EmitActorSdkOperation(operation);
        }
    }

    private sealed class LeakingRuntimeCompiler
    {
        public void EmitActorSdkOperation(Sdk2DOperation operation)
        {
        }
    }

    private sealed class ActorFact;

    private sealed class FixtureActorState
    {
        private readonly List<ActorFact> facts = [];
    }

    private sealed class FixtureRootState
    {
        private readonly FixtureActorState actors = new();
    }

    private sealed class LeakingRootLowerer
    {
        private static void GenerateActor(ActorFact fact)
        {
        }
    }
}
