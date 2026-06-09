using System.Text;

namespace RetroSharp.Parser;

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
        foreach (var typeAlias in programSyntax.TypeAliases)
        {
            typeAlias.Accept(this);
        }

        foreach (var constant in programSyntax.Constants)
        {
            constant.Accept(this);
        }

        foreach (var enumSyntax in programSyntax.Enums)
        {
            enumSyntax.Accept(this);
        }

        foreach (var structSyntax in programSyntax.Structs)
        {
            structSyntax.Accept(this);
        }

        foreach (var function in programSyntax.Functions)
        {
            function.Accept(this);
        }
    }

    public void VisitTypeAlias(TypeAliasSyntax typeAlias)
    {
        resultBuilder.AppendLine($"type {typeAlias.Name}={typeAlias.Type};");
    }

    public void VisitConstDeclaration(ConstDeclarationSyntax constDeclaration)
    {
        resultBuilder.Append(new string('\t', indentationLevel));
        resultBuilder.Append("const ");
        constDeclaration.TypeAnnotation.Execute(type =>
        {
            resultBuilder.Append(type);
            resultBuilder.Append(" ");
        });
        resultBuilder.Append($"{constDeclaration.Name}=");
        constDeclaration.Value.Accept(this);
        resultBuilder.AppendLine(";");
    }

    public void VisitEnum(EnumSyntax enumSyntax)
    {
        resultBuilder.AppendLine($"enum {enumSyntax.Name}");
        resultBuilder.AppendLine("{");
        indentationLevel++;
        for (var i = 0; i < enumSyntax.Members.Count; i++)
        {
            resultBuilder.Append(new string('\t', indentationLevel));
            enumSyntax.Members[i].Accept(this);
            if (i < enumSyntax.Members.Count - 1)
            {
                resultBuilder.Append(",");
            }

            resultBuilder.AppendLine();
        }

        indentationLevel--;
        resultBuilder.AppendLine("}");
    }

    public void VisitEnumMember(EnumMemberSyntax enumMember)
    {
        resultBuilder.Append(enumMember.Name);
        enumMember.Value.Execute(value =>
        {
            resultBuilder.Append("=");
            value.Accept(this);
        });
    }

    public void VisitStruct(StructSyntax structSyntax)
    {
        resultBuilder.AppendLine($"struct {structSyntax.Name}");
        resultBuilder.AppendLine("{");
        indentationLevel++;
        foreach (var field in structSyntax.Fields)
        {
            field.Accept(this);
        }
        indentationLevel--;
        resultBuilder.AppendLine("}");
    }

    public void VisitStructField(StructFieldSyntax structField)
    {
        resultBuilder.Append(new string('\t', indentationLevel));
        resultBuilder.AppendLine($"{structField.Type} {structField.Name};");
    }

    public void VisitArrayInitializer(ArrayInitializerSyntax arrayInitializerSyntax)
    {
        resultBuilder.Append("[");
        for (var i = 0; i < arrayInitializerSyntax.Elements.Count; i++)
        {
            if (i > 0)
            {
                resultBuilder.Append(", ");
            }

            arrayInitializerSyntax.Elements[i].Accept(this);
        }

        resultBuilder.Append("]");
    }

    public void VisitStructInitializer(StructInitializerSyntax structInitializerSyntax)
    {
        resultBuilder.Append("{ ");
        for (var i = 0; i < structInitializerSyntax.Fields.Count; i++)
        {
            if (i > 0)
            {
                resultBuilder.Append(", ");
            }

            var field = structInitializerSyntax.Fields[i];
            resultBuilder.Append(field.Name);
            resultBuilder.Append(": ");
            field.Expression.Accept(this);
        }

        resultBuilder.Append(" }");
    }

    public void VisitSdkDotCall(SdkDotCallSyntax sdkDotCallSyntax)
    {
        resultBuilder.Append(sdkDotCallSyntax.Module);
        resultBuilder.Append(".");
        resultBuilder.Append(sdkDotCallSyntax.Method);
        resultBuilder.Append("(");
        var first = true;
        foreach (var parameter in sdkDotCallSyntax.Parameters)
        {
            if (!first)
            {
                resultBuilder.Append(", ");
            }

            first = false;
            parameter.Accept(this);
        }

        resultBuilder.Append(")");
    }

    public void VisitFunctionCall(FunctionCall functionCall)
    {
        resultBuilder.Append(functionCall.Name);
        resultBuilder.Append("(");
        var first = true;
        foreach (var parameter in functionCall.Parameters)
        {
            if (!first)
            {
                resultBuilder.Append(", ");
            }

            first = false;
            parameter.Accept(this);
        }
        resultBuilder.Append(")");
    }

    public void VisitNamedArgument(NamedArgumentSyntax namedArgumentSyntax)
    {
        resultBuilder.Append(namedArgumentSyntax.Name);
        resultBuilder.Append(": ");
        namedArgumentSyntax.Expression.Accept(this);
    }

    public void VisitIdentifierLValue(IdentifierLValue identifierLValue)
    {
        resultBuilder.Append(identifierLValue.Identifier);
    }

    public void VisitPointerDerefLValue(PointerDerefLValue pointerDerefLValue)
    {
        resultBuilder.Append("*");
        pointerDerefLValue.Expression.Accept(this);
    }

    public void VisitIndexLValue(IndexLValue indexLValue)
    {
        resultBuilder.Append(indexLValue.BaseIdentifier);
        resultBuilder.Append("[");
        indexLValue.Index.Accept(this);
        resultBuilder.Append("]");
    }

    public void VisitMemberAccessLValue(MemberAccessLValue memberAccessLValue)
    {
        memberAccessLValue.MemberAccess.Accept(this);
    }

    public void VisitAssignment(AssignmentSyntax assignmentSyntax)
    {
        assignmentSyntax.Left.Accept(this);
        resultBuilder.Append(assignmentSyntax.OperatorSymbol);
        assignmentSyntax.Right.Accept(this);
    }

    public void VisitPostfixMutation(PostfixMutationSyntax postfixMutationSyntax)
    {
        postfixMutationSyntax.Target.Accept(this);
        resultBuilder.Append(postfixMutationSyntax.OperatorSymbol);
    }

    public void VisitExpressionStatement(ExpressionStatementSyntax expressionStatementSyntax)
    {
        resultBuilder.Append(new string('\t', indentationLevel));
        expressionStatementSyntax.Expression.Accept(this);
        resultBuilder.AppendLine(";");
    }

    public void VisitFunction(FunctionSyntax function)
    {
        if (function.IsInline)
        {
            resultBuilder.Append("inline ");
        }

        if (function.IsPure)
        {
            resultBuilder.Append("pure ");
        }

        resultBuilder.Append($"{function.Type} {function.Name}");
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
        if (function.IsExpressionBodied)
        {
            resultBuilder.Append("=>");
            if (function.Block.Statements is not [ReturnSyntax { Expression.HasValue: true } returnSyntax])
            {
                throw new InvalidOperationException($"Expression-bodied function '{function.Name}' must contain exactly one return expression.");
            }

            returnSyntax.Expression.Value.Accept(this);
            resultBuilder.AppendLine(";");
            return;
        }

        resultBuilder.AppendLine();
        function.Block.Accept(this);
    }

    public void VisitConstant(ConstantSyntax constantSyntax)
    {
        resultBuilder.Append(constantSyntax.Value);
    }

    public void VisitDeclaration(DeclarationSyntax declarationSyntax)
    {
        resultBuilder.Append(new string('\t', indentationLevel));
        if (declarationSyntax.IsImmutable)
        {
            resultBuilder.Append("let " + declarationSyntax.Name);
            declarationSyntax.Initialization.Execute(init =>
            {
                resultBuilder.Append("=");
                init.Accept(this);
            });
            resultBuilder.AppendLine(";");
            return;
        }

        resultBuilder.Append(declarationSyntax.Type + " " + declarationSyntax.Name);
        declarationSyntax.ArrayLength.Execute(length =>
        {
            resultBuilder.Append("[");
            length.Accept(this);
            resultBuilder.Append("]");
        });
        declarationSyntax.Initialization.Execute(init =>
        {
            resultBuilder.Append("=");
            init.Accept(this);
        });
        resultBuilder.AppendLine(";");
    }

    public void VisitParameter(ParameterSyntax parameterSyntax)
    {
        if (parameterSyntax.IsReceiver)
        {
            resultBuilder.Append("this ");
        }

        resultBuilder.Append(parameterSyntax.Type + " " + parameterSyntax.Name);
        parameterSyntax.DefaultValue.Execute(defaultValue =>
        {
            resultBuilder.Append("=");
            defaultValue.Accept(this);
        });
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

    public void VisitMemberAccess(MemberAccessSyntax memberAccessSyntax)
    {
        memberAccessSyntax.Target.Accept(this);
        resultBuilder.Append(".");
        resultBuilder.Append(memberAccessSyntax.Member);
    }

    public void VisitIndexExpression(IndexExpressionSyntax indexExpressionSyntax)
    {
        resultBuilder.Append(indexExpressionSyntax.BaseIdentifier);
        resultBuilder.Append("[");
        indexExpressionSyntax.Index.Accept(this);
        resultBuilder.Append("]");
    }

    public void VisitSizeOf(SizeOfSyntax sizeOfSyntax)
    {
        resultBuilder.Append("sizeof(");
        resultBuilder.Append(sizeOfSyntax.Type);
        resultBuilder.Append(")");
    }

    public void VisitOffsetOf(OffsetOfSyntax offsetOfSyntax)
    {
        resultBuilder.Append("offsetof(");
        resultBuilder.Append(offsetOfSyntax.Type);
        resultBuilder.Append(", ");
        resultBuilder.Append(offsetOfSyntax.Field);
        resultBuilder.Append(")");
    }

    public void VisitCountOf(CountOfSyntax countOfSyntax)
    {
        resultBuilder.Append("countof(");
        resultBuilder.Append(countOfSyntax.BaseIdentifier);
        resultBuilder.Append(")");
    }

    public void VisitUnaryOperator(UnaryExpressionSyntax unaryExpressionSyntax)
    {
        resultBuilder.Append(unaryExpressionSyntax.OperatorSymbol);
        if (unaryExpressionSyntax.Operand is BinaryExpressionSyntax or ConditionalExpressionSyntax)
        {
            resultBuilder.Append("(");
            unaryExpressionSyntax.Operand.Accept(this);
            resultBuilder.Append(")");
            return;
        }

        unaryExpressionSyntax.Operand.Accept(this);
    }

    public void VisitCast(CastSyntax castSyntax)
    {
        resultBuilder.Append("(");
        resultBuilder.Append(castSyntax.Type);
        resultBuilder.Append(")");
        if (castSyntax.Expression is BinaryExpressionSyntax or ConditionalExpressionSyntax)
        {
            resultBuilder.Append("(");
            castSyntax.Expression.Accept(this);
            resultBuilder.Append(")");
            return;
        }

        castSyntax.Expression.Accept(this);
    }

    public void VisitBinaryOperator(BinaryExpressionSyntax binaryExpressionSyntax)
    {
        VisitOperand(binaryExpressionSyntax, binaryExpressionSyntax.Left);
        resultBuilder.Append(binaryExpressionSyntax.Operator.Symbol);
        VisitOperand(binaryExpressionSyntax, binaryExpressionSyntax.Right);
    }

    public void VisitConditionalExpression(ConditionalExpressionSyntax conditionalExpressionSyntax)
    {
        conditionalExpressionSyntax.Condition.Accept(this);
        resultBuilder.Append("?");
        conditionalExpressionSyntax.WhenTrue.Accept(this);
        resultBuilder.Append(":");
        conditionalExpressionSyntax.WhenFalse.Accept(this);
    }

    public void VisitSwitchExpression(SwitchExpressionSyntax switchExpressionSyntax)
    {
        switchExpressionSyntax.Subject.Accept(this);
        resultBuilder.Append(" switch { ");
        for (var i = 0; i < switchExpressionSyntax.Arms.Count; i++)
        {
            if (i > 0)
            {
                resultBuilder.Append(", ");
            }

            VisitSwitchExpressionArm(switchExpressionSyntax.Arms[i]);
        }

        switchExpressionSyntax.DefaultValue.Execute(defaultValue =>
        {
            if (switchExpressionSyntax.Arms.Count > 0)
            {
                resultBuilder.Append(", ");
            }

            resultBuilder.Append("_=>");
            defaultValue.Accept(this);
        });
        resultBuilder.Append(" }");
    }

    public void VisitPipelineExpression(PipelineExpressionSyntax pipelineExpressionSyntax)
    {
        pipelineExpressionSyntax.Value.Accept(this);
        foreach (var step in pipelineExpressionSyntax.Steps)
        {
            resultBuilder.Append(" |> ");
            resultBuilder.Append(step.FunctionName);
            resultBuilder.Append("(");
            var first = true;
            foreach (var argument in step.Arguments)
            {
                if (!first)
                {
                    resultBuilder.Append(", ");
                }

                first = false;
                argument.Accept(this);
            }

            resultBuilder.Append(")");
        }
    }

    private void VisitSwitchExpressionArm(SwitchExpressionArmSyntax arm)
    {
        for (var i = 0; i < arm.Patterns.Count; i++)
        {
            if (i > 0)
            {
                resultBuilder.Append(", ");
            }

            VisitSwitchPattern(arm.Patterns[i]);
        }

        resultBuilder.Append("=>");
        arm.Value.Accept(this);
    }

    private void VisitSwitchPattern(SwitchCasePatternSyntax pattern)
    {
        pattern.Start.Accept(this);
        pattern.End.Execute(end =>
        {
            resultBuilder.Append("..");
            end.Accept(this);
        });
    }

    private void VisitOperand(BinaryExpressionSyntax parent, ExpressionSyntax child)
    {
        if (child is ConditionalExpressionSyntax)
        {
            resultBuilder.Append("(");
            child.Accept(this);
            resultBuilder.Append(")");
            return;
        }

        if (child is BinaryExpressionSyntax childBinary)
        {
            if (childBinary.Operator.Precedence > parent.Operator.Precedence)
            {
                resultBuilder.Append("(");
                child.Accept(this);
                resultBuilder.Append(")");
                return;
            }
        }
        child.Accept(this);
    }

    public void VisitIfElse(IfElseSyntax ifElseSyntax)
    {
        resultBuilder.Append(new string('\t', indentationLevel));
        resultBuilder.Append("if");
        resultBuilder.Append("(");
        ifElseSyntax.Condition.Accept(this);
        resultBuilder.AppendLine(")");
        ifElseSyntax.ThenBlock.Accept(this);
        ifElseSyntax.ElseBlock.Execute(block =>
        {
            if (block.Statements is [IfElseSyntax nestedIf])
            {
                resultBuilder.Append(new string('\t', indentationLevel));
                resultBuilder.Append("else ");
                nestedIf.Accept(this);
                return;
            }

            resultBuilder.AppendLine("else");
            block.Accept(this);
        });
    }

    public void VisitWhile(WhileSyntax whileSyntax)
    {
        resultBuilder.Append(new string('\t', indentationLevel));
        resultBuilder.Append("while");
        resultBuilder.Append("(");
        whileSyntax.Condition.Accept(this);
        resultBuilder.AppendLine(")");
        whileSyntax.Body.Accept(this);
    }

    public void VisitDoWhile(DoWhileSyntax doWhileSyntax)
    {
        resultBuilder.AppendLine(new string('\t', indentationLevel) + "do");
        doWhileSyntax.Body.Accept(this);
        resultBuilder.Append(new string('\t', indentationLevel));
        resultBuilder.Append("while");
        resultBuilder.Append("(");
        doWhileSyntax.Condition.Accept(this);
        resultBuilder.AppendLine(");");
    }

    public void VisitLoop(LoopSyntax loopSyntax)
    {
        resultBuilder.AppendLine(new string('\t', indentationLevel) + "loop");
        loopSyntax.Body.Accept(this);
    }

    public void VisitRangeFor(RangeForSyntax rangeForSyntax)
    {
        resultBuilder.Append(new string('\t', indentationLevel));
        resultBuilder.Append("for");
        resultBuilder.Append("(");
        resultBuilder.Append(rangeForSyntax.Type);
        resultBuilder.Append(" ");
        resultBuilder.Append(rangeForSyntax.Identifier);
        resultBuilder.Append(" in ");
        rangeForSyntax.Start.Accept(this);
        resultBuilder.Append("..");
        rangeForSyntax.End.Accept(this);
        resultBuilder.AppendLine(")");
        rangeForSyntax.Body.Accept(this);
    }

    public void VisitFor(ForSyntax forSyntax)
    {
        resultBuilder.Append(new string('\t', indentationLevel));
        resultBuilder.Append("for");
        resultBuilder.Append("(");
        forSyntax.Initializer.Execute(VisitForInitializer);
        resultBuilder.Append(";");
        forSyntax.Condition.Execute(condition => condition.Accept(this));
        resultBuilder.Append(";");
        forSyntax.Increment.Execute(increment => increment.Accept(this));
        resultBuilder.AppendLine(")");
        forSyntax.Body.Accept(this);
    }

    public void VisitSwitch(SwitchSyntax switchSyntax)
    {
        resultBuilder.Append(new string('\t', indentationLevel));
        resultBuilder.Append("switch");
        resultBuilder.Append("(");
        switchSyntax.Subject.Accept(this);
        resultBuilder.AppendLine(")");
        resultBuilder.AppendLine(new string('\t', indentationLevel) + "{");
        indentationLevel++;
        foreach (var switchCase in switchSyntax.Cases)
        {
            switchCase.Accept(this);
        }

        switchSyntax.DefaultBlock.Execute(defaultBlock =>
        {
            resultBuilder.AppendLine(new string('\t', indentationLevel) + "default");
            defaultBlock.Accept(this);
        });
        indentationLevel--;
        resultBuilder.AppendLine(new string('\t', indentationLevel) + "}");
    }

    public void VisitSwitchCase(SwitchCaseSyntax switchCaseSyntax)
    {
        resultBuilder.Append(new string('\t', indentationLevel));
        resultBuilder.Append("case ");
        for (var i = 0; i < switchCaseSyntax.Patterns.Count; i++)
        {
            if (i > 0)
            {
                resultBuilder.Append(", ");
            }

            var pattern = switchCaseSyntax.Patterns[i];
            pattern.Start.Accept(this);
            pattern.End.Execute(end =>
            {
                resultBuilder.Append("..");
                end.Accept(this);
            });
        }

        resultBuilder.AppendLine();
        switchCaseSyntax.Block.Accept(this);
    }

    public void VisitBreak(BreakSyntax breakSyntax)
    {
        resultBuilder.Append(new string('\t', indentationLevel));
        resultBuilder.AppendLine("break;");
    }

    public void VisitContinue(ContinueSyntax continueSyntax)
    {
        resultBuilder.Append(new string('\t', indentationLevel));
        resultBuilder.AppendLine("continue;");
    }

    private void VisitForInitializer(StatementSyntax initializer)
    {
        switch (initializer)
        {
            case DeclarationSyntax declaration:
                resultBuilder.Append(declaration.Type + " " + declaration.Name);
                declaration.ArrayLength.Execute(length =>
                {
                    resultBuilder.Append("[");
                    length.Accept(this);
                    resultBuilder.Append("]");
                });
                declaration.Initialization.Execute(init =>
                {
                    resultBuilder.Append("=");
                    init.Accept(this);
                });
                break;
            case ExpressionStatementSyntax expressionStatement:
                expressionStatement.Expression.Accept(this);
                break;
            default:
                throw new InvalidOperationException($"Unsupported for initializer '{initializer.GetType().Name}'.");
        }
    }
}
