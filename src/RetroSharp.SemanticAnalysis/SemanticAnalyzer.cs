using RetroSharp.Parser;

namespace RetroSharp.SemanticAnalysis;

public class SemanticAnalyzer
{
    public AnalyzeResult<SemanticNode> Analyze(ProgramSyntax program) => AnalyzeProgram(program, Scope.Empty);

    private AnalyzeResult<SemanticNode> AnalyzeProgram(ProgramSyntax node, Scope scope)
    {
        var functions = new List<FunctionNode>();
        foreach (var function in node.Functions)
        {
            var functionResult = AnalyzeFunction(function, scope);
            scope = functionResult.Scope;
            functions.Add(functionResult.Node);
        }

        return new AnalyzeResult<SemanticNode>(new ProgramNode(functions), scope);
    }

    private AnalyzeResult<FunctionNode> AnalyzeFunction(FunctionSyntax function, Scope parentScope)
    {
        // Create a child scope for the function body so locals don't leak out
        var functionScope = new Scope(parentScope);

        // Declare parameters in the function scope so they can be referenced in the body
        var errors = new List<string>();
        foreach (var p in function.Parameters)
        {
            var result = functionScope.TryDeclare(new Symbol(p.Name, IntType.Instance));
            if (result.IsSuccess)
            {
                functionScope = result.Value;
            }
            else
            {
                errors.Add($"Parameter '{p.Name}' is already declared");
            }
        }

        var analyzeBlockResult = AnalyzeBlock(function.Block, functionScope);
        var paramNames = function.Parameters.Select(p => p.Name).ToList();
        var node = new FunctionNode(function.Type, function.Name, analyzeBlockResult.Node, paramNames)
        {
            Errors = errors
        };
        // Return the unchanged parent scope (function-local declarations are not visible outside)
        return new AnalyzeResult<FunctionNode>(node, parentScope);
    }

    private AnalyzeResult<BlockNode> AnalyzeBlock(BlockSyntax block, Scope outerScope)
    {
        // Each block introduces a new child scope
        var blockScope = new Scope(outerScope);
        var statements = new List<StatementNode>();
        foreach (var statement in block.Statements)
        {
            var analyzedStatementResult = AnalyzeStatement(statement, blockScope);
            statements.Add(analyzedStatementResult.Node);
            blockScope = analyzedStatementResult.Scope;
        }

        // Do not leak the block scope; return to the outer scope
        return new AnalyzeResult<BlockNode>(new BlockNode(statements), outerScope);
    }

    private AnalyzeResult<StatementNode> AnalyzeStatement(StatementSyntax statement, Scope scope)
    {
        switch (statement)
        {
            case DeclarationSyntax declarationStatement:
                return AnalyzeDeclaration(declarationStatement, scope);
            case ExpressionStatementSyntax expressionStatement:
                return AnalyzeExpressionStatement(expressionStatement, scope);
            case IfElseSyntax ifElseStatement:
                return AnalyzeIfElse(ifElseStatement, scope);
            case ReturnSyntax returnStatement:
                return AnalyzeReturn(returnStatement, scope);
            default:
                throw new ArgumentOutOfRangeException(nameof(statement));
        }
    }

    private AnalyzeResult<StatementNode> AnalyzeExpressionStatement(ExpressionStatementSyntax expressionStatement, Scope scope)
    {
        var analyzeExpressionResult = AnalyzeExpression(expressionStatement.Expression, scope);
        return new AnalyzeResult<StatementNode>(new ExpressionStatementNode(analyzeExpressionResult.Node), scope);
    }

    private AnalyzeResult<ExpressionNode> AnalyzeExpression(ExpressionSyntax expression, Scope scope)
    {
        if (expression is AssignmentSyntax assignment)
        {
            return AnalyzeAssignmentExpression(assignment, scope);
        }
        
        if (expression is ConstantSyntax c)
        {
            return new AnalyzeResult<ExpressionNode>(new ConstantNode(c.Value), scope);
        }

        if (expression is BinaryExpressionSyntax binaryExpression)
        {
            return AnalyzeBinaryExpression(binaryExpression, scope);
        }

        if (expression is IdentifierSyntax i)
        {
            var symbolNode = GetSymbolNode(scope, i.Identifier);
            var symbolExpressionNode = new SymbolExpressionNode(symbolNode)
            {
                Errors = SymbolError(symbolNode)
            };
            return new AnalyzeResult<ExpressionNode>(symbolExpressionNode, scope);
        }

        if (expression is FunctionCall functionCall)
        {
            var analyzedArgs = functionCall.Parameters.Select(arg => AnalyzeExpression(arg, scope).Node).ToList();
            return new AnalyzeResult<ExpressionNode>(new FunctionCallExpressionNode(functionCall.Name, analyzedArgs), scope);
        }

        throw new InvalidOperationException("Por aquí no vas a ningún sitio");
    }

    private IEnumerable<string> SymbolError(SymbolNode symbolNode)
    {
        return CheckSymbol(symbolNode).Map(s => new List<string> { s }).GetValueOrDefault([]);
    }

    private Maybe<string> CheckSymbol(SymbolNode symbolNode)
    {
        if (symbolNode is UnknownSymbol)
        {
            return $"Use of undeclared variable '{symbolNode}'";
        }

        return Maybe<string>.None;
    }

    private SymbolNode GetSymbolNode(Scope scope, string name)
    {
        return scope.Get(name).Match(symbol => (SymbolNode)new KnownSymbolNode(symbol), () => new UnknownSymbol(name));
    }

    private AnalyzeResult<ExpressionNode> AnalyzeBinaryExpression(BinaryExpressionSyntax binaryExpression, Scope scope)
    {
        var left = AnalyzeExpression(binaryExpression.Left, scope);
        var right = AnalyzeExpression(binaryExpression.Right, scope);
        return new AnalyzeResult<ExpressionNode>(new BinaryExpressionNode(left.Node, right.Node, binaryExpression.Operator), scope);

        throw new InvalidOperationException("Por aquí no vas a ningún sitio tampoco");
    }

    private AnalyzeResult<ExpressionNode> AnalyzeAssignmentExpression(AssignmentSyntax assignment, Scope scope)
    {
        var symbolNode = GetSymbolNode(scope, ((IdentifierLValue)assignment.Left).Identifier);

        var analyzeExpression = AnalyzeExpression(assignment.Right, scope);

        return new AnalyzeResult<ExpressionNode>(new AssignmentNode(symbolNode, analyzeExpression.Node)
        {
            Errors = SymbolError(symbolNode)
        }, scope);
    }

    private AnalyzeResult<StatementNode> AnalyzeDeclaration(DeclarationSyntax declarationStatement, Scope scope)
    {
        var declaration = scope
            .TryDeclare(new Symbol(declarationStatement.Name, IntType.Instance))
            .Match(
                s => new AnalyzeResult<StatementNode>(new DeclarationNode(declarationStatement.Name, s), s),
                _ => new AnalyzeResult<StatementNode>(new DeclarationNode(declarationStatement.Name, scope)
                {
                    Errors = [$"Variable {declarationStatement.Name} is already declared"]
                }, scope));
        return declaration;
    }

    private AnalyzeResult<StatementNode> AnalyzeIfElse(IfElseSyntax ifElse, Scope scope)
    {
        var cond = AnalyzeExpression(ifElse.Condition, scope).Node;
        var thenBlock = AnalyzeBlock(ifElse.ThenBlock, scope).Node;
        var elseBlock = ifElse.ElseBlock.Match(
            b => AnalyzeBlock(b, scope).Node,
            () => null as BlockNode
        );
        var maybeElse = elseBlock is null ? Maybe<BlockNode>.None : Maybe.From(elseBlock);
        return new AnalyzeResult<StatementNode>(new IfElseNode(cond, thenBlock, maybeElse), scope);
    }

    private AnalyzeResult<StatementNode> AnalyzeReturn(ReturnSyntax ret, Scope scope)
    {
        var maybeExpr = ret.Expression.Map(expr => AnalyzeExpression(expr, scope).Node);
        return new AnalyzeResult<StatementNode>(new ReturnNode(maybeExpr), scope);
    }
}
