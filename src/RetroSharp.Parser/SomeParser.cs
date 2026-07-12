using System.Xml.Linq;
using RetroSharp.Core;
using Zafiro.Core.Mixins;
using static RetroSharp.Parser.RetroSharpParser;
namespace RetroSharp.Parser;

public class SomeParser
{
    private sealed record LoweredStaticClass(
        StructSyntax Struct,
        IReadOnlyList<ConstDeclarationSyntax> Constants,
        IReadOnlyList<FunctionSyntax> Functions,
        IReadOnlyList<StaticClassMethod> StaticMethods);

    private sealed record LoweredClassFunction(string SourceName, FunctionSyntax Function);

    private sealed record StaticClassMethod(string SourceName, string FunctionName);

    public Result<ProgramSyntax> Parse(string input) => Tokenize(input)
        .Bind(RejectUnsupportedManagedObjectForms)
        .Bind(Parse)
        .Map(ParseProgram);

    private static Result<CommonTokenStream> RejectUnsupportedManagedObjectForms(CommonTokenStream tokenStream)
    {
        tokenStream.Fill();
        var tokens = tokenStream.GetTokens().Where(token => token.Type != TokenConstants.EOF).ToList();
        for (var i = 0; i < tokens.Count; i++)
        {
            var text = tokens[i].Text;
            var diagnostic = text switch
            {
                "virtual" => "Unsupported managed-object form 'virtual': virtual dispatch requires a runtime method table; RetroSharp classes lower to fixed static data and receiver helpers.",
                "override" => "Unsupported managed-object form 'override': overriding requires a runtime method table; RetroSharp classes lower to fixed static data and receiver helpers.",
                "abstract" => "Unsupported managed-object form 'abstract': abstract dispatch requires a runtime method table; RetroSharp classes lower to fixed static data and receiver helpers.",
                "interface" => "Unsupported managed-object form 'interface': interface dispatch requires runtime type tests and method tables; RetroSharp classes lower to fixed static data and receiver helpers.",
                "new" when IsTypeConstruction(tokens, i) => "Unsupported managed-object form 'new': object construction requires heap allocation and object headers; use fixed locals or struct/class initializers instead.",
                "~" when IsDestructor(tokens, i) => "Unsupported managed-object form 'destructor': destructors require runtime lifetime tracking and finalization; RetroSharp classes have no managed object lifetime.",
                "class" when IsClassInheritance(tokens, i) => "Unsupported managed-object form 'class inheritance': inheritance requires object layout metadata and a runtime method table; RetroSharp classes lower to fixed static data and receiver helpers.",
                "is" when IsRuntimeTypeTest(tokens, i) => "Unsupported managed-object form 'is': runtime type tests require RTTI and object headers; RetroSharp classes have no runtime type identity.",
                "as" when IsRuntimeTypeTest(tokens, i) => "Unsupported managed-object form 'as': runtime type tests require RTTI and object headers; RetroSharp classes have no runtime type identity.",
                "typeof" when TokenText(tokens, i + 1) == "(" => "Unsupported managed-object form 'typeof': runtime type identity requires RTTI and object headers; RetroSharp classes have no runtime type identity.",
                "dynamic_cast" => "Unsupported managed-object form 'dynamic_cast': dynamic casts require RTTI and object headers; RetroSharp classes have no runtime type identity.",
                _ => null,
            };

            if (diagnostic is not null)
            {
                return Result.Failure<CommonTokenStream>(diagnostic);
            }
        }

        tokenStream.Seek(0);
        return tokenStream;
    }

    private static bool IsTypeConstruction(IReadOnlyList<IToken> tokens, int index)
    {
        return IsIdentifier(tokens, index + 1) && TokenText(tokens, index + 2) == "(";
    }

    private static bool IsDestructor(IReadOnlyList<IToken> tokens, int index)
    {
        return IsIdentifier(tokens, index + 1) && TokenText(tokens, index + 2) == "(";
    }

    private static bool IsClassInheritance(IReadOnlyList<IToken> tokens, int index)
    {
        var nameIndex = index + 1;
        return IsIdentifier(tokens, nameIndex) && TokenText(tokens, nameIndex + 1) == ":";
    }

    private static bool IsRuntimeTypeTest(IReadOnlyList<IToken> tokens, int index)
    {
        return IsIdentifier(tokens, index - 1) && IsIdentifier(tokens, index + 1);
    }

    private static string? TokenText(IReadOnlyList<IToken> tokens, int index)
    {
        return index >= 0 && index < tokens.Count ? tokens[index].Text : null;
    }

    private static bool IsIdentifier(IReadOnlyList<IToken> tokens, int index)
    {
        var text = TokenText(tokens, index);
        return text is not null && IsIdentifierText(text);
    }

    private static bool IsIdentifierText(string text)
    {
        if (text.Length == 0 || !(char.IsLetter(text[0]) || text[0] == '_'))
        {
            return false;
        }

        return text.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');
    }

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
        var imports = program.importDeclaration().Select(ParseImport);
        var aliases = program.typeAliasDeclaration().Select(ParseTypeAlias);
        var constants = program.constDeclaration().Select(ParseConstDeclaration);
        var enums = program.enumDeclaration().Select(ParseEnum);
        var classes = program.classDeclaration().Select(ParseStaticClass).ToList();
        var structs = program.structDeclaration().Select(ParseStruct);
        var funcs = program.function().Select(f => ParseFunction(f));
        var externs = program.externFunction().Select(ParseExternFunction);
        var staticMethods = classes
            .SelectMany(staticClass => staticClass.StaticMethods)
            .ToDictionary(method => method.SourceName, method => method.FunctionName, StringComparer.Ordinal);
        var syntax = new ProgramSyntax(
            imports.ToList(),
            aliases.ToList(),
            constants.Concat(classes.SelectMany(staticClass => staticClass.Constants)).ToList(),
            enums.ToList(),
            structs.Concat(classes.Select(staticClass => staticClass.Struct)).ToList(),
            classes.SelectMany(staticClass => staticClass.Functions).Concat(externs).Concat(funcs).ToList());
        return StaticClassLowerer.LowerStaticCalls(syntax, staticMethods);
    }

    private ImportSyntax ParseImport(ImportDeclarationContext importContext)
    {
        return new ImportSyntax(importContext.qualifiedIdentifier().GetText());
    }

    private TypeAliasSyntax ParseTypeAlias(TypeAliasDeclarationContext aliasContext)
    {
        return new TypeAliasSyntax(aliasContext.IDENTIFIER().GetText(), aliasContext.type().GetText());
    }

    private ConstDeclarationSyntax ParseConstDeclaration(ConstDeclarationContext constContext)
    {
        Maybe<string> type = constContext.type() is { } typeContext
            ? Maybe.From(typeContext.GetText())
            : Maybe<string>.None;
        var name = constContext.IDENTIFIER().GetText();
        var value = ParseExpression(constContext.expression());
        if (type.HasNoValue && TryInferLiteralSuffixType(value, out var suffixType))
        {
            type = Maybe.From(suffixType);
        }

        return new ConstDeclarationSyntax(
            type,
            name,
            value);
    }

    private EnumSyntax ParseEnum(EnumDeclarationContext enumContext)
    {
        var members = enumContext.enumMember().Select(ParseEnumMember).ToList();
        return new EnumSyntax(enumContext.IDENTIFIER().GetText(), members);
    }

    private EnumMemberSyntax ParseEnumMember(EnumMemberContext enumMemberContext)
    {
        var maybeValue = enumMemberContext.expression() is { } expr
            ? Maybe.From(ParseExpression(expr))
            : Maybe<ExpressionSyntax>.None;
        return new EnumMemberSyntax(enumMemberContext.IDENTIFIER().GetText(), maybeValue);
    }

    private StructSyntax ParseStruct(StructDeclarationContext structContext)
    {
        var fields = structContext.structField().Select(ParseStructField).ToList();
        return new StructSyntax(structContext.IDENTIFIER().GetText(), fields);
    }

    private LoweredStaticClass ParseStaticClass(ClassDeclarationContext classContext)
    {
        var name = classContext.IDENTIFIER().GetText();
        var isStatic = classContext.STATIC() is not null;
        var fields = classContext.classMember()
            .Select(member => member.structField())
            .Where(field => field is not null)
            .Select(field => ParseStructField(field!))
            .ToList();
        var fieldNames = fields.Select(field => field.Name).ToHashSet(StringComparer.Ordinal);

        var constants = classContext.classMember()
            .Select(member => member.classConstDeclaration())
            .Where(constant => constant is not null)
            .Select(constant => ParseClassConst(name, constant!))
            .ToList();
        var parsedInstanceFunctions = classContext.classMember()
            .Select(member => member.classFunction())
            .Where(function => function is not null)
            .Select(function => ParseClassFunction(function!))
            .ToList();
        var instanceMethodNames = parsedInstanceFunctions.Select(function => function.Name).ToHashSet(StringComparer.Ordinal);
        var instanceFunctions = parsedInstanceFunctions
            .Select(function => StaticClassLowerer.LowerInstanceMethod(name, fieldNames, instanceMethodNames, function))
            .ToList();
        var staticFunctions = classContext.classMember()
            .Select(member => member.classStaticFunction())
            .Where(function => function is not null)
            .Select(function => ParseClassStaticFunction(name, function!))
            .ToList();

        if (isStatic && (fields.Count > 0 || parsedInstanceFunctions.Count > 0))
        {
            throw new InvalidOperationException(
                $"Static class '{name}' may only declare const members and static methods, not instance fields or instance methods.");
        }

        return new LoweredStaticClass(
            new StructSyntax(name, fields),
            constants,
            instanceFunctions.Concat(staticFunctions.Select(function => function.Function)).ToList(),
            staticFunctions.Select(function => new StaticClassMethod($"{name}.{function.SourceName}", function.Function.Name)).ToList());
    }

    private ConstDeclarationSyntax ParseClassConst(string className, ClassConstDeclarationContext constContext)
    {
        var constant = ParseConstDeclaration(constContext.constDeclaration());
        return new ConstDeclarationSyntax(constant.TypeAnnotation, $"{className}.{constant.Name}", constant.Value);
    }

    private StructFieldSyntax ParseStructField(StructFieldContext fieldContext)
    {
        return new StructFieldSyntax(fieldContext.type().GetText(), fieldContext.IDENTIFIER().GetText());
    }

    private FunctionSyntax ParseFunction(FunctionContext functionContext)
    {
        var parameters = ParseParameters(functionContext.parameters()).ToList();
        var modifiers = functionContext.functionModifier().Select(modifier => modifier.GetText()).ToHashSet(StringComparer.Ordinal);
        var isInline = modifiers.Contains("inline");
        var isPure = modifiers.Contains("pure");
        var attributes = ParseAttributes(functionContext.attrs());
        if (functionContext.block() is { } block)
        {
            return new FunctionSyntax(
                functionContext.type().GetText(),
                functionContext.IDENTIFIER().ToString()!,
                parameters,
                ParseBlock(block),
                isInline: isInline,
                isPure: isPure,
                attributes: attributes);
        }

        var expressionBody = new BlockSyntax([new ReturnSyntax(Maybe.From(ParseExpression(functionContext.expression())))]);
        return new FunctionSyntax(
            functionContext.type().GetText(),
            functionContext.IDENTIFIER().ToString()!,
            parameters,
            expressionBody,
            true,
            isInline,
            isPure,
            attributes: attributes);
    }

    private FunctionSyntax ParseClassFunction(ClassFunctionContext functionContext)
    {
        var parameters = ParseParameters(functionContext.parameters()).ToList();
        var modifiers = functionContext.functionModifier().Select(modifier => modifier.GetText()).ToHashSet(StringComparer.Ordinal);
        var isInline = modifiers.Contains("inline");
        var isPure = modifiers.Contains("pure");
        var attributes = ParseAttributes(functionContext.attrs());
        if (functionContext.block() is { } block)
        {
            return new FunctionSyntax(
                functionContext.type().GetText(),
                functionContext.IDENTIFIER().ToString()!,
                parameters,
                ParseBlock(block),
                isInline: isInline,
                isPure: isPure,
                attributes: attributes);
        }

        var expressionBody = new BlockSyntax([new ReturnSyntax(Maybe.From(ParseExpression(functionContext.expression())))]);
        return new FunctionSyntax(
            functionContext.type().GetText(),
            functionContext.IDENTIFIER().ToString()!,
            parameters,
            expressionBody,
            true,
            isInline,
            isPure,
            attributes: attributes);
    }

    private LoweredClassFunction ParseClassStaticFunction(string className, ClassStaticFunctionContext functionContext)
    {
        var parameters = ParseParameters(functionContext.parameters()).ToList();
        var modifiers = functionContext.functionModifier().Select(modifier => modifier.GetText()).ToHashSet(StringComparer.Ordinal);
        var isInline = modifiers.Contains("inline");
        var isPure = modifiers.Contains("pure");
        var attributes = ParseAttributes(functionContext.attrs());
        var sourceName = functionContext.IDENTIFIER().ToString()!;
        var functionName = $"{className}_{sourceName}";
        if (functionContext.block() is { } block)
        {
            return new LoweredClassFunction(
                sourceName,
                new FunctionSyntax(
                    functionContext.type().GetText(),
                    functionName,
                    parameters,
                    ParseBlock(block),
                    isInline: isInline,
                    isPure: isPure,
                    attributes: attributes));
        }

        var expressionBody = new BlockSyntax([new ReturnSyntax(Maybe.From(ParseExpression(functionContext.expression())))]);
        return new LoweredClassFunction(
            sourceName,
            new FunctionSyntax(
                functionContext.type().GetText(),
                functionName,
                parameters,
                expressionBody,
                true,
                isInline,
                isPure,
                attributes: attributes));
    }

    private FunctionSyntax ParseExternFunction(ExternFunctionContext functionContext)
    {
        return new FunctionSyntax(
            functionContext.type().GetText(),
            functionContext.IDENTIFIER().ToString()!,
            ParseParameters(functionContext.parameters()).ToList(),
            new BlockSyntax([]),
            isExtern: true,
            attributes: ParseAttributes(functionContext.attrs()));
    }

    private IReadOnlyList<FunctionAttributeSyntax> ParseAttributes(AttrsContext attrs)
    {
        var attributes = new List<FunctionAttributeSyntax>();
        for (var i = 0; i < attrs.ChildCount; i++)
        {
            if (attrs.GetChild(i).GetText() != "[")
            {
                continue;
            }

            var name = attrs.GetChild(i + 1).GetText();
            var arguments = new List<ExpressionSyntax>();
            if (i + 3 < attrs.ChildCount && attrs.GetChild(i + 2).GetText() == "(" && attrs.GetChild(i + 3) is ArgumentsContext args)
            {
                arguments.AddRange(ParseArguments(args));
            }

            attributes.Add(new FunctionAttributeSyntax(name, arguments));
        }

        return attributes;
    }

    private IEnumerable<ParameterSyntax> ParseParameters(ParametersContext parameters)
    {
        if (parameters is { } parameterContexts)
        {
            return parameterContexts.parameter().Select(ParseParameter);
        }

        return Enumerable.Empty<ParameterSyntax>();
    }

    private ParameterSyntax ParseParameter(ParameterContext parameterContext)
    {
        if (parameterContext.receiverParameter() is { } receiver)
        {
            return new ParameterSyntax(
                receiver.type().GetText(),
                receiver.IDENTIFIER().GetText(),
                Maybe<ExpressionSyntax>.None,
                true);
        }

        var defaultValue = parameterContext.expression() is { } defaultExpression
            ? Maybe.From(ParseExpression(defaultExpression))
            : Maybe<ExpressionSyntax>.None;
        return new ParameterSyntax(parameterContext.type().GetText(), parameterContext.IDENTIFIER().GetText(), defaultValue);
    }

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

        if (statementContext.constDeclaration() is { } constDeclaration)
        {
            return ParseConstDeclaration(constDeclaration);
        }

        if (statementContext.letDeclaration() is { } letDeclaration)
        {
            return ParseLetDeclaration(letDeclaration);
        }

        if (statementContext.conditional() is { } conditional)
        {
            return ParseConditional(conditional);
        }

        if (statementContext.whileLoop() is { } whileLoop)
        {
            return ParseWhileLoop(whileLoop);
        }

        if (statementContext.doWhileLoop() is { } doWhileLoop)
        {
            return ParseDoWhileLoop(doWhileLoop);
        }

        if (statementContext.rangeForLoop() is { } rangeForLoop)
        {
            return ParseRangeForLoop(rangeForLoop);
        }

        if (statementContext.forLoop() is { } forLoop)
        {
            return ParseForLoop(forLoop);
        }

        if (statementContext.switchStatement() is { } switchStatement)
        {
            return ParseSwitch(switchStatement);
        }

        if (statementContext.breakStatement() is not null)
        {
            return new BreakSyntax();
        }

        if (statementContext.continueStatement() is not null)
        {
            return new ContinueSyntax();
        }

        if (statementContext.postfixMutation() is { } postfixMutation)
        {
            return new ExpressionStatementSyntax(ParsePostfixMutation(postfixMutation));
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

    private DeclarationSyntax ParseLetDeclaration(LetDeclarationContext letDeclaration)
    {
        var expression = ParseExpression(letDeclaration.expression());
        var type = TryInferLiteralSuffixType(expression, out var suffixType)
            ? suffixType
            : IsLiteralExpression(expression)
                ? "u8"
                : LetTypeInference.UnresolvedTypeName;
        return new DeclarationSyntax(
            type,
            letDeclaration.IDENTIFIER().GetText(),
            Maybe<ExpressionSyntax>.None,
            Maybe.From(expression),
            true);
    }

    // Makes integer-literal width suffixes load-bearing: an unannotated `let` or
    // `const` initialized with a single suffixed integer literal (optionally signed)
    // infers its width from the suffix instead of defaulting to u8. Unsuffixed
    // literals keep the zero-cost u8 default.
    private static bool TryInferLiteralSuffixType(ExpressionSyntax expression, out string type)
    {
        switch (expression)
        {
            case ConstantSyntax constant:
                return IntegerLiteral.TryGetSuffixType(Convert.ToString(constant.Value), out type);
            case UnaryExpressionSyntax { OperatorSymbol: "-" or "+", Operand: var operand }:
                return TryInferLiteralSuffixType(operand, out type);
            default:
                type = "u8";
                return false;
        }
    }

    private static bool IsLiteralExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            ConstantSyntax => true,
            UnaryExpressionSyntax { OperatorSymbol: "-" or "+", Operand: var operand } => IsLiteralExpression(operand),
            _ => false,
        };
    }

    private StatementSyntax ParseReturn(ReturnStatementContext returnStatement)
    {
        var maybeExpression = returnStatement.expression() is { } expr
            ? Maybe.From(ParseExpression(expr))
            : Maybe<ExpressionSyntax>.None;
        return new ReturnSyntax(maybeExpression);
    }

    private StatementSyntax ParseDeclaration(VariableDeclarationContext declaration)
    {
        return ParseDeclaration(declaration.variableDeclarator());
    }

    private DeclarationSyntax ParseDeclaration(VariableDeclaratorContext declaration)
    {
        var maybeInitialization = declaration.variableInitializer() is { } initializer
            ? Maybe.From(ParseVariableInitializer(initializer))
            : Maybe<ExpressionSyntax>.None;
        var maybeArrayLength = declaration.arraySize() is { } arraySize
            ? Maybe.From(ParseArrayLength(arraySize, maybeInitialization))
            : Maybe<ExpressionSyntax>.None;
        return new DeclarationSyntax(declaration.type().GetText(), declaration.IDENTIFIER().GetText(), maybeArrayLength, maybeInitialization);
    }

    private ExpressionSyntax ParseArrayLength(ArraySizeContext arraySize, Maybe<ExpressionSyntax> initialization)
    {
        if (arraySize.expression() is { } explicitLength)
        {
            return ParseExpression(explicitLength);
        }

        if (initialization.HasValue && initialization.Value is ArrayInitializerSyntax arrayInitializer)
        {
            return new ConstantSyntax(arrayInitializer.Elements.Count.ToString());
        }

        return new ConstantSyntax("0");
    }

    private ExpressionSyntax ParseVariableInitializer(VariableInitializerContext initializer)
    {
        if (initializer.arrayInitializer() is { } arrayInitializer)
        {
            return new ArrayInitializerSyntax(arrayInitializer.variableInitializer().Select(ParseVariableInitializer));
        }

        if (initializer.typedStructInitializer() is { } typedStructInitializer)
        {
            return ParseStructInitializer(typedStructInitializer.structInitializer());
        }

        if (initializer.structInitializer() is { } structInitializer)
        {
            return ParseStructInitializer(structInitializer);
        }

        return ParseExpression(initializer.expression());
    }

    private ExpressionSyntax ParseStructInitializer(StructInitializerContext structInitializer)
    {
        return new StructInitializerSyntax(structInitializer.fieldInitializer().Select(ParseFieldInitializer));
    }

    private StructFieldInitializerSyntax ParseFieldInitializer(FieldInitializerContext fieldInitializer)
    {
        var name = fieldInitializer.IDENTIFIER().GetText();
        var expression = fieldInitializer.expression() is { } explicitExpression
            ? ParseExpression(explicitExpression)
            : new IdentifierSyntax(name);
        return new StructFieldInitializerSyntax(name, expression);
    }

    private ExpressionSyntax ParseFunctionCall(FunctionCallContext functionCall)
    {
        var name = functionCall.IDENTIFIER().ToString()!;
        return new FunctionCall(name, ParseArguments(functionCall.arguments()));
    }

    private IEnumerable<ExpressionSyntax> ParseArguments(ArgumentsContext? arguments)
    {
        return arguments?.argument()?.Select(ParseArgument) ?? Enumerable.Empty<ExpressionSyntax>();
    }

    private ExpressionSyntax ParseArgument(ArgumentContext argument)
    {
        var expression = ParseExpression(argument.expression());
        return argument.IDENTIFIER() is { } identifier
            ? new NamedArgumentSyntax(identifier.GetText(), expression)
            : expression;
    }

    private StatementSyntax ParseWhileLoop(WhileLoopContext whileLoop)
    {
        return new WhileSyntax(ParseExpression(whileLoop.expression()), ParseBlock(whileLoop.block()));
    }

    private StatementSyntax ParseDoWhileLoop(DoWhileLoopContext doWhileLoop)
    {
        return new DoWhileSyntax(ParseBlock(doWhileLoop.block()), ParseExpression(doWhileLoop.expression()));
    }

    private StatementSyntax ParseRangeForLoop(RangeForLoopContext rangeForLoop)
    {
        return new RangeForSyntax(
            rangeForLoop.type().GetText(),
            rangeForLoop.IDENTIFIER().GetText(),
            ParseExpression(rangeForLoop.expression()[0]),
            ParseExpression(rangeForLoop.expression()[1]),
            ParseBlock(rangeForLoop.block()));
    }

    private StatementSyntax ParseForLoop(ForLoopContext forLoop)
    {
        var initializer = forLoop.forInitializer() is { } init
            ? Maybe.From(ParseForInitializer(init))
            : Maybe<StatementSyntax>.None;
        var condition = forLoop.expression() is { } conditionContext
            ? Maybe.From(ParseExpression(conditionContext))
            : Maybe<ExpressionSyntax>.None;
        var increment = forLoop.forIncrement() is { } incrementContext
            ? Maybe.From(ParseForIncrement(incrementContext))
            : Maybe<ExpressionSyntax>.None;

        return new ForSyntax(initializer, condition, increment, ParseBlock(forLoop.block()));
    }

    private ExpressionSyntax ParseForIncrement(ForIncrementContext increment)
    {
        if (increment.assignment() is { } assignment)
        {
            return ParseAssignment(assignment);
        }

        if (increment.postfixMutation() is { } postfixMutation)
        {
            return ParsePostfixMutation(postfixMutation);
        }

        throw new InvalidOperationException("Unsupported for increment.");
    }

    private ExpressionSyntax ParsePostfixMutation(PostfixMutationContext postfixMutation)
    {
        return new PostfixMutationSyntax(ParseLValue(postfixMutation.lvalue()), postfixMutation.postfixOperator().GetText());
    }

    private StatementSyntax ParseSwitch(SwitchStatementContext switchStatement)
    {
        var subject = ParseExpression(switchStatement.expression());
        var cases = switchStatement.switchCase()
            .Select(ParseSwitchCase)
            .ToList();
        var defaultBlock = switchStatement.switchDefault() is { } switchDefault
            ? Maybe.From(ParseBlock(switchDefault.block()))
            : Maybe<BlockSyntax>.None;

        return new SwitchSyntax(subject, cases, defaultBlock);
    }

    private SwitchCaseSyntax ParseSwitchCase(SwitchCaseContext switchCase)
    {
        return new SwitchCaseSyntax(switchCase.switchCasePattern().Select(ParseSwitchCasePattern).ToList(), ParseBlock(switchCase.block()));
    }

    private SwitchCasePatternSyntax ParseSwitchCasePattern(SwitchCasePatternContext switchCasePattern)
    {
        var expressions = switchCasePattern.expression();
        return expressions.Length switch
        {
            1 => new SwitchCasePatternSyntax(ParseExpression(expressions[0])),
            2 => new SwitchCasePatternSyntax(ParseExpression(expressions[0]), ParseExpression(expressions[1])),
            _ => throw new InvalidOperationException("Unsupported switch case pattern."),
        };
    }

    private StatementSyntax ParseForInitializer(ForInitializerContext initializer)
    {
        if (initializer.variableDeclarator() is { } declaration)
        {
            return ParseDeclaration(declaration);
        }

        if (initializer.assignment() is { } assignment)
        {
            return new ExpressionStatementSyntax(ParseAssignment(assignment));
        }

        throw new InvalidOperationException("Unsupported for initializer.");
    }

    private StatementSyntax ParseConditional(ConditionalContext conditional)
    {
        var condition = ParseExpression(conditional.expression());
        var thenBlock = ParseBlock(conditional.block()[0]);
        var elseBlock = ParseElseBlock(conditional);
        return new IfElseSyntax(condition, thenBlock, elseBlock);
    }

    private Maybe<BlockSyntax> ParseElseBlock(ConditionalContext conditional)
    {
        if (conditional.conditional() is { } elseIf)
        {
            return Maybe.From(new BlockSyntax([ParseConditional(elseIf)]));
        }

        return conditional.block().Length > 1
            ? Maybe.From(ParseBlock(conditional.block()[1]))
            : Maybe<BlockSyntax>.None;
    }

    private ExpressionSyntax ParseAssignment(AssignmentContext assignment)
    {
        var lvalue = ParseLValue(assignment.lvalue());
        return new AssignmentSyntax(lvalue, assignment.assignmentOperator().GetText(), ParseExpression(assignment.expression()));
    }

    private ExpressionSyntax ParseExpression(ExpressionContext expression)
    {
        if (expression.assignment() is { } assignmentContext)
        {
            return ParseAssignment(assignmentContext);
        }

        if (expression.switchExpression() is { } switchExpression)
        {
            return ParseSwitchExpression(switchExpression);
        }

        if (expression.pipelineExpression() is { } pipelineExpression)
        {
            return ParsePipelineExpression(pipelineExpression);
        }

        if (expression.conditionalExpression() is { } conditionalExpression)
        {
            return ParseConditionalExpression(conditionalExpression);
        }

        throw new NotImplementedException(expression.ToString());
    }

    private ExpressionSyntax ParsePipelineExpression(PipelineExpressionContext pipelineExpression)
    {
        return new PipelineExpressionSyntax(
            ParseConditionalExpression(pipelineExpression.conditionalExpression()),
            pipelineExpression.pipelineStep().Select(ParsePipelineStep).ToList());
    }

    private PipelineStepSyntax ParsePipelineStep(PipelineStepContext pipelineStep)
    {
        return new PipelineStepSyntax(
            pipelineStep.IDENTIFIER().GetText(),
            ParseArguments(pipelineStep.arguments()));
    }

    private ExpressionSyntax ParseSwitchExpression(SwitchExpressionContext switchExpression)
    {
        var subject = ParseConditionalExpression(switchExpression.conditionalExpression());
        var arms = new List<SwitchExpressionArmSyntax>();
        var defaultValue = Maybe<ExpressionSyntax>.None;
        foreach (var arm in switchExpression.switchExpressionArm())
        {
            if (arm.switchExpressionDefaultArm() is { } defaultArm)
            {
                if (defaultValue.HasValue)
                {
                    throw new InvalidOperationException("Switch expression can only contain one default arm.");
                }

                defaultValue = Maybe.From(ParseExpression(defaultArm.expression()));
                continue;
            }

            var caseArm = arm.switchExpressionCaseArm();
            arms.Add(new SwitchExpressionArmSyntax(
                caseArm.switchCasePattern().Select(ParseSwitchCasePattern).ToList(),
                ParseExpression(caseArm.expression())));
        }

        return new SwitchExpressionSyntax(subject, arms, defaultValue);
    }

    private ExpressionSyntax ParseConditionalExpression(ConditionalExpressionContext conditionalExpression)
    {
        var condition = ParseConditionalOr(conditionalExpression.conditionalOrExpression());
        var branches = conditionalExpression.expression();
        return branches.Length switch
        {
            0 => condition,
            2 => new ConditionalExpressionSyntax(condition, ParseExpression(branches[0]), ParseExpression(branches[1])),
            _ => throw new InvalidOperationException("Unsupported conditional expression."),
        };
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
            var right = ParseBitwiseOr(conditionalAndExpression.bitwiseOrExpression());
            return new BinaryExpressionSyntax(left, right, Operator.Get("&&"));
        }

        return ParseBitwiseOr(conditionalAndExpression.bitwiseOrExpression());
    }

    private ExpressionSyntax ParseBitwiseOr(BitwiseOrExpressionContext bitwiseOrExpression)
    {
        if (bitwiseOrExpression.bitwiseOrExpression() is { } bitwiseOr)
        {
            var left = ParseBitwiseOr(bitwiseOr);
            var right = ParseBitwiseXor(bitwiseOrExpression.bitwiseXorExpression());
            return new BinaryExpressionSyntax(left, right, Operator.Get("|"));
        }

        return ParseBitwiseXor(bitwiseOrExpression.bitwiseXorExpression());
    }

    private ExpressionSyntax ParseBitwiseXor(BitwiseXorExpressionContext bitwiseXorExpression)
    {
        if (bitwiseXorExpression.bitwiseXorExpression() is { } bitwiseXor)
        {
            var left = ParseBitwiseXor(bitwiseXor);
            var right = ParseBitwiseAnd(bitwiseXorExpression.bitwiseAndExpression());
            return new BinaryExpressionSyntax(left, right, Operator.Get("^"));
        }

        return ParseBitwiseAnd(bitwiseXorExpression.bitwiseAndExpression());
    }

    private ExpressionSyntax ParseBitwiseAnd(BitwiseAndExpressionContext bitwiseAndExpression)
    {
        if (bitwiseAndExpression.bitwiseAndExpression() is { } bitwiseAnd)
        {
            var left = ParseBitwiseAnd(bitwiseAnd);
            var right = ParseConditionalEquality(bitwiseAndExpression.equalityExpression());
            return new BinaryExpressionSyntax(left, right, Operator.Get("&"));
        }

        return ParseConditionalEquality(bitwiseAndExpression.equalityExpression());
    }

    private ExpressionSyntax ParseConditionalEquality(EqualityExpressionContext equalityExpression)
    {
        if (equalityExpression.equalityExpression() is { } equality)
        {
            var left = ParseConditionalEquality(equality);
            var right = ParseRangeMembership(equalityExpression.rangeMembershipExpression());
            return new BinaryExpressionSyntax(left, right, Operator.Get(equalityExpression.children[1].GetText()));
        }

        return ParseRangeMembership(equalityExpression.rangeMembershipExpression());
    }

    private ExpressionSyntax ParseRangeMembership(RangeMembershipExpressionContext rangeMembership)
    {
        var shiftExpressions = rangeMembership.shiftExpression();
        if (shiftExpressions.Length == 3)
        {
            var subject = ParseShiftExpression(shiftExpressions[0]);
            var start = ParseShiftExpression(shiftExpressions[1]);
            var end = ParseShiftExpression(shiftExpressions[2]);
            var lowerBound = new BinaryExpressionSyntax(subject, start, Operator.Get(">="));
            var upperBound = new BinaryExpressionSyntax(subject, end, Operator.Get("<"));
            return new BinaryExpressionSyntax(lowerBound, upperBound, Operator.Get("&&"));
        }

        return ParseConditionalRelational(rangeMembership.relationalExpression());
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
        if (ctx.memberAccess() is { } memberAccess)
        {
            return new MemberAccessLValue(ParseMemberAccess(memberAccess));
        }

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
        if (mulExpression.mulExpression() is { } multExpr)
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
        if (unaryExpression.castExpression() is { } castExpression)
        {
            return ParseCast(castExpression);
        }

        if (unaryExpression.unaryExpression() is { } unaryExpr)
        {
            return new UnaryExpressionSyntax(unaryExpression.children[0].GetText(), ParseUnary(unaryExpr));
        }

        return ParsePrimary(unaryExpression.primary());
    }

    private ExpressionSyntax ParseCast(CastExpressionContext castExpression)
    {
        return new CastSyntax(castExpression.type().GetText(), ParseUnary(castExpression.unaryExpression()));
    }

    private ExpressionSyntax ParsePrimary(PrimaryContext primary)
    {
        if (primary.LITERAL() is { } node)
        {
            return new ConstantSyntax(node.GetText());
        }

        if (primary.sizeofExpression() is { } sizeofExpression)
        {
            return new SizeOfSyntax(sizeofExpression.type().GetText());
        }

        if (primary.offsetofExpression() is { } offsetofExpression)
        {
            return new OffsetOfSyntax(offsetofExpression.type().GetText(), offsetofExpression.IDENTIFIER().GetText());
        }

        if (primary.countofExpression() is { } countofExpression)
        {
            return new CountOfSyntax(countofExpression.IDENTIFIER().GetText());
        }

        if (primary.qualifiedCall() is { } qualifiedCall)
        {
            return ParseQualifiedCall(qualifiedCall);
        }

        if (primary.functionCall() is { } functionCall)
        {
            return ParseFunctionCall(functionCall);
        }

        if (primary.memberAccess() is { } memberAccess)
        {
            return ParseMemberAccess(memberAccess);
        }

        if (primary.indexExpression() is { } indexExpression)
        {
            return ParseIndexExpression(indexExpression);
        }

        if (primary.IDENTIFIER() is { } identifier)
        {
            return new IdentifierSyntax(identifier.GetText());
        }

        if (primary.children[0].GetText() == "(")
        {
            return ParseExpression((ExpressionContext)primary.children[1]);
        }
        throw new NotImplementedException();
    }

    private ExpressionSyntax ParseQualifiedCall(QualifiedCallContext qualifiedCall)
    {
        var identifiers = qualifiedCall.IDENTIFIER();
        var qualifier = string.Join(".", identifiers.Take(identifiers.Length - 1).Select(identifier => identifier.GetText()));
        return new QualifiedCallSyntax(
            qualifier,
            identifiers[^1].GetText(),
            ParseArguments(qualifiedCall.arguments()));
    }

    private MemberAccessSyntax ParseMemberAccess(MemberAccessContext memberAccess)
    {
        ExpressionSyntax current;
        var childIndex = 0;
        if (memberAccess.GetChild(0) is IndexExpressionContext indexExpression)
        {
            current = ParseIndexExpression(indexExpression);
            childIndex = 1;
        }
        else
        {
            current = new IdentifierSyntax(memberAccess.GetChild(0).GetText());
            childIndex = 1;
        }

        while (childIndex < memberAccess.ChildCount)
        {
            if (memberAccess.GetChild(childIndex).GetText() != ".")
            {
                throw new InvalidOperationException("Unsupported member access syntax.");
            }

            current = new MemberAccessSyntax(current, memberAccess.GetChild(childIndex + 1).GetText());
            childIndex += 2;
        }

        return (MemberAccessSyntax)current;
    }

    private ExpressionSyntax ParseIndexExpression(IndexExpressionContext indexExpression)
    {
        return new IndexExpressionSyntax(indexExpression.IDENTIFIER().GetText(), ParseExpression(indexExpression.expression()));
    }
}
