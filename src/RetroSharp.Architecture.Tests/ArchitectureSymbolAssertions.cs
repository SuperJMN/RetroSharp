using System.Reflection;
using System.Reflection.Emit;
using RetroSharp.Core.Sdk;

namespace RetroSharp.Architecture.Tests;

internal static class ArchitectureSymbolAssertions
{
    private const BindingFlags DeclaredMethods =
        BindingFlags.Public |
        BindingFlags.NonPublic |
        BindingFlags.Instance |
        BindingFlags.Static |
        BindingFlags.DeclaredOnly;

    private static readonly IReadOnlyDictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opCode => opCode.Value);

    public static void AssertSdkOperationOwnership(
        Assembly assembly,
        string ownerTypeName,
        string runtimeTypeName)
    {
        var owner = RequiredType(assembly, ownerTypeName);
        var runtime = RequiredType(assembly, runtimeTypeName);
        var operationEntryPoints = assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(DeclaredMethods))
            .Where(method =>
                method.ReturnType == typeof(void) &&
                method.GetParameters() is [{ ParameterType: var parameterType }] &&
                parameterType == typeof(Sdk2DOperation))
            .ToList();

        var operationEntryPoint = Assert.Single(operationEntryPoints.Where(method => method.DeclaringType == owner));
        Assert.Equal(owner, operationEntryPoint.DeclaringType);
        Assert.DoesNotContain(
            CalledMethods(owner),
            method => method.DeclaringType == runtime);

        Assert.All(
            operationEntryPoints.Where(method => method.DeclaringType != owner),
            router =>
            {
                Assert.Equal(runtime, router.DeclaringType);
                var routerCalls = CalledMethods(router).ToList();
                var delegatedCall = Assert.Single(routerCalls);
                Assert.Equal(owner, delegatedCall.DeclaringType);
                Assert.Equal(operationEntryPoint.MetadataToken, delegatedCall.MetadataToken);
            });
        Assert.Contains(CalledMethods(runtime), method =>
            method.DeclaringType == owner &&
            method.MetadataToken == operationEntryPoint.MetadataToken);
    }

    public static void AssertRuntimeMemoryOwnership(
        Assembly assembly,
        string layoutTypeName,
        params string[] allowedByteConstants)
    {
        var layout = RequiredType(assembly, layoutTypeName);
        var allowedByteConstantSet = allowedByteConstants.ToHashSet(StringComparer.Ordinal);
        var reservedRanges = RequiredEnumerableProperty(layout, "ReservedRanges")
            .Cast<object>()
            .Select(RangeBounds)
            .ToList();
        var aliasRanges = layout
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(property => property.Name.StartsWith("Intentional", StringComparison.Ordinal))
            .SelectMany(property => property.GetValue(null) is System.Collections.IEnumerable values
                ? values.Cast<object>()
                : [])
            .Select(alias => alias.GetType().GetProperty("Alias")?.GetValue(alias))
            .Where(alias => alias is not null)
            .Select(alias => RangeBounds(alias!))
            .ToHashSet();
        var canonicalRanges = reservedRanges
            .Where(range => !aliasRanges.Contains(range))
            .ToList();
        var namedAddressValues = RequiredEnumerableProperty(layout, "NamedAddresses")
            .Cast<object>()
            .Select(address => Convert.ToInt64(address.GetType().GetProperty("Address")?.GetValue(address)))
            .ToHashSet();
        var byteAddressRanges = canonicalRanges
            .Where(range => namedAddressValues.Any(address => address >= range.Start && address < range.EndExclusive))
            .ToList();

        var leakedFields = assembly
            .GetTypes()
            .Where(type => !IsOwnedBy(type, layout))
            .SelectMany(type => type.GetFields(DeclaredMethods))
            .Where(field => field.IsLiteral && !field.IsSpecialName)
            .Select(field => (Field: field, Value: NumericConstant(field)))
            .Where(candidate => candidate.Value.HasValue)
            .Where(candidate =>
                candidate.Field.FieldType != typeof(byte) ||
                !allowedByteConstantSet.Contains(FieldId(candidate.Field)))
            .Where(candidate =>
                candidate.Field.FieldType == typeof(ushort) &&
                canonicalRanges.Any(range => candidate.Value >= range.Start && candidate.Value < range.EndExclusive) ||
                candidate.Field.FieldType == typeof(byte) &&
                byteAddressRanges.Any(range => candidate.Value >= range.Start && candidate.Value < range.EndExclusive))
            .ToList();

        Assert.True(
            leakedFields.Count == 0,
            $"Runtime-memory constants escaped '{layout.FullName}':{Environment.NewLine}" +
            string.Join(Environment.NewLine, leakedFields.Select(candidate =>
                $"{FieldId(candidate.Field)}=0x{candidate.Value:X4}")));
    }

    public static void AssertExclusiveFrontendPreparation(
        Type preparation,
        IReadOnlyCollection<Type> preparationStages,
        params Type[] targetCompilers)
    {
        var stageSet = preparationStages.ToHashSet();
        var preparationCalls = CalledMethods(preparation);
        Assert.All(
            preparationStages,
            stage => Assert.Contains(preparationCalls, call => call.DeclaringType == stage));
        Assert.All(
            targetCompilers,
            targetCompiler => Assert.DoesNotContain(
                CalledMethods(targetCompiler),
                call => call.DeclaringType is not null && stageSet.Contains(call.DeclaringType)));
    }

    private static bool IsOwnedBy(Type candidate, Type owner)
    {
        for (var current = candidate; current is not null; current = current.DeclaringType)
        {
            if (current == owner)
            {
                return true;
            }
        }

        return false;
    }

    private static long? NumericConstant(FieldInfo field)
    {
        if (field.FieldType != typeof(byte) && field.FieldType != typeof(ushort))
        {
            return null;
        }

        return Convert.ToInt64(field.GetRawConstantValue());
    }

    private static string FieldId(FieldInfo field) => $"{field.DeclaringType?.FullName}.{field.Name}";

    private static System.Collections.IEnumerable RequiredEnumerableProperty(Type owner, string name)
    {
        var property = owner.GetProperty(
            name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(property);
        return Assert.IsAssignableFrom<System.Collections.IEnumerable>(property.GetValue(null));
    }

    private static (long Start, long EndExclusive) RangeBounds(object range)
    {
        var type = range.GetType();
        return (
            Convert.ToInt64(type.GetProperty("Start")?.GetValue(range)),
            Convert.ToInt64(type.GetProperty("EndExclusive")?.GetValue(range)));
    }

    public static void AssertDomainStateOwnership(
        Type root,
        Type rootState,
        IReadOnlyCollection<Type> domainStates)
    {
        var rootFields = rootState.GetFields(DeclaredMethods);
        Assert.DoesNotContain(rootFields, field => IsMutableCollection(field.FieldType));
        Assert.All(
            domainStates,
            domainState => Assert.Contains(rootFields, field => field.FieldType == domainState));

        var domainFactTypes = domainStates
            .SelectMany(domainState => domainState.GetFields(DeclaredMethods))
            .Where(field => IsMutableCollection(field.FieldType))
            .SelectMany(field => field.FieldType.GetGenericArguments())
            .Where(type => type.Assembly == root.Assembly && type != rootState && !domainStates.Contains(type))
            .ToHashSet();

        Assert.All(
            domainStates,
            domainState => Assert.Contains(
                domainState.GetFields(DeclaredMethods),
                field => IsMutableCollection(field.FieldType)));
        Assert.DoesNotContain(
            root.GetMethods(DeclaredMethods),
            method => SignatureTypes(method).Any(domainFactTypes.Contains));
        Assert.DoesNotContain(
            CalledMethods(root),
            method => method.DeclaringType is not null && domainStates.Contains(method.DeclaringType));
        Assert.DoesNotContain(
            ReferencedFields(root),
            field => field.DeclaringType is not null && domainStates.Contains(field.DeclaringType));
    }

    public static Type RequiredNestedType(Type owner, string name)
    {
        return owner.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new Xunit.Sdk.XunitException($"Type '{owner.FullName}' must declare nested type '{name}'.");
    }

    public static IReadOnlyList<MethodBase> CalledMethods(Type source)
    {
        var executableMembers = source
            .GetMethods(DeclaredMethods)
            .Cast<MethodBase>()
            .Concat(source.GetConstructors(DeclaredMethods))
            .Concat(source.TypeInitializer is { } typeInitializer ? [typeInitializer] : [])
            .DistinctBy(member => (member.Module, member.MetadataToken));
        return executableMembers
            .SelectMany(CalledMethods)
            .ToList();
    }

    public static IReadOnlyList<FieldInfo> ReferencedFields(Type source)
    {
        var executableMembers = source
            .GetMethods(DeclaredMethods)
            .Cast<MethodBase>()
            .Concat(source.GetConstructors(DeclaredMethods))
            .Concat(source.TypeInitializer is { } typeInitializer ? [typeInitializer] : [])
            .DistinctBy(member => (member.Module, member.MetadataToken));
        return executableMembers
            .SelectMany(ReferencedFields)
            .ToList();
    }

    public static bool IsMutableCollection(Type type)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        var definition = type.GetGenericTypeDefinition();
        return definition == typeof(Dictionary<,>) ||
               definition == typeof(List<>) ||
               definition == typeof(HashSet<>) ||
               definition == typeof(IDictionary<,>) ||
               definition == typeof(IList<>) ||
               definition == typeof(ISet<>);
    }

    private static IEnumerable<Type> SignatureTypes(MethodInfo method)
    {
        yield return Unwrap(method.ReturnType);
        foreach (var parameter in method.GetParameters())
        {
            yield return Unwrap(parameter.ParameterType);
        }
    }

    private static Type Unwrap(Type type)
    {
        return type.IsByRef || type.IsPointer || type.IsArray
            ? type.GetElementType()!
            : type;
    }

    public static Type RequiredType(Assembly assembly, string name)
    {
        return assembly.GetType(name, throwOnError: false)
            ?? throw new Xunit.Sdk.XunitException($"Assembly '{assembly.GetName().Name}' must declare type '{name}'.");
    }

    public static IEnumerable<MethodBase> CalledMethods(MethodBase source)
    {
        var body = source.GetMethodBody();
        var bytes = body?.GetILAsByteArray();
        if (bytes is null)
        {
            yield break;
        }

        var index = 0;
        while (index < bytes.Length)
        {
            var opCode = ReadOpCode(bytes, ref index);
            if (opCode.OperandType == OperandType.InlineMethod)
            {
                var token = BitConverter.ToInt32(bytes, index);
                MethodBase? called = null;
                try
                {
                    called = source.Module.ResolveMethod(
                        token,
                        source.DeclaringType?.GetGenericArguments(),
                        MethodGenericArguments(source));
                }
                catch (ArgumentException)
                {
                    // A malformed or unavailable generic instantiation is irrelevant to the
                    // ownership edge; the rest of the method body can still be inspected.
                }

                if (called is not null)
                {
                    yield return called;
                }
            }

            index += OperandSize(opCode.OperandType, bytes, index);
        }
    }

    private static IEnumerable<FieldInfo> ReferencedFields(MethodBase source)
    {
        var body = source.GetMethodBody();
        var bytes = body?.GetILAsByteArray();
        if (bytes is null)
        {
            yield break;
        }

        var index = 0;
        while (index < bytes.Length)
        {
            var opCode = ReadOpCode(bytes, ref index);
            if (opCode.OperandType == OperandType.InlineField)
            {
                var token = BitConverter.ToInt32(bytes, index);
                FieldInfo? field = null;
                try
                {
                    field = source.Module.ResolveField(
                        token,
                        source.DeclaringType?.GetGenericArguments(),
                        MethodGenericArguments(source));
                }
                catch (ArgumentException)
                {
                    // Continue inspecting the remaining field edges when a generic
                    // instantiation cannot be resolved in this reflection context.
                }

                if (field is not null)
                {
                    yield return field;
                }
            }

            index += OperandSize(opCode.OperandType, bytes, index);
        }
    }

    private static OpCode ReadOpCode(byte[] bytes, ref int index)
    {
        short value = bytes[index++];
        if (value == 0xFE)
        {
            value = unchecked((short)(0xFE00 | bytes[index++]));
        }

        return OpCodesByValue[value];
    }

    private static Type[] MethodGenericArguments(MethodBase source)
    {
        return source is MethodInfo method ? method.GetGenericArguments() : Type.EmptyTypes;
    }

    private static int OperandSize(OperandType operandType, byte[] bytes, int index)
    {
        return operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineBrTarget or
            OperandType.InlineField or
            OperandType.InlineI or
            OperandType.InlineMethod or
            OperandType.InlineSig or
            OperandType.InlineString or
            OperandType.InlineTok or
            OperandType.InlineType or
            OperandType.ShortInlineR => 4,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + (BitConverter.ToInt32(bytes, index) * 4),
            _ => throw new InvalidOperationException($"Unsupported IL operand type '{operandType}'."),
        };
    }
}
