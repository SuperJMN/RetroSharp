namespace RetroSharp.Parser;

public static class TypeAliasResolver
{
    public static ProgramSyntax Resolve(ProgramSyntax program)
    {
        var aliases = BuildAliasTable(program.TypeAliases);
        var constants = program.Constants
            .Select(constant => new ConstDeclarationSyntax(
                constant.TypeAnnotation.Map(type => ResolveType(type, aliases)),
                constant.Name,
                ResolveExpression(constant.Value, aliases)))
            .ToList();
        var structs = program.Structs
            .Select(structSyntax => new StructSyntax(
                structSyntax.Name,
                structSyntax.Fields
                    .Select(field => new StructFieldSyntax(ResolveType(field.Type, aliases), field.Name))
                    .ToList()))
            .ToList();
        var functions = program.Functions
            .Select(function => new FunctionSyntax(
                ResolveType(function.Type, aliases),
                function.Name,
                function.Parameters
                    .Select(parameter => new ParameterSyntax(
                        ResolveType(parameter.Type, aliases),
                        parameter.Name,
                        parameter.DefaultValue.Map(expression => ResolveExpression(expression, aliases)),
                        parameter.IsReceiver))
                    .ToList(),
                ResolveBlock(function.Block, aliases),
                function.IsExpressionBodied,
                function.IsInline,
                function.IsPure))
            .ToList();

        return new ProgramSyntax([], constants, program.Enums, structs, functions);
    }

    private static IReadOnlyDictionary<string, string> BuildAliasTable(IEnumerable<TypeAliasSyntax> typeAliases)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var alias in typeAliases)
        {
            if (aliases.ContainsKey(alias.Name))
            {
                throw new InvalidOperationException($"Type alias '{alias.Name}' is already declared.");
            }

            aliases[alias.Name] = ResolveType(alias.Type, aliases);
        }

        return aliases;
    }

    private static BlockSyntax ResolveBlock(BlockSyntax block, IReadOnlyDictionary<string, string> aliases)
    {
        return new BlockSyntax(block.Statements.Select(statement => ResolveStatement(statement, aliases)).ToList());
    }

    private static StatementSyntax ResolveStatement(StatementSyntax statement, IReadOnlyDictionary<string, string> aliases)
    {
        return statement switch
        {
            ConstDeclarationSyntax constant => new ConstDeclarationSyntax(
                constant.TypeAnnotation.Map(type => ResolveType(type, aliases)),
                constant.Name,
                ResolveExpression(constant.Value, aliases)),
            DeclarationSyntax declaration => new DeclarationSyntax(
                ResolveType(declaration.Type, aliases),
                declaration.Name,
                declaration.ArrayLength.Map(expression => ResolveExpression(expression, aliases)),
                declaration.Initialization.Map(expression => ResolveExpression(expression, aliases)),
                declaration.IsImmutable),
            ExpressionStatementSyntax expressionStatement => new ExpressionStatementSyntax(ResolveExpression(expressionStatement.Expression, aliases)),
            IfElseSyntax ifElse => new IfElseSyntax(
                ResolveExpression(ifElse.Condition, aliases),
                ResolveBlock(ifElse.ThenBlock, aliases),
                ifElse.ElseBlock.Map(block => ResolveBlock(block, aliases))),
            WhileSyntax whileSyntax => new WhileSyntax(ResolveExpression(whileSyntax.Condition, aliases), ResolveBlock(whileSyntax.Body, aliases)),
            DoWhileSyntax doWhileSyntax => new DoWhileSyntax(
                ResolveBlock(doWhileSyntax.Body, aliases),
                ResolveExpression(doWhileSyntax.Condition, aliases)),
            LoopSyntax loopSyntax => new LoopSyntax(ResolveBlock(loopSyntax.Body, aliases)),
            RangeForSyntax rangeForSyntax => new RangeForSyntax(
                ResolveType(rangeForSyntax.Type, aliases),
                rangeForSyntax.Identifier,
                ResolveExpression(rangeForSyntax.Start, aliases),
                ResolveExpression(rangeForSyntax.End, aliases),
                ResolveBlock(rangeForSyntax.Body, aliases)),
            ForSyntax forSyntax => new ForSyntax(
                forSyntax.Initializer.Map(initializer => ResolveStatement(initializer, aliases)),
                forSyntax.Condition.Map(condition => ResolveExpression(condition, aliases)),
                forSyntax.Increment.Map(increment => ResolveExpression(increment, aliases)),
                ResolveBlock(forSyntax.Body, aliases)),
            SwitchSyntax switchSyntax => new SwitchSyntax(
                ResolveExpression(switchSyntax.Subject, aliases),
                switchSyntax.Cases
                    .Select(switchCase => new SwitchCaseSyntax(
                        switchCase.Patterns.Select(pattern => ResolveSwitchCasePattern(pattern, aliases)).ToList(),
                        ResolveBlock(switchCase.Block, aliases)))
                    .ToList(),
                switchSyntax.DefaultBlock.Map(block => ResolveBlock(block, aliases))),
            ReturnSyntax returnSyntax => new ReturnSyntax(returnSyntax.Expression.Map(expression => ResolveExpression(expression, aliases))),
            _ => statement,
        };
    }

    private static ExpressionSyntax ResolveExpression(ExpressionSyntax expression, IReadOnlyDictionary<string, string> aliases)
    {
        return expression switch
        {
            AssignmentSyntax assignment => new AssignmentSyntax(
                ResolveLValue(assignment.Left, aliases),
                assignment.OperatorSymbol,
                ResolveExpression(assignment.Right, aliases)),
            BinaryExpressionSyntax binary => new BinaryExpressionSyntax(
                ResolveExpression(binary.Left, aliases),
                ResolveExpression(binary.Right, aliases),
                binary.Operator),
            ConditionalExpressionSyntax conditional => new ConditionalExpressionSyntax(
                ResolveExpression(conditional.Condition, aliases),
                ResolveExpression(conditional.WhenTrue, aliases),
                ResolveExpression(conditional.WhenFalse, aliases)),
            SwitchExpressionSyntax switchExpression => new SwitchExpressionSyntax(
                ResolveExpression(switchExpression.Subject, aliases),
                switchExpression.Arms
                    .Select(arm => new SwitchExpressionArmSyntax(
                        arm.Patterns.Select(pattern => ResolveSwitchCasePattern(pattern, aliases)).ToList(),
                        ResolveExpression(arm.Value, aliases)))
                    .ToList(),
                switchExpression.DefaultValue.Map(expression => ResolveExpression(expression, aliases))),
            PipelineExpressionSyntax pipeline => new PipelineExpressionSyntax(
                ResolveExpression(pipeline.Value, aliases),
                pipeline.Steps
                    .Select(step => new PipelineStepSyntax(
                        step.FunctionName,
                        step.Arguments.Select(argument => ResolveExpression(argument, aliases))))
                    .ToList()),
            ArrayInitializerSyntax arrayInitializer => new ArrayInitializerSyntax(arrayInitializer.Elements.Select(element => ResolveExpression(element, aliases))),
            StructInitializerSyntax structInitializer => new StructInitializerSyntax(structInitializer.Fields.Select(field => new StructFieldInitializerSyntax(field.Name, ResolveExpression(field.Expression, aliases)))),
            UnaryExpressionSyntax unary => new UnaryExpressionSyntax(unary.OperatorSymbol, ResolveExpression(unary.Operand, aliases)),
            CastSyntax cast => new CastSyntax(ResolveType(cast.Type, aliases), ResolveExpression(cast.Expression, aliases)),
            SdkDotCallSyntax sdkDotCall => new SdkDotCallSyntax(
                sdkDotCall.Module,
                sdkDotCall.Method,
                sdkDotCall.Parameters.Select(parameter => ResolveExpression(parameter, aliases))),
            FunctionCall call => new FunctionCall(call.Name, call.Parameters.Select(parameter => ResolveExpression(parameter, aliases))),
            NamedArgumentSyntax namedArgument => new NamedArgumentSyntax(namedArgument.Name, ResolveExpression(namedArgument.Expression, aliases)),
            MemberAccessSyntax memberAccess => new MemberAccessSyntax(ResolveExpression(memberAccess.Target, aliases), memberAccess.Member),
            IndexExpressionSyntax indexExpression => new IndexExpressionSyntax(indexExpression.BaseIdentifier, ResolveExpression(indexExpression.Index, aliases)),
            PostfixMutationSyntax postfix => new PostfixMutationSyntax(ResolveLValue(postfix.Target, aliases), postfix.OperatorSymbol),
            SizeOfSyntax sizeOf => new SizeOfSyntax(ResolveType(sizeOf.Type, aliases)),
            OffsetOfSyntax offsetOf => new OffsetOfSyntax(ResolveType(offsetOf.Type, aliases), offsetOf.Field),
            CountOfSyntax countOf => countOf,
            _ => expression,
        };
    }

    private static SwitchCasePatternSyntax ResolveSwitchCasePattern(SwitchCasePatternSyntax pattern, IReadOnlyDictionary<string, string> aliases)
    {
        var start = ResolveExpression(pattern.Start, aliases);
        return pattern.End.Match(
            end => new SwitchCasePatternSyntax(start, ResolveExpression(end, aliases)),
            () => new SwitchCasePatternSyntax(start));
    }

    private static LValue ResolveLValue(LValue lValue, IReadOnlyDictionary<string, string> aliases)
    {
        return lValue switch
        {
            IndexLValue index => new IndexLValue(index.BaseIdentifier, ResolveExpression(index.Index, aliases)),
            PointerDerefLValue pointer => new PointerDerefLValue(ResolveExpression(pointer.Expression, aliases)),
            MemberAccessLValue memberAccess => new MemberAccessLValue((MemberAccessSyntax)ResolveExpression(memberAccess.MemberAccess, aliases)),
            _ => lValue,
        };
    }

    private static string ResolveType(string type, IReadOnlyDictionary<string, string> aliases)
    {
        if (type.StartsWith("ptr<", StringComparison.Ordinal) && type.EndsWith(">", StringComparison.Ordinal))
        {
            var inner = type[4..^1];
            return $"ptr<{ResolveType(inner, aliases)}>";
        }

        return aliases.TryGetValue(type, out var resolved) ? resolved : type;
    }
}
