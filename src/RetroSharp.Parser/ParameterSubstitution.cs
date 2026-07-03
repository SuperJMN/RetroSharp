namespace RetroSharp.Parser;

public static class ParameterSubstitution
{
    public static BlockSyntax Substitute(FunctionSyntax function, FunctionCall call, string targetName)
    {
        var substitutions = BuildSubstitutions(function, call, targetName);

        return Substitute(function.Block, substitutions);
    }

    public static ExpressionSyntax SubstituteReturnExpression(FunctionSyntax function, FunctionCall call, string targetName)
    {
        FunctionContractValidator.RequireValueInlineSubstitution(function, targetName);

        if (function.Block.Statements.Count != 1 ||
            function.Block.Statements[0] is not ReturnSyntax returnSyntax ||
            !returnSyntax.Expression.HasValue)
        {
            throw new InvalidOperationException($"{targetName} target can use '{call.Name}' as a value only when the helper body is exactly one return expression.");
        }

        var substitutions = BuildSubstitutions(function, call, targetName);
        return ConstantFolder.FoldConstants(Substitute(returnSyntax.Expression.Value, substitutions));
    }

    private static IReadOnlyDictionary<string, ExpressionSyntax> BuildSubstitutions(FunctionSyntax function, FunctionCall call, string targetName)
    {
        var parameters = function.Parameters.ToList();
        var arguments = BindArguments(parameters, call, targetName);

        var substitutions = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
        for (var index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index];
            substitutions.Add(parameter.Name, ArgumentFor(parameter, index, arguments, call.Name, targetName, substitutions));
        }

        return substitutions;
    }

    private static IReadOnlyDictionary<string, ExpressionSyntax> BindArguments(
        IReadOnlyList<ParameterSyntax> parameters,
        FunctionCall call,
        string targetName)
    {
        var parameterNames = parameters.Select(parameter => parameter.Name).ToHashSet(StringComparer.Ordinal);
        var arguments = new Dictionary<string, ExpressionSyntax>(StringComparer.Ordinal);
        var positionalIndex = 0;
        var namedArgumentSeen = false;

        foreach (var argument in call.Parameters)
        {
            if (argument is NamedArgumentSyntax namedArgument)
            {
                namedArgumentSeen = true;
                if (!parameterNames.Contains(namedArgument.Name))
                {
                    throw new InvalidOperationException($"{targetName} target call '{call.Name}' has no parameter named '{namedArgument.Name}'.");
                }

                if (!arguments.TryAdd(namedArgument.Name, namedArgument.Expression))
                {
                    throw new InvalidOperationException($"{targetName} target call '{call.Name}' supplies parameter '{namedArgument.Name}' more than once.");
                }

                continue;
            }

            if (namedArgumentSeen)
            {
                throw new InvalidOperationException($"{targetName} target call '{call.Name}' cannot use positional arguments after named arguments.");
            }

            if (positionalIndex >= parameters.Count)
            {
                throw new InvalidOperationException($"{targetName} target expected at most {parameters.Count} argument(s) for '{call.Name}', but got {call.Parameters.Count()}.");
            }

            arguments.Add(parameters[positionalIndex].Name, argument);
            positionalIndex++;
        }

        return arguments;
    }

    private static ExpressionSyntax ArgumentFor(
        ParameterSyntax parameter,
        int index,
        IReadOnlyDictionary<string, ExpressionSyntax> arguments,
        string callName,
        string targetName,
        IReadOnlyDictionary<string, ExpressionSyntax> substitutions)
    {
        if (arguments.TryGetValue(parameter.Name, out var argument))
        {
            return argument;
        }

        return parameter.DefaultValue.Match(
            defaultValue => ConstantFolder.FoldConstants(Substitute(defaultValue, substitutions)),
            () => throw new InvalidOperationException($"{targetName} target expected argument {index + 1} for '{callName}' because parameter '{parameter.Name}' has no default value."));
    }

    private static BlockSyntax Substitute(BlockSyntax block, IReadOnlyDictionary<string, ExpressionSyntax> substitutions)
    {
        return new BlockSyntax(block.Statements.Select(statement => Substitute(statement, substitutions)).ToList());
    }

    private static StatementSyntax Substitute(StatementSyntax statement, IReadOnlyDictionary<string, ExpressionSyntax> substitutions)
    {
        return statement switch
        {
            ConstDeclarationSyntax constant => new ConstDeclarationSyntax(
                constant.TypeAnnotation,
                constant.Name,
                Substitute(constant.Value, substitutions)),
            DeclarationSyntax declaration => new DeclarationSyntax(
                declaration.Type,
                declaration.Name,
                declaration.ArrayLength.Map(expression => Substitute(expression, substitutions)),
                declaration.Initialization.Map(expression => Substitute(expression, substitutions)),
                declaration.IsImmutable),
            ExpressionStatementSyntax expressionStatement => new ExpressionStatementSyntax(Substitute(expressionStatement.Expression, substitutions)),
            IfElseSyntax ifElse => new IfElseSyntax(
                Substitute(ifElse.Condition, substitutions),
                Substitute(ifElse.ThenBlock, substitutions),
                ifElse.ElseBlock.Map(block => Substitute(block, substitutions))),
            WhileSyntax whileSyntax => new WhileSyntax(Substitute(whileSyntax.Condition, substitutions), Substitute(whileSyntax.Body, substitutions)),
            DoWhileSyntax doWhileSyntax => new DoWhileSyntax(
                Substitute(doWhileSyntax.Body, substitutions),
                Substitute(doWhileSyntax.Condition, substitutions)),
            RangeForSyntax rangeForSyntax => new RangeForSyntax(
                rangeForSyntax.Type,
                rangeForSyntax.Identifier,
                Substitute(rangeForSyntax.Start, substitutions),
                Substitute(rangeForSyntax.End, substitutions),
                Substitute(rangeForSyntax.Body, substitutions)),
            ForSyntax forSyntax => new ForSyntax(
                forSyntax.Initializer.Map(initializer => Substitute(initializer, substitutions)),
                forSyntax.Condition.Map(condition => Substitute(condition, substitutions)),
                forSyntax.Increment.Map(increment => Substitute(increment, substitutions)),
                Substitute(forSyntax.Body, substitutions)),
            SwitchSyntax switchSyntax => new SwitchSyntax(
                Substitute(switchSyntax.Subject, substitutions),
                switchSyntax.Cases
                    .Select(switchCase => new SwitchCaseSyntax(
                        switchCase.Patterns.Select(pattern => Substitute(pattern, substitutions)).ToList(),
                        Substitute(switchCase.Block, substitutions)))
                    .ToList(),
                switchSyntax.DefaultBlock.Map(block => Substitute(block, substitutions))),
            ReturnSyntax returnSyntax => new ReturnSyntax(returnSyntax.Expression.Map(expression => Substitute(expression, substitutions))),
            _ => statement,
        };
    }

    private static ExpressionSyntax Substitute(ExpressionSyntax expression, IReadOnlyDictionary<string, ExpressionSyntax> substitutions)
    {
        return expression switch
        {
            IdentifierSyntax identifier when substitutions.TryGetValue(identifier.Identifier, out var value) => value,
            AssignmentSyntax assignment => new AssignmentSyntax(Substitute(assignment.Left, substitutions), assignment.OperatorSymbol, Substitute(assignment.Right, substitutions)),
            BinaryExpressionSyntax binary => new BinaryExpressionSyntax(
                Substitute(binary.Left, substitutions),
                Substitute(binary.Right, substitutions),
                binary.Operator),
            ArrayInitializerSyntax arrayInitializer => new ArrayInitializerSyntax(arrayInitializer.Elements.Select(element => Substitute(element, substitutions))),
            StructInitializerSyntax structInitializer => new StructInitializerSyntax(structInitializer.Fields.Select(field => new StructFieldInitializerSyntax(field.Name, Substitute(field.Expression, substitutions)))),
            ConditionalExpressionSyntax conditional => new ConditionalExpressionSyntax(
                Substitute(conditional.Condition, substitutions),
                Substitute(conditional.WhenTrue, substitutions),
                Substitute(conditional.WhenFalse, substitutions)),
            SwitchExpressionSyntax switchExpression => new SwitchExpressionSyntax(
                Substitute(switchExpression.Subject, substitutions),
                switchExpression.Arms
                    .Select(arm => new SwitchExpressionArmSyntax(
                        arm.Patterns.Select(pattern => Substitute(pattern, substitutions)).ToList(),
                        Substitute(arm.Value, substitutions)))
                    .ToList(),
                switchExpression.DefaultValue.Map(expression => Substitute(expression, substitutions))),
            PipelineExpressionSyntax pipeline => new PipelineExpressionSyntax(
                Substitute(pipeline.Value, substitutions),
                pipeline.Steps
                    .Select(step => new PipelineStepSyntax(
                        step.FunctionName,
                        step.Arguments.Select(argument => Substitute(argument, substitutions))))
                    .ToList()),
            UnaryExpressionSyntax unary => new UnaryExpressionSyntax(unary.OperatorSymbol, Substitute(unary.Operand, substitutions)),
            CastSyntax cast => new CastSyntax(cast.Type, Substitute(cast.Expression, substitutions)),
            QualifiedCallSyntax qualifiedCall => new QualifiedCallSyntax(
                qualifiedCall.Qualifier,
                qualifiedCall.Method,
                qualifiedCall.Parameters.Select(parameter => Substitute(parameter, substitutions))),
            FunctionCall call => new FunctionCall(call.Name, call.Parameters.Select(parameter => Substitute(parameter, substitutions))),
            NamedArgumentSyntax namedArgument => new NamedArgumentSyntax(namedArgument.Name, Substitute(namedArgument.Expression, substitutions)),
            MemberAccessSyntax memberAccess => new MemberAccessSyntax(Substitute(memberAccess.Target, substitutions), memberAccess.Member),
            IndexExpressionSyntax indexExpression => new IndexExpressionSyntax(indexExpression.BaseIdentifier, Substitute(indexExpression.Index, substitutions)),
            PostfixMutationSyntax postfix => new PostfixMutationSyntax(Substitute(postfix.Target, substitutions), postfix.OperatorSymbol),
            CountOfSyntax countOf => countOf,
            _ => expression,
        };
    }

    private static SwitchCasePatternSyntax Substitute(SwitchCasePatternSyntax pattern, IReadOnlyDictionary<string, ExpressionSyntax> substitutions)
    {
        var start = Substitute(pattern.Start, substitutions);
        return pattern.End.Match(
            end => new SwitchCasePatternSyntax(start, Substitute(end, substitutions)),
            () => new SwitchCasePatternSyntax(start));
    }

    private static LValue Substitute(LValue lValue, IReadOnlyDictionary<string, ExpressionSyntax> substitutions)
    {
        return lValue switch
        {
            IndexLValue index => new IndexLValue(index.BaseIdentifier, Substitute(index.Index, substitutions)),
            PointerDerefLValue pointer => new PointerDerefLValue(Substitute(pointer.Expression, substitutions)),
            MemberAccessLValue memberAccess => new MemberAccessLValue((MemberAccessSyntax)Substitute(memberAccess.MemberAccess, substitutions)),
            _ => lValue,
        };
    }
}
