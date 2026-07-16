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

            var structs = program.Structs.ToList();
            Actors.AddGeneratedStructs(state, structs);
            Projectiles.AddGeneratedStructs(state, structs);
            Effects.AddGeneratedStructs(state, structs);

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

            ValidateNameCollisions(program, state);

            var functions = rewrittenFunctions
                .Concat(Actors.GeneratedFunctions(state))
                .ToList();

            return new ProgramSyntax(
                program.Imports,
                program.TypeAliases,
                program.Constants
                    .Concat(Actors.GeneratedConstants(state.EnemyDefs))
                    .Concat(Projectiles.GeneratedConstants(state.ProjectileDefs))
                    .Concat(Effects.GeneratedConstants(state.EffectDefs))
                    .ToList(),
                program.Enums,
                structs,
                functions);
        }

        public static void ValidateNameCollisions(ProgramSyntax program, ActorFrameworkState state)
        {
            var userSymbols = UserSymbols(program);
            var generatedNames = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var generatedName in GeneratedNames(state))
            {
                if (userSymbols.TryGetValue(generatedName.Name, out var userSymbol))
                {
                    throw new InvalidOperationException($"actor framework cannot generate {generatedName.Origin} named '{generatedName.Name}' because {userSymbol} is already declared.");
                }

                if (!generatedNames.TryAdd(generatedName.Name, generatedName.Origin))
                {
                    throw new InvalidOperationException($"actor framework cannot generate {generatedName.Origin} named '{generatedName.Name}' because {generatedNames[generatedName.Name]} also generates '{generatedName.Name}'.");
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
            return Actors.GeneratedNames(state)
                .Concat(Projectiles.GeneratedNames(state))
                .Concat(Effects.GeneratedNames(state));
        }

    }

    private sealed record GeneratedName(string Name, string Origin);
}
