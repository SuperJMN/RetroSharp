using System.Linq;
using System.Collections.Generic;
using CSharpFunctionalExtensions;
using Xunit;
using RetroSharp.Parser;
using RetroSharp.SemanticAnalysis;

namespace RetroSharp.SemanticAnalysis.Tests;

public class SemanticAnalyzerScopeTests
{
    [Fact]
    public void Locals_do_not_leak_out_of_function_scope()
    {
        // program: void main() { i16 a; }
        var decl = new DeclarationSyntax("i16", "a", Maybe<ExpressionSyntax>.None);
        var block = new BlockSyntax(new List<StatementSyntax> { decl });
        var func = new FunctionSyntax("void", "main", new List<ParameterSyntax>(), block);
        var program = new ProgramSyntax(new List<FunctionSyntax> { func });

        var analyzer = new SemanticAnalyzer();
        var result = analyzer.Analyze(program);

        var programNode = (ProgramNode)result.Node;
        // Ensure the returned top-level scope does not contain 'a'
        Assert.True(result.Scope.Get("a").HasNoValue);

        // But inside the function, the declaration node should have a scope where 'a' exists
        var funcNode = programNode.Functions.Single();
        var declNode = funcNode.Block.Statements.OfType<DeclarationNode>().Single();
        Assert.True(declNode.Scope.Get("a").HasValue);
    }

    [Fact]
    public void Declared_variable_is_visible_in_following_statement_within_block()
    {
        // void main() { i16 a; a; }
        var decl = new DeclarationSyntax("i16", "a", Maybe<ExpressionSyntax>.None);
        var use = new ExpressionStatementSyntax(new IdentifierSyntax("a"));
        var block = new BlockSyntax(new List<StatementSyntax> { decl, use });
        var func = new FunctionSyntax("void", "main", new List<ParameterSyntax>(), block);
        var program = new ProgramSyntax(new List<FunctionSyntax> { func });

        var analyzer = new SemanticAnalyzer();
        var result = analyzer.Analyze(program);

        var programNode = (ProgramNode)result.Node;
        var funcNode = programNode.Functions.Single();
        var exprStmt = funcNode.Block.Statements.OfType<ExpressionStatementNode>().Single();
        var symExpr = (SymbolExpressionNode)exprStmt.Expression;

        // No errors expected for known symbol
        Assert.Empty(symExpr.Errors);
    }

    [Fact]
    public void Undeclared_variable_reports_error()
    {
        // void main() { b; }
        var use = new ExpressionStatementSyntax(new IdentifierSyntax("b"));
        var block = new BlockSyntax(new List<StatementSyntax> { use });
        var func = new FunctionSyntax("void", "main", new List<ParameterSyntax>(), block);
        var program = new ProgramSyntax(new List<FunctionSyntax> { func });

        var analyzer = new SemanticAnalyzer();
        var result = analyzer.Analyze(program);

        var programNode = (ProgramNode)result.Node;
        var funcNode = programNode.Functions.Single();
        var exprStmt = funcNode.Block.Statements.OfType<ExpressionStatementNode>().Single();
        var symExpr = (SymbolExpressionNode)exprStmt.Expression;

        var errors = symExpr.Errors.ToList();
        Assert.Single(errors);
        Assert.Equal("Use of undeclared variable 'b'", errors[0]);
    }
}
