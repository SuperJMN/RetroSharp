namespace RetroSharp.Parser;

internal static class StaticClassLowerer
{
    public static FunctionSyntax LowerInstanceMethod(
        string className,
        IReadOnlySet<string> fieldNames,
        IReadOnlySet<string> instanceMethodNames,
        FunctionSyntax method)
    {
        var receiverName = ReceiverName(className);
        var parameters = new[] { new ParameterSyntax(className, receiverName, Maybe<ExpressionSyntax>.None, true) }
            .Concat(method.Parameters)
            .ToList();
        var locals = parameters.Select(parameter => parameter.Name).ToHashSet(StringComparer.Ordinal);
        var body = RewriteInstanceBlock(method.Block, receiverName, fieldNames, instanceMethodNames, locals);

        return new FunctionSyntax(
            method.Type,
            method.Name,
            parameters,
            body,
            method.IsExpressionBodied,
            method.IsInline,
            method.IsPure,
            method.IsExtern,
            method.Attributes);
    }

    public static ProgramSyntax LowerStaticCalls(
        ProgramSyntax program,
        IReadOnlyDictionary<string, string> staticMethods)
    {
        if (staticMethods.Count == 0)
        {
            return program;
        }

        var constants = program.Constants
            .Select(constant => new ConstDeclarationSyntax(
                constant.TypeAnnotation,
                constant.Name,
                RewriteStaticExpression(constant.Value, staticMethods, new HashSet<string>(StringComparer.Ordinal))))
            .ToList();
        var functions = program.Functions
            .Select(function => new FunctionSyntax(
                function.Type,
                function.Name,
                function.Parameters,
                RewriteStaticBlock(
                    function.Block,
                    staticMethods,
                    function.Parameters.Select(parameter => parameter.Name)),
                function.IsExpressionBodied,
                function.IsInline,
                function.IsPure,
                function.IsExtern,
                function.Attributes))
            .ToList();

        return new ProgramSyntax(program.Imports, program.TypeAliases, constants, program.Enums, program.Structs, functions);
    }

    private static BlockSyntax RewriteInstanceBlock(
        BlockSyntax block,
        string receiverName,
        IReadOnlySet<string> fieldNames,
        IReadOnlySet<string> instanceMethodNames,
        HashSet<string> visibleNames)
    {
        var scopedNames = new HashSet<string>(visibleNames, StringComparer.Ordinal);
        var statements = new List<StatementSyntax>();
        foreach (var statement in block.Statements)
        {
            statements.Add(RewriteInstanceStatement(statement, receiverName, fieldNames, instanceMethodNames, scopedNames));
            AddDeclaredName(statement, scopedNames);
        }

        return new BlockSyntax(statements);
    }

    private static StatementSyntax RewriteInstanceStatement(
        StatementSyntax statement,
        string receiverName,
        IReadOnlySet<string> fieldNames,
        IReadOnlySet<string> instanceMethodNames,
        HashSet<string> visibleNames)
    {
        return statement switch
        {
            ConstDeclarationSyntax constant => new ConstDeclarationSyntax(
                constant.TypeAnnotation,
                constant.Name,
                RewriteInstanceExpression(constant.Value, receiverName, fieldNames, instanceMethodNames, visibleNames)),
            DeclarationSyntax declaration => new DeclarationSyntax(
                declaration.Type,
                declaration.Name,
                declaration.ArrayLength.Map(expression => RewriteInstanceExpression(expression, receiverName, fieldNames, instanceMethodNames, visibleNames)),
                declaration.Initialization.Map(expression => RewriteInstanceExpression(expression, receiverName, fieldNames, instanceMethodNames, visibleNames)),
                declaration.IsImmutable),
            ExpressionStatementSyntax expressionStatement => new ExpressionStatementSyntax(
                RewriteInstanceExpression(expressionStatement.Expression, receiverName, fieldNames, instanceMethodNames, visibleNames)),
            IfElseSyntax ifElse => new IfElseSyntax(
                RewriteInstanceExpression(ifElse.Condition, receiverName, fieldNames, instanceMethodNames, visibleNames),
                RewriteInstanceBlock(ifElse.ThenBlock, receiverName, fieldNames, instanceMethodNames, visibleNames),
                ifElse.ElseBlock.Map(block => RewriteInstanceBlock(block, receiverName, fieldNames, instanceMethodNames, visibleNames))),
            WhileSyntax whileSyntax => new WhileSyntax(
                RewriteInstanceExpression(whileSyntax.Condition, receiverName, fieldNames, instanceMethodNames, visibleNames),
                RewriteInstanceBlock(whileSyntax.Body, receiverName, fieldNames, instanceMethodNames, visibleNames)),
            DoWhileSyntax doWhileSyntax => new DoWhileSyntax(
                RewriteInstanceBlock(doWhileSyntax.Body, receiverName, fieldNames, instanceMethodNames, visibleNames),
                RewriteInstanceExpression(doWhileSyntax.Condition, receiverName, fieldNames, instanceMethodNames, visibleNames)),
            LoopSyntax loopSyntax => new LoopSyntax(RewriteInstanceBlock(loopSyntax.Body, receiverName, fieldNames, instanceMethodNames, visibleNames)),
            RangeForSyntax rangeForSyntax => RewriteRangeFor(rangeForSyntax, receiverName, fieldNames, instanceMethodNames, visibleNames),
            ForSyntax forSyntax => RewriteFor(forSyntax, receiverName, fieldNames, instanceMethodNames, visibleNames),
            SwitchSyntax switchSyntax => new SwitchSyntax(
                RewriteInstanceExpression(switchSyntax.Subject, receiverName, fieldNames, instanceMethodNames, visibleNames),
                switchSyntax.Cases.Select(switchCase => new SwitchCaseSyntax(
                    switchCase.Patterns.Select(pattern => RewriteSwitchPattern(pattern, receiverName, fieldNames, instanceMethodNames, visibleNames)).ToList(),
                    RewriteInstanceBlock(switchCase.Block, receiverName, fieldNames, instanceMethodNames, visibleNames))).ToList(),
                switchSyntax.DefaultBlock.Map(block => RewriteInstanceBlock(block, receiverName, fieldNames, instanceMethodNames, visibleNames))),
            ReturnSyntax returnSyntax => new ReturnSyntax(
                returnSyntax.Expression.Map(expression => RewriteInstanceExpression(expression, receiverName, fieldNames, instanceMethodNames, visibleNames))),
            BreakSyntax or ContinueSyntax => statement,
            _ => statement,
        };
    }

    private static RangeForSyntax RewriteRangeFor(
        RangeForSyntax rangeForSyntax,
        string receiverName,
        IReadOnlySet<string> fieldNames,
        IReadOnlySet<string> instanceMethodNames,
        HashSet<string> visibleNames)
    {
        var bodyNames = new HashSet<string>(visibleNames, StringComparer.Ordinal) { rangeForSyntax.Identifier };
        return new RangeForSyntax(
            rangeForSyntax.Type,
            rangeForSyntax.Identifier,
            RewriteInstanceExpression(rangeForSyntax.Start, receiverName, fieldNames, instanceMethodNames, visibleNames),
            RewriteInstanceExpression(rangeForSyntax.End, receiverName, fieldNames, instanceMethodNames, visibleNames),
            RewriteInstanceBlock(rangeForSyntax.Body, receiverName, fieldNames, instanceMethodNames, bodyNames));
    }

    private static ForSyntax RewriteFor(
        ForSyntax forSyntax,
        string receiverName,
        IReadOnlySet<string> fieldNames,
        IReadOnlySet<string> instanceMethodNames,
        HashSet<string> visibleNames)
    {
        var forNames = new HashSet<string>(visibleNames, StringComparer.Ordinal);
        var initializer = forSyntax.Initializer.Map(initializer =>
        {
            var rewritten = RewriteInstanceStatement(initializer, receiverName, fieldNames, instanceMethodNames, forNames);
            AddDeclaredName(initializer, forNames);
            return rewritten;
        });
        return new ForSyntax(
            initializer,
            forSyntax.Condition.Map(condition => RewriteInstanceExpression(condition, receiverName, fieldNames, instanceMethodNames, forNames)),
            forSyntax.Increment.Map(increment => RewriteInstanceExpression(increment, receiverName, fieldNames, instanceMethodNames, forNames)),
            RewriteInstanceBlock(forSyntax.Body, receiverName, fieldNames, instanceMethodNames, forNames));
    }

    private static ExpressionSyntax RewriteInstanceExpression(
        ExpressionSyntax expression,
        string receiverName,
        IReadOnlySet<string> fieldNames,
        IReadOnlySet<string> instanceMethodNames,
        IReadOnlySet<string> visibleNames)
    {
        return expression switch
        {
            IdentifierSyntax identifier when IsFieldReference(identifier.Identifier, fieldNames, visibleNames) =>
                Member(receiverName, identifier.Identifier),
            FunctionCall call when instanceMethodNames.Contains(call.Name) => new FunctionCall(
                call.Name,
                new ExpressionSyntax[] { new IdentifierSyntax(receiverName) }
                    .Concat(call.Parameters.Select(parameter => RewriteInstanceExpression(parameter, receiverName, fieldNames, instanceMethodNames, visibleNames)))),
            BinaryExpressionSyntax binary => new BinaryExpressionSyntax(
                RewriteInstanceExpression(binary.Left, receiverName, fieldNames, instanceMethodNames, visibleNames),
                RewriteInstanceExpression(binary.Right, receiverName, fieldNames, instanceMethodNames, visibleNames),
                binary.Operator),
            UnaryExpressionSyntax unary => new UnaryExpressionSyntax(
                unary.OperatorSymbol,
                RewriteInstanceExpression(unary.Operand, receiverName, fieldNames, instanceMethodNames, visibleNames)),
            ConditionalExpressionSyntax conditional => new ConditionalExpressionSyntax(
                RewriteInstanceExpression(conditional.Condition, receiverName, fieldNames, instanceMethodNames, visibleNames),
                RewriteInstanceExpression(conditional.WhenTrue, receiverName, fieldNames, instanceMethodNames, visibleNames),
                RewriteInstanceExpression(conditional.WhenFalse, receiverName, fieldNames, instanceMethodNames, visibleNames)),
            CastSyntax cast => new CastSyntax(cast.Type, RewriteInstanceExpression(cast.Expression, receiverName, fieldNames, instanceMethodNames, visibleNames)),
            FunctionCall call => new FunctionCall(
                call.Name,
                call.Parameters.Select(parameter => RewriteInstanceExpression(parameter, receiverName, fieldNames, instanceMethodNames, visibleNames))),
            SdkDotCallSyntax call => new SdkDotCallSyntax(
                call.Module,
                call.Method,
                call.Parameters.Select(parameter => RewriteInstanceExpression(parameter, receiverName, fieldNames, instanceMethodNames, visibleNames))),
            MemberAccessSyntax memberAccess => new MemberAccessSyntax(
                RewriteInstanceExpression(memberAccess.Target, receiverName, fieldNames, instanceMethodNames, visibleNames),
                memberAccess.Member),
            IndexExpressionSyntax index => new IndexExpressionSyntax(
                index.BaseIdentifier,
                RewriteInstanceExpression(index.Index, receiverName, fieldNames, instanceMethodNames, visibleNames)),
            NamedArgumentSyntax named => new NamedArgumentSyntax(
                named.Name,
                RewriteInstanceExpression(named.Expression, receiverName, fieldNames, instanceMethodNames, visibleNames)),
            PipelineExpressionSyntax pipeline => new PipelineExpressionSyntax(
                RewriteInstanceExpression(pipeline.Value, receiverName, fieldNames, instanceMethodNames, visibleNames),
                pipeline.Steps.Select(step => new PipelineStepSyntax(
                    step.FunctionName,
                    step.Arguments.Select(argument => RewriteInstanceExpression(argument, receiverName, fieldNames, instanceMethodNames, visibleNames)))).ToList()),
            SwitchExpressionSyntax switchExpression => new SwitchExpressionSyntax(
                RewriteInstanceExpression(switchExpression.Subject, receiverName, fieldNames, instanceMethodNames, visibleNames),
                switchExpression.Arms.Select(arm => new SwitchExpressionArmSyntax(
                    arm.Patterns.Select(pattern => RewriteSwitchPattern(pattern, receiverName, fieldNames, instanceMethodNames, visibleNames)).ToList(),
                    RewriteInstanceExpression(arm.Value, receiverName, fieldNames, instanceMethodNames, visibleNames))).ToList(),
                switchExpression.DefaultValue.Map(value => RewriteInstanceExpression(value, receiverName, fieldNames, instanceMethodNames, visibleNames))),
            ArrayInitializerSyntax array => new ArrayInitializerSyntax(
                array.Elements.Select(element => RewriteInstanceExpression(element, receiverName, fieldNames, instanceMethodNames, visibleNames))),
            StructInitializerSyntax initializer => new StructInitializerSyntax(
                initializer.Fields.Select(field => new StructFieldInitializerSyntax(
                    field.Name,
                    RewriteInstanceExpression(field.Expression, receiverName, fieldNames, instanceMethodNames, visibleNames)))),
            AssignmentSyntax assignment => new AssignmentSyntax(
                RewriteInstanceLValue(assignment.Left, receiverName, fieldNames, instanceMethodNames, visibleNames),
                assignment.OperatorSymbol,
                RewriteInstanceExpression(assignment.Right, receiverName, fieldNames, instanceMethodNames, visibleNames)),
            PostfixMutationSyntax postfix => new PostfixMutationSyntax(
                RewriteInstanceLValue(postfix.Target, receiverName, fieldNames, instanceMethodNames, visibleNames),
                postfix.OperatorSymbol),
            _ => expression,
        };
    }

    private static LValue RewriteInstanceLValue(
        LValue lValue,
        string receiverName,
        IReadOnlySet<string> fieldNames,
        IReadOnlySet<string> instanceMethodNames,
        IReadOnlySet<string> visibleNames)
    {
        return lValue switch
        {
            IdentifierLValue identifier when IsFieldReference(identifier.Identifier, fieldNames, visibleNames) =>
                new MemberAccessLValue(Member(receiverName, identifier.Identifier)),
            MemberAccessLValue memberAccess => new MemberAccessLValue(
                RewriteMemberAccess(memberAccess.MemberAccess, receiverName, fieldNames, instanceMethodNames, visibleNames)),
            IndexLValue index => new IndexLValue(
                index.BaseIdentifier,
                RewriteInstanceExpression(index.Index, receiverName, fieldNames, instanceMethodNames, visibleNames)),
            PointerDerefLValue pointer => new PointerDerefLValue(
                RewriteInstanceExpression(pointer.Expression, receiverName, fieldNames, instanceMethodNames, visibleNames)),
            _ => lValue,
        };
    }

    private static MemberAccessSyntax RewriteMemberAccess(
        MemberAccessSyntax memberAccess,
        string receiverName,
        IReadOnlySet<string> fieldNames,
        IReadOnlySet<string> instanceMethodNames,
        IReadOnlySet<string> visibleNames)
    {
        return new MemberAccessSyntax(
            RewriteInstanceExpression(memberAccess.Target, receiverName, fieldNames, instanceMethodNames, visibleNames),
            memberAccess.Member);
    }

    private static BlockSyntax RewriteStaticBlock(
        BlockSyntax block,
        IReadOnlyDictionary<string, string> staticMethods,
        IEnumerable<string> visibleNames)
    {
        var scopedNames = new HashSet<string>(visibleNames, StringComparer.Ordinal);
        var statements = new List<StatementSyntax>();
        foreach (var statement in block.Statements)
        {
            statements.Add(RewriteStaticStatement(statement, staticMethods, scopedNames));
            AddDeclaredName(statement, scopedNames);
        }

        return new BlockSyntax(statements);
    }

    private static StatementSyntax RewriteStaticStatement(
        StatementSyntax statement,
        IReadOnlyDictionary<string, string> staticMethods,
        IReadOnlySet<string> visibleNames)
    {
        return statement switch
        {
            ConstDeclarationSyntax constant => new ConstDeclarationSyntax(
                constant.TypeAnnotation,
                constant.Name,
                RewriteStaticExpression(constant.Value, staticMethods, visibleNames)),
            DeclarationSyntax declaration => new DeclarationSyntax(
                declaration.Type,
                declaration.Name,
                declaration.ArrayLength.Map(expression => RewriteStaticExpression(expression, staticMethods, visibleNames)),
                declaration.Initialization.Map(expression => RewriteStaticExpression(expression, staticMethods, visibleNames)),
                declaration.IsImmutable),
            ExpressionStatementSyntax expressionStatement => new ExpressionStatementSyntax(
                RewriteStaticExpression(expressionStatement.Expression, staticMethods, visibleNames)),
            IfElseSyntax ifElse => new IfElseSyntax(
                RewriteStaticExpression(ifElse.Condition, staticMethods, visibleNames),
                RewriteStaticBlock(ifElse.ThenBlock, staticMethods, visibleNames),
                ifElse.ElseBlock.Map(block => RewriteStaticBlock(block, staticMethods, visibleNames))),
            WhileSyntax whileSyntax => new WhileSyntax(
                RewriteStaticExpression(whileSyntax.Condition, staticMethods, visibleNames),
                RewriteStaticBlock(whileSyntax.Body, staticMethods, visibleNames)),
            DoWhileSyntax doWhileSyntax => new DoWhileSyntax(
                RewriteStaticBlock(doWhileSyntax.Body, staticMethods, visibleNames),
                RewriteStaticExpression(doWhileSyntax.Condition, staticMethods, visibleNames)),
            LoopSyntax loopSyntax => new LoopSyntax(RewriteStaticBlock(loopSyntax.Body, staticMethods, visibleNames)),
            RangeForSyntax rangeForSyntax => RewriteStaticRangeFor(rangeForSyntax, staticMethods, visibleNames),
            ForSyntax forSyntax => RewriteStaticFor(forSyntax, staticMethods, visibleNames),
            SwitchSyntax switchSyntax => new SwitchSyntax(
                RewriteStaticExpression(switchSyntax.Subject, staticMethods, visibleNames),
                switchSyntax.Cases.Select(switchCase => new SwitchCaseSyntax(
                    switchCase.Patterns.Select(pattern => RewriteStaticSwitchPattern(pattern, staticMethods, visibleNames)).ToList(),
                    RewriteStaticBlock(switchCase.Block, staticMethods, visibleNames))).ToList(),
                switchSyntax.DefaultBlock.Map(block => RewriteStaticBlock(block, staticMethods, visibleNames))),
            ReturnSyntax returnSyntax => new ReturnSyntax(returnSyntax.Expression.Map(expression => RewriteStaticExpression(expression, staticMethods, visibleNames))),
            BreakSyntax or ContinueSyntax => statement,
            _ => statement,
        };
    }

    private static RangeForSyntax RewriteStaticRangeFor(
        RangeForSyntax rangeForSyntax,
        IReadOnlyDictionary<string, string> staticMethods,
        IReadOnlySet<string> visibleNames)
    {
        var bodyNames = new HashSet<string>(visibleNames, StringComparer.Ordinal) { rangeForSyntax.Identifier };
        return new RangeForSyntax(
            rangeForSyntax.Type,
            rangeForSyntax.Identifier,
            RewriteStaticExpression(rangeForSyntax.Start, staticMethods, visibleNames),
            RewriteStaticExpression(rangeForSyntax.End, staticMethods, visibleNames),
            RewriteStaticBlock(rangeForSyntax.Body, staticMethods, bodyNames));
    }

    private static ForSyntax RewriteStaticFor(
        ForSyntax forSyntax,
        IReadOnlyDictionary<string, string> staticMethods,
        IReadOnlySet<string> visibleNames)
    {
        var forNames = new HashSet<string>(visibleNames, StringComparer.Ordinal);
        var initializer = forSyntax.Initializer.Map(initializer =>
        {
            var rewritten = RewriteStaticStatement(initializer, staticMethods, forNames);
            AddDeclaredName(initializer, forNames);
            return rewritten;
        });
        return new ForSyntax(
            initializer,
            forSyntax.Condition.Map(condition => RewriteStaticExpression(condition, staticMethods, forNames)),
            forSyntax.Increment.Map(increment => RewriteStaticExpression(increment, staticMethods, forNames)),
            RewriteStaticBlock(forSyntax.Body, staticMethods, forNames));
    }

    private static ExpressionSyntax RewriteStaticExpression(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, string> staticMethods,
        IReadOnlySet<string> visibleNames)
    {
        return expression switch
        {
            SdkDotCallSyntax call when !visibleNames.Contains(call.Module)
                                      && staticMethods.TryGetValue($"{call.Module}.{call.Method}", out var functionName) =>
                new FunctionCall(functionName, call.Parameters.Select(parameter => RewriteStaticExpression(parameter, staticMethods, visibleNames))),
            BinaryExpressionSyntax binary => new BinaryExpressionSyntax(
                RewriteStaticExpression(binary.Left, staticMethods, visibleNames),
                RewriteStaticExpression(binary.Right, staticMethods, visibleNames),
                binary.Operator),
            UnaryExpressionSyntax unary => new UnaryExpressionSyntax(unary.OperatorSymbol, RewriteStaticExpression(unary.Operand, staticMethods, visibleNames)),
            ConditionalExpressionSyntax conditional => new ConditionalExpressionSyntax(
                RewriteStaticExpression(conditional.Condition, staticMethods, visibleNames),
                RewriteStaticExpression(conditional.WhenTrue, staticMethods, visibleNames),
                RewriteStaticExpression(conditional.WhenFalse, staticMethods, visibleNames)),
            CastSyntax cast => new CastSyntax(cast.Type, RewriteStaticExpression(cast.Expression, staticMethods, visibleNames)),
            FunctionCall call => new FunctionCall(call.Name, call.Parameters.Select(parameter => RewriteStaticExpression(parameter, staticMethods, visibleNames))),
            SdkDotCallSyntax call => new SdkDotCallSyntax(
                call.Module,
                call.Method,
                call.Parameters.Select(parameter => RewriteStaticExpression(parameter, staticMethods, visibleNames))),
            MemberAccessSyntax memberAccess => new MemberAccessSyntax(RewriteStaticExpression(memberAccess.Target, staticMethods, visibleNames), memberAccess.Member),
            IndexExpressionSyntax index => new IndexExpressionSyntax(index.BaseIdentifier, RewriteStaticExpression(index.Index, staticMethods, visibleNames)),
            NamedArgumentSyntax named => new NamedArgumentSyntax(named.Name, RewriteStaticExpression(named.Expression, staticMethods, visibleNames)),
            PipelineExpressionSyntax pipeline => new PipelineExpressionSyntax(
                RewriteStaticExpression(pipeline.Value, staticMethods, visibleNames),
                pipeline.Steps.Select(step => new PipelineStepSyntax(
                    step.FunctionName,
                    step.Arguments.Select(argument => RewriteStaticExpression(argument, staticMethods, visibleNames)))).ToList()),
            SwitchExpressionSyntax switchExpression => new SwitchExpressionSyntax(
                RewriteStaticExpression(switchExpression.Subject, staticMethods, visibleNames),
                switchExpression.Arms.Select(arm => new SwitchExpressionArmSyntax(
                    arm.Patterns.Select(pattern => RewriteStaticSwitchPattern(pattern, staticMethods, visibleNames)).ToList(),
                    RewriteStaticExpression(arm.Value, staticMethods, visibleNames))).ToList(),
                switchExpression.DefaultValue.Map(value => RewriteStaticExpression(value, staticMethods, visibleNames))),
            ArrayInitializerSyntax array => new ArrayInitializerSyntax(array.Elements.Select(element => RewriteStaticExpression(element, staticMethods, visibleNames))),
            StructInitializerSyntax initializer => new StructInitializerSyntax(
                initializer.Fields.Select(field => new StructFieldInitializerSyntax(field.Name, RewriteStaticExpression(field.Expression, staticMethods, visibleNames)))),
            AssignmentSyntax assignment => new AssignmentSyntax(
                RewriteStaticLValue(assignment.Left, staticMethods, visibleNames),
                assignment.OperatorSymbol,
                RewriteStaticExpression(assignment.Right, staticMethods, visibleNames)),
            PostfixMutationSyntax postfix => new PostfixMutationSyntax(RewriteStaticLValue(postfix.Target, staticMethods, visibleNames), postfix.OperatorSymbol),
            _ => expression,
        };
    }

    private static LValue RewriteStaticLValue(
        LValue lValue,
        IReadOnlyDictionary<string, string> staticMethods,
        IReadOnlySet<string> visibleNames)
    {
        return lValue switch
        {
            MemberAccessLValue memberAccess => new MemberAccessLValue(RewriteStaticMemberAccess(memberAccess.MemberAccess, staticMethods, visibleNames)),
            IndexLValue index => new IndexLValue(index.BaseIdentifier, RewriteStaticExpression(index.Index, staticMethods, visibleNames)),
            PointerDerefLValue pointer => new PointerDerefLValue(RewriteStaticExpression(pointer.Expression, staticMethods, visibleNames)),
            _ => lValue,
        };
    }

    private static MemberAccessSyntax RewriteStaticMemberAccess(
        MemberAccessSyntax memberAccess,
        IReadOnlyDictionary<string, string> staticMethods,
        IReadOnlySet<string> visibleNames)
    {
        return new MemberAccessSyntax(RewriteStaticExpression(memberAccess.Target, staticMethods, visibleNames), memberAccess.Member);
    }

    private static SwitchCasePatternSyntax RewriteSwitchPattern(
        SwitchCasePatternSyntax pattern,
        string receiverName,
        IReadOnlySet<string> fieldNames,
        IReadOnlySet<string> instanceMethodNames,
        IReadOnlySet<string> visibleNames)
    {
        return pattern.End.Match(
            end => new SwitchCasePatternSyntax(
                RewriteInstanceExpression(pattern.Start, receiverName, fieldNames, instanceMethodNames, visibleNames),
                RewriteInstanceExpression(end, receiverName, fieldNames, instanceMethodNames, visibleNames)),
            () => new SwitchCasePatternSyntax(RewriteInstanceExpression(pattern.Start, receiverName, fieldNames, instanceMethodNames, visibleNames)));
    }

    private static SwitchCasePatternSyntax RewriteStaticSwitchPattern(
        SwitchCasePatternSyntax pattern,
        IReadOnlyDictionary<string, string> staticMethods,
        IReadOnlySet<string> visibleNames)
    {
        return pattern.End.Match(
            end => new SwitchCasePatternSyntax(
                RewriteStaticExpression(pattern.Start, staticMethods, visibleNames),
                RewriteStaticExpression(end, staticMethods, visibleNames)),
            () => new SwitchCasePatternSyntax(RewriteStaticExpression(pattern.Start, staticMethods, visibleNames)));
    }

    private static bool IsFieldReference(string name, IReadOnlySet<string> fieldNames, IReadOnlySet<string> visibleNames)
    {
        return fieldNames.Contains(name) && !visibleNames.Contains(name);
    }

    private static void AddDeclaredName(StatementSyntax statement, HashSet<string> visibleNames)
    {
        switch (statement)
        {
            case DeclarationSyntax declaration:
                visibleNames.Add(declaration.Name);
                break;
            case ConstDeclarationSyntax constant:
                visibleNames.Add(constant.Name);
                break;
        }
    }

    private static MemberAccessSyntax Member(string receiverName, string fieldName)
    {
        return new MemberAccessSyntax(new IdentifierSyntax(receiverName), fieldName);
    }

    private static string ReceiverName(string className)
    {
        return className.Length switch
        {
            0 => "self",
            1 => char.ToLowerInvariant(className[0]).ToString(),
            _ => char.ToLowerInvariant(className[0]) + className[1..],
        };
    }
}
