using RetroSharp.Core;

namespace RetroSharp.Parser;

public sealed record LetTypeInferenceResult(ProgramSyntax Program, IReadOnlyList<string> Errors);

public static class LetTypeInference
{
    internal const string UnresolvedTypeName = "<inferred>";

    private static readonly HashSet<string> ComparisonOperators =
    [
        "==", "!=", "<", "<=", ">", ">=", "&&", "||",
    ];

    public static LetTypeInferenceResult Resolve(ProgramSyntax program)
    {
        program = TypeAliasResolver.Resolve(program);
        var errors = new List<string>();
        var structs = program.Structs.ToDictionary(
            syntax => syntax.Name,
            syntax => (IReadOnlyDictionary<string, string>)syntax.Fields.ToDictionary(field => field.Name, field => field.Type, StringComparer.Ordinal),
            StringComparer.Ordinal);
        var enumTypes = program.Enums.Select(syntax => syntax.Name).ToHashSet(StringComparer.Ordinal);
        var functions = program.Functions
            .GroupBy(function => function.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var globals = new TypeScope();
        foreach (var constant in program.Constants)
        {
            globals.Declare(constant.Name, constant.Type);
        }

        var resolver = new Resolver(structs, enumTypes, functions, errors);
        var resolvedFunctions = program.Functions
            .Select(function => resolver.ResolveFunction(function, globals))
            .ToList();
        var resolvedProgram = new ProgramSyntax(
            program.Imports,
            program.TypeAliases,
            program.Constants,
            program.Enums,
            program.Structs,
            resolvedFunctions);
        return new LetTypeInferenceResult(resolvedProgram, errors);
    }

    public static ProgramSyntax ResolveOrThrow(ProgramSyntax program)
    {
        var inference = Resolve(program);
        if (inference.Errors.Count != 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, inference.Errors));
        }

        return inference.Program;
    }

    private sealed class Resolver(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> structs,
        IReadOnlySet<string> enumTypes,
        IReadOnlyDictionary<string, List<FunctionSyntax>> functions,
        List<string> errors)
    {
        public FunctionSyntax ResolveFunction(FunctionSyntax function, TypeScope globals)
        {
            var scope = globals.CreateChild();
            foreach (var parameter in function.Parameters)
            {
                scope.Declare(parameter.Name, parameter.Type);
            }

            return new FunctionSyntax(
                function.Type,
                function.Name,
                function.Parameters,
                ResolveBlock(function.Block, scope),
                function.IsExpressionBodied,
                function.IsInline,
                function.IsPure,
                function.IsExtern,
                function.Attributes);
        }

        private BlockSyntax ResolveBlock(BlockSyntax block, TypeScope scope)
        {
            var statements = new List<StatementSyntax>();
            foreach (var statement in block.Statements)
            {
                statements.Add(ResolveStatement(statement, scope));
            }

            return new BlockSyntax(statements);
        }

        private StatementSyntax ResolveStatement(StatementSyntax statement, TypeScope scope)
        {
            switch (statement)
            {
                case ConstDeclarationSyntax constant:
                    scope.Declare(constant.Name, constant.Type);
                    return constant;
                case DeclarationSyntax declaration:
                    return ResolveDeclaration(declaration, scope);
                case IfElseSyntax ifElse:
                    return new IfElseSyntax(
                        ifElse.Condition,
                        ResolveBlock(ifElse.ThenBlock, scope.CreateChild()),
                        ifElse.ElseBlock.Map(block => ResolveBlock(block, scope.CreateChild())));
                case WhileSyntax whileSyntax:
                    return new WhileSyntax(whileSyntax.Condition, ResolveBlock(whileSyntax.Body, scope.CreateChild()));
                case DoWhileSyntax doWhileSyntax:
                    return new DoWhileSyntax(ResolveBlock(doWhileSyntax.Body, scope.CreateChild()), doWhileSyntax.Condition);
                case RangeForSyntax rangeForSyntax:
                    {
                        var loopScope = scope.CreateChild();
                        loopScope.Declare(rangeForSyntax.Identifier, rangeForSyntax.Type);
                        return new RangeForSyntax(
                            rangeForSyntax.Type,
                            rangeForSyntax.Identifier,
                            rangeForSyntax.Start,
                            rangeForSyntax.End,
                            ResolveBlock(rangeForSyntax.Body, loopScope));
                    }
                case ForSyntax forSyntax:
                    {
                        var loopScope = scope.CreateChild();
                        return new ForSyntax(
                            forSyntax.Initializer.Map(initializer => ResolveStatement(initializer, loopScope)),
                            forSyntax.Condition,
                            forSyntax.Increment,
                            ResolveBlock(forSyntax.Body, loopScope.CreateChild()));
                    }
                case SwitchSyntax switchSyntax:
                    return new SwitchSyntax(
                        switchSyntax.Subject,
                        switchSyntax.Cases
                            .Select(switchCase => new SwitchCaseSyntax(
                                switchCase.Patterns,
                                ResolveBlock(switchCase.Block, scope.CreateChild())))
                            .ToList(),
                        switchSyntax.DefaultBlock.Map(block => ResolveBlock(block, scope.CreateChild())));
                default:
                    return statement;
            }
        }

        private DeclarationSyntax ResolveDeclaration(DeclarationSyntax declaration, TypeScope scope)
        {
            var type = declaration.Type;
            if (declaration.IsImmutable && type == UnresolvedTypeName)
            {
                if (!declaration.Initialization.HasValue)
                {
                    errors.Add($"Cannot infer type of let '{declaration.Name}': an initializer is required.");
                    type = "u8";
                }
                else
                {
                    var inferred = InferExpressionType(declaration.Initialization.Value, scope);
                    if (inferred.Error is not null)
                    {
                        errors.Add(inferred.Error == MixedWordTypes
                            ? $"Cannot infer type of let '{declaration.Name}': expression mixes i16 and u16 word values; add an explicit cast."
                            : $"Cannot infer type of let '{declaration.Name}' safely from {inferred.Error}; add an explicit cast.");
                        type = "u8";
                    }
                    else
                    {
                        type = inferred.Type!;
                    }
                }
            }

            var resolved = new DeclarationSyntax(
                type,
                declaration.Name,
                declaration.ArrayLength,
                declaration.Initialization,
                declaration.IsImmutable);
            scope.Declare(declaration.Name, type);
            return resolved;
        }

        private TypeResult InferExpressionType(ExpressionSyntax expression, TypeScope scope)
        {
            switch (expression)
            {
                case ConstantSyntax constant:
                    return IntegerLiteral.TryGetSuffixType(Convert.ToString(constant.Value), out var suffixType)
                        ? TypeResult.Success(suffixType)
                        : TypeResult.Success("u8");
                case IdentifierSyntax { Identifier: "true" or "false" }:
                    return TypeResult.Success("bool");
                case IdentifierSyntax identifier:
                    return scope.TryGet(identifier.Identifier, out var identifierType)
                        ? TypeResult.Success(ScalarInferenceType(identifierType))
                        : TypeResult.Failure($"unknown symbol '{identifier.Identifier}'");
                case MemberAccessSyntax memberAccess:
                    return InferMemberType(memberAccess, scope);
                case IndexExpressionSyntax indexExpression:
                    return scope.TryGet(indexExpression.BaseIdentifier, out var elementType)
                        ? TypeResult.Success(ScalarInferenceType(elementType))
                        : TypeResult.Failure($"unknown indexed symbol '{indexExpression.BaseIdentifier}'");
                case CastSyntax cast:
                    return IsScalarType(cast.Type)
                        ? TypeResult.Success(cast.Type)
                        : TypeResult.Failure($"non-scalar cast type '{cast.Type}'");
                case UnaryExpressionSyntax unary:
                    return unary.OperatorSymbol == "!"
                        ? TypeResult.Success("bool")
                        : InferExpressionType(unary.Operand, scope);
                case BinaryExpressionSyntax binary:
                    return InferBinaryType(binary, scope);
                case ConditionalExpressionSyntax conditional:
                    return CombineTypes(
                        [InferExpressionType(conditional.WhenTrue, scope), InferExpressionType(conditional.WhenFalse, scope)],
                        preserveBool: true);
                case SwitchExpressionSyntax switchExpression:
                    {
                        var values = switchExpression.Arms.Select(arm => InferExpressionType(arm.Value, scope)).ToList();
                        if (switchExpression.DefaultValue.HasValue)
                        {
                            values.Add(InferExpressionType(switchExpression.DefaultValue.Value, scope));
                        }

                        return CombineTypes(values, preserveBool: true);
                    }
                case FunctionCall call:
                    return InferFunctionReturnType(call.Name);
                case QualifiedCallSyntax qualifiedCall:
                    return ReceiverMethodLowerer.TryLower(qualifiedCall, functions.Values.SelectMany(candidates => candidates), out _, out var receiverFunction)
                        ? TypeResult.Success(ScalarInferenceType(receiverFunction.Type))
                        : InferFunctionReturnType(qualifiedCall.Method);
                case PipelineExpressionSyntax pipeline:
                    return pipeline.Steps.Count == 0
                        ? InferExpressionType(pipeline.Value, scope)
                        : InferFunctionReturnType(pipeline.Steps[^1].FunctionName);
                case NamedArgumentSyntax namedArgument:
                    return InferExpressionType(namedArgument.Expression, scope);
                case SizeOfSyntax or OffsetOfSyntax or CountOfSyntax:
                    return TypeResult.Success("u8");
                case AssignmentSyntax assignment:
                    return InferLValueType(assignment.Left, scope);
                case PostfixMutationSyntax postfix:
                    return InferLValueType(postfix.Target, scope);
                default:
                    return TypeResult.Failure($"unsupported expression '{expression.GetType().Name}'");
            }
        }

        private TypeResult InferBinaryType(BinaryExpressionSyntax binary, TypeScope scope)
        {
            if (ComparisonOperators.Contains(binary.Operator.Symbol))
            {
                return TypeResult.Success("bool");
            }

            var left = InferExpressionType(binary.Left, scope);
            if (binary.Operator.Symbol is "<<" or ">>")
            {
                return left.Error is null ? TypeResult.Success(ScalarInferenceType(left.Type!)) : left;
            }

            return CombineTypes([left, InferExpressionType(binary.Right, scope)], preserveBool: false);
        }

        private TypeResult InferMemberType(MemberAccessSyntax memberAccess, TypeScope scope)
        {
            if (TryGetMemberPath(memberAccess, out var memberPath) && scope.TryGet(memberPath, out var staticType))
            {
                return TypeResult.Success(ScalarInferenceType(staticType));
            }

            if (memberAccess.Target is IdentifierSyntax enumIdentifier && enumTypes.Contains(enumIdentifier.Identifier))
            {
                return TypeResult.Success("u8");
            }

            var target = InferExpressionType(memberAccess.Target, scope);
            if (target.Error is not null)
            {
                return target;
            }

            if (!structs.TryGetValue(target.Type!, out var fields) || !fields.TryGetValue(memberAccess.Member, out var fieldType))
            {
                return TypeResult.Failure($"member '{memberAccess.Member}' without a known scalar field type");
            }

            return TypeResult.Success(ScalarInferenceType(fieldType));
        }

        private TypeResult InferFunctionReturnType(string name)
        {
            if (!functions.TryGetValue(name, out var candidates))
            {
                return TypeResult.Failure($"unknown function '{name}'");
            }

            var returnTypes = candidates.Select(candidate => candidate.Type).Distinct(StringComparer.Ordinal).ToList();
            return returnTypes.Count == 1 && IsScalarType(returnTypes[0])
                ? TypeResult.Success(ScalarInferenceType(returnTypes[0]))
                : TypeResult.Failure($"function '{name}' without one known scalar return type");
        }

        private TypeResult InferLValueType(LValue lValue, TypeScope scope)
        {
            return lValue switch
            {
                IdentifierLValue identifier when scope.TryGet(identifier.Identifier, out var type) => TypeResult.Success(ScalarInferenceType(type)),
                IndexLValue index when scope.TryGet(index.BaseIdentifier, out var type) => TypeResult.Success(ScalarInferenceType(type)),
                MemberAccessLValue member => InferMemberType(member.MemberAccess, scope),
                _ => TypeResult.Failure("an lvalue without a known scalar type"),
            };
        }

        private static TypeResult CombineTypes(IReadOnlyList<TypeResult> types, bool preserveBool)
        {
            var error = types.FirstOrDefault(type => type.Error is not null);
            if (error?.Error is not null)
            {
                return error;
            }

            var wordTypes = types
                .Select(type => type.Type!)
                .Where(IsWordType)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (wordTypes.Count > 1)
            {
                return TypeResult.Failure(MixedWordTypes);
            }

            if (wordTypes.Count == 1)
            {
                return TypeResult.Success(wordTypes[0]);
            }

            return preserveBool && types.Count > 0 && types.All(type => type.Type == "bool")
                ? TypeResult.Success("bool")
                : TypeResult.Success("u8");
        }

        private static string ScalarInferenceType(string type)
        {
            return IsWordType(type) ? type : type == "bool" ? "bool" : IsScalarType(type) ? "u8" : type;
        }

        private static bool TryGetMemberPath(MemberAccessSyntax memberAccess, out string path)
        {
            if (memberAccess.Target is IdentifierSyntax identifier)
            {
                path = $"{identifier.Identifier}.{memberAccess.Member}";
                return true;
            }

            if (memberAccess.Target is MemberAccessSyntax parent && TryGetMemberPath(parent, out var parentPath))
            {
                path = $"{parentPath}.{memberAccess.Member}";
                return true;
            }

            path = string.Empty;
            return false;
        }
    }

    private sealed class TypeScope(TypeScope? parent = null)
    {
        private readonly Dictionary<string, string> symbols = new(StringComparer.Ordinal);

        public TypeScope CreateChild() => new(this);

        public void Declare(string name, string type) => symbols[name] = type;

        public bool TryGet(string name, out string type)
        {
            if (symbols.TryGetValue(name, out type!))
            {
                return true;
            }

            if (parent is not null)
            {
                return parent.TryGet(name, out type!);
            }

            type = string.Empty;
            return false;
        }
    }

    private sealed record TypeResult(string? Type, string? Error)
    {
        public static TypeResult Success(string type) => new(type, null);
        public static TypeResult Failure(string error) => new(null, error);
    }

    private const string MixedWordTypes = "mixed i16/u16 word types";

    private static bool IsWordType(string type) => type is "i16" or "u16";

    private static bool IsScalarType(string type) => type is "i8" or "u8" or "i16" or "u16" or "bool";
}
