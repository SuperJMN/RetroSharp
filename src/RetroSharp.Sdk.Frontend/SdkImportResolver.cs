namespace RetroSharp.Sdk;

using CSharpFunctionalExtensions;
using RetroSharp.Core.Sdk;
using RetroSharp.Parser;

public static class SdkImportResolver
{
    public const string Portable2D = "RetroSharp.Portable2D";

    public static void ValidateImports(ProgramSyntax program, SdkLibraryRegistry? registry = null)
    {
        registry ??= SdkLibraryRegistry.Default;
        foreach (var import in program.Imports)
        {
            if (!registry.TryResolve(import.Path, out _))
            {
                throw new InvalidOperationException($"Unknown import '{import.Path}'.");
            }
        }
    }

    public static void ValidateSdkUsage(
        ProgramSyntax program,
        SdkLibraryImportMode importMode,
        IReadOnlyList<string>? libraryImportPaths = null)
    {
        if (importMode == SdkLibraryImportMode.LegacyAutoImport
            || ImportsPortable2D(program)
            || ImportsPortable2D(libraryImportPaths))
        {
            return;
        }

        var module = FindSdkModuleUse(program);
        if (module is not null)
        {
            throw new InvalidOperationException($"SDK module '{module}' requires import '{Portable2D}'.");
        }
    }

    public static bool ImportsPortable2D(ProgramSyntax program)
    {
        return program.Imports.Any(import => import.Path == Portable2D);
    }

    private static bool ImportsPortable2D(IReadOnlyList<string>? libraryImportPaths)
    {
        return libraryImportPaths?.Any(importPath => importPath == Portable2D) == true;
    }

    private static string? FindSdkModuleUse(ProgramSyntax program)
    {
        foreach (var function in program.Functions)
        {
            var module = FindSdkModuleUse(function.Block);
            if (module is not null)
            {
                return module;
            }
        }

        return null;
    }

    private static string? FindSdkModuleUse(BlockSyntax block)
    {
        foreach (var statement in block.Statements)
        {
            var module = FindSdkModuleUse(statement);
            if (module is not null)
            {
                return module;
            }
        }

        return null;
    }

    private static string? FindSdkModuleUse(StatementSyntax statement)
    {
        return statement switch
        {
            ExpressionStatementSyntax expression => FindSdkModuleUse(expression.Expression),
            ReturnSyntax { Expression.HasValue: true } ret => FindSdkModuleUse(ret.Expression.Value),
            DeclarationSyntax declaration => FindSdkModuleUse(declaration.ArrayLength)
                ?? FindSdkModuleUse(declaration.Initialization),
            ConstDeclarationSyntax constant => FindSdkModuleUse(constant.Value),
            IfElseSyntax branch => FindSdkModuleUse(branch.Condition)
                ?? FindSdkModuleUse(branch.ThenBlock)
                ?? (branch.ElseBlock.HasValue ? FindSdkModuleUse(branch.ElseBlock.Value) : null),
            WhileSyntax loop => FindSdkModuleUse(loop.Condition) ?? FindSdkModuleUse(loop.Body),
            DoWhileSyntax loop => FindSdkModuleUse(loop.Body) ?? FindSdkModuleUse(loop.Condition),
            LoopSyntax loop => FindSdkModuleUse(loop.Body),
            ForSyntax loop => FindSdkModuleUse(loop.Initializer)
                ?? FindSdkModuleUse(loop.Condition)
                ?? FindSdkModuleUse(loop.Increment)
                ?? FindSdkModuleUse(loop.Body),
            RangeForSyntax loop => FindSdkModuleUse(loop.Start)
                ?? FindSdkModuleUse(loop.End)
                ?? FindSdkModuleUse(loop.Body),
            SwitchSyntax switchSyntax => FindSdkModuleUse(switchSyntax.Subject)
                ?? switchSyntax.Cases.Select(FindSdkModuleUse).FirstOrDefault(module => module is not null)
                ?? (switchSyntax.DefaultBlock.HasValue ? FindSdkModuleUse(switchSyntax.DefaultBlock.Value) : null),
            BreakSyntax or ContinueSyntax => null,
            _ => null,
        };
    }

    private static string? FindSdkModuleUse(SwitchCaseSyntax switchCase)
    {
        return switchCase.Patterns.Select(FindSdkModuleUse).FirstOrDefault(module => module is not null)
            ?? FindSdkModuleUse(switchCase.Block);
    }

    private static string? FindSdkModuleUse(SwitchCasePatternSyntax pattern)
    {
        return FindSdkModuleUse(pattern.Start)
            ?? (pattern.End.HasValue ? FindSdkModuleUse(pattern.End.Value) : null);
    }

    private static string? FindSdkModuleUse(Maybe<StatementSyntax> statement)
    {
        return statement.HasValue ? FindSdkModuleUse(statement.Value) : null;
    }

    private static string? FindSdkModuleUse(Maybe<ExpressionSyntax> expression)
    {
        return expression.HasValue ? FindSdkModuleUse(expression.Value) : null;
    }

    private static string? FindSdkModuleUse(ExpressionSyntax expression)
    {
        return expression switch
        {
            SdkDotCallSyntax call when SdkModuleRegistry.IsKnownModule(call.Module) => call.Module,
            SdkDotCallSyntax call => call.Parameters.Select(FindSdkModuleUse).FirstOrDefault(module => module is not null),
            FunctionCall call => call.Parameters.Select(FindSdkModuleUse).FirstOrDefault(module => module is not null),
            NamedArgumentSyntax named => FindSdkModuleUse(named.Expression),
            AssignmentSyntax assignment => FindSdkModuleUse(assignment.Right),
            BinaryExpressionSyntax binary => FindSdkModuleUse(binary.Left) ?? FindSdkModuleUse(binary.Right),
            UnaryExpressionSyntax unary => FindSdkModuleUse(unary.Operand),
            ConditionalExpressionSyntax conditional => FindSdkModuleUse(conditional.Condition)
                ?? FindSdkModuleUse(conditional.WhenTrue)
                ?? FindSdkModuleUse(conditional.WhenFalse),
            SwitchExpressionSyntax switchExpression => FindSdkModuleUse(switchExpression.Subject)
                ?? switchExpression.Arms.Select(FindSdkModuleUse).FirstOrDefault(module => module is not null)
                ?? FindSdkModuleUse(switchExpression.DefaultValue),
            PipelineExpressionSyntax pipeline => FindSdkModuleUse(pipeline.Value)
                ?? pipeline.Steps.SelectMany(step => step.Arguments).Select(FindSdkModuleUse).FirstOrDefault(module => module is not null),
            ArrayInitializerSyntax array => array.Elements.Select(FindSdkModuleUse).FirstOrDefault(module => module is not null),
            StructInitializerSyntax initializer => initializer.Fields.Select(field => FindSdkModuleUse(field.Expression)).FirstOrDefault(module => module is not null),
            IndexExpressionSyntax index => FindSdkModuleUse(index.Index),
            MemberAccessSyntax member => FindSdkModuleUse(member.Target),
            CastSyntax cast => FindSdkModuleUse(cast.Expression),
            PostfixMutationSyntax => null,
            SizeOfSyntax or OffsetOfSyntax or CountOfSyntax or ConstantSyntax or IdentifierSyntax => null,
            _ => null,
        };
    }

    private static string? FindSdkModuleUse(SwitchExpressionArmSyntax arm)
    {
        return arm.Patterns.Select(FindSdkModuleUse).FirstOrDefault(module => module is not null)
            ?? FindSdkModuleUse(arm.Value);
    }
}
