using System.Globalization;
using System.Text;
using RetroSharp.Core;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;
using RetroSharp.Sdk;

namespace RetroSharp.GameBoy;

internal sealed partial class GameBoyRuntimeCompiler
{
    private void EmitWhile(WhileSyntax whileSyntax)
    {
        var startLabel = builder.CreateLabel("while_start");
        var endLabel = builder.CreateLabel("while_end");

        builder.Label(startLabel);
        EmitConditionFalseJump(whileSyntax.Condition, endLabel);
        loopTargets.Push(new LoopTarget(endLabel, startLabel));
        try
        {
            EmitBlock(whileSyntax.Body);
        }
        finally
        {
            loopTargets.Pop();
        }

        builder.JumpAbsolute(startLabel);
        builder.Label(endLabel);
    }

    private void EmitDoWhile(DoWhileSyntax doWhileSyntax)
    {
        var startLabel = builder.CreateLabel("do_start");
        var continueLabel = builder.CreateLabel("do_continue");
        var endLabel = builder.CreateLabel("do_end");

        builder.Label(startLabel);
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
        builder.JumpAbsolute(startLabel);
        builder.Label(endLabel);
    }

    private void EmitFor(ForSyntax forSyntax)
    {
        if (forSyntax.Initializer.HasValue)
        {
            EmitStatement(forSyntax.Initializer.Value);
        }

        var startLabel = builder.CreateLabel("for_start");
        var continueLabel = builder.CreateLabel("for_continue");
        var endLabel = builder.CreateLabel("for_end");

        builder.Label(startLabel);
        if (forSyntax.Condition.HasValue)
        {
            EmitConditionFalseJump(forSyntax.Condition.Value, endLabel);
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
                throw new InvalidOperationException($"Unsupported Game Boy for increment '{forSyntax.Increment.Value.GetType().Name}'.");
            }

            EmitAssignment(increment);
        }

        builder.JumpAbsolute(startLabel);
        builder.Label(endLabel);
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

    private void EmitIf(IfElseSyntax ifElseSyntax)
    {
        var falseLabel = builder.CreateLabel("if_false");
        var endLabel = builder.CreateLabel("if_end");

        EmitConditionFalseJump(ifElseSyntax.Condition, falseLabel);
        EmitBlock(ifElseSyntax.ThenBlock);
        if (ifElseSyntax.ElseBlock.HasValue)
        {
            builder.JumpAbsolute(endLabel);
            builder.Label(falseLabel);
            EmitBlock(ifElseSyntax.ElseBlock.Value);
            builder.Label(endLabel);
        }
        else
        {
            builder.Label(falseLabel);
        }
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
                    builder.JumpAbsolute(0xC2, falseLabel); // JP NZ,falseLabel
                    return;
                case "!=":
                    if (IsWordComparison(binary))
                    {
                        EmitWordInequalityFalseJump(binary.Left, binary.Right, falseLabel);
                        return;
                    }

                    EmitCompare(binary.Left, binary.Right);
                    builder.JumpAbsolute(0xCA, falseLabel); // JP Z,falseLabel
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
        builder.Emit(0xFE, 0x00);                   // CP $00
        builder.JumpAbsolute(0xCA, falseLabel);     // JP Z,falseLabel
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
                    builder.JumpAbsolute(0xCA, trueLabel); // JP Z,trueLabel
                    return;
                case "!=":
                    if (IsWordComparison(binary))
                    {
                        EmitWordInequalityTrueJump(binary.Left, binary.Right, trueLabel);
                        return;
                    }

                    EmitCompare(binary.Left, binary.Right);
                    builder.JumpAbsolute(0xC2, trueLabel); // JP NZ,trueLabel
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
        builder.JumpAbsolute(0xC2, trueLabel);       // JP NZ,trueLabel
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

        EmitVariableOperandsToAAndB(left, right);
        builder.CompareB();
    }

    private void EmitVariableOperandsToAAndB(ExpressionSyntax left, ExpressionSyntax right)
    {
        EmitExpressionToA(left);
        builder.PushAf();
        EmitExpressionToA(right);
        builder.LoadBFromA();
        builder.PopAf();
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
        builder.JumpAbsolute(0xC2, falseLabel); // JP NZ,falseLabel
        EmitCompareWordByte(left, right, highByte: false, signedHighByte: false);
        builder.JumpAbsolute(0xC2, falseLabel); // JP NZ,falseLabel
    }

    private void EmitWordInequalityFalseJump(ExpressionSyntax left, ExpressionSyntax right, string falseLabel)
    {
        var trueLabel = builder.CreateLabel("word_neq_true");
        EmitCompareWordByte(left, right, highByte: true, signedHighByte: false);
        builder.JumpAbsolute(0xC2, trueLabel); // JP NZ,trueLabel
        EmitCompareWordByte(left, right, highByte: false, signedHighByte: false);
        builder.JumpAbsolute(0xC2, trueLabel); // JP NZ,trueLabel
        builder.JumpAbsolute(falseLabel);
        builder.Label(trueLabel);
    }

    private void EmitWordEqualityTrueJump(ExpressionSyntax left, ExpressionSyntax right, string trueLabel)
    {
        var endLabel = builder.CreateLabel("word_eq_end");
        EmitCompareWordByte(left, right, highByte: true, signedHighByte: false);
        builder.JumpAbsolute(0xC2, endLabel); // JP NZ,endLabel
        EmitCompareWordByte(left, right, highByte: false, signedHighByte: false);
        builder.JumpAbsolute(0xCA, trueLabel); // JP Z,trueLabel
        builder.Label(endLabel);
    }

    private void EmitWordInequalityTrueJump(ExpressionSyntax left, ExpressionSyntax right, string trueLabel)
    {
        EmitCompareWordByte(left, right, highByte: true, signedHighByte: false);
        builder.JumpAbsolute(0xC2, trueLabel); // JP NZ,trueLabel
        EmitCompareWordByte(left, right, highByte: false, signedHighByte: false);
        builder.JumpAbsolute(0xC2, trueLabel); // JP NZ,trueLabel
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
                builder.JumpAbsolute(0xDA, trueLabel);  // JP C,trueLabel
                builder.JumpAbsolute(0xC2, falseLabel); // JP NZ,falseLabel
                EmitCompareWordByte(binary.Left, binary.Right, highByte: false, signedHighByte: false);
                builder.JumpAbsolute(0xDA, trueLabel);  // JP C,trueLabel
                builder.JumpAbsolute(falseLabel);
                return;
            case "<=":
                builder.JumpAbsolute(0xDA, trueLabel);  // JP C,trueLabel
                builder.JumpAbsolute(0xC2, falseLabel); // JP NZ,falseLabel
                EmitCompareWordByte(binary.Left, binary.Right, highByte: false, signedHighByte: false);
                builder.JumpAbsolute(0xDA, trueLabel);  // JP C,trueLabel
                builder.JumpAbsolute(0xCA, trueLabel);  // JP Z,trueLabel
                builder.JumpAbsolute(falseLabel);
                return;
            case ">":
                builder.JumpAbsolute(0xDA, falseLabel); // JP C,falseLabel
                builder.JumpAbsolute(0xC2, trueLabel);  // JP NZ,trueLabel
                EmitCompareWordByte(binary.Left, binary.Right, highByte: false, signedHighByte: false);
                builder.JumpAbsolute(0xDA, falseLabel); // JP C,falseLabel
                builder.JumpAbsolute(0xCA, falseLabel); // JP Z,falseLabel
                builder.JumpAbsolute(trueLabel);
                return;
            case ">=":
                builder.JumpAbsolute(0xDA, falseLabel); // JP C,falseLabel
                builder.JumpAbsolute(0xC2, trueLabel);  // JP NZ,trueLabel
                EmitCompareWordByte(binary.Left, binary.Right, highByte: false, signedHighByte: false);
                builder.JumpAbsolute(0xDA, falseLabel); // JP C,falseLabel
                builder.JumpAbsolute(trueLabel);
                return;
            default:
                throw new InvalidOperationException($"Unsupported Game Boy relational operator '{binary.Operator.Symbol}'.");
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

        // Preserve the left operand in A while the right operand is materialized: loading the right
        // byte routes through A, so without this it would clobber the left byte and the comparison
        // would degrade to right-vs-right (always equal), breaking every word variable-vs-variable
        // relational compare. Mirrors the byte path in EmitVariableOperandsToAAndB.
        builder.PushAf();
        EmitLoadWordByteToB(right, highByte, signedHighByte);
        builder.PopAf();
        builder.CompareB();
    }

    private void EmitLoadWordByteToB(ExpressionSyntax expression, bool highByte, bool signedHighByte)
    {
        EmitLoadWordByteToA(expression, highByte, signedHighByte);
        builder.LoadBFromA();
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
                builder.LoadA(address);
            }
            else if (IsWordBackedType(type))
            {
                builder.LoadA(HighAddress(address));
            }
            else if (type == "i8")
            {
                builder.LoadA(address);
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

        EmitWordExpressionToStorage(expression, GameBoyRuntimeMemoryLayout.Runtime.WordScratchLow, WordExpressionType(expression));
        builder.LoadA(highByte ? GameBoyRuntimeMemoryLayout.Runtime.WordScratchHigh : GameBoyRuntimeMemoryLayout.Runtime.WordScratchLow);
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

            builder.CompareImmediate(signed ? rightConstant ^ 0x80 : rightConstant);
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

            builder.CompareImmediate(signed ? leftConstant ^ 0x80 : leftConstant);
            EmitRelationalFalseJump(FlipRelationalOperator(binary.Operator.Symbol), falseLabel);
            return;
        }

        EmitRelationalCompare(binary.Left, binary.Right, signed);
        EmitRelationalFalseJump(binary.Operator.Symbol, falseLabel);
    }

    // Signed relational comparison reuses the unsigned CP path after flipping both operands' sign bit
    // (XOR 0x80), which maps signed [-128,127] onto unsigned [0,255] while preserving order.
    private void EmitRelationalCompare(ExpressionSyntax left, ExpressionSyntax right, bool signed)
    {
        if (!signed)
        {
            EmitCompare(left, right);
            return;
        }

        EmitExpressionToA(left);
        builder.XorImmediate(0x80);
        builder.PushAf();
        EmitExpressionToA(right);
        builder.XorImmediate(0x80);
        builder.LoadBFromA();
        builder.PopAf();
        builder.CompareB();
    }

    private void EmitRelationalFalseJump(string op, string falseLabel)
    {
        switch (op)
        {
            case "<":
                builder.JumpAbsolute(0xD2, falseLabel); // JP NC,falseLabel
                return;
            case "<=":
                EmitGreaterThanFalseJump(falseLabel);
                return;
            case ">":
                builder.JumpAbsolute(0xDA, falseLabel); // JP C,falseLabel
                builder.JumpAbsolute(0xCA, falseLabel); // JP Z,falseLabel
                return;
            case ">=":
                builder.JumpAbsolute(0xDA, falseLabel); // JP C,falseLabel
                return;
            default:
                throw new InvalidOperationException($"Unsupported Game Boy relational operator '{op}'.");
        }
    }

    private void EmitGreaterThanFalseJump(string falseLabel)
    {
        var trueLabel = builder.CreateLabel("rel_true");
        builder.JumpAbsolute(0xDA, trueLabel);      // JP C,trueLabel
        builder.JumpAbsolute(0xCA, trueLabel);      // JP Z,trueLabel
        builder.JumpAbsolute(falseLabel);
        builder.Label(trueLabel);
    }

    private static string FlipRelationalOperator(string op)
    {
        return op switch
        {
            "<" => ">",
            "<=" => ">=",
            ">" => "<",
            ">=" => "<=",
            _ => throw new InvalidOperationException($"Unsupported Game Boy relational operator '{op}'."),
        };
    }
}
