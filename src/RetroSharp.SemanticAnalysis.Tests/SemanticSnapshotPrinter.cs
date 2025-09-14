using System.Text;
using CSharpFunctionalExtensions;
using RetroSharp.SemanticAnalysis;

namespace RetroSharp.SemanticAnalysis.Tests;

// Deterministic, human-friendly snapshot of the semantic model.
// Intentionally simple and stable: prints structure + resolved symbols and aggregates diagnostics.
internal class SemanticSnapshotPrinter : INodeVisitor
{
    private readonly StringBuilder sb = new();
    private int indent;

    public static string Print(ProgramNode program)
    {
        var printer = new SemanticSnapshotPrinter();
        program.Accept(printer);
        // Diagnostics block at the end for stability
        var diagnostics = program.AllErrors
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();
        if (diagnostics.Count > 0)
        {
            printer.sb.AppendLine("== Diagnostics ==");
            foreach (var d in diagnostics)
            {
                printer.sb.AppendLine(d);
            }
        }
        return Normalize(printer.sb.ToString());
    }

    private static string Normalize(string s) => s.Replace("\r\n", "\n");

    private void WriteLine(string text = "") => sb.AppendLine(new string('\t', indent) + text);
    private void Write(string text) => sb.Append(new string('\t', indent) + text);

    public void VisitProgramNode(ProgramNode node)
    {
        foreach (var f in node.Functions)
        {
            f.Accept(this);
        }
    }

    public void VisitFunctionNode(FunctionNode node)
    {
        WriteLine($"{node.ReturnType} {node.Name}()");
        node.Block.Accept(this);
    }

    public void VisitBlockNode(BlockNode node)
    {
        WriteLine("{");
        indent++;
        foreach (var st in node.Statements)
        {
            st.Accept(this);
        }
        indent--;
        WriteLine("}");
    }

    public void VisitDeclarationNode(DeclarationNode node)
    {
        // Deterministically print the declared symbol from the node scope
        var symbol = node.Scope.Get(node.Name).Match(x => x.ToString(), () => $"<Unknown '{node.Name}'>");
        WriteLine(symbol + ";");
    }

    public void VisitExpressionStatement(ExpressionStatementNode node)
    {
        // One-liner expression
        sb.Append(new string('\t', indent));
        node.Expression.Accept(this);
        sb.AppendLine(";");
    }

    public void VisitAssignment(AssignmentNode node)
    {
        node.Left.Accept(this);
        sb.Append("=");
        node.Right.Accept(this);
    }

    public void VisitConstant(ConstantNode node)
    {
        sb.Append(node.Value);
    }

    public void VisitKnownSymbol(KnownSymbolNode node)
    {
        sb.Append(node.Symbol.Name);
    }

    public void VisitUnknownSymbol(UnknownSymbol node)
    {
        sb.Append($"<Unknown '{node}'>");
    }

    public void VisitSymbolExpression(SymbolExpressionNode node)
    {
        node.SymbolNode.Accept(this);
    }

    public void VisitBinaryExpression(BinaryExpressionNode node)
    {
        VisitOperand(node, node.Left);
        sb.Append(node.Operator.Symbol);
        VisitOperand(node, node.Right);
    }

    private void VisitOperand(BinaryExpressionNode parent, ExpressionNode child)
    {
        if (child is BinaryExpressionNode bin && bin.Operator.Precedence > parent.Operator.Precedence)
        {
            sb.Append("(");
            child.Accept(this);
            sb.Append(")");
        }
        else
        {
            child.Accept(this);
        }
    }

    // New visitors (no-op formatting consistent with existing snapshot style)
    public void VisitReturn(ReturnNode returnNode)
    {
        sb.Append(new string('\t', indent));
        sb.Append("return");
        if (returnNode.Expression.HasValue)
        {
            sb.Append(" ");
            returnNode.Expression.Value.Accept(this);
        }
        sb.AppendLine(";");
    }

    public void VisitIfElse(IfElseNode ifElseNode)
    {
        sb.Append(new string('\t', indent));
        sb.Append("if (");
        ifElseNode.Condition.Accept(this);
        sb.AppendLine(")");
        ifElseNode.Then.Accept(this);
        if (ifElseNode.Else.HasValue)
        {
            sb.Append(new string('\t', indent));
            sb.AppendLine("else");
            ifElseNode.Else.Value.Accept(this);
        }
    }

    public void VisitFunctionCall(FunctionCallExpressionNode functionCall)
    {
        sb.Append(functionCall.Name);
        sb.Append("(");
        var first = true;
        foreach (var arg in functionCall.Arguments)
        {
            if (!first) sb.Append(", ");
            first = false;
            arg.Accept(this);
        }
        sb.Append(")");
    }
}
