using RetroSharp.Parser;

namespace RetroSharp.Sdk;

public static partial class ActorFrameworkLowerer
{
    private static class GeneratedProgramArtifacts
    {
        public static ProgramSyntax Build(ProgramSyntax program, ActorFrameworkState state)
        {
            if (!state.HasDirectives)
            {
                return program;
            }

            var contributions = ActorFrameworkDomains.Contributions;
            var structs = program.Structs.ToList();
            foreach (var contribution in contributions)
            {
                contribution.AddGeneratedStructs(state, structs);
            }

            var rewrittenFunctions = program.Functions
                .Select(function => new FunctionSyntax(
                    function.Type,
                    function.Name,
                    function.Parameters,
                    RewriteBlock(function.Block, state),
                    function.IsExpressionBodied,
                    function.IsInline,
                    function.IsPure,
                    function.IsExtern,
                    function.Attributes))
                .ToList();

            ValidateNameCollisions(program, contributions.SelectMany(contribution => contribution.GeneratedNames(state)));

            var functions = rewrittenFunctions
                .Concat(contributions.SelectMany(contribution => contribution.GeneratedFunctions(state)))
                .ToList();

            return new ProgramSyntax(
                program.Imports,
                program.TypeAliases,
                program.Constants
                    .Concat(contributions.SelectMany(contribution => contribution.GeneratedConstants(state)))
                    .ToList(),
                program.Enums,
                structs,
                functions);
        }

        public static void ValidateNameCollisions(ProgramSyntax program, IEnumerable<GeneratedName> generatedNames)
        {
            var userSymbols = UserSymbols(program);
            var generatedSymbols = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var generatedName in generatedNames)
            {
                if (userSymbols.TryGetValue(generatedName.Name, out var userSymbol))
                {
                    throw new InvalidOperationException($"actor framework cannot generate {generatedName.Origin} named '{generatedName.Name}' because {userSymbol} is already declared.");
                }

                if (!generatedSymbols.TryAdd(generatedName.Name, generatedName.Origin))
                {
                    throw new InvalidOperationException($"actor framework cannot generate {generatedName.Origin} named '{generatedName.Name}' because {generatedSymbols[generatedName.Name]} also generates '{generatedName.Name}'.");
                }
            }
        }

        private static IReadOnlyDictionary<string, string> UserSymbols(ProgramSyntax program)
        {
            var symbols = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var typeAlias in program.TypeAliases)
            {
                symbols.TryAdd(typeAlias.Name, $"user type alias '{typeAlias.Name}'");
            }

            foreach (var constant in program.Constants)
            {
                symbols.TryAdd(constant.Name, $"user constant '{constant.Name}'");
            }

            foreach (var enumSyntax in program.Enums)
            {
                symbols.TryAdd(enumSyntax.Name, $"user enum '{enumSyntax.Name}'");
            }

            foreach (var structSyntax in program.Structs)
            {
                symbols.TryAdd(structSyntax.Name, $"user struct '{structSyntax.Name}'");
            }

            foreach (var function in program.Functions)
            {
                symbols.TryAdd(function.Name, $"user function '{function.Name}'");
            }

            return symbols;
        }

        public static IEnumerable<GeneratedName> GeneratedNames(ActorFrameworkState state)
        {
            return ActorFrameworkDomains.Contributions
                .SelectMany(contribution => contribution.GeneratedNames(state));
        }

    }

    private static class ActorFrameworkDomains
    {
        public static IReadOnlyList<GeneratedProgramContribution> Contributions { get; } =
        [
            new(
                "Actors",
                Actors.AddGeneratedStructs,
                static state => Actors.GeneratedConstants(state.Actors.EnemyDefs),
                Actors.GeneratedFunctions,
                Actors.GeneratedNames),
            new(
                "Projectiles",
                Projectiles.AddGeneratedStructs,
                static state => Projectiles.GeneratedConstants(state.Projectiles.Definitions),
                static _ => [],
                Projectiles.GeneratedNames),
            new(
                "Effects",
                Effects.AddGeneratedStructs,
                static state => Effects.GeneratedConstants(state.Effects.Definitions),
                static _ => [],
                Effects.GeneratedNames),
        ];
    }

    private sealed record GeneratedProgramContribution(
        string Domain,
        Action<ActorFrameworkState, IList<StructSyntax>> AddGeneratedStructs,
        Func<ActorFrameworkState, IEnumerable<ConstDeclarationSyntax>> GeneratedConstants,
        Func<ActorFrameworkState, IEnumerable<FunctionSyntax>> GeneratedFunctions,
        Func<ActorFrameworkState, IEnumerable<GeneratedName>> GeneratedNames);

    private sealed record GeneratedName(string Name, string Origin);
}
