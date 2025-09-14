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
        var funcs = program.function().Select(f => ParseFunction(f));
        return new ProgramSyntax(funcs.ToList());
    }

    private FunctionSyntax ParseFunction(FunctionContext functionContext)
    {
        var block = ParseBlock(functionContext.block());
        return new FunctionSyntax(functionContext.type().GetText(), functionContext.IDENTIFIER().ToString()!, ParseParameters(functionContext.parameters()).ToList(), block);
    }

    private IEnumerable<ParameterSyntax> ParseParameters(ParametersContext parameters)
    {
        if (parameters is { } parameterContexts)
        {
            return parameterContexts.parameter().Select(ParseParameter);
        }

        return Enumerable.Empty<ParameterSyntax>();
    }

    private ParameterSyntax ParseParameter(ParameterContext parameterContext) => new(parameterContext.type().GetText(), parameterContext.IDENTIFIER().GetText());

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

        if (statementContext.conditional() is { } conditional)
        {
            return ParseConditional(conditional);
        }

        if (statementContext.whileLoop() is { } whileLoop)
        {
            return ParseWhileLoop(whileLoop);
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
        var maybeExpression = Maybe.From(returnStatement.expression() is { } expr ? ParseExpression(expr) : null);
        return new ReturnSyntax(maybeExpression);
    }

    private StatementSyntax ParseDeclaration(VariableDeclarationContext declaration)
    {
        var maybeInitialization = Maybe.From(declaration.expression() is { } expr ? ParseExpression(expr) : null);
        return new DeclarationSyntax(declaration.type().GetText(), declaration.IDENTIFIER().GetText(), maybeInitialization);
    }

    private ExpressionSyntax ParseFunctionCall(FunctionCallContext functionCall)
    {
        var name = functionCall.IDENTIFIER().ToString()!;
        var arguments = functionCall.arguments()?.expression()?.Select(ParseExpression) ?? Enumerable.Empty<ExpressionSyntax>();
        
        return new FunctionCall(name, arguments);
    }

    private StatementSyntax ParseWhileLoop(WhileLoopContext whileLoop) => throw new NotImplementedException();

    private StatementSyntax ParseConditional(ConditionalContext conditional)
    {
        var condition = ParseExpression(conditional.expression());
        var thenBlock = ParseBlock(conditional.block()[0]);
        var elseBlock = Maybe.From<BlockContext>(conditional.block().Length > 1 ? conditional.block()[1] : null).Map(context => ParseBlock(context!));
        return new IfElseSyntax(condition, thenBlock, elseBlock);
    }

    private ExpressionSyntax ParseAssignment(AssignmentContext assignment)
    {
        var lvalue = ParseLValue(assignment.lvalue());
        return new AssignmentSyntax(lvalue, ParseExpression(assignment.expression()));
    }

    private ExpressionSyntax ParseExpression(ExpressionContext expression)
    {
        if (expression.assignment() is {} assignmentContext)
        {
            return ParseAssignment(assignmentContext);
        }

        if (expression.conditionalOrExpression() is { } conditionalOr)
        {
            return ParseConditionalOr(conditionalOr);
        }

        throw new NotImplementedException(expression.ToString());
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
            var right = ParseConditionalEquality(conditionalAnd.equalityExpression());
            return new BinaryExpressionSyntax(left, right, Operator.Get("&&"));
        }
        
        return ParseConditionalEquality(conditionalAndExpression.equalityExpression());
    }

    private ExpressionSyntax ParseConditionalEquality(EqualityExpressionContext equalityExpression)
    {
        if (equalityExpression.equalityExpression() is { } equality)
        {
            var left = ParseConditionalEquality(equality);
            var right = ParseConditionalRelational(equalityExpression.relationalExpression());
            return new BinaryExpressionSyntax(left, right, Operator.Get(equalityExpression.children[1].GetText()));
        }
        
        return ParseConditionalRelational(equalityExpression.relationalExpression());
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
        if (mulExpression.mulExpression() is {} multExpr)
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
        if (unaryExpression.unaryExpression() is { } unaryExpr)
        {
            return new UnaryExpressionSyntax(ParsePrimary(unaryExpr.primary()));
        }

        return ParsePrimary(unaryExpression.primary());
    }

    private ExpressionSyntax ParsePrimary(PrimaryContext primary)
    {
        if (primary.LITERAL() is { } node)
        {
            return new ConstantSyntax(node.GetText());
        }

        if (primary.IDENTIFIER() is { } identifier)
        {
            return new IdentifierSyntax(identifier.GetText());
        }
        
        if (primary.functionCall() is { } functionCall)
        {
            return ParseFunctionCall(functionCall);
        }

        if (primary.children[0].GetText() == "(")
        {
            return ParseExpression((ExpressionContext) primary.children[1]);
        }
        throw new NotImplementedException();
    }
}

internal class UnaryExpressionSyntax : ExpressionSyntax
{
    public UnaryExpressionSyntax(ExpressionSyntax parseAtom)
    {
        
    }

    public override void Accept(ISyntaxVisitor visitor)
    {
        throw new NotImplementedException();
    }
}