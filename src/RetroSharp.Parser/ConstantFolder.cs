using System.Globalization;
using RetroSharp.Core;

namespace RetroSharp.Parser;

public static class ConstantFolder
{
    public static ExpressionSyntax FoldConstants(ExpressionSyntax expression)
    {
        return FoldExpression(
            expression,
            new Dictionary<string, string>(StringComparer.Ordinal),
            BuildTypeSizeTable([], []),
            new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal),
            new Dictionary<string, int>(StringComparer.Ordinal));
    }

    public static ProgramSyntax Fold(ProgramSyntax program)
    {
        program = TypeAliasResolver.Resolve(program);
        program = StaticClassLowerer.LowerStaticCalls(program, DeclaredStaticMethodIndex.Build(program));
        var typeSizes = BuildTypeSizeTable(program.Enums, program.Structs);
        var fieldOffsets = BuildFieldOffsetTable(program.Structs, typeSizes);
        var constants = BuildConstantTable(program.Constants, program.Enums, typeSizes, fieldOffsets);
        var functionIndex = program.Functions.ToDictionary(function => function.Name, StringComparer.Ordinal);
        var functions = program.Functions.Select(function => new FunctionSyntax(
            function.Type,
            function.Name,
            function.Parameters
                .Select(parameter => new ParameterSyntax(
                    parameter.Type,
                    parameter.Name,
                    parameter.DefaultValue.Map(expression => FoldExpression(expression, constants, typeSizes, fieldOffsets, new Dictionary<string, int>(StringComparer.Ordinal))),
                    parameter.IsReceiver))
                .ToList(),
            FoldBlock(function.Block, constants, typeSizes, fieldOffsets, new Dictionary<string, int>(StringComparer.Ordinal), functionIndex.Values),
            function.IsExpressionBodied,
            function.IsInline,
            function.IsPure,
            function.IsExtern,
            function.Attributes)).ToList();

        return new ProgramSyntax(program.Imports, program.TypeAliases, program.Constants, program.Enums, program.Structs, functions);
    }

    private static IReadOnlyDictionary<string, int> BuildTypeSizeTable(IEnumerable<EnumSyntax> enums, IEnumerable<StructSyntax> structs)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["i8"] = 1,
            ["u8"] = 1,
            ["bool"] = 1,
            ["i16"] = 2,
            ["u16"] = 2,
        };

        foreach (var enumSyntax in enums)
        {
            result[enumSyntax.Name] = 1;
        }

        foreach (var structSyntax in structs)
        {
            var size = structSyntax.Fields.Sum(field => SizeOfType(field.Type, result, structSyntax.Name));
            result[structSyntax.Name] = size;
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> BuildFieldOffsetTable(
        IEnumerable<StructSyntax> structs,
        IReadOnlyDictionary<string, int> typeSizes)
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, int>>(StringComparer.Ordinal);
        foreach (var structSyntax in structs)
        {
            var offset = 0;
            var fields = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var field in structSyntax.Fields)
            {
                fields[field.Name] = offset;
                offset += SizeOfType(field.Type, typeSizes, structSyntax.Name);
            }

            result[structSyntax.Name] = fields;
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> BuildConstantTable(
        IEnumerable<ConstDeclarationSyntax> constants,
        IEnumerable<EnumSyntax> enums,
        IReadOnlyDictionary<string, int> typeSizes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var arrays = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var constant in constants)
        {
            AddConstant(result, constant, typeSizes, fieldOffsets, arrays);
        }

        foreach (var enumSyntax in enums)
        {
            AddEnumMembers(enumSyntax, result, typeSizes, fieldOffsets, arrays);
        }

        return result;
    }

    private static void AddConstant(
        Dictionary<string, string> constants,
        ConstDeclarationSyntax constant,
        IReadOnlyDictionary<string, int> typeSizes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets,
        IReadOnlyDictionary<string, int> arrays)
    {
        if (!constants.TryAdd(constant.Name, ConstantText(constant.Value, constants, typeSizes, fieldOffsets, arrays, constant.Name)))
        {
            throw new InvalidOperationException($"Constant '{constant.Name}' is already declared.");
        }
    }

    private static void AddEnumMembers(
        EnumSyntax enumSyntax,
        Dictionary<string, string> constants,
        IReadOnlyDictionary<string, int> typeSizes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets,
        IReadOnlyDictionary<string, int> arrays)
    {
        var nextValue = 0;
        foreach (var member in enumSyntax.Members)
        {
            var qualifiedName = $"{enumSyntax.Name}.{member.Name}";
            var value = member.Value.HasValue
                ? ConstantInt(member.Value.Value, constants, typeSizes, fieldOffsets, arrays, qualifiedName)
                : nextValue;

            if (!constants.TryAdd(qualifiedName, value.ToString(CultureInfo.InvariantCulture)))
            {
                throw new InvalidOperationException($"Enum member '{qualifiedName}' is already declared.");
            }

            nextValue = checked(value + 1);
        }
    }

    private static string ConstantText(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, string> constants,
        IReadOnlyDictionary<string, int> typeSizes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets,
        IReadOnlyDictionary<string, int> arrays,
        string name)
    {
        return expression switch
        {
            ConstantSyntax constant => ConstantLiteralText(constant.Value, name),
            IdentifierSyntax { Identifier: "true" } => "1",
            IdentifierSyntax { Identifier: "false" } => "0",
            IdentifierSyntax identifier when constants.TryGetValue(identifier.Identifier, out var value) => value,
            MemberAccessSyntax memberAccess when constants.TryGetValue(MemberAccessName(memberAccess), out var value) => value,
            BinaryExpressionSyntax binary => ConstantBinary(binary, constants, typeSizes, fieldOffsets, arrays, name).ToString(CultureInfo.InvariantCulture),
            ConditionalExpressionSyntax conditional => ConstantConditional(conditional, constants, typeSizes, fieldOffsets, arrays, name).ToString(CultureInfo.InvariantCulture),
            UnaryExpressionSyntax unary => ConstantUnary(unary, constants, typeSizes, fieldOffsets, arrays, name).ToString(CultureInfo.InvariantCulture),
            SizeOfSyntax sizeOf => SizeOfType(sizeOf.Type, typeSizes, name).ToString(CultureInfo.InvariantCulture),
            OffsetOfSyntax offsetOf => OffsetOfField(offsetOf, fieldOffsets, name).ToString(CultureInfo.InvariantCulture),
            CountOfSyntax countOf => CountOfArray(countOf, arrays, name).ToString(CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Constant '{name}' must be initialized with a literal, earlier constant, or constant integer expression."),
        };
    }

    private static string ConstantLiteralText(object value, string name)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture)
                   ?? throw new InvalidOperationException($"Constant '{name}' has no value.");
        return text switch
        {
            "true" => "1",
            "false" => "0",
            _ => text,
        };
    }

    private static int ConstantInt(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, string> constants,
        IReadOnlyDictionary<string, int> typeSizes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets,
        IReadOnlyDictionary<string, int> arrays,
        string name)
    {
        var text = ConstantText(expression, constants, typeSizes, fieldOffsets, arrays, name);
        if (!IntegerLiteral.TryParse(text, out var value))
        {
            throw new InvalidOperationException($"Constant '{name}' must be initialized with an integer or boolean literal, earlier constant, or constant integer expression.");
        }

        return value;
    }

    private static int ConstantConditional(
        ConditionalExpressionSyntax conditional,
        IReadOnlyDictionary<string, string> constants,
        IReadOnlyDictionary<string, int> typeSizes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets,
        IReadOnlyDictionary<string, int> arrays,
        string name)
    {
        var condition = ConstantInt(conditional.Condition, constants, typeSizes, fieldOffsets, arrays, name);
        var selected = condition != 0 ? conditional.WhenTrue : conditional.WhenFalse;
        return ConstantInt(selected, constants, typeSizes, fieldOffsets, arrays, name);
    }

    private static int ConstantBinary(
        BinaryExpressionSyntax binary,
        IReadOnlyDictionary<string, string> constants,
        IReadOnlyDictionary<string, int> typeSizes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets,
        IReadOnlyDictionary<string, int> arrays,
        string name)
    {
        var left = ConstantInt(binary.Left, constants, typeSizes, fieldOffsets, arrays, name);
        var right = ConstantInt(binary.Right, constants, typeSizes, fieldOffsets, arrays, name);
        return binary.Operator.Symbol switch
        {
            "+" => checked(left + right),
            "-" => checked(left - right),
            "*" => checked(left * right),
            "/" when right != 0 => left / right,
            "/" => throw new InvalidOperationException($"Constant '{name}' divides by zero."),
            "%" when right != 0 => left % right,
            "%" => throw new InvalidOperationException($"Constant '{name}' divides by zero."),
            "<<" => left << right,
            ">>" => left >> right,
            "&" => left & right,
            "^" => left ^ right,
            "|" => left | right,
            _ => throw new InvalidOperationException($"Constant '{name}' uses unsupported constant operator '{binary.Operator.Symbol}'."),
        };
    }

    private static int ConstantUnary(
        UnaryExpressionSyntax unary,
        IReadOnlyDictionary<string, string> constants,
        IReadOnlyDictionary<string, int> typeSizes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets,
        IReadOnlyDictionary<string, int> arrays,
        string name)
    {
        var operand = ConstantInt(unary.Operand, constants, typeSizes, fieldOffsets, arrays, name);
        return unary.OperatorSymbol switch
        {
            "+" => operand,
            "-" => checked(-operand),
            "~" => ~operand,
            _ => throw new InvalidOperationException($"Constant '{name}' uses unsupported constant operator '{unary.OperatorSymbol}'."),
        };
    }

    private static int SizeOfType(string type, IReadOnlyDictionary<string, int> typeSizes, string context)
    {
        if (type.StartsWith("ptr<", StringComparison.Ordinal) && type.EndsWith(">", StringComparison.Ordinal))
        {
            return 2;
        }

        if (typeSizes.TryGetValue(type, out var size))
        {
            return size;
        }

        throw new InvalidOperationException($"Cannot compute sizeof({type}) for '{context}'.");
    }

    private static int OffsetOfField(
        OffsetOfSyntax offsetOf,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets,
        string context)
    {
        if (!fieldOffsets.TryGetValue(offsetOf.Type, out var fields))
        {
            throw new InvalidOperationException($"Cannot compute offsetof({offsetOf.Type}, {offsetOf.Field}) for '{context}'.");
        }

        if (!fields.TryGetValue(offsetOf.Field, out var offset))
        {
            throw new InvalidOperationException($"Type '{offsetOf.Type}' does not contain field '{offsetOf.Field}' for '{context}'.");
        }

        return offset;
    }

    private static int CountOfArray(
        CountOfSyntax countOf,
        IReadOnlyDictionary<string, int> arrays,
        string context)
    {
        if (arrays.TryGetValue(countOf.BaseIdentifier, out var length))
        {
            return length;
        }

        throw new InvalidOperationException($"Cannot compute countof({countOf.BaseIdentifier}) for '{context}'.");
    }

    private static BlockSyntax FoldBlock(
        BlockSyntax block,
        IReadOnlyDictionary<string, string> constants,
        IReadOnlyDictionary<string, int> typeSizes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets,
        IReadOnlyDictionary<string, int> arrays)
    {
        return FoldBlock(block, constants, typeSizes, fieldOffsets, arrays, []);
    }

    private static BlockSyntax FoldBlock(
        BlockSyntax block,
        IReadOnlyDictionary<string, string> constants,
        IReadOnlyDictionary<string, int> typeSizes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets,
        IReadOnlyDictionary<string, int> arrays,
        IEnumerable<FunctionSyntax> functions)
    {
        var scopedConstants = new Dictionary<string, string>(constants, StringComparer.Ordinal);
        var scopedArrays = new Dictionary<string, int>(arrays, StringComparer.Ordinal);
        var statements = new List<StatementSyntax>();
        foreach (var statement in block.Statements)
        {
            if (statement is ConstDeclarationSyntax localConstant)
            {
                AddConstant(scopedConstants, localConstant, typeSizes, fieldOffsets, scopedArrays);
                scopedArrays.Remove(localConstant.Name);
                continue;
            }

            var folded = FoldStatement(statement, scopedConstants, typeSizes, fieldOffsets, scopedArrays, functions);
            RegisterDeclaration(folded, scopedConstants, typeSizes, fieldOffsets, scopedArrays);
            statements.Add(folded);
        }

        return new BlockSyntax(statements);
    }

    private static void RegisterDeclaration(
        StatementSyntax statement,
        Dictionary<string, string> constants,
        IReadOnlyDictionary<string, int> typeSizes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets,
        Dictionary<string, int> arrays)
    {
        if (statement is not DeclarationSyntax declaration)
        {
            return;
        }

        if (!declaration.ArrayLength.HasValue)
        {
            constants.Remove(declaration.Name);
            arrays.Remove(declaration.Name);
            return;
        }

        var length = ConstantInt(
            declaration.ArrayLength.Value,
            constants,
            typeSizes,
            fieldOffsets,
            arrays,
            $"{declaration.Name} array length");
        constants.Remove(declaration.Name);
        arrays[declaration.Name] = length;
    }

    private static StatementSyntax FoldStatement(
        StatementSyntax statement,
        IReadOnlyDictionary<string, string> constants,
        IReadOnlyDictionary<string, int> typeSizes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets,
        IReadOnlyDictionary<string, int> arrays)
    {
        return FoldStatement(statement, constants, typeSizes, fieldOffsets, arrays, []);
    }

    private static StatementSyntax FoldStatement(
        StatementSyntax statement,
        IReadOnlyDictionary<string, string> constants,
        IReadOnlyDictionary<string, int> typeSizes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets,
        IReadOnlyDictionary<string, int> arrays,
        IEnumerable<FunctionSyntax> functions)
    {
        return statement switch
        {
            DeclarationSyntax declaration => new DeclarationSyntax(
                declaration.Type,
                declaration.Name,
                declaration.ArrayLength.Map(expression => FoldExpression(expression, constants, typeSizes, fieldOffsets, arrays, functions)),
                declaration.Initialization.Map(expression => FoldExpression(expression, constants, typeSizes, fieldOffsets, arrays, functions)),
                declaration.IsImmutable),
            ExpressionStatementSyntax expressionStatement => new ExpressionStatementSyntax(FoldExpression(expressionStatement.Expression, constants, typeSizes, fieldOffsets, arrays, functions)),
            IfElseSyntax ifElse => new IfElseSyntax(
                FoldExpression(ifElse.Condition, constants, typeSizes, fieldOffsets, arrays, functions),
                FoldBlock(ifElse.ThenBlock, constants, typeSizes, fieldOffsets, arrays, functions),
                ifElse.ElseBlock.Map(block => FoldBlock(block, constants, typeSizes, fieldOffsets, arrays, functions))),
            WhileSyntax whileSyntax => new WhileSyntax(FoldExpression(whileSyntax.Condition, constants, typeSizes, fieldOffsets, arrays, functions), FoldBlock(whileSyntax.Body, constants, typeSizes, fieldOffsets, arrays, functions)),
            DoWhileSyntax doWhileSyntax => new DoWhileSyntax(
                FoldBlock(doWhileSyntax.Body, constants, typeSizes, fieldOffsets, arrays, functions),
                FoldExpression(doWhileSyntax.Condition, constants, typeSizes, fieldOffsets, arrays, functions)),
            LoopSyntax loopSyntax => new LoopSyntax(FoldBlock(loopSyntax.Body, constants, typeSizes, fieldOffsets, arrays, functions)),
            RangeForSyntax rangeForSyntax => FoldFor(RangeForLowerer.Lower(rangeForSyntax), constants, typeSizes, fieldOffsets, arrays, functions),
            ForSyntax forSyntax => FoldFor(forSyntax, constants, typeSizes, fieldOffsets, arrays, functions),
            SwitchSyntax switchSyntax => FoldSwitch(switchSyntax, constants, typeSizes, fieldOffsets, arrays, functions),
            ReturnSyntax returnSyntax => new ReturnSyntax(returnSyntax.Expression.Map(expression => FoldExpression(expression, constants, typeSizes, fieldOffsets, arrays, functions))),
            _ => statement,
        };
    }

    private static IfElseSyntax FoldSwitch(
        SwitchSyntax switchSyntax,
        IReadOnlyDictionary<string, string> constants,
        IReadOnlyDictionary<string, int> typeSizes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets,
        IReadOnlyDictionary<string, int> arrays)
    {
        return FoldSwitch(switchSyntax, constants, typeSizes, fieldOffsets, arrays, []);
    }

    private static IfElseSyntax FoldSwitch(
        SwitchSyntax switchSyntax,
        IReadOnlyDictionary<string, string> constants,
        IReadOnlyDictionary<string, int> typeSizes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets,
        IReadOnlyDictionary<string, int> arrays,
        IEnumerable<FunctionSyntax> functions)
    {
        var foldedSwitch = new SwitchSyntax(
            FoldExpression(switchSyntax.Subject, constants, typeSizes, fieldOffsets, arrays, functions),
            switchSyntax.Cases
                .Select(switchCase => new SwitchCaseSyntax(
                    switchCase.Patterns
                        .Select(pattern => FoldSwitchCasePattern(pattern, constants, typeSizes, fieldOffsets, arrays))
                        .ToList(),
                    FoldBlock(switchCase.Block, constants, typeSizes, fieldOffsets, arrays, functions)))
                .ToList(),
            switchSyntax.DefaultBlock.Map(block => FoldBlock(block, constants, typeSizes, fieldOffsets, arrays, functions)));

        return SwitchLowerer.Lower(foldedSwitch);
    }

    private static SwitchCasePatternSyntax FoldSwitchCasePattern(
        SwitchCasePatternSyntax pattern,
        IReadOnlyDictionary<string, string> constants,
        IReadOnlyDictionary<string, int> typeSizes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets,
        IReadOnlyDictionary<string, int> arrays)
    {
        var start = FoldExpression(pattern.Start, constants, typeSizes, fieldOffsets, arrays);
        return pattern.End.Match(
            end => new SwitchCasePatternSyntax(start, FoldExpression(end, constants, typeSizes, fieldOffsets, arrays)),
            () => new SwitchCasePatternSyntax(start));
    }

    private static ForSyntax FoldFor(
        ForSyntax forSyntax,
        IReadOnlyDictionary<string, string> constants,
        IReadOnlyDictionary<string, int> typeSizes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets,
        IReadOnlyDictionary<string, int> arrays)
    {
        return FoldFor(forSyntax, constants, typeSizes, fieldOffsets, arrays, []);
    }

    private static ForSyntax FoldFor(
        ForSyntax forSyntax,
        IReadOnlyDictionary<string, string> constants,
        IReadOnlyDictionary<string, int> typeSizes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets,
        IReadOnlyDictionary<string, int> arrays,
        IEnumerable<FunctionSyntax> functions)
    {
        var scopedConstants = new Dictionary<string, string>(constants, StringComparer.Ordinal);
        var scopedArrays = new Dictionary<string, int>(arrays, StringComparer.Ordinal);
        var initializer = forSyntax.Initializer.Map(init =>
        {
            var folded = FoldStatement(init, scopedConstants, typeSizes, fieldOffsets, scopedArrays, functions);
            RegisterDeclaration(folded, scopedConstants, typeSizes, fieldOffsets, scopedArrays);
            return folded;
        });

        return new ForSyntax(
            initializer,
            forSyntax.Condition.Map(condition => FoldExpression(condition, scopedConstants, typeSizes, fieldOffsets, scopedArrays, functions)),
            forSyntax.Increment.Map(increment => FoldExpression(increment, scopedConstants, typeSizes, fieldOffsets, scopedArrays, functions)),
            FoldBlock(forSyntax.Body, scopedConstants, typeSizes, fieldOffsets, scopedArrays, functions));
    }

    private static ExpressionSyntax FoldExpression(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, string> constants,
        IReadOnlyDictionary<string, int> typeSizes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets,
        IReadOnlyDictionary<string, int> arrays)
    {
        return FoldExpression(expression, constants, typeSizes, fieldOffsets, arrays, []);
    }

    private static ExpressionSyntax FoldExpression(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, string> constants,
        IReadOnlyDictionary<string, int> typeSizes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets,
        IReadOnlyDictionary<string, int> arrays,
        IEnumerable<FunctionSyntax> functions)
    {
        return expression switch
        {
            IdentifierSyntax identifier when constants.TryGetValue(identifier.Identifier, out var value) => new ConstantSyntax(value),
            MemberAccessSyntax memberAccess when constants.TryGetValue(MemberAccessName(memberAccess), out var value) => new ConstantSyntax(value),
            SizeOfSyntax sizeOf => new ConstantSyntax(SizeOfType(sizeOf.Type, typeSizes, sizeOf.Type).ToString(CultureInfo.InvariantCulture)),
            OffsetOfSyntax offsetOf => new ConstantSyntax(OffsetOfField(offsetOf, fieldOffsets, offsetOf.Type).ToString(CultureInfo.InvariantCulture)),
            CountOfSyntax countOf => new ConstantSyntax(CountOfArray(countOf, arrays, countOf.BaseIdentifier).ToString(CultureInfo.InvariantCulture)),
            AssignmentSyntax assignment => new AssignmentSyntax(FoldLValue(assignment.Left, constants, typeSizes, fieldOffsets, arrays), assignment.OperatorSymbol, FoldExpression(assignment.Right, constants, typeSizes, fieldOffsets, arrays, functions)),
            PostfixMutationSyntax postfix => FoldExpression(postfix.ToAssignment(), constants, typeSizes, fieldOffsets, arrays, functions),
            BinaryExpressionSyntax binary => FoldConstantExpression(
                new BinaryExpressionSyntax(
                    FoldExpression(binary.Left, constants, typeSizes, fieldOffsets, arrays, functions),
                    FoldExpression(binary.Right, constants, typeSizes, fieldOffsets, arrays, functions),
                    binary.Operator),
                constants,
                typeSizes,
                fieldOffsets,
                arrays),
            ConditionalExpressionSyntax conditional => FoldConditionalExpression(conditional, constants, typeSizes, fieldOffsets, arrays, functions),
            SwitchExpressionSyntax switchExpression => FoldExpression(
                SwitchExpressionLowerer.Lower(new SwitchExpressionSyntax(
                    FoldExpression(switchExpression.Subject, constants, typeSizes, fieldOffsets, arrays, functions),
                    switchExpression.Arms
                        .Select(arm => new SwitchExpressionArmSyntax(
                            arm.Patterns.Select(pattern => FoldSwitchCasePattern(pattern, constants, typeSizes, fieldOffsets, arrays)).ToList(),
                            FoldExpression(arm.Value, constants, typeSizes, fieldOffsets, arrays, functions)))
                        .ToList(),
                    switchExpression.DefaultValue.Map(expression => FoldExpression(expression, constants, typeSizes, fieldOffsets, arrays, functions)))),
                constants,
                typeSizes,
                fieldOffsets,
                arrays,
                functions),
            PipelineExpressionSyntax pipeline => FoldExpression(
                PipelineExpressionLowerer.Lower(new PipelineExpressionSyntax(
                    FoldExpression(pipeline.Value, constants, typeSizes, fieldOffsets, arrays, functions),
                    pipeline.Steps
                        .Select(step => new PipelineStepSyntax(
                            step.FunctionName,
                            step.Arguments.Select(argument => FoldExpression(argument, constants, typeSizes, fieldOffsets, arrays, functions))))
                        .ToList())),
                constants,
                typeSizes,
                fieldOffsets,
                arrays,
                functions),
            ArrayInitializerSyntax arrayInitializer => new ArrayInitializerSyntax(arrayInitializer.Elements.Select(element => FoldExpression(element, constants, typeSizes, fieldOffsets, arrays, functions))),
            StructInitializerSyntax structInitializer => new StructInitializerSyntax(structInitializer.Fields.Select(field => new StructFieldInitializerSyntax(field.Name, FoldExpression(field.Expression, constants, typeSizes, fieldOffsets, arrays, functions)))),
            UnaryExpressionSyntax unary => FoldConstantExpression(
                new UnaryExpressionSyntax(unary.OperatorSymbol, FoldExpression(unary.Operand, constants, typeSizes, fieldOffsets, arrays, functions)),
                constants,
                typeSizes,
                fieldOffsets,
                arrays),
            CastSyntax cast => new CastSyntax(cast.Type, FoldExpression(cast.Expression, constants, typeSizes, fieldOffsets, arrays, functions)),
            SdkDotCallSyntax sdkDotCall => FoldExpression(
                LowerDotCall(new SdkDotCallSyntax(
                    sdkDotCall.Module,
                    sdkDotCall.Method,
                    sdkDotCall.Parameters.Select(parameter => FoldExpression(parameter, constants, typeSizes, fieldOffsets, arrays, functions))),
                    functions),
                constants,
                typeSizes,
                fieldOffsets,
                arrays,
                functions),
            FunctionCall call => new FunctionCall(call.Name, call.Parameters.Select(parameter => FoldExpression(parameter, constants, typeSizes, fieldOffsets, arrays, functions))),
            NamedArgumentSyntax namedArgument => new NamedArgumentSyntax(namedArgument.Name, FoldExpression(namedArgument.Expression, constants, typeSizes, fieldOffsets, arrays, functions)),
            MemberAccessSyntax memberAccess => new MemberAccessSyntax(FoldExpression(memberAccess.Target, constants, typeSizes, fieldOffsets, arrays, functions), memberAccess.Member),
            IndexExpressionSyntax indexExpression => new IndexExpressionSyntax(indexExpression.BaseIdentifier, FoldExpression(indexExpression.Index, constants, typeSizes, fieldOffsets, arrays, functions)),
            _ => expression,
        };
    }

    private static ExpressionSyntax FoldConditionalExpression(
        ConditionalExpressionSyntax conditional,
        IReadOnlyDictionary<string, string> constants,
        IReadOnlyDictionary<string, int> typeSizes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets,
        IReadOnlyDictionary<string, int> arrays)
    {
        return FoldConditionalExpression(conditional, constants, typeSizes, fieldOffsets, arrays, []);
    }

    private static ExpressionSyntax FoldConditionalExpression(
        ConditionalExpressionSyntax conditional,
        IReadOnlyDictionary<string, string> constants,
        IReadOnlyDictionary<string, int> typeSizes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets,
        IReadOnlyDictionary<string, int> arrays,
        IEnumerable<FunctionSyntax> functions)
    {
        var foldedCondition = FoldExpression(conditional.Condition, constants, typeSizes, fieldOffsets, arrays, functions);
        var foldedWhenTrue = FoldExpression(conditional.WhenTrue, constants, typeSizes, fieldOffsets, arrays, functions);
        var foldedWhenFalse = FoldExpression(conditional.WhenFalse, constants, typeSizes, fieldOffsets, arrays, functions);

        try
        {
            return ConstantInt(foldedCondition, constants, typeSizes, fieldOffsets, arrays, "conditional expression") != 0
                ? foldedWhenTrue
                : foldedWhenFalse;
        }
        catch (InvalidOperationException)
        {
            return FoldConstantExpression(
                new ConditionalExpressionSyntax(foldedCondition, foldedWhenTrue, foldedWhenFalse),
                constants,
                typeSizes,
                fieldOffsets,
                arrays);
        }
    }

    private static FunctionCall LowerDotCall(SdkDotCallSyntax call, IEnumerable<FunctionSyntax> functions)
    {
        if (ReceiverMethodLowerer.TryLower(call, functions, out var receiverCall))
        {
            return receiverCall;
        }

        throw new InvalidOperationException($"Unknown static or receiver method '{call.Module}.{call.Method}'.");
    }

    private static ExpressionSyntax FoldConstantExpression(
        ExpressionSyntax expression,
        IReadOnlyDictionary<string, string> constants,
        IReadOnlyDictionary<string, int> typeSizes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets,
        IReadOnlyDictionary<string, int> arrays)
    {
        try
        {
            return new ConstantSyntax(ConstantText(expression, constants, typeSizes, fieldOffsets, arrays, "expression"));
        }
        catch (InvalidOperationException)
        {
            return expression;
        }
    }

    private static string MemberAccessName(MemberAccessSyntax memberAccess)
    {
        var parts = new Stack<string>();
        ExpressionSyntax current = memberAccess;
        while (current is MemberAccessSyntax member)
        {
            parts.Push(member.Member);
            current = member.Target;
        }

        if (current is not IdentifierSyntax identifier)
        {
            return string.Empty;
        }

        parts.Push(identifier.Identifier);
        return string.Join(".", parts);
    }

    private static LValue FoldLValue(
        LValue lValue,
        IReadOnlyDictionary<string, string> constants,
        IReadOnlyDictionary<string, int> typeSizes,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> fieldOffsets,
        IReadOnlyDictionary<string, int> arrays)
    {
        return lValue switch
        {
            IndexLValue index => new IndexLValue(index.BaseIdentifier, FoldExpression(index.Index, constants, typeSizes, fieldOffsets, arrays)),
            PointerDerefLValue pointer => new PointerDerefLValue(FoldExpression(pointer.Expression, constants, typeSizes, fieldOffsets, arrays)),
            MemberAccessLValue memberAccess => new MemberAccessLValue((MemberAccessSyntax)FoldExpression(memberAccess.MemberAccess, constants, typeSizes, fieldOffsets, arrays)),
            _ => lValue,
        };
    }
}
