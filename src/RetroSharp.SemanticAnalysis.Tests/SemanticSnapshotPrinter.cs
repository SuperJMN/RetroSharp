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

    public void VisitConstDeclarationNode(ConstDeclarationNode node)
    {
        var symbol = node.Scope.Get(node.Name).Match(x => x.ToString(), () => $"<Unknown '{node.Name}'>");
        Write("const " + symbol + "=");
        node.Value.Accept(this);
        sb.AppendLine(";");
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

    public void VisitConditionalExpression(ConditionalExpressionNode node)
    {
        node.Condition.Accept(this);
        sb.Append("?");
        node.WhenTrue.Accept(this);
        sb.Append(":");
        node.WhenFalse.Accept(this);
    }

    public void VisitUnaryExpression(UnaryExpressionNode node)
    {
        sb.Append(node.OperatorSymbol);
        if (node.Operand is BinaryExpressionNode or ConditionalExpressionNode)
        {
            sb.Append("(");
            node.Operand.Accept(this);
            sb.Append(")");
            return;
        }

        node.Operand.Accept(this);
    }

    private void VisitOperand(BinaryExpressionNode parent, ExpressionNode child)
    {
        if (child is ConditionalExpressionNode)
        {
            sb.Append("(");
            child.Accept(this);
            sb.Append(")");
            return;
        }

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

    public void VisitFor(ForNode forNode)
    {
        sb.Append(new string('\t', indent));
        sb.Append("for (");
        forNode.Initializer.Execute(initializer => initializer.Accept(this));
        sb.Append("; ");
        forNode.Condition.Execute(condition => condition.Accept(this));
        sb.Append("; ");
        forNode.Increment.Execute(increment => increment.Accept(this));
        sb.AppendLine(")");
        forNode.Body.Accept(this);
    }

    public void VisitDoWhile(DoWhileNode doWhileNode)
    {
        WriteLine("do");
        doWhileNode.Body.Accept(this);
        sb.Append(new string('\t', indent));
        sb.Append("while (");
        doWhileNode.Condition.Accept(this);
        sb.AppendLine(");");
    }

    public void VisitLoop(LoopNode loopNode)
    {
        WriteLine("loop");
        loopNode.Body.Accept(this);
    }

    public void VisitBreak(BreakNode breakNode)
    {
        sb.Append(new string('\t', indent));
        sb.AppendLine("break;");
    }

    public void VisitContinue(ContinueNode continueNode)
    {
        sb.Append(new string('\t', indent));
        sb.AppendLine("continue;");
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

    public void VisitCastExpression(CastExpressionNode castExpressionNode)
    {
        sb.Append("(");
        sb.Append(castExpressionNode.Type);
        sb.Append(")");
        if (castExpressionNode.Expression is BinaryExpressionNode or ConditionalExpressionNode)
        {
            sb.Append("(");
            castExpressionNode.Expression.Accept(this);
            sb.Append(")");
            return;
        }

        castExpressionNode.Expression.Accept(this);
    }
}
