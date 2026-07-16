using System.Reflection;
using RetroSharp.Sdk;

namespace RetroSharp.Architecture.Tests;

public sealed class ActorFrameworkDomainArchitectureTests
{
    private const BindingFlags DeclaredMembers =
        BindingFlags.Public |
        BindingFlags.NonPublic |
        BindingFlags.Instance |
        BindingFlags.Static |
        BindingFlags.DeclaredOnly;

    [Fact]
    public void Mutable_actor_framework_facts_are_owned_by_domain_state_symbols()
    {
        var lowerer = typeof(ActorFrameworkLowerer);
        var rootState = ArchitectureSymbolAssertions.RequiredNestedType(lowerer, "ActorFrameworkState");
        var domainStates = new[]
        {
            ArchitectureSymbolAssertions.RequiredNestedType(lowerer, "ActorState"),
            ArchitectureSymbolAssertions.RequiredNestedType(lowerer, "SpawnState"),
            ArchitectureSymbolAssertions.RequiredNestedType(lowerer, "ProjectileState"),
            ArchitectureSymbolAssertions.RequiredNestedType(lowerer, "EffectState"),
            ArchitectureSymbolAssertions.RequiredNestedType(lowerer, "GeneratedCallState"),
        };

        ArchitectureSymbolAssertions.AssertDomainStateOwnership(lowerer, rootState, domainStates);
    }

    [Fact]
    public void Generated_program_assembly_consumes_one_ordered_domain_contribution_catalog()
    {
        var lowerer = typeof(ActorFrameworkLowerer);
        var generatedProgram = ArchitectureSymbolAssertions.RequiredNestedType(lowerer, "GeneratedProgramArtifacts");
        var domains = ArchitectureSymbolAssertions.RequiredNestedType(lowerer, "ActorFrameworkDomains");
        var contributionContract = ArchitectureSymbolAssertions.RequiredNestedType(lowerer, "GeneratedProgramContribution");
        var domainModules = DomainModules(lowerer);
        var calls = ArchitectureSymbolAssertions.CalledMethods(generatedProgram);
        var catalogProperty = domains.GetProperty("Contributions", DeclaredMembers);
        Assert.NotNull(catalogProperty);
        Assert.Equal(typeof(IReadOnlyList<>).MakeGenericType(contributionContract), catalogProperty.PropertyType);
        var contributions = Assert.IsAssignableFrom<System.Collections.IEnumerable>(catalogProperty.GetValue(null))
            .Cast<object>()
            .ToList();
        var domainProperty = contributionContract.GetProperty("Domain", DeclaredMembers);
        Assert.NotNull(domainProperty);

        Assert.Equal(
            ["Actors", "Projectiles", "Effects"],
            contributions.Select(contribution => Assert.IsType<string>(domainProperty.GetValue(contribution))));
        Assert.All(
            ["AddGeneratedStructs", "GeneratedConstants", "GeneratedFunctions", "GeneratedNames"],
            propertyName =>
            {
                var property = contributionContract.GetProperty(propertyName, DeclaredMembers);
                Assert.NotNull(property);
                Assert.True(typeof(Delegate).IsAssignableFrom(property.PropertyType));
            });

        Assert.Contains(calls, method => method.DeclaringType == domains);
        Assert.DoesNotContain(calls, method => domainModules.Contains(method.DeclaringType));
    }

    [Fact]
    public void Root_orchestration_reaches_every_feature_module_through_compiled_call_edges()
    {
        var lowerer = typeof(ActorFrameworkLowerer);
        var generatedProgram = ArchitectureSymbolAssertions.RequiredNestedType(lowerer, "GeneratedProgramArtifacts");
        var domainModules = DomainModules(lowerer);
        var calls = ArchitectureSymbolAssertions.CalledMethods(lowerer);

        Assert.Contains(calls, method => method.DeclaringType == generatedProgram);
        Assert.All(
            domainModules,
            module => Assert.Contains(calls, method => method.DeclaringType == module));
    }

    [Fact]
    public void Feature_modules_own_domain_state_calls_and_consume_shared_root_primitives()
    {
        var lowerer = typeof(ActorFrameworkLowerer);
        var actors = ArchitectureSymbolAssertions.RequiredNestedType(lowerer, "Actors");
        var projectiles = ArchitectureSymbolAssertions.RequiredNestedType(lowerer, "Projectiles");
        var effects = ArchitectureSymbolAssertions.RequiredNestedType(lowerer, "Effects");
        var expectedStateOwners = new Dictionary<Type, Type[]>
        {
            [actors] =
            [
                ArchitectureSymbolAssertions.RequiredNestedType(lowerer, "ActorState"),
                ArchitectureSymbolAssertions.RequiredNestedType(lowerer, "SpawnState"),
            ],
            [projectiles] = [ArchitectureSymbolAssertions.RequiredNestedType(lowerer, "ProjectileState")],
            [effects] = [ArchitectureSymbolAssertions.RequiredNestedType(lowerer, "EffectState")],
        };

        foreach (var (module, stateOwners) in expectedStateOwners)
        {
            var calls = ArchitectureSymbolAssertions.CalledMethods(module);
            Assert.Contains(calls, method => method.DeclaringType == lowerer);
            Assert.All(
                stateOwners,
                stateOwner => Assert.Contains(calls, method => method.DeclaringType == stateOwner));
        }
    }

    private static Type[] DomainModules(Type lowerer)
    {
        return
        [
            ArchitectureSymbolAssertions.RequiredNestedType(lowerer, "Actors"),
            ArchitectureSymbolAssertions.RequiredNestedType(lowerer, "Projectiles"),
            ArchitectureSymbolAssertions.RequiredNestedType(lowerer, "Effects"),
        ];
    }
}
