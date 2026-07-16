using System.Globalization;
using RetroSharp.Core;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;
using RetroSharp.Sdk;

namespace RetroSharp.NES;

internal sealed partial class NesRuntimeCompiler
{
    private void EmitWhile(WhileSyntax whileSyntax)
    {
        var conditionIsConstant = TryConst(whileSyntax.Condition, out var condition);
        if (conditionIsConstant && condition == 0)
        {
            return;
        }

        var whileLoopId = nextWhileLoopId++;
        var loopLabel = $"while_{whileLoopId}";
        var endLabel = $"while_end_{whileLoopId}";
        builder.Label(loopLabel);
        if (!conditionIsConstant)
        {
            if (longWhileLoopIds.Contains(whileLoopId))
            {
                EmitConditionFalseJumpToFarLabel(whileSyntax.Condition, endLabel, $"while_condition_false_{whileLoopId}");
            }
            else
            {
                EmitConditionFalseJump(whileSyntax.Condition, endLabel);
            }
        }

        loopTargets.Push(new LoopTarget(endLabel, loopLabel));
        try
        {
            EmitBlock(whileSyntax.Body);
        }
        finally
        {
            loopTargets.Pop();
        }

        builder.JumpAbsolute(loopLabel);
        builder.Label(endLabel);
    }

    private void EmitDoWhile(DoWhileSyntax doWhileSyntax)
    {
        var loopLabel = builder.CreateLabel("do");
        var continueLabel = builder.CreateLabel("do_continue");
        var endLabel = builder.CreateLabel("do_end");

        builder.Label(loopLabel);
        loopTargets.Push(new LoopTarget(endLabel, continueLabel));
        try
        {
            EmitBlock(doWhileSyntax.Body);
        }
        finally
        {
            loopTargets.Pop();
        }

        builder.Label(continueLabel);
        EmitConditionFalseJump(doWhileSyntax.Condition, endLabel);
        builder.JumpAbsolute(loopLabel);
        builder.Label(endLabel);
    }

    private void EmitFor(ForSyntax forSyntax)
    {
        if (forSyntax.Initializer.HasValue)
        {
            EmitStatement(forSyntax.Initializer.Value);
        }

        var forLoopId = nextForLoopId++;
        var loopLabel = $"for_{forLoopId}";
        var continueLabel = $"for_continue_{forLoopId}";
        var endLabel = $"for_end_{forLoopId}";

        builder.Label(loopLabel);
        if (forSyntax.Condition.HasValue)
        {
            if (longForLoopIds.Contains(forLoopId))
            {
                EmitConditionFalseJumpToFarLabel(forSyntax.Condition.Value, endLabel, $"for_condition_false_{forLoopId}");
            }
            else
            {
                EmitConditionFalseJump(forSyntax.Condition.Value, endLabel);
            }
        }

        loopTargets.Push(new LoopTarget(endLabel, continueLabel));
        try
        {
            EmitBlock(forSyntax.Body);
        }
        finally
        {
            loopTargets.Pop();
        }

        builder.Label(continueLabel);
        if (forSyntax.Increment.HasValue)
        {
            if (forSyntax.Increment.Value is not AssignmentSyntax increment)
            {
                throw new InvalidOperationException($"Unsupported NES for increment '{forSyntax.Increment.Value.GetType().Name}'.");
            }

            EmitAssignment(increment);
        }

        builder.JumpAbsolute(loopLabel);
        builder.Label(endLabel);
    }

    private void EmitConditionFalseJumpToFarLabel(ExpressionSyntax condition, string targetLabel, string prefix)
    {
        var trampolineLabel = builder.CreateLabel(prefix);
        var continueLabel = builder.CreateLabel($"{prefix}_continue");
        EmitConditionFalseJump(condition, trampolineLabel);
        builder.JumpAbsolute(continueLabel);
        builder.Label(trampolineLabel);
        builder.JumpAbsolute(targetLabel);
        builder.Label(continueLabel);
    }

    private bool EmitIf(IfElseSyntax ifElseSyntax)
    {
        var trueLabel = builder.CreateLabel("if_true");
        var falseTrampolineLabel = builder.CreateLabel("if_false_trampoline");
        var falseLabel = builder.CreateLabel("if_false");
        var endLabel = builder.CreateLabel("if_end");

        EmitConditionFalseJump(ifElseSyntax.Condition, falseTrampolineLabel);
        builder.JumpAbsolute(trueLabel);
        builder.Label(falseTrampolineLabel);
        builder.JumpAbsolute(falseLabel);
        builder.Label(trueLabel);
        var thenTerminates = EmitBlock(ifElseSyntax.ThenBlock);
        if (ifElseSyntax.ElseBlock.HasValue)
        {
            if (!thenTerminates)
            {
                builder.JumpAbsolute(endLabel);
            }

            builder.Label(falseLabel);
            var elseTerminates = EmitBlock(ifElseSyntax.ElseBlock.Value);
            builder.Label(endLabel);
            return thenTerminates && elseTerminates;
        }

        builder.Label(falseLabel);
        return false;
    }

    private void EmitBreak()
    {
        if (loopTargets.Count == 0)
        {
            throw new InvalidOperationException("break can only be used inside a loop.");
        }

        builder.JumpAbsolute(loopTargets.Peek().BreakLabel);
    }

    private void EmitContinue()
    {
        if (loopTargets.Count == 0)
        {
            throw new InvalidOperationException("continue can only be used inside a loop.");
        }

        builder.JumpAbsolute(loopTargets.Peek().ContinueLabel);
    }

    private void EmitConditionFalseJump(ExpressionSyntax condition, string falseLabel)
    {
        if (TryConst(condition, out var constant))
        {
            if (constant == 0)
            {
                builder.JumpAbsolute(falseLabel);
            }

            return;
        }

        if (condition is BinaryExpressionSyntax binary)
        {
            switch (binary.Operator.Symbol)
            {
                case "&&":
                    EmitConditionFalseJump(binary.Left, falseLabel);
                    EmitConditionFalseJump(binary.Right, falseLabel);
                    return;
                case "||":
                    var trueLabel = builder.CreateLabel("or_true");
                    EmitConditionTrueJump(binary.Left, trueLabel);
                    EmitConditionFalseJump(binary.Right, falseLabel);
                    builder.Label(trueLabel);
                    return;
                case "==":
                    if (IsWordComparison(binary))
                    {
                        EmitWordEqualityFalseJump(binary.Left, binary.Right, falseLabel);
                        return;
                    }

                    EmitCompare(binary.Left, binary.Right);
                    builder.BranchRelative(0xD0, falseLabel); // BNE falseLabel
                    return;
                case "!=":
                    if (IsWordComparison(binary))
                    {
                        EmitWordInequalityFalseJump(binary.Left, binary.Right, falseLabel);
                        return;
                    }

                    EmitCompare(binary.Left, binary.Right);
                    builder.BranchRelative(0xF0, falseLabel); // BEQ falseLabel
                    return;
                case "<":
                case "<=":
                case ">":
                case ">=":
                    if (IsWordComparison(binary))
                    {
                        EmitWordRelationalFalseJump(binary, falseLabel);
                        return;
                    }

                    EmitRelationalFalseJump(binary, falseLabel);
                    return;
            }
        }

        if (condition is UnaryExpressionSyntax { OperatorSymbol: "!" } unary)
        {
            EmitConditionTrueJump(unary.Operand, falseLabel);
            return;
        }

        EmitExpressionToA(condition);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xF0, falseLabel);   // BEQ falseLabel
    }

    private void EmitConditionTrueJump(ExpressionSyntax condition, string trueLabel)
    {
        if (TryConst(condition, out var constant))
        {
            if (constant != 0)
            {
                builder.JumpAbsolute(trueLabel);
            }

            return;
        }

        if (condition is BinaryExpressionSyntax binary)
        {
            switch (binary.Operator.Symbol)
            {
                case "&&":
                    var falseLabel = builder.CreateLabel("and_false");
                    EmitConditionFalseJump(binary.Left, falseLabel);
                    EmitConditionTrueJump(binary.Right, trueLabel);
                    builder.Label(falseLabel);
                    return;
                case "||":
                    EmitConditionTrueJump(binary.Left, trueLabel);
                    EmitConditionTrueJump(binary.Right, trueLabel);
                    return;
                case "==":
                    if (IsWordComparison(binary))
                    {
                        EmitWordEqualityTrueJump(binary.Left, binary.Right, trueLabel);
                        return;
                    }

                    EmitCompare(binary.Left, binary.Right);
                    builder.BranchRelative(0xF0, trueLabel); // BEQ trueLabel
                    return;
                case "!=":
                    if (IsWordComparison(binary))
                    {
                        EmitWordInequalityTrueJump(binary.Left, binary.Right, trueLabel);
                        return;
                    }

                    EmitCompare(binary.Left, binary.Right);
                    builder.BranchRelative(0xD0, trueLabel); // BNE trueLabel
                    return;
            }
        }

        if (condition is UnaryExpressionSyntax { OperatorSymbol: "!" } unary)
        {
            EmitConditionFalseJump(unary.Operand, trueLabel);
            return;
        }

        EmitExpressionToA(condition);
        builder.CompareImmediate(0);
        builder.BranchRelative(0xD0, trueLabel);     // BNE trueLabel
    }

    private void EmitCompare(ExpressionSyntax left, ExpressionSyntax right)
    {
        if (TryConst(right, out var rightConstant))
        {
            EmitExpressionToA(left);
            builder.CompareImmediate(rightConstant);
            return;
        }

        if (TryConst(left, out var leftConstant))
        {
            EmitExpressionToA(right);
            builder.CompareImmediate(leftConstant);
            return;
        }

        EmitVariableOperandsToAAndScratch(left, right);
        builder.CompareZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
    }

    private void EmitVariableOperandsToAAndScratch(ExpressionSyntax left, ExpressionSyntax right)
    {
        EmitExpressionToA(left);
        builder.PushA();
        EmitExpressionToA(right);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        builder.PullA();
    }

    private bool IsWordComparison(BinaryExpressionSyntax binary)
    {
        return IsWordExpression(binary.Left) || IsWordExpression(binary.Right);
    }

    private bool IsWordExpression(ExpressionSyntax expression)
    {
        return TryExpressionStorageType(expression, out var type) && IsWordBackedType(type);
    }

    private void EmitWordEqualityFalseJump(ExpressionSyntax left, ExpressionSyntax right, string falseLabel)
    {
        EmitCompareWordByte(left, right, highByte: true, signedHighByte: false);
        builder.BranchRelative(0xD0, falseLabel); // BNE falseLabel
        EmitCompareWordByte(left, right, highByte: false, signedHighByte: false);
        builder.BranchRelative(0xD0, falseLabel); // BNE falseLabel
    }

    private void EmitWordInequalityFalseJump(ExpressionSyntax left, ExpressionSyntax right, string falseLabel)
    {
        var trueLabel = builder.CreateLabel("word_neq_true");
        EmitCompareWordByte(left, right, highByte: true, signedHighByte: false);
        builder.BranchRelative(0xD0, trueLabel); // BNE trueLabel
        EmitCompareWordByte(left, right, highByte: false, signedHighByte: false);
        builder.BranchRelative(0xD0, trueLabel); // BNE trueLabel
        builder.JumpAbsolute(falseLabel);
        builder.Label(trueLabel);
    }

    private void EmitWordEqualityTrueJump(ExpressionSyntax left, ExpressionSyntax right, string trueLabel)
    {
        var endLabel = builder.CreateLabel("word_eq_end");
        EmitCompareWordByte(left, right, highByte: true, signedHighByte: false);
        builder.BranchRelative(0xD0, endLabel); // BNE endLabel
        EmitCompareWordByte(left, right, highByte: false, signedHighByte: false);
        builder.BranchRelative(0xF0, trueLabel); // BEQ trueLabel
        builder.Label(endLabel);
    }

    private void EmitWordInequalityTrueJump(ExpressionSyntax left, ExpressionSyntax right, string trueLabel)
    {
        EmitCompareWordByte(left, right, highByte: true, signedHighByte: false);
        builder.BranchRelative(0xD0, trueLabel); // BNE trueLabel
        EmitCompareWordByte(left, right, highByte: false, signedHighByte: false);
        builder.BranchRelative(0xD0, trueLabel); // BNE trueLabel
    }

    private void EmitWordRelationalFalseJump(BinaryExpressionSyntax binary, string falseLabel)
    {
        var trueLabel = builder.CreateLabel("word_rel_true");
        var localFalseLabel = builder.CreateLabel("word_rel_false");
        EmitWordRelationalJump(binary, trueLabel, localFalseLabel);
        builder.Label(localFalseLabel);
        builder.JumpAbsolute(falseLabel);
        builder.Label(trueLabel);
    }

    private void EmitWordRelationalJump(BinaryExpressionSyntax binary, string trueLabel, string falseLabel)
    {
        var signed = IsSignedRelationalOperand(binary.Left) || IsSignedRelationalOperand(binary.Right);
        EmitCompareWordByte(binary.Left, binary.Right, highByte: true, signedHighByte: signed);

        switch (binary.Operator.Symbol)
        {
            case "<":
                builder.BranchRelative(0x90, trueLabel);  // BCC trueLabel
                builder.BranchRelative(0xD0, falseLabel); // BNE falseLabel
                EmitCompareWordByte(binary.Left, binary.Right, highByte: false, signedHighByte: false);
                builder.BranchRelative(0x90, trueLabel);  // BCC trueLabel
                builder.JumpAbsolute(falseLabel);
                return;
            case "<=":
                builder.BranchRelative(0x90, trueLabel);  // BCC trueLabel
                builder.BranchRelative(0xD0, falseLabel); // BNE falseLabel
                EmitCompareWordByte(binary.Left, binary.Right, highByte: false, signedHighByte: false);
                builder.BranchRelative(0x90, trueLabel);  // BCC trueLabel
                builder.BranchRelative(0xF0, trueLabel);  // BEQ trueLabel
                builder.JumpAbsolute(falseLabel);
                return;
            case ">":
                builder.BranchRelative(0x90, falseLabel); // BCC falseLabel
                builder.BranchRelative(0xD0, trueLabel);  // BNE trueLabel
                EmitCompareWordByte(binary.Left, binary.Right, highByte: false, signedHighByte: false);
                builder.BranchRelative(0x90, falseLabel); // BCC falseLabel
                builder.BranchRelative(0xF0, falseLabel); // BEQ falseLabel
                builder.JumpAbsolute(trueLabel);
                return;
            case ">=":
                builder.BranchRelative(0x90, falseLabel); // BCC falseLabel
                builder.BranchRelative(0xD0, trueLabel);  // BNE trueLabel
                EmitCompareWordByte(binary.Left, binary.Right, highByte: false, signedHighByte: false);
                builder.BranchRelative(0x90, falseLabel); // BCC falseLabel
                builder.JumpAbsolute(trueLabel);
                return;
            default:
                throw new InvalidOperationException($"Unsupported NES relational operator '{binary.Operator.Symbol}'.");
        }
    }

    private void EmitCompareWordByte(ExpressionSyntax left, ExpressionSyntax right, bool highByte, bool signedHighByte)
    {
        EmitLoadWordByteToA(left, highByte, signedHighByte);
        if (TryConst(right, out var rightConstant))
        {
            var value = WordByte(rightConstant, highByte);
            builder.CompareImmediate(signedHighByte ? value ^ 0x80 : value);
            return;
        }

        builder.PushA();
        EmitLoadWordByteToScratch(right, highByte, signedHighByte);
        builder.PullA();
        builder.CompareZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
    }

    private void EmitLoadWordByteToScratch(ExpressionSyntax expression, bool highByte, bool signedHighByte)
    {
        EmitLoadWordByteToA(expression, highByte, signedHighByte);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
    }

    private void EmitLoadWordByteToA(ExpressionSyntax expression, bool highByte, bool signedHighByte)
    {
        if (TryConst(expression, out var constant))
        {
            var value = WordByte(constant, highByte);
            builder.LoadAImmediate(signedHighByte ? value ^ 0x80 : value);
            return;
        }

        if (TryDirectStorageExpression(expression, out var address, out var type))
        {
            if (!highByte)
            {
                builder.LoadAZeroPage(address);
            }
            else if (IsWordBackedType(type))
            {
                builder.LoadAZeroPage(HighAddress(address));
            }
            else if (type == "i8")
            {
                builder.LoadAZeroPage(address);
                EmitSignExtensionFromA();
            }
            else
            {
                builder.LoadAImmediate(0);
            }

            if (signedHighByte)
            {
                builder.XorImmediate(0x80);
            }

            return;
        }

        EmitWordExpressionToStorage(expression, NesRuntimeMemoryLayout.Runtime.IndexScratch, WordExpressionType(expression));
        builder.LoadAZeroPage(highByte ? NesRuntimeMemoryLayout.Runtime.ExpressionScratch : NesRuntimeMemoryLayout.Runtime.IndexScratch);
        if (signedHighByte)
        {
            builder.XorImmediate(0x80);
        }
    }

    private static int WordByte(int value, bool highByte)
    {
        return highByte ? (value >> 8) & 0xFF : value & 0xFF;
    }

    private void EmitRelationalFalseJump(BinaryExpressionSyntax binary, string falseLabel)
    {
        var signed = IsSignedRelationalOperand(binary.Left) || IsSignedRelationalOperand(binary.Right);

        if (TryConst(binary.Right, out var rightConstant))
        {
            EmitExpressionToA(binary.Left);
            if (signed)
            {
                builder.XorImmediate(0x80);
            }

            builder.CompareImmediate(signed ? (rightConstant & 0xFF) ^ 0x80 : rightConstant);
            EmitRelationalFalseJump(binary.Operator.Symbol, falseLabel);
            return;
        }

        if (TryConst(binary.Left, out var leftConstant))
        {
            EmitExpressionToA(binary.Right);
            if (signed)
            {
                builder.XorImmediate(0x80);
            }

            builder.CompareImmediate(signed ? (leftConstant & 0xFF) ^ 0x80 : leftConstant);
            EmitRelationalFalseJump(FlipRelationalOperator(binary.Operator.Symbol), falseLabel);
            return;
        }

        EmitRelationalCompare(binary.Left, binary.Right, signed);
        EmitRelationalFalseJump(binary.Operator.Symbol, falseLabel);
    }

    // Signed relational comparison reuses the unsigned CMP path after flipping both operands' sign bit
    // (EOR #$80), which maps signed [-128,127] onto unsigned [0,255] while preserving order.
    private void EmitRelationalCompare(ExpressionSyntax left, ExpressionSyntax right, bool signed)
    {
        if (!signed)
        {
            EmitCompare(left, right);
            return;
        }

        EmitExpressionToA(left);
        builder.XorImmediate(0x80);
        builder.PushA();
        EmitExpressionToA(right);
        builder.XorImmediate(0x80);
        builder.StoreAZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
        builder.PullA();
        builder.CompareZeroPage(NesRuntimeMemoryLayout.Runtime.ExpressionScratch);
    }

    private void EmitRelationalFalseJump(string op, string falseLabel)
    {
        switch (op)
        {
            case "<":
                builder.BranchRelative(0xB0, falseLabel); // BCS falseLabel
                return;
            case "<=":
                var trueLabel = builder.CreateLabel("rel_true");
                builder.BranchRelative(0x90, trueLabel);  // BCC trueLabel
                builder.BranchRelative(0xF0, trueLabel);  // BEQ trueLabel
                builder.JumpAbsolute(falseLabel);
                builder.Label(trueLabel);
                return;
            case ">":
                builder.BranchRelative(0x90, falseLabel); // BCC falseLabel
                builder.BranchRelative(0xF0, falseLabel); // BEQ falseLabel
                return;
            case ">=":
                builder.BranchRelative(0x90, falseLabel); // BCC falseLabel
                return;
            default:
                throw new InvalidOperationException($"Unsupported NES relational operator '{op}'.");
        }
    }

    private static string FlipRelationalOperator(string op)
    {
        return op switch
        {
            "<" => ">",
            "<=" => ">=",
            ">" => "<",
            ">=" => "<=",
            _ => throw new InvalidOperationException($"Unsupported NES relational operator '{op}'."),
        };
    }

    private void EmitSdkOperation<TOperation>(string callName)
        where TOperation : Sdk2DOperation
    {
        sdkOperationLowerer.Emit(sdkOperations.ConsumeOperation<TOperation>(callName));
    }

}
