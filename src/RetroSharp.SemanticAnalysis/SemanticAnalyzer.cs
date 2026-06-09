using RetroSharp.Core;
using RetroSharp.Parser;

namespace RetroSharp.SemanticAnalysis;

public class SemanticAnalyzer
{
    public AnalyzeResult<SemanticNode> Analyze(ProgramSyntax program) => AnalyzeProgram(program, Scope.Empty);

    private AnalyzeResult<SemanticNode> AnalyzeProgram(ProgramSyntax node, Scope scope)
    {
        node = TypeAliasResolver.Resolve(node);
        var types = BuildTypeTable(node.Enums, node.Structs);
        scope = DeclareConstants(node.Constants, scope, types);
        var functionIndex = node.Functions.ToDictionary(function => function.Name, StringComparer.Ordinal);
        var functions = new List<FunctionNode>();
        foreach (var function in node.Functions)
        {
            var functionResult = AnalyzeFunction(function, scope, types, functionIndex);
            scope = functionResult.Scope;
            functions.Add(functionResult.Node);
        }

        return new AnalyzeResult<SemanticNode>(new ProgramNode(functions), scope);
    }

    private static Scope DeclareConstants(IEnumerable<ConstDeclarationSyntax> constants, Scope scope, IReadOnlyDictionary<string, SymbolType> types)
    {
        foreach (var constant in constants)
        {
            var result = scope.TryDeclare(new Symbol(constant.Name, ResolveType(constant.Type, types)));
            if (result.IsSuccess)
            {
                scope = result.Value;
            }
        }

        return scope;
    }

    private static IReadOnlyDictionary<string, SymbolType> BuildTypeTable(IEnumerable<EnumSyntax> enums, IEnumerable<StructSyntax> structs)
    {
        var types = BuiltinTypes();
        foreach (var enumSyntax in enums)
        {
            types[enumSyntax.Name] = new EnumType(enumSyntax.Name, enumSyntax.Members.Select(member => member.Name).ToList());
        }

        foreach (var structSyntax in structs)
        {
            var offset = 0;
            var fields = new List<StructField>();
            foreach (var field in structSyntax.Fields)
            {
                var fieldType = ResolveType(field.Type, types);
                fields.Add(new StructField(field.Name, fieldType, offset));
                offset += SizeOf(fieldType);
            }

            types[structSyntax.Name] = new StructType(structSyntax.Name, fields);
        }

        return types;
    }

    private static Dictionary<string, SymbolType> BuiltinTypes() => new(StringComparer.Ordinal)
    {
        ["i8"] = PrimitiveType.I8,
        ["u8"] = PrimitiveType.U8,
        ["i16"] = IntType.Instance,
        ["u16"] = PrimitiveType.U16,
        ["bool"] = PrimitiveType.Bool,
    };

    private static SymbolType ResolveType(string name, IReadOnlyDictionary<string, SymbolType> types)
    {
        return types.TryGetValue(name, out var type) ? type : new UnknownType(name);
    }

    private static int SizeOf(SymbolType type)
    {
        return type switch
        {
            PrimitiveType primitive => primitive.Size,
            IntType => 2,
            EnumType => 1,
            StructType structType => structType.Fields.Sum(field => SizeOf(field.Type)),
            _ => 1,
        };
    }

    private static bool TrySizeOfType(string typeName, IReadOnlyDictionary<string, SymbolType> types, out int size)
    {
        if (typeName.StartsWith("ptr<", StringComparison.Ordinal) && typeName.EndsWith(">", StringComparison.Ordinal))
        {
            size = 2;
            return true;
        }

        if (types.TryGetValue(typeName, out var type) && type is not UnknownType)
        {
            size = SizeOf(type);
            return true;
        }

        size = 0;
        return false;
    }

    private AnalyzeResult<FunctionNode> AnalyzeFunction(FunctionSyntax function, Scope parentScope, IReadOnlyDictionary<string, SymbolType> types, IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        // Create a child scope for the function body so locals don't leak out
        var functionScope = new Scope(parentScope);

        // Declare parameters in the function scope so they can be referenced in the body
        var errors = new List<string>();
        foreach (var p in function.Parameters)
        {
            p.DefaultValue.Execute(defaultValue =>
            {
                var defaultResult = AnalyzeExpression(defaultValue, functionScope, types);
                errors.AddRange(defaultResult.Node.AllErrors);
            });

            var result = functionScope.TryDeclare(new Symbol(p.Name, ResolveType(p.Type, types)));
            if (result.IsSuccess)
            {
                functionScope = result.Value;
            }
            else
            {
                errors.Add($"Parameter '{p.Name}' is already declared");
            }
        }

        var analyzeBlockResult = AnalyzeBlock(function.Block, functionScope, types, functions);
        errors.AddRange(FunctionContractValidator.Validate(function));
        var paramNames = function.Parameters.Select(p => p.Name).ToList();
        var node = new FunctionNode(function.Type, function.Name, analyzeBlockResult.Node, paramNames)
        {
            Errors = errors
        };
        // Return the unchanged parent scope (function-local declarations are not visible outside)
        return new AnalyzeResult<FunctionNode>(node, parentScope);
    }

    private AnalyzeResult<BlockNode> AnalyzeBlock(BlockSyntax block, Scope outerScope, IReadOnlyDictionary<string, SymbolType> types)
    {
        return AnalyzeBlock(block, outerScope, types, new Dictionary<string, FunctionSyntax>());
    }

    private AnalyzeResult<BlockNode> AnalyzeBlock(BlockSyntax block, Scope outerScope, IReadOnlyDictionary<string, SymbolType> types, IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        // Each block introduces a new child scope
        var blockScope = new Scope(outerScope);
        var statements = new List<StatementNode>();
        foreach (var statement in block.Statements)
        {
            var analyzedStatementResult = AnalyzeStatement(statement, blockScope, types, functions);
            statements.Add(analyzedStatementResult.Node);
            blockScope = analyzedStatementResult.Scope;
        }

        // Do not leak the block scope; return to the outer scope
        return new AnalyzeResult<BlockNode>(new BlockNode(statements), outerScope);
    }

    private AnalyzeResult<StatementNode> AnalyzeStatement(StatementSyntax statement, Scope scope, IReadOnlyDictionary<string, SymbolType> types)
    {
        return AnalyzeStatement(statement, scope, types, new Dictionary<string, FunctionSyntax>());
    }

    private AnalyzeResult<StatementNode> AnalyzeStatement(StatementSyntax statement, Scope scope, IReadOnlyDictionary<string, SymbolType> types, IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        switch (statement)
        {
            case ConstDeclarationSyntax constDeclaration:
                return AnalyzeConstDeclaration(constDeclaration, scope, types);
            case DeclarationSyntax declarationStatement:
                return AnalyzeDeclaration(declarationStatement, scope, types, functions);
            case ExpressionStatementSyntax expressionStatement:
                return AnalyzeExpressionStatement(expressionStatement, scope, types, functions);
            case IfElseSyntax ifElseStatement:
                return AnalyzeIfElse(ifElseStatement, scope, types, functions);
            case DoWhileSyntax doWhileStatement:
                return AnalyzeDoWhile(doWhileStatement, scope, types, functions);
            case LoopSyntax loopStatement:
                return AnalyzeLoop(loopStatement, scope, types, functions);
            case RangeForSyntax rangeForStatement:
                return AnalyzeFor(RangeForLowerer.Lower(rangeForStatement), scope, types, functions);
            case ForSyntax forStatement:
                return AnalyzeFor(forStatement, scope, types, functions);
            case SwitchSyntax switchStatement:
                return AnalyzeIfElse(SwitchLowerer.Lower(switchStatement), scope, types, functions);
            case BreakSyntax:
                return new AnalyzeResult<StatementNode>(new BreakNode(), scope);
            case ContinueSyntax:
                return new AnalyzeResult<StatementNode>(new ContinueNode(), scope);
            case ReturnSyntax returnStatement:
                return AnalyzeReturn(returnStatement, scope, types, functions);
            default:
                throw new ArgumentOutOfRangeException(nameof(statement));
        }
    }

    private AnalyzeResult<StatementNode> AnalyzeExpressionStatement(ExpressionStatementSyntax expressionStatement, Scope scope, IReadOnlyDictionary<string, SymbolType> types)
    {
        return AnalyzeExpressionStatement(expressionStatement, scope, types, new Dictionary<string, FunctionSyntax>());
    }

    private AnalyzeResult<StatementNode> AnalyzeExpressionStatement(ExpressionStatementSyntax expressionStatement, Scope scope, IReadOnlyDictionary<string, SymbolType> types, IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        var analyzeExpressionResult = AnalyzeExpression(expressionStatement.Expression, scope, types, functions);
        return new AnalyzeResult<StatementNode>(new ExpressionStatementNode(analyzeExpressionResult.Node), scope);
    }

    private AnalyzeResult<ExpressionNode> AnalyzeExpression(ExpressionSyntax expression, Scope scope, IReadOnlyDictionary<string, SymbolType> types)
    {
        return AnalyzeExpression(expression, scope, types, new Dictionary<string, FunctionSyntax>());
    }

    private AnalyzeResult<ExpressionNode> AnalyzeExpression(ExpressionSyntax expression, Scope scope, IReadOnlyDictionary<string, SymbolType> types, IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        if (expression is AssignmentSyntax assignment)
        {
            return AnalyzeAssignmentExpression(assignment, scope, types, functions);
        }
        
        if (expression is ConstantSyntax c)
        {
            return new AnalyzeResult<ExpressionNode>(new ConstantNode(c.Value), scope);
        }

        if (expression is BinaryExpressionSyntax binaryExpression)
        {
            return AnalyzeBinaryExpression(binaryExpression, scope, types, functions);
        }

        if (expression is ConditionalExpressionSyntax conditionalExpression)
        {
            return AnalyzeConditionalExpression(conditionalExpression, scope, types, functions);
        }

        if (expression is UnaryExpressionSyntax unaryExpression)
        {
            return AnalyzeUnaryExpression(unaryExpression, scope, types, functions);
        }

        if (expression is CastSyntax castExpression)
        {
            return AnalyzeCastExpression(castExpression, scope, types, functions);
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

        if (expression is MemberAccessSyntax memberAccess)
        {
            var symbolNode = GetMemberSymbolNode(scope, memberAccess, types, true);
            var symbolExpressionNode = new SymbolExpressionNode(symbolNode)
            {
                Errors = SymbolError(symbolNode)
            };
            return new AnalyzeResult<ExpressionNode>(symbolExpressionNode, scope);
        }

        if (expression is IndexExpressionSyntax indexExpression)
        {
            var symbolNode = GetIndexedSymbolNode(scope, indexExpression);
            var symbolExpressionNode = new SymbolExpressionNode(symbolNode)
            {
                Errors = SymbolError(symbolNode)
            };
            return new AnalyzeResult<ExpressionNode>(symbolExpressionNode, scope);
        }

        if (expression is SizeOfSyntax sizeOf)
        {
            return AnalyzeSizeOf(sizeOf, scope, types);
        }

        if (expression is OffsetOfSyntax offsetOf)
        {
            return AnalyzeOffsetOf(offsetOf, scope, types);
        }

        if (expression is CountOfSyntax countOf)
        {
            return AnalyzeCountOf(countOf, scope);
        }

        if (expression is PostfixMutationSyntax postfixMutation)
        {
            return AnalyzeAssignmentExpression(postfixMutation.ToAssignment(), scope, types, functions);
        }

        if (expression is SwitchExpressionSyntax switchExpression)
        {
            return AnalyzeSwitchExpression(switchExpression, scope, types, functions);
        }

        if (expression is SdkDotCallSyntax sdkDotCall)
        {
            if (scope.Get(sdkDotCall.Module).HasValue)
            {
                return AnalyzeReceiverDotCall(sdkDotCall, scope, types, functions);
            }

            if (SdkDotCallLowerer.IsKnownModule(sdkDotCall.Module))
            {
                return AnalyzeExpression(SdkDotCallLowerer.Lower(sdkDotCall), scope, types, functions);
            }

            return new AnalyzeResult<ExpressionNode>(new FunctionCallExpressionNode(sdkDotCall.Method, [])
            {
                Errors = [$"Unknown SDK module or receiver method '{sdkDotCall.Module}.{sdkDotCall.Method}'"]
            }, scope);
        }

        if (expression is PipelineExpressionSyntax pipeline)
        {
            return AnalyzeExpression(PipelineExpressionLowerer.Lower(pipeline), scope, types, functions);
        }

        if (expression is FunctionCall functionCall)
        {
            var analyzedArgs = functionCall.Parameters.Select(arg => AnalyzeExpression(arg, scope, types, functions).Node).ToList();
            return new AnalyzeResult<ExpressionNode>(new FunctionCallExpressionNode(functionCall.Name, analyzedArgs), scope);
        }

        if (expression is NamedArgumentSyntax namedArgument)
        {
            return AnalyzeExpression(namedArgument.Expression, scope, types, functions);
        }

        throw new InvalidOperationException("Por aquí no vas a ningún sitio");
    }

    private AnalyzeResult<ExpressionNode> AnalyzeReceiverDotCall(
        SdkDotCallSyntax sdkDotCall,
        Scope scope,
        IReadOnlyDictionary<string, SymbolType> types,
        IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        if (!ReceiverMethodLowerer.TryLower(sdkDotCall, functions.Values, out var receiverCall, out var receiverFunction))
        {
            return new AnalyzeResult<ExpressionNode>(new FunctionCallExpressionNode(sdkDotCall.Method, [])
            {
                Errors = [$"Unknown receiver method '{sdkDotCall.Module}.{sdkDotCall.Method}'"]
            }, scope);
        }

        var receiverSymbol = scope.Get(sdkDotCall.Module).Value;
        var expectedType = ResolveType(receiverFunction.Parameters[0].Type, types);
        if (receiverSymbol.Type != expectedType)
        {
            return new AnalyzeResult<ExpressionNode>(new FunctionCallExpressionNode(sdkDotCall.Method, [])
            {
                Errors = [$"receiver method '{sdkDotCall.Method}' expects receiver type '{expectedType}', but '{sdkDotCall.Module}' has type '{receiverSymbol.Type}'"]
            }, scope);
        }

        return AnalyzeExpression(receiverCall, scope, types, functions);
    }

    private AnalyzeResult<ExpressionNode> AnalyzeSwitchExpression(SwitchExpressionSyntax switchExpression, Scope scope, IReadOnlyDictionary<string, SymbolType> types)
    {
        return AnalyzeSwitchExpression(switchExpression, scope, types, new Dictionary<string, FunctionSyntax>());
    }

    private AnalyzeResult<ExpressionNode> AnalyzeSwitchExpression(SwitchExpressionSyntax switchExpression, Scope scope, IReadOnlyDictionary<string, SymbolType> types, IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        var errors = SwitchExpressionValidator.Validate(switchExpression).ToList();
        if (errors.Count != 0)
        {
            var subject = AnalyzeExpression(switchExpression.Subject, scope, types);
            return new AnalyzeResult<ExpressionNode>(new ConstantNode(0)
            {
                Errors = subject.Node.AllErrors.Concat(errors)
            }, scope);
        }

        if (!switchExpression.DefaultValue.HasValue)
        {
            var subject = AnalyzeExpression(switchExpression.Subject, scope, types);
            return new AnalyzeResult<ExpressionNode>(new ConstantNode(0)
            {
                Errors = subject.Node.AllErrors.Concat(["switch expression requires a default arm"])
            }, scope);
        }

        return AnalyzeExpression(SwitchExpressionLowerer.Lower(switchExpression), scope, types, functions);
    }

    private AnalyzeResult<ExpressionNode> AnalyzeConditionalExpression(ConditionalExpressionSyntax conditionalExpression, Scope scope, IReadOnlyDictionary<string, SymbolType> types)
    {
        return AnalyzeConditionalExpression(conditionalExpression, scope, types, new Dictionary<string, FunctionSyntax>());
    }

    private AnalyzeResult<ExpressionNode> AnalyzeConditionalExpression(ConditionalExpressionSyntax conditionalExpression, Scope scope, IReadOnlyDictionary<string, SymbolType> types, IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        var condition = AnalyzeExpression(conditionalExpression.Condition, scope, types, functions);
        var whenTrue = AnalyzeExpression(conditionalExpression.WhenTrue, scope, types, functions);
        var whenFalse = AnalyzeExpression(conditionalExpression.WhenFalse, scope, types, functions);
        return new AnalyzeResult<ExpressionNode>(new ConditionalExpressionNode(condition.Node, whenTrue.Node, whenFalse.Node), scope);
    }

    private AnalyzeResult<ExpressionNode> AnalyzeSizeOf(SizeOfSyntax sizeOf, Scope scope, IReadOnlyDictionary<string, SymbolType> types)
    {
        if (TrySizeOfType(sizeOf.Type, types, out var size))
        {
            return new AnalyzeResult<ExpressionNode>(new ConstantNode(size), scope);
        }

        return new AnalyzeResult<ExpressionNode>(new ConstantNode(0)
        {
            Errors = [$"Cannot compute sizeof({sizeOf.Type})"]
        }, scope);
    }

    private AnalyzeResult<ExpressionNode> AnalyzeOffsetOf(OffsetOfSyntax offsetOf, Scope scope, IReadOnlyDictionary<string, SymbolType> types)
    {
        if (!types.TryGetValue(offsetOf.Type, out var type) || type is not StructType structType)
        {
            return new AnalyzeResult<ExpressionNode>(new ConstantNode(0)
            {
                Errors = [$"Cannot compute offsetof({offsetOf.Type}, {offsetOf.Field})"]
            }, scope);
        }

        return structType.Field(offsetOf.Field)
            .Match(
                field => new AnalyzeResult<ExpressionNode>(new ConstantNode(field.Offset), scope),
                () => new AnalyzeResult<ExpressionNode>(new ConstantNode(0)
                {
                    Errors = [$"Type '{offsetOf.Type}' does not contain field '{offsetOf.Field}'"]
                }, scope));
    }

    private AnalyzeResult<ExpressionNode> AnalyzeCountOf(CountOfSyntax countOf, Scope scope)
    {
        return scope.Get(countOf.BaseIdentifier)
            .Match(
                symbol => symbol.Type is ArrayType arrayType
                    ? new AnalyzeResult<ExpressionNode>(new ConstantNode(arrayType.Length), scope)
                    : new AnalyzeResult<ExpressionNode>(new ConstantNode(0)
                    {
                        Errors = [$"Type '{symbol.Type}' is not an array"]
                    }, scope),
                () => new AnalyzeResult<ExpressionNode>(new ConstantNode(0)
                {
                    Errors = [$"Use of undeclared variable '{countOf.BaseIdentifier}'"]
                }, scope));
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

    private SymbolNode GetMemberSymbolNode(Scope scope, MemberAccessSyntax memberAccess, IReadOnlyDictionary<string, SymbolType> types, bool allowEnumMembers)
    {
        if (allowEnumMembers && TryGetEnumMemberSymbolNode(memberAccess, types) is { } enumMember)
        {
            return enumMember;
        }

        var (name, type, errors) = ResolveMember(scope, memberAccess);
        return errors.Count == 0
            ? new KnownSymbolNode(new Symbol(name, type))
            : new UnknownSymbol(name)
            {
                Errors = errors
            };
    }

    private static SymbolNode? TryGetEnumMemberSymbolNode(MemberAccessSyntax memberAccess, IReadOnlyDictionary<string, SymbolType> types)
    {
        if (memberAccess.Target is not IdentifierSyntax identifier)
        {
            return null;
        }

        if (!types.TryGetValue(identifier.Identifier, out var type) || type is not EnumType enumType)
        {
            return null;
        }

        var name = $"{identifier.Identifier}.{memberAccess.Member}";
        return enumType.HasMember(memberAccess.Member)
            ? new KnownSymbolNode(new Symbol(name, enumType))
            : new UnknownSymbol(name)
            {
                Errors = [$"Enum '{enumType.EnumName}' does not contain member '{memberAccess.Member}'"]
            };
    }

    private (string Name, SymbolType Type, List<string> Errors) ResolveMember(Scope scope, MemberAccessSyntax memberAccess)
    {
        var (targetName, targetType, errors) = ResolveMemberTarget(scope, memberAccess.Target);
        var fullName = $"{targetName}.{memberAccess.Member}";
        if (errors.Count > 0)
        {
            return (fullName, SymbolType.Unknown, errors);
        }

        if (targetType is not StructType structType)
        {
            return (fullName, SymbolType.Unknown, [$"Type '{targetType}' does not contain field '{memberAccess.Member}'"]);
        }

        return structType.Field(memberAccess.Member)
            .Match(
                field => (fullName, field.Type, new List<string>()),
                () => (fullName, SymbolType.Unknown, new List<string> { $"Type '{structType.Name}' does not contain field '{memberAccess.Member}'" }));
    }

    private SymbolNode GetIndexedSymbolNode(Scope scope, IndexExpressionSyntax indexExpression)
    {
        var (name, type, errors) = ResolveIndexedSymbol(scope, indexExpression.BaseIdentifier, indexExpression.Index);
        return errors.Count == 0
            ? new KnownSymbolNode(new Symbol(name, type))
            : new UnknownSymbol(name)
            {
                Errors = errors
            };
    }

    private (string Name, SymbolType Type, List<string> Errors) ResolveIndexedSymbol(Scope scope, string baseIdentifier, ExpressionSyntax index)
    {
        var name = IndexedName(baseIdentifier, index);
        return scope.Get(baseIdentifier)
            .Match(
                symbol => symbol.Type is ArrayType arrayType
                    ? (name, arrayType.ElementType, new List<string>())
                    : (name, SymbolType.Unknown, new List<string> { $"Type '{symbol.Type}' is not an array" }),
                () => (name, SymbolType.Unknown, new List<string> { $"Use of undeclared variable '{baseIdentifier}'" }));
    }

    private static string IndexedName(string baseIdentifier, ExpressionSyntax index)
    {
        if (index is ConstantSyntax constant && IntegerLiteral.TryParse(Convert.ToString(constant.Value), out var value))
        {
            return $"{baseIdentifier}[{value}]";
        }

        return index is ConstantSyntax text
            ? $"{baseIdentifier}[{text.Value}]"
            : $"{baseIdentifier}[?]";
    }

    private (string Name, SymbolType Type, List<string> Errors) ResolveMemberTarget(Scope scope, ExpressionSyntax expression)
    {
        if (expression is IdentifierSyntax identifier)
        {
            return scope.Get(identifier.Identifier)
                .Match(
                    symbol => (symbol.Name, symbol.Type, new List<string>()),
                    () => (identifier.Identifier, SymbolType.Unknown, new List<string> { $"Use of undeclared variable '{identifier.Identifier}'" }));
        }

        if (expression is MemberAccessSyntax memberAccess)
        {
            return ResolveMember(scope, memberAccess);
        }

        return ("<expression>", SymbolType.Unknown, ["Member access target must be a symbol or another member access"]);
    }

    private AnalyzeResult<ExpressionNode> AnalyzeBinaryExpression(BinaryExpressionSyntax binaryExpression, Scope scope, IReadOnlyDictionary<string, SymbolType> types)
    {
        return AnalyzeBinaryExpression(binaryExpression, scope, types, new Dictionary<string, FunctionSyntax>());
    }

    private AnalyzeResult<ExpressionNode> AnalyzeBinaryExpression(BinaryExpressionSyntax binaryExpression, Scope scope, IReadOnlyDictionary<string, SymbolType> types, IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        var left = AnalyzeExpression(binaryExpression.Left, scope, types, functions);
        var right = AnalyzeExpression(binaryExpression.Right, scope, types, functions);
        return new AnalyzeResult<ExpressionNode>(new BinaryExpressionNode(left.Node, right.Node, binaryExpression.Operator), scope);

        throw new InvalidOperationException("Por aquí no vas a ningún sitio tampoco");
    }

    private AnalyzeResult<ExpressionNode> AnalyzeUnaryExpression(UnaryExpressionSyntax unaryExpression, Scope scope, IReadOnlyDictionary<string, SymbolType> types)
    {
        return AnalyzeUnaryExpression(unaryExpression, scope, types, new Dictionary<string, FunctionSyntax>());
    }

    private AnalyzeResult<ExpressionNode> AnalyzeUnaryExpression(UnaryExpressionSyntax unaryExpression, Scope scope, IReadOnlyDictionary<string, SymbolType> types, IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        var operand = AnalyzeExpression(unaryExpression.Operand, scope, types, functions);
        return new AnalyzeResult<ExpressionNode>(new UnaryExpressionNode(unaryExpression.OperatorSymbol, operand.Node), scope);
    }

    private AnalyzeResult<ExpressionNode> AnalyzeCastExpression(CastSyntax castExpression, Scope scope, IReadOnlyDictionary<string, SymbolType> types)
    {
        return AnalyzeCastExpression(castExpression, scope, types, new Dictionary<string, FunctionSyntax>());
    }

    private AnalyzeResult<ExpressionNode> AnalyzeCastExpression(CastSyntax castExpression, Scope scope, IReadOnlyDictionary<string, SymbolType> types, IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        var expression = AnalyzeExpression(castExpression.Expression, scope, types, functions);
        var errors = TrySizeOfType(castExpression.Type, types, out _)
            ? []
            : new[] { $"Cannot cast to unknown type '{castExpression.Type}'" };
        return new AnalyzeResult<ExpressionNode>(new CastExpressionNode(castExpression.Type, expression.Node)
        {
            Errors = errors
        }, scope);
    }

    private AnalyzeResult<ExpressionNode> AnalyzeAssignmentExpression(AssignmentSyntax assignment, Scope scope, IReadOnlyDictionary<string, SymbolType> types)
    {
        return AnalyzeAssignmentExpression(assignment, scope, types, new Dictionary<string, FunctionSyntax>());
    }

    private AnalyzeResult<ExpressionNode> AnalyzeAssignmentExpression(AssignmentSyntax assignment, Scope scope, IReadOnlyDictionary<string, SymbolType> types, IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        var symbolNode = GetLValueSymbolNode(scope, assignment.Left);

        var analyzeExpression = AnalyzeExpression(assignment.Right, scope, types, functions);

        return new AnalyzeResult<ExpressionNode>(new AssignmentNode(symbolNode, analyzeExpression.Node)
        {
            Errors = AssignmentErrors(symbolNode)
        }, scope);
    }

    private IEnumerable<string> AssignmentErrors(SymbolNode symbolNode)
    {
        foreach (var error in SymbolError(symbolNode))
        {
            yield return error;
        }

        if (symbolNode is KnownSymbolNode { Symbol.IsImmutable: true } knownSymbol)
        {
            yield return $"Cannot assign to immutable local '{knownSymbol.Symbol.Name}'.";
        }
    }

    private SymbolNode GetLValueSymbolNode(Scope scope, LValue lValue)
    {
        return lValue switch
        {
            IdentifierLValue identifier => GetSymbolNode(scope, identifier.Identifier),
            MemberAccessLValue memberAccess => GetMemberSymbolNode(scope, memberAccess.MemberAccess, new Dictionary<string, SymbolType>(), false),
            IndexLValue index => GetIndexedSymbolNode(scope, new IndexExpressionSyntax(index.BaseIdentifier, index.Index)),
            _ => new UnknownSymbol(lValue.ToString() ?? "<lvalue>")
            {
                Errors = ["Unsupported assignment target"]
            },
        };
    }

    private AnalyzeResult<StatementNode> AnalyzeConstDeclaration(ConstDeclarationSyntax constDeclaration, Scope scope, IReadOnlyDictionary<string, SymbolType> types)
    {
        var value = AnalyzeExpression(constDeclaration.Value, scope, types);
        var result = scope
            .TryDeclare(new Symbol(constDeclaration.Name, ResolveType(constDeclaration.Type, types)))
            .Match(
                s => new AnalyzeResult<StatementNode>(new ConstDeclarationNode(constDeclaration.Name, s, value.Node), s),
                _ => new AnalyzeResult<StatementNode>(new ConstDeclarationNode(constDeclaration.Name, scope, value.Node)
                {
                    Errors = [$"Constant {constDeclaration.Name} is already declared"]
                }, scope));

        return result;
    }

    private AnalyzeResult<StatementNode> AnalyzeDeclaration(DeclarationSyntax declarationStatement, Scope scope, IReadOnlyDictionary<string, SymbolType> types)
    {
        return AnalyzeDeclaration(declarationStatement, scope, types, new Dictionary<string, FunctionSyntax>());
    }

    private AnalyzeResult<StatementNode> AnalyzeDeclaration(DeclarationSyntax declarationStatement, Scope scope, IReadOnlyDictionary<string, SymbolType> types, IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        var type = ResolveType(declarationStatement.Type, types);
        if (declarationStatement.ArrayLength.HasValue)
        {
            type = new ArrayType(type, ArrayLength(declarationStatement.ArrayLength.Value));
        }

        var initializerErrors = AnalyzeDeclarationInitializer(declarationStatement, scope, types, functions).ToList();
        var declaration = scope
            .TryDeclare(new Symbol(declarationStatement.Name, type, declarationStatement.IsImmutable))
            .Match(
                s => new AnalyzeResult<StatementNode>(new DeclarationNode(declarationStatement.Name, s)
                {
                    Errors = initializerErrors
                }, s),
                _ => new AnalyzeResult<StatementNode>(new DeclarationNode(declarationStatement.Name, scope)
                {
                    Errors = initializerErrors.Concat([$"Variable {declarationStatement.Name} is already declared"])
                }, scope));
        return declaration;
    }

    private IEnumerable<string> AnalyzeDeclarationInitializer(DeclarationSyntax declaration, Scope scope, IReadOnlyDictionary<string, SymbolType> types)
    {
        return AnalyzeDeclarationInitializer(declaration, scope, types, new Dictionary<string, FunctionSyntax>());
    }

    private IEnumerable<string> AnalyzeDeclarationInitializer(DeclarationSyntax declaration, Scope scope, IReadOnlyDictionary<string, SymbolType> types, IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        if (!declaration.Initialization.HasValue)
        {
            return [];
        }

        var initialization = declaration.Initialization.Value;
        if (initialization is ArrayInitializerSyntax arrayInitializer)
        {
            var errors = new List<string>();
            if (!declaration.ArrayLength.HasValue)
            {
                errors.Add("Array initializer requires a fixed-size array declaration");
            }

            errors.AddRange(arrayInitializer.Elements.SelectMany(element => AnalyzeExpression(element, scope, types, functions).Node.AllErrors));
            return errors;
        }

        if (initialization is StructInitializerSyntax structInitializer)
        {
            return AnalyzeStructInitializer(declaration, structInitializer, scope, types, functions);
        }

        var expressionErrors = AnalyzeExpression(initialization, scope, types, functions).Node.AllErrors;
        return declaration.ArrayLength.HasValue
            ? expressionErrors.Concat([$"Fixed-size array '{declaration.Name}' requires an array initializer"])
            : expressionErrors;
    }

    private IEnumerable<string> AnalyzeStructInitializer(
        DeclarationSyntax declaration,
        StructInitializerSyntax initializer,
        Scope scope,
        IReadOnlyDictionary<string, SymbolType> types)
    {
        return AnalyzeStructInitializer(declaration, initializer, scope, types, new Dictionary<string, FunctionSyntax>());
    }

    private IEnumerable<string> AnalyzeStructInitializer(
        DeclarationSyntax declaration,
        StructInitializerSyntax initializer,
        Scope scope,
        IReadOnlyDictionary<string, SymbolType> types,
        IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        var errors = new List<string>();
        if (declaration.ArrayLength.HasValue)
        {
            errors.Add($"Struct initializer for '{declaration.Name}' cannot initialize an array declaration");
        }

        var type = ResolveType(declaration.Type, types);
        if (type is not StructType structType)
        {
            errors.Add($"Struct initializer for '{declaration.Name}' requires a struct type");
        }

        var initializedFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fieldInitializer in initializer.Fields)
        {
            if (!initializedFields.Add(fieldInitializer.Name))
            {
                errors.Add($"Struct initializer for '{declaration.Name}' supplies field '{fieldInitializer.Name}' more than once");
            }

            if (type is StructType concreteStruct && !concreteStruct.Field(fieldInitializer.Name).HasValue)
            {
                errors.Add($"Struct '{type}' does not contain field '{fieldInitializer.Name}'");
            }

            errors.AddRange(AnalyzeExpression(fieldInitializer.Expression, scope, types, functions).Node.AllErrors);
        }

        return errors;
    }

    private static int ArrayLength(ExpressionSyntax expression)
    {
        return expression is ConstantSyntax constant && IntegerLiteral.TryParse(Convert.ToString(constant.Value), out var length)
            ? length
            : 0;
    }

    private AnalyzeResult<StatementNode> AnalyzeIfElse(IfElseSyntax ifElse, Scope scope, IReadOnlyDictionary<string, SymbolType> types)
    {
        return AnalyzeIfElse(ifElse, scope, types, new Dictionary<string, FunctionSyntax>());
    }

    private AnalyzeResult<StatementNode> AnalyzeIfElse(IfElseSyntax ifElse, Scope scope, IReadOnlyDictionary<string, SymbolType> types, IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        var cond = AnalyzeExpression(ifElse.Condition, scope, types, functions).Node;
        var thenBlock = AnalyzeBlock(ifElse.ThenBlock, scope, types, functions).Node;
        var elseBlock = ifElse.ElseBlock.Match(
            b => AnalyzeBlock(b, scope, types, functions).Node,
            () => null as BlockNode
        );
        var maybeElse = elseBlock is null ? Maybe<BlockNode>.None : Maybe.From(elseBlock);
        return new AnalyzeResult<StatementNode>(new IfElseNode(cond, thenBlock, maybeElse), scope);
    }

    private AnalyzeResult<StatementNode> AnalyzeDoWhile(DoWhileSyntax doWhileSyntax, Scope scope, IReadOnlyDictionary<string, SymbolType> types)
    {
        return AnalyzeDoWhile(doWhileSyntax, scope, types, new Dictionary<string, FunctionSyntax>());
    }

    private AnalyzeResult<StatementNode> AnalyzeDoWhile(DoWhileSyntax doWhileSyntax, Scope scope, IReadOnlyDictionary<string, SymbolType> types, IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        var body = AnalyzeBlock(doWhileSyntax.Body, scope, types, functions).Node;
        var condition = AnalyzeExpression(doWhileSyntax.Condition, scope, types, functions).Node;
        return new AnalyzeResult<StatementNode>(new DoWhileNode(body, condition), scope);
    }

    private AnalyzeResult<StatementNode> AnalyzeLoop(LoopSyntax loopSyntax, Scope scope, IReadOnlyDictionary<string, SymbolType> types)
    {
        return AnalyzeLoop(loopSyntax, scope, types, new Dictionary<string, FunctionSyntax>());
    }

    private AnalyzeResult<StatementNode> AnalyzeLoop(LoopSyntax loopSyntax, Scope scope, IReadOnlyDictionary<string, SymbolType> types, IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        var body = AnalyzeBlock(loopSyntax.Body, scope, types, functions).Node;
        return new AnalyzeResult<StatementNode>(new LoopNode(body), scope);
    }

    private AnalyzeResult<StatementNode> AnalyzeFor(ForSyntax forSyntax, Scope scope, IReadOnlyDictionary<string, SymbolType> types)
    {
        return AnalyzeFor(forSyntax, scope, types, new Dictionary<string, FunctionSyntax>());
    }

    private AnalyzeResult<StatementNode> AnalyzeFor(ForSyntax forSyntax, Scope scope, IReadOnlyDictionary<string, SymbolType> types, IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        var forScope = new Scope(scope);
        var initializer = forSyntax.Initializer.Match(
            statement =>
            {
                var result = AnalyzeStatement(statement, forScope, types, functions);
                forScope = result.Scope;
                return Maybe.From(result.Node);
            },
            () => Maybe<StatementNode>.None);
        var condition = forSyntax.Condition.Map(expression => AnalyzeExpression(expression, forScope, types, functions).Node);
        var body = AnalyzeBlock(forSyntax.Body, forScope, types, functions).Node;
        var increment = forSyntax.Increment.Map(expression => AnalyzeExpression(expression, forScope, types, functions).Node);

        return new AnalyzeResult<StatementNode>(new ForNode(initializer, condition, increment, body), scope);
    }

    private AnalyzeResult<StatementNode> AnalyzeReturn(ReturnSyntax ret, Scope scope, IReadOnlyDictionary<string, SymbolType> types)
    {
        return AnalyzeReturn(ret, scope, types, new Dictionary<string, FunctionSyntax>());
    }

    private AnalyzeResult<StatementNode> AnalyzeReturn(ReturnSyntax ret, Scope scope, IReadOnlyDictionary<string, SymbolType> types, IReadOnlyDictionary<string, FunctionSyntax> functions)
    {
        var maybeExpr = ret.Expression.Map(expr => AnalyzeExpression(expr, scope, types, functions).Node);
        return new AnalyzeResult<StatementNode>(new ReturnNode(maybeExpr), scope);
    }
}
