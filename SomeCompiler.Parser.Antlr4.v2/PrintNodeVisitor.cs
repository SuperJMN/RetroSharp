﻿using System.Text;
using CSharpFunctionalExtensions;
using MoreLinq.Extensions;

namespace SomeCompiler.Parser;

public class PrintNodeVisitor : ISyntaxVisitor
{
    private readonly StringBuilder resultBuilder = new();
    private int indentationLevel = 0;


    public override string ToString()
    {
        return resultBuilder.ToString();
    }

    public void VisitBlock(BlockSyntax block)
    {
        resultBuilder.AppendLine(new string('\t', indentationLevel) + "{");
        indentationLevel++;
        foreach (var statement in block.Statements)
        {
            statement.Accept(this);
        }
        indentationLevel--;
        resultBuilder.AppendLine(new string('\t', indentationLevel) + "}");
    }

    public void VisitProgram(ProgramSyntax programSyntax)
    {
        foreach (var function in programSyntax.Functions)
        {
            function.Accept(this);
        }
    }

    public void VisitFunctionCall(FunctionCall functionCall)
    {
        resultBuilder.Append(functionCall.Name);
        resultBuilder.Append("(");
        foreach (var parameter in functionCall.Parameters)
        {
            parameter.Accept(this);
        }
        resultBuilder.Append(")");
    }

    public void VisitMult(MultExpression multExpression)
    {
        multExpression.Left.Accept(this);
        resultBuilder.Append("*");
        multExpression.Right.Accept(this);
    }

    public void VisitIdentifierLValue(IdentifierLValue identifierLValue)
    {
        resultBuilder.Append(identifierLValue.Identifier);
    }

    public void VisitAdd(AddExpression addExpression)
    {
        addExpression.Left.Accept(this);
        resultBuilder.Append("+");
        addExpression.Right.Accept(this);
    }

    public void VisitAssignment(AssignmentSyntax assignmentSyntax)
    {
        assignmentSyntax.Left.Accept(this);
        resultBuilder.Append("=");
        assignmentSyntax.Right.Accept(this);
        resultBuilder.AppendLine(";");
    }

    public void VisitExpressionStatement(ExpressionStatementSyntax expressionStatementSyntax)
    {
        resultBuilder.Append(new string('\t', indentationLevel));
        expressionStatementSyntax.Expression.Accept(this);
        resultBuilder.AppendLine(";");
    }

    public void VisitFunction(FunctionSyntax function)
    {
        resultBuilder.AppendLine($"{function.Type} {function.Name}");
        resultBuilder.Append("(");
        for (int i = 0; i < function.Parameters.Count; i++)
        {
            function.Parameters[i].Accept(this);
            if (i < function.Parameters.Count - 1)
            {
                resultBuilder.Append(", ");
            }
        }
        resultBuilder.Append(")");
        function.Block.Accept(this);
    }

    public void VisitConstant(ConstantSyntax constantSyntax)
    {
        resultBuilder.Append(constantSyntax.Value);
    }

    public void VisitDeclaration(DeclarationSyntax declarationSyntax)
    {
        resultBuilder.Append(new string('\t', indentationLevel));
        resultBuilder.Append(declarationSyntax.Type + " " + declarationSyntax.Name);
        declarationSyntax.Initialization.Execute(init =>
        {
            resultBuilder.Append("=");
            init.Accept(this);
        });
        resultBuilder.AppendLine(";");
    }

    public void VisitParameter(ParameterSyntax parameterSyntax)
    {
        resultBuilder.Append(parameterSyntax.Type + " " + parameterSyntax.Name);
    }

    public void VisitReturn(ReturnSyntax returnSyntax)
    {
        resultBuilder.Append("return");
        returnSyntax.Expression.Execute(init =>
        {
            resultBuilder.Append(" ");
            init.Accept(this);
        });
        resultBuilder.AppendLine(";");
    }

    public void VisitIdentifier(IdentifierSyntax identifierSyntax)
    {
        resultBuilder.Append(identifierSyntax.Identifier);
    }
}