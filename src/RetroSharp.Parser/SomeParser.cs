using System.Xml.Linq;
using RetroSharp.Core;
using Zafiro.Core.Mixins;
using static RetroSharp.Parser.RetroSharpParser;
namespace RetroSharp.Parser;

public class SomeParser
{
    public Result<ProgramSyntax> Parse(string input) => Tokenize(input)
        .Bind(Parse)
        .Map(ParseProgram);

    private static Result<ProgramContext> Parse(CommonTokenStream tokenStream)
    {
        var parser = new RetroSharpParser(tokenStream);
        var listenerLexer = new ErrorListener<IToken>();
        parser.AddErrorListener(listenerLexer);
        var result = parser.program();

        if (listenerLexer.Errors.Any())
        {
            return Result.Failure<ProgramContext>(listenerLexer.Errors.Select(x => x.ToString()).JoinWithLines());
        }

        return result;
    }

    private static Result<CommonTokenStream> Tokenize(string input)
    {
        var lexer = new RetroSharpLexer(CharStreams.fromString(input));
        var listenerLexer = new ErrorListener<int>();
        lexer.AddErrorListener(listenerLexer);

        var tokenStream = new CommonTokenStream(lexer);

        if (listenerLexer.Errors.Any())
        {
            return Result.Failure<CommonTokenStream>(listenerLexer.Errors.Select(x => x.ToString()).JoinWithLines());
        }

        return tokenStream;
    }

    private ProgramSyntax ParseProgram(ProgramContext program)
    {
        var aliases = program.typeAliasDeclaration().Select(ParseTypeAlias);
        var constants = program.constDeclaration().Select(ParseConstDeclaration);
        var enums = program.enumDeclaration().Select(ParseEnum);
        var structs = program.structDeclaration().Select(ParseStruct);
        var funcs = program.function().Select(f => ParseFunction(f));
        return new ProgramSyntax(aliases.ToList(), constants.ToList(), enums.ToList(), structs.ToList(), funcs.ToList());
    }

    private TypeAliasSyntax ParseTypeAlias(TypeAliasDeclarationContext aliasContext)
    {
        return new TypeAliasSyntax(aliasContext.IDENTIFIER().GetText(), aliasContext.type().GetText());
    }

    private ConstDeclarationSyntax ParseConstDeclaration(ConstDeclarationContext constContext)
    {
        Maybe<string> type = constContext.type() is { } typeContext
            ? Maybe.From(typeContext.GetText())
            : Maybe<string>.None;
        var name = constContext.IDENTIFIER().GetText();
        return new ConstDeclarationSyntax(
            type,
            name,
            ParseExpression(constContext.expression()));
    }

    private EnumSyntax ParseEnum(EnumDeclarationContext enumContext)
    {
        var members = enumContext.enumMember().Select(ParseEnumMember).ToList();
        return new EnumSyntax(enumContext.IDENTIFIER().GetText(), members);
    }

    private EnumMemberSyntax ParseEnumMember(EnumMemberContext enumMemberContext)
    {
        var maybeValue = enumMemberContext.expression() is { } expr
            ? Maybe.From(ParseExpression(expr))
            : Maybe<ExpressionSyntax>.None;
        return new EnumMemberSyntax(enumMemberContext.IDENTIFIER().GetText(), maybeValue);
    }

    private StructSyntax ParseStruct(StructDeclarationContext structContext)
    {
        var fields = structContext.structField().Select(ParseStructField).ToList();
        return new StructSyntax(structContext.IDENTIFIER().GetText(), fields);
    }

    private StructFieldSyntax ParseStructField(StructFieldContext fieldContext)
    {
        return new StructFieldSyntax(fieldContext.type().GetText(), fieldContext.IDENTIFIER().GetText());
    }

    private FunctionSyntax ParseFunction(FunctionContext functionContext)
    {
        var parameters = ParseParameters(functionContext.parameters()).ToList();
        if (functionContext.block() is { } block)
        {
            return new FunctionSyntax(functionContext.type().GetText(), functionContext.IDENTIFIER().ToString()!, parameters, ParseBlock(block));
        }

        var expressionBody = new BlockSyntax([new ReturnSyntax(Maybe.From(ParseExpression(functionContext.expression())))]);
        return new FunctionSyntax(functionContext.type().GetText(), functionContext.IDENTIFIER().ToString()!, parameters, expressionBody, true);
    }

    private IEnumerable<ParameterSyntax> ParseParameters(ParametersContext parameters)
    {
        if (parameters is { } parameterContexts)
        {
            return parameterContexts.parameter().Select(ParseParameter);
        }

        return Enumerable.Empty<ParameterSyntax>();
    }

    private ParameterSyntax ParseParameter(ParameterContext parameterContext)
    {
        var defaultValue = parameterContext.expression() is { } defaultExpression
            ? Maybe.From(ParseExpression(defaultExpression))
            : Maybe<ExpressionSyntax>.None;
        return new ParameterSyntax(parameterContext.type().GetText(), parameterContext.IDENTIFIER().GetText(), defaultValue);
    }

    private BlockSyntax ParseBlock(BlockContext block)
    {
        var statements = block.statement().Select(ParseStatement);
        return new BlockSyntax(statements.ToList());
    }

    private StatementSyntax ParseStatement(StatementContext statementContext)
    {
        if (statementContext.expression() is { } expression)
        {
            return new ExpressionStatementSyntax(ParseExpression(expression));
        }

        if (statementContext.constDeclaration() is { } constDeclaration)
        {
            return ParseConstDeclaration(constDeclaration);
        }

        if (statementContext.conditional() is { } conditional)
        {
            return ParseConditional(conditional);
        }

        if (statementContext.whileLoop() is { } whileLoop)
        {
            return ParseWhileLoop(whileLoop);
        }

        if (statementContext.doWhileLoop() is { } doWhileLoop)
        {
            return ParseDoWhileLoop(doWhileLoop);
        }

        if (statementContext.loopStatement() is { } loopStatement)
        {
            return ParseLoop(loopStatement);
        }

        if (statementContext.rangeForLoop() is { } rangeForLoop)
        {
            return ParseRangeForLoop(rangeForLoop);
        }

        if (statementContext.forLoop() is { } forLoop)
        {
            return ParseForLoop(forLoop);
        }

        if (statementContext.switchStatement() is { } switchStatement)
        {
            return ParseSwitch(switchStatement);
        }

        if (statementContext.breakStatement() is not null)
        {
            return new BreakSyntax();
        }

        if (statementContext.continueStatement() is not null)
        {
            return new ContinueSyntax();
        }

        if (statementContext.postfixMutation() is { } postfixMutation)
        {
            return new ExpressionStatementSyntax(ParsePostfixMutation(postfixMutation));
        }

        if (statementContext.variableDeclaration() is { } declaration)
        {
            return ParseDeclaration(declaration);
        }

        if (statementContext.returnStatement() is { } returnStatement)
        {
            return ParseReturn(returnStatement);
        }

        throw new InvalidOperationException();
    }

    private StatementSyntax ParseReturn(ReturnStatementContext returnStatement)
    {
        var maybeExpression = returnStatement.expression() is { } expr
            ? Maybe.From(ParseExpression(expr))
            : Maybe<ExpressionSyntax>.None;
        return new ReturnSyntax(maybeExpression);
    }

    private StatementSyntax ParseDeclaration(VariableDeclarationContext declaration)
    {
        return ParseDeclaration(declaration.variableDeclarator());
    }

    private DeclarationSyntax ParseDeclaration(VariableDeclaratorContext declaration)
    {
        var maybeInitialization = declaration.variableInitializer() is { } initializer
            ? Maybe.From(ParseVariableInitializer(initializer))
            : Maybe<ExpressionSyntax>.None;
        var maybeArrayLength = declaration.arraySize() is { } arraySize
            ? Maybe.From(ParseArrayLength(arraySize, maybeInitialization))
            : Maybe<ExpressionSyntax>.None;
        return new DeclarationSyntax(declaration.type().GetText(), declaration.IDENTIFIER().GetText(), maybeArrayLength, maybeInitialization);
    }

    private ExpressionSyntax ParseArrayLength(ArraySizeContext arraySize, Maybe<ExpressionSyntax> initialization)
    {
        if (arraySize.expression() is { } explicitLength)
        {
            return ParseExpression(explicitLength);
        }

        if (initialization.HasValue && initialization.Value is ArrayInitializerSyntax arrayInitializer)
        {
            return new ConstantSyntax(arrayInitializer.Elements.Count.ToString());
        }

        return new ConstantSyntax("0");
    }

    private ExpressionSyntax ParseVariableInitializer(VariableInitializerContext initializer)
    {
        if (initializer.arrayInitializer() is { } arrayInitializer)
        {
            return new ArrayInitializerSyntax(arrayInitializer.expression().Select(ParseExpression));
        }

        if (initializer.structInitializer() is { } structInitializer)
        {
            return new StructInitializerSyntax(structInitializer.fieldInitializer().Select(ParseFieldInitializer));
        }

        return ParseExpression(initializer.expression());
    }

    private StructFieldInitializerSyntax ParseFieldInitializer(FieldInitializerContext fieldInitializer)
    {
        var name = fieldInitializer.IDENTIFIER().GetText();
        var expression = fieldInitializer.expression() is { } explicitExpression
            ? ParseExpression(explicitExpression)
            : new IdentifierSyntax(name);
        return new StructFieldInitializerSyntax(name, expression);
    }

    private ExpressionSyntax ParseFunctionCall(FunctionCallContext functionCall)
    {
        var name = functionCall.IDENTIFIER().ToString()!;
        var arguments = functionCall.arguments()?.argument()?.Select(ParseArgument) ?? Enumerable.Empty<ExpressionSyntax>();

        return new FunctionCall(name, arguments);
    }

    private ExpressionSyntax ParseArgument(ArgumentContext argument)
    {
        var expression = ParseExpression(argument.expression());
        return argument.IDENTIFIER() is { } identifier
            ? new NamedArgumentSyntax(identifier.GetText(), expression)
            : expression;
    }

    private StatementSyntax ParseWhileLoop(WhileLoopContext whileLoop)
    {
        return new WhileSyntax(ParseExpression(whileLoop.expression()), ParseBlock(whileLoop.block()));
    }

    private StatementSyntax ParseDoWhileLoop(DoWhileLoopContext doWhileLoop)
    {
        return new DoWhileSyntax(ParseBlock(doWhileLoop.block()), ParseExpression(doWhileLoop.expression()));
    }

    private StatementSyntax ParseLoop(LoopStatementContext loopStatement)
    {
        return new LoopSyntax(ParseBlock(loopStatement.block()));
    }

    private StatementSyntax ParseRangeForLoop(RangeForLoopContext rangeForLoop)
    {
        return new RangeForSyntax(
            rangeForLoop.type().GetText(),
            rangeForLoop.IDENTIFIER().GetText(),
            ParseExpression(rangeForLoop.expression()[0]),
            ParseExpression(rangeForLoop.expression()[1]),
            ParseBlock(rangeForLoop.block()));
    }

    private StatementSyntax ParseForLoop(ForLoopContext forLoop)
    {
        var initializer = forLoop.forInitializer() is { } init
            ? Maybe.From(ParseForInitializer(init))
            : Maybe<StatementSyntax>.None;
        var condition = forLoop.expression() is { } conditionContext
            ? Maybe.From(ParseExpression(conditionContext))
            : Maybe<ExpressionSyntax>.None;
        var increment = forLoop.forIncrement() is { } incrementContext
            ? Maybe.From(ParseForIncrement(incrementContext))
            : Maybe<ExpressionSyntax>.None;

        return new ForSyntax(initializer, condition, increment, ParseBlock(forLoop.block()));
    }

    private ExpressionSyntax ParseForIncrement(ForIncrementContext increment)
    {
        if (increment.assignment() is { } assignment)
        {
            return ParseAssignment(assignment);
        }

        if (increment.postfixMutation() is { } postfixMutation)
        {
            return ParsePostfixMutation(postfixMutation);
        }

        throw new InvalidOperationException("Unsupported for increment.");
    }

    private ExpressionSyntax ParsePostfixMutation(PostfixMutationContext postfixMutation)
    {
        return new PostfixMutationSyntax(ParseLValue(postfixMutation.lvalue()), postfixMutation.postfixOperator().GetText());
    }

    private StatementSyntax ParseSwitch(SwitchStatementContext switchStatement)
    {
        var subject = ParseExpression(switchStatement.expression());
        var cases = switchStatement.switchCase()
            .Select(ParseSwitchCase)
            .ToList();
        var defaultBlock = switchStatement.switchDefault() is { } switchDefault
            ? Maybe.From(ParseBlock(switchDefault.block()))
            : Maybe<BlockSyntax>.None;

        return new SwitchSyntax(subject, cases, defaultBlock);
    }

    private SwitchCaseSyntax ParseSwitchCase(SwitchCaseContext switchCase)
    {
        return new SwitchCaseSyntax(switchCase.switchCasePattern().Select(ParseSwitchCasePattern).ToList(), ParseBlock(switchCase.block()));
    }

    private SwitchCasePatternSyntax ParseSwitchCasePattern(SwitchCasePatternContext switchCasePattern)
    {
        var expressions = switchCasePattern.expression();
        return expressions.Length switch
        {
            1 => new SwitchCasePatternSyntax(ParseExpression(expressions[0])),
            2 => new SwitchCasePatternSyntax(ParseExpression(expressions[0]), ParseExpression(expressions[1])),
            _ => throw new InvalidOperationException("Unsupported switch case pattern."),
        };
    }

    private StatementSyntax ParseForInitializer(ForInitializerContext initializer)
    {
        if (initializer.variableDeclarator() is { } declaration)
        {
            return ParseDeclaration(declaration);
        }

        if (initializer.assignment() is { } assignment)
        {
            return new ExpressionStatementSyntax(ParseAssignment(assignment));
        }

        throw new InvalidOperationException("Unsupported for initializer.");
    }

    private StatementSyntax ParseConditional(ConditionalContext conditional)
    {
        var condition = ParseExpression(conditional.expression());
        var thenBlock = ParseBlock(conditional.block()[0]);
        var elseBlock = ParseElseBlock(conditional);
        return new IfElseSyntax(condition, thenBlock, elseBlock);
    }

    private Maybe<BlockSyntax> ParseElseBlock(ConditionalContext conditional)
    {
        if (conditional.conditional() is { } elseIf)
        {
            return Maybe.From(new BlockSyntax([ParseConditional(elseIf)]));
        }

        return conditional.block().Length > 1
            ? Maybe.From(ParseBlock(conditional.block()[1]))
            : Maybe<BlockSyntax>.None;
    }

    private ExpressionSyntax ParseAssignment(AssignmentContext assignment)
    {
        var lvalue = ParseLValue(assignment.lvalue());
        return new AssignmentSyntax(lvalue, assignment.assignmentOperator().GetText(), ParseExpression(assignment.expression()));
    }

    private ExpressionSyntax ParseExpression(ExpressionContext expression)
    {
        if (expression.assignment() is { } assignmentContext)
        {
            return ParseAssignment(assignmentContext);
        }

        if (expression.conditionalExpression() is { } conditionalExpression)
        {
            return ParseConditionalExpression(conditionalExpression);
        }

        throw new NotImplementedException(expression.ToString());
    }

    private ExpressionSyntax ParseConditionalExpression(ConditionalExpressionContext conditionalExpression)
    {
        var condition = ParseConditionalOr(conditionalExpression.conditionalOrExpression());
        var branches = conditionalExpression.expression();
        return branches.Length switch
        {
            0 => condition,
            2 => new ConditionalExpressionSyntax(condition, ParseExpression(branches[0]), ParseExpression(branches[1])),
            _ => throw new InvalidOperationException("Unsupported conditional expression."),
        };
    }

    private ExpressionSyntax ParseConditionalOr(ConditionalOrExpressionContext conditionalOr)
    {
        if (conditionalOr.conditionalOrExpression() is { } or)
        {
            var left = ParseConditionalOr(or);
            var right = ParseConditionalAnd(conditionalOr.conditionalAndExpression());
            return new BinaryExpressionSyntax(left, right, Operator.Get("||"));
        }

        return ParseConditionalAnd(conditionalOr.conditionalAndExpression());
    }

    private ExpressionSyntax ParseConditionalAnd(ConditionalAndExpressionContext conditionalAndExpression)
    {
        if (conditionalAndExpression.conditionalAndExpression() is { } conditionalAnd)
        {
            var left = ParseConditionalAnd(conditionalAnd);
            var right = ParseBitwiseOr(conditionalAndExpression.bitwiseOrExpression());
            return new BinaryExpressionSyntax(left, right, Operator.Get("&&"));
        }

        return ParseBitwiseOr(conditionalAndExpression.bitwiseOrExpression());
    }

    private ExpressionSyntax ParseBitwiseOr(BitwiseOrExpressionContext bitwiseOrExpression)
    {
        if (bitwiseOrExpression.bitwiseOrExpression() is { } bitwiseOr)
        {
            var left = ParseBitwiseOr(bitwiseOr);
            var right = ParseBitwiseXor(bitwiseOrExpression.bitwiseXorExpression());
            return new BinaryExpressionSyntax(left, right, Operator.Get("|"));
        }

        return ParseBitwiseXor(bitwiseOrExpression.bitwiseXorExpression());
    }

    private ExpressionSyntax ParseBitwiseXor(BitwiseXorExpressionContext bitwiseXorExpression)
    {
        if (bitwiseXorExpression.bitwiseXorExpression() is { } bitwiseXor)
        {
            var left = ParseBitwiseXor(bitwiseXor);
            var right = ParseBitwiseAnd(bitwiseXorExpression.bitwiseAndExpression());
            return new BinaryExpressionSyntax(left, right, Operator.Get("^"));
        }

        return ParseBitwiseAnd(bitwiseXorExpression.bitwiseAndExpression());
    }

    private ExpressionSyntax ParseBitwiseAnd(BitwiseAndExpressionContext bitwiseAndExpression)
    {
        if (bitwiseAndExpression.bitwiseAndExpression() is { } bitwiseAnd)
        {
            var left = ParseBitwiseAnd(bitwiseAnd);
            var right = ParseConditionalEquality(bitwiseAndExpression.equalityExpression());
            return new BinaryExpressionSyntax(left, right, Operator.Get("&"));
        }

        return ParseConditionalEquality(bitwiseAndExpression.equalityExpression());
    }

    private ExpressionSyntax ParseConditionalEquality(EqualityExpressionContext equalityExpression)
    {
        if (equalityExpression.equalityExpression() is { } equality)
        {
            var left = ParseConditionalEquality(equality);
            var right = ParseRangeMembership(equalityExpression.rangeMembershipExpression());
            return new BinaryExpressionSyntax(left, right, Operator.Get(equalityExpression.children[1].GetText()));
        }

        return ParseRangeMembership(equalityExpression.rangeMembershipExpression());
    }

    private ExpressionSyntax ParseRangeMembership(RangeMembershipExpressionContext rangeMembership)
    {
        var shiftExpressions = rangeMembership.shiftExpression();
        if (shiftExpressions.Length == 3)
        {
            var subject = ParseShiftExpression(shiftExpressions[0]);
            var start = ParseShiftExpression(shiftExpressions[1]);
            var end = ParseShiftExpression(shiftExpressions[2]);
            var lowerBound = new BinaryExpressionSyntax(subject, start, Operator.Get(">="));
            var upperBound = new BinaryExpressionSyntax(subject, end, Operator.Get("<"));
            return new BinaryExpressionSyntax(lowerBound, upperBound, Operator.Get("&&"));
        }

        return ParseConditionalRelational(rangeMembership.relationalExpression());
    }

    private ExpressionSyntax ParseConditionalRelational(RelationalExpressionContext relationalExpression)
    {
        if (relationalExpression.relationalExpression() is { } relational)
        {
            var left = ParseConditionalRelational(relational);
            var right = ParseShiftExpression(relationalExpression.shiftExpression());
            var @operator = relationalExpression.children[1].GetText();
            return new BinaryExpressionSyntax(left, right, Operator.Get(@operator));
        }

        return ParseShiftExpression(relationalExpression.shiftExpression());
    }

    private LValue ParseLValue(LvalueContext ctx)
    {
        if (ctx.memberAccess() is { } memberAccess)
        {
            return new MemberAccessLValue(ParseMemberAccess(memberAccess));
        }

        if (ctx.IDENTIFIER() != null && ctx.children.Count == 1)
        {
            return new IdentifierLValue(ctx.IDENTIFIER().GetText());
        }
        if (ctx.children[0].GetText() == "*")
        {
            // For now, treat *expr lvalue as unsupported in semantic, but keep syntax node
            var inner = ParseExpression((ExpressionContext)ctx.children[1]);
            return new PointerDerefLValue(inner);
        }
        if (ctx.IDENTIFIER() != null && ctx.children.Count >= 4 && ctx.children[1].GetText() == "[")
        {
            var baseIdent = ctx.IDENTIFIER().GetText();
            var indexExpr = ParseExpression((ExpressionContext)ctx.children[2]);
            return new IndexLValue(baseIdent, indexExpr);
        }
        throw new NotImplementedException("Unsupported lvalue form");
    }

    private ExpressionSyntax ParseAddExpression(AddExpressionContext addExpression)
    {
        if (addExpression.addExpression() is { } addExpr)
        {
            var left = ParseAddExpression(addExpr);
            var right = ParseMultExpression(addExpression.mulExpression());
            var opText = addExpression.children[1].GetText();
            var op = Operator.Get(opText);
            return new BinaryExpressionSyntax(left, right, op);
        }

        return ParseMultExpression(addExpression.mulExpression());
    }

    private ExpressionSyntax ParseShiftExpression(ShiftExpressionContext shiftExpression)
    {
        if (shiftExpression.shiftExpression() is { } shiftExpr)
        {
            var left = ParseShiftExpression(shiftExpr);
            var right = ParseAddExpression(shiftExpression.addExpression());
            var opText = shiftExpression.children[1].GetText();
            var op = Operator.Get(opText);
            return new BinaryExpressionSyntax(left, right, op);
        }

        return ParseAddExpression(shiftExpression.addExpression());
    }

    private ExpressionSyntax ParseMultExpression(MulExpressionContext mulExpression)
    {
        if (mulExpression.mulExpression() is { } multExpr)
        {
            var left = ParseMultExpression(multExpr);
            var right = ParseUnary(mulExpression.unaryExpression());
            // children[1] should be '*' or '/'
            var opText = mulExpression.children[1].GetText();
            var op = Operator.Get(opText);
            return new BinaryExpressionSyntax(left, right, op);
        }

        if (mulExpression.unaryExpression() is { } unary)
        {
            return ParseUnary(unary);
        }

        var l = ParseExpression((ExpressionContext)mulExpression.children[0]);
        var r = ParseExpression((ExpressionContext)mulExpression.children[1]);
        var opTail = mulExpression.children.Count > 2 ? mulExpression.children[2].GetText() : "*";
        var op2 = Operator.Get(opTail);
        return new BinaryExpressionSyntax(l, r, op2);
    }

    private ExpressionSyntax ParseUnary(UnaryExpressionContext unaryExpression)
    {
        if (unaryExpression.castExpression() is { } castExpression)
        {
            return ParseCast(castExpression);
        }

        if (unaryExpression.unaryExpression() is { } unaryExpr)
        {
            return new UnaryExpressionSyntax(unaryExpression.children[0].GetText(), ParseUnary(unaryExpr));
        }

        return ParsePrimary(unaryExpression.primary());
    }

    private ExpressionSyntax ParseCast(CastExpressionContext castExpression)
    {
        return new CastSyntax(castExpression.type().GetText(), ParseUnary(castExpression.unaryExpression()));
    }

    private ExpressionSyntax ParsePrimary(PrimaryContext primary)
    {
        if (primary.LITERAL() is { } node)
        {
            return new ConstantSyntax(node.GetText());
        }

        if (primary.sizeofExpression() is { } sizeofExpression)
        {
            return new SizeOfSyntax(sizeofExpression.type().GetText());
        }

        if (primary.offsetofExpression() is { } offsetofExpression)
        {
            return new OffsetOfSyntax(offsetofExpression.type().GetText(), offsetofExpression.IDENTIFIER().GetText());
        }

        if (primary.countofExpression() is { } countofExpression)
        {
            return new CountOfSyntax(countofExpression.IDENTIFIER().GetText());
        }

        if (primary.functionCall() is { } functionCall)
        {
            return ParseFunctionCall(functionCall);
        }

        if (primary.memberAccess() is { } memberAccess)
        {
            return ParseMemberAccess(memberAccess);
        }

        if (primary.indexExpression() is { } indexExpression)
        {
            return ParseIndexExpression(indexExpression);
        }

        if (primary.IDENTIFIER() is { } identifier)
        {
            return new IdentifierSyntax(identifier.GetText());
        }

        if (primary.children[0].GetText() == "(")
        {
            return ParseExpression((ExpressionContext)primary.children[1]);
        }
        throw new NotImplementedException();
    }

    private static MemberAccessSyntax ParseMemberAccess(MemberAccessContext memberAccess)
    {
        var identifiers = memberAccess.IDENTIFIER().Select(identifier => identifier.GetText()).ToList();
        ExpressionSyntax current = new IdentifierSyntax(identifiers[0]);
        foreach (var member in identifiers.Skip(1))
        {
            current = new MemberAccessSyntax(current, member);
        }

        return (MemberAccessSyntax)current;
    }

    private ExpressionSyntax ParseIndexExpression(IndexExpressionContext indexExpression)
    {
        return new IndexExpressionSyntax(indexExpression.IDENTIFIER().GetText(), ParseExpression(indexExpression.expression()));
    }
}
