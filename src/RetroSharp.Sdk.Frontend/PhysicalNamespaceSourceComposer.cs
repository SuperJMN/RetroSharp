namespace RetroSharp.Sdk;

using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using RetroSharp.Parser;
using static RetroSharp.Parser.RetroSharpParser;

public static class PhysicalNamespaceSourceComposer
{
    public static string Compose(
        IEnumerable<PhysicalNamespaceSourceFile> files,
        string rootNamespace,
        string sourceRoot)
    {
        return Compose(new PhysicalNamespaceSourceGroup(files.ToArray(), rootNamespace, sourceRoot));
    }

    public static string Compose(PhysicalNamespaceSourceGroup sourceGroup)
    {
        return Compose([sourceGroup], []);
    }

    public static string Compose(
        IEnumerable<PhysicalNamespaceSourceGroup> sourceGroups,
        IEnumerable<PhysicalNamespaceSourceFile> consumerFiles)
    {
        var normalizedSourceGroups = sourceGroups
            .Select(NormalizeSourceGroup)
            .ToList();
        var openNamespaces = normalizedSourceGroups
            .Select(sourceGroup => sourceGroup.RootNamespace)
            .ToArray();
        var physicalUnits = normalizedSourceGroups
            .SelectMany(CreatePhysicalUnits)
            .ToList();
        var consumerUnits = consumerFiles
            .Select(file => SourceUnit.FromConsumer(file, openNamespaces))
            .ToList();
        var physicalDefinitions = physicalUnits.SelectMany(CollectDefinitions).ToList();
        var consumerDefinitions = consumerUnits.SelectMany(CollectDefinitions).ToList();
        var physicalResolver = new DefinitionResolver(physicalDefinitions);
        var consumerResolver = new DefinitionResolver(physicalDefinitions.Concat(consumerDefinitions).ToList());

        return string.Concat(physicalUnits.Select(unit => Rewrite(unit, physicalResolver)))
               + string.Concat(consumerUnits.Select(unit => Rewrite(unit, consumerResolver)));
    }

    private static IEnumerable<Definition> CollectDefinitions(SourceUnit unit)
    {
        foreach (var declaration in unit.Program.classDeclaration())
        {
            yield return Definition.Type(unit, declaration.IDENTIFIER().GetText(), unit.InternalName(declaration.IDENTIFIER().GetText()));
        }

        foreach (var declaration in unit.Program.structDeclaration())
        {
            yield return Definition.Type(unit, declaration.IDENTIFIER().GetText(), unit.InternalName(declaration.IDENTIFIER().GetText()));
        }

        foreach (var declaration in unit.Program.enumDeclaration())
        {
            yield return Definition.Type(unit, declaration.IDENTIFIER().GetText(), unit.InternalName(declaration.IDENTIFIER().GetText()));
        }

        foreach (var declaration in unit.Program.typeAliasDeclaration())
        {
            yield return Definition.Type(unit, declaration.IDENTIFIER().GetText(), unit.InternalName(declaration.IDENTIFIER().GetText()));
        }

        foreach (var declaration in unit.Program.function())
        {
            var name = declaration.IDENTIFIER().GetText();
            yield return Definition.Function(unit, name, name == "Main" ? name : unit.InternalName(name));
        }

        foreach (var declaration in unit.Program.externFunction())
        {
            var name = declaration.IDENTIFIER().GetText();
            yield return Definition.Function(unit, name, unit.InternalName(name));
        }

        foreach (var declaration in unit.Program.constDeclaration())
        {
            yield return Definition.Constant(unit, declaration.IDENTIFIER().GetText(), unit.InternalName(declaration.IDENTIFIER().GetText()));
        }
    }

    private static string Rewrite(SourceUnit unit, DefinitionResolver resolver)
    {
        var replacements = new List<TextReplacement>();

        foreach (var declaration in unit.Program.usingDeclaration())
        {
            AddRangeReplacement(replacements, declaration.Start, declaration.Stop, string.Empty);
        }

        foreach (var declaration in unit.Program.classDeclaration())
        {
            AddDeclarationReplacement(replacements, unit, declaration.IDENTIFIER().Symbol);
        }

        foreach (var declaration in unit.Program.structDeclaration())
        {
            AddDeclarationReplacement(replacements, unit, declaration.IDENTIFIER().Symbol);
        }

        foreach (var declaration in unit.Program.enumDeclaration())
        {
            AddDeclarationReplacement(replacements, unit, declaration.IDENTIFIER().Symbol);
        }

        foreach (var declaration in unit.Program.typeAliasDeclaration())
        {
            AddDeclarationReplacement(replacements, unit, declaration.IDENTIFIER().Symbol);
        }

        foreach (var declaration in unit.Program.function())
        {
            var name = declaration.IDENTIFIER().GetText();
            if (name != "Main" && unit.RewriteDeclarations)
            {
                AddTokenReplacement(replacements, declaration.IDENTIFIER().Symbol, unit.InternalName(name));
            }
        }

        foreach (var declaration in unit.Program.externFunction())
        {
            AddDeclarationReplacement(replacements, unit, declaration.IDENTIFIER().Symbol);
        }

        foreach (var declaration in unit.Program.constDeclaration())
        {
            AddDeclarationReplacement(replacements, unit, declaration.IDENTIFIER().Symbol);
        }

        foreach (var type in Descendants<TypeContext>(unit.Program))
        {
            AddTypeReplacement(replacements, unit, resolver, type);
        }

        foreach (var call in Descendants<FunctionCallContext>(unit.Program))
        {
            var identifier = call.IDENTIFIER();
            if (resolver.ResolveUnqualified(unit, identifier.GetText(), DefinitionKind.Function) is { } resolved)
            {
                AddTokenReplacement(replacements, identifier.Symbol, resolved);
            }
        }

        foreach (var call in Descendants<SdkDotCallContext>(unit.Program))
        {
            AddSdkDotCallReplacement(replacements, unit, resolver, call);
        }

        foreach (var memberAccess in Descendants<MemberAccessContext>(unit.Program))
        {
            AddMemberAccessReplacement(replacements, unit, resolver, memberAccess);
        }

        return ApplyReplacements(unit.Source, replacements);
    }

    private static void AddDeclarationReplacement(List<TextReplacement> replacements, SourceUnit unit, IToken token)
    {
        if (unit.RewriteDeclarations)
        {
            AddTokenReplacement(replacements, token, unit.InternalName(token.Text));
        }
    }

    private static void AddTypeReplacement(
        List<TextReplacement> replacements,
        SourceUnit unit,
        DefinitionResolver resolver,
        TypeContext type)
    {
        if (type.qualifiedIdentifier() is not { } qualifiedIdentifier)
        {
            return;
        }

        var identifiers = qualifiedIdentifier.IDENTIFIER()
            .Select(identifier => identifier.Symbol)
            .ToArray();
        if (ResolvePath(unit, resolver, identifiers, DefinitionKind.Type) is { } resolved)
        {
            AddRangeReplacement(replacements, identifiers[0], identifiers[^1], resolved);
        }
    }

    private static void AddSdkDotCallReplacement(
        List<TextReplacement> replacements,
        SourceUnit unit,
        DefinitionResolver resolver,
        SdkDotCallContext call)
    {
        var identifiers = call.IDENTIFIER()
            .Select(identifier => identifier.Symbol)
            .ToArray();
        if (identifiers.Length < 2)
        {
            return;
        }

        if (ResolvePath(unit, resolver, identifiers, DefinitionKind.Function) is { } resolvedFunction)
        {
            AddRangeReplacement(replacements, identifiers[0], identifiers[^1], resolvedFunction);
            return;
        }

        for (var prefixLength = identifiers.Length - 1; prefixLength >= 1; prefixLength--)
        {
            if (ResolvePath(unit, resolver, identifiers.Take(prefixLength).ToArray(), DefinitionKind.Type) is not { } resolvedType)
            {
                continue;
            }

            AddRangeReplacement(replacements, identifiers[0], identifiers[prefixLength - 1], resolvedType);
            return;
        }
    }

    private static void AddMemberAccessReplacement(
        List<TextReplacement> replacements,
        SourceUnit unit,
        DefinitionResolver resolver,
        MemberAccessContext memberAccess)
    {
        if (memberAccess.GetChild(0) is IndexExpressionContext)
        {
            return;
        }

        var identifiers = memberAccess.IDENTIFIER()
            .Select(identifier => identifier.Symbol)
            .ToArray();
        if (identifiers.Length < 2)
        {
            return;
        }

        for (var prefixLength = identifiers.Length - 1; prefixLength >= 1; prefixLength--)
        {
            var path = identifiers.Take(prefixLength).Select(identifier => identifier.Text).ToArray();
            if (resolver.ResolveQualified(unit, path, DefinitionKind.Type) is not { } resolved)
            {
                continue;
            }

            AddRangeReplacement(replacements, identifiers[0], identifiers[prefixLength - 1], resolved);
            return;
        }
    }

    private static string? ResolvePath(
        SourceUnit unit,
        DefinitionResolver resolver,
        IReadOnlyList<IToken> identifiers,
        DefinitionKind kind)
    {
        var path = identifiers.Select(identifier => identifier.Text).ToArray();
        return resolver.ResolveQualified(unit, path, kind);
    }

    private static string ApplyReplacements(string source, IReadOnlyCollection<TextReplacement> replacements)
    {
        var ordered = replacements
            .Distinct()
            .OrderBy(replacement => replacement.Start)
            .ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Start <= ordered[i - 1].End)
            {
                if (ordered[i].Start == ordered[i - 1].Start
                    && ordered[i].End == ordered[i - 1].End
                    && ordered[i].Text == ordered[i - 1].Text)
                {
                    continue;
                }

                throw new InvalidOperationException("Physical namespace rewriting produced overlapping replacements.");
            }
        }

        var result = source;
        foreach (var replacement in ordered.OrderByDescending(replacement => replacement.Start))
        {
            result = result.Remove(replacement.Start, replacement.End - replacement.Start + 1)
                .Insert(replacement.Start, replacement.Text);
        }

        return result.EndsWith('\n') ? result : result + Environment.NewLine;
    }

    private static void AddTokenReplacement(List<TextReplacement> replacements, IToken token, string text)
    {
        AddRangeReplacement(replacements, token, token, text);
    }

    private static void AddRangeReplacement(List<TextReplacement> replacements, IToken start, IToken end, string text)
    {
        if (start.Text == text && start == end)
        {
            return;
        }

        replacements.Add(new TextReplacement(start.StartIndex, end.StopIndex, text));
    }

    private static IEnumerable<T> Descendants<T>(IParseTree tree)
        where T : IParseTree
    {
        if (tree is T match)
        {
            yield return match;
        }

        for (var i = 0; i < tree.ChildCount; i++)
        {
            foreach (var child in Descendants<T>(tree.GetChild(i)))
            {
                yield return child;
            }
        }
    }

    private static string NormalizeRootNamespace(string rootNamespace)
    {
        if (string.IsNullOrWhiteSpace(rootNamespace))
        {
            throw new InvalidOperationException("Physical namespace mode requires a root namespace.");
        }

        var segments = rootNamespace.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            throw new InvalidOperationException("Physical namespace mode requires a root namespace.");
        }

        return string.Join(".", segments.Select(NormalizeIdentifier));
    }

    private static string NamespaceForFile(string path, string rootNamespace, string sourceRoot)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? sourceRoot;
        var relativeDirectory = Path.GetRelativePath(sourceRoot, directory);
        if (relativeDirectory == "." || relativeDirectory.StartsWith("..", StringComparison.Ordinal))
        {
            return rootNamespace;
        }

        var segments = relativeDirectory
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .Select(NormalizePathSegment)
            .ToArray();
        return segments.Length == 0 ? rootNamespace : rootNamespace + "." + string.Join(".", segments);
    }

    private static IReadOnlyList<string> UsingNamespaces(ProgramContext program)
    {
        return program.usingDeclaration()
            .Select(declaration => declaration.qualifiedIdentifier().IDENTIFIER()
                .Select(identifier => NormalizePathSegment(identifier.GetText())))
            .Select(segments => string.Join(".", segments))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string NormalizePathSegment(string segment)
    {
        var parts = segment
            .Split(['-', '_', '.', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..])
            .ToArray();
        return NormalizeIdentifier(parts.Length == 0 ? segment : string.Concat(parts));
    }

    private static string NormalizeIdentifier(string value)
    {
        var filtered = new string(value.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrEmpty(filtered))
        {
            return "_";
        }

        if (!char.IsLetter(filtered[0]) && filtered[0] != '_')
        {
            filtered = "_" + filtered;
        }

        return filtered;
    }

    private static ProgramContext Parse(string source, string path)
    {
        var lexer = new RetroSharpLexer(CharStreams.fromString(source));
        var lexerListener = new ErrorListener<int>();
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(lexerListener);
        var tokenStream = new CommonTokenStream(lexer);
        var parser = new RetroSharpParser(tokenStream);
        var parserListener = new ErrorListener<IToken>();
        parser.RemoveErrorListeners();
        parser.AddErrorListener(parserListener);
        var program = parser.program();
        var errors = lexerListener.Errors
            .Select(error => error.ToString())
            .Concat(parserListener.Errors.Select(error => error.ToString()))
            .ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidOperationException($"Could not parse RetroSharp source '{path}' for physical namespace rewriting: {string.Join(Environment.NewLine, errors)}");
        }

        return program;
    }

    private static IEnumerable<SourceUnit> CreatePhysicalUnits(PhysicalNamespaceSourceGroup sourceGroup)
    {
        return sourceGroup.Files.Select(file => SourceUnit.FromPhysical(file, sourceGroup.RootNamespace, sourceGroup.SourceRoot));
    }

    private static PhysicalNamespaceSourceGroup NormalizeSourceGroup(PhysicalNamespaceSourceGroup sourceGroup)
    {
        return new PhysicalNamespaceSourceGroup(
            sourceGroup.Files,
            NormalizeRootNamespace(sourceGroup.RootNamespace),
            Path.GetFullPath(sourceGroup.SourceRoot));
    }

    private sealed record SourceUnit(
        string Path,
        string Source,
        string Namespace,
        ProgramContext Program,
        bool RewriteDeclarations,
        IReadOnlyList<string> OpenNamespaces)
    {
        public static SourceUnit FromPhysical(PhysicalNamespaceSourceFile file, string rootNamespace, string sourceRoot)
        {
            var path = System.IO.Path.GetFullPath(file.Path);
            var program = Parse(file.Source, path);
            return new SourceUnit(
                path,
                file.Source,
                NamespaceForFile(path, rootNamespace, sourceRoot),
                program,
                true,
                UsingNamespaces(program));
        }

        public static SourceUnit FromConsumer(PhysicalNamespaceSourceFile file, IReadOnlyList<string> openNamespaces)
        {
            var path = System.IO.Path.GetFullPath(file.Path);
            var program = Parse(file.Source, path);
            return new SourceUnit(
                path,
                file.Source,
                "__RetroSharpConsumer",
                program,
                false,
                openNamespaces.Concat(UsingNamespaces(program)).Distinct(StringComparer.Ordinal).ToArray());
        }

        public string InternalName(string name)
        {
            return RewriteDeclarations
                ? Namespace.Replace('.', '_') + "_" + name
                : name;
        }
    }

    private sealed record Definition(SourceUnit Unit, string Namespace, string Name, string InternalName, DefinitionKind Kind)
    {
        public static Definition Type(SourceUnit unit, string name, string internalName) =>
            new(unit, unit.Namespace, name, internalName, DefinitionKind.Type);

        public static Definition Function(SourceUnit unit, string name, string internalName) =>
            new(unit, unit.Namespace, name, internalName, DefinitionKind.Function);

        public static Definition Constant(SourceUnit unit, string name, string internalName) =>
            new(unit, unit.Namespace, name, internalName, DefinitionKind.Constant);
    }

    private sealed class DefinitionResolver(IReadOnlyList<Definition> definitions)
    {
        public string? ResolveUnqualified(SourceUnit unit, string name, DefinitionKind kind)
        {
            var sameNamespace = definitions
                .Where(definition => definition.Kind == kind
                                     && definition.Namespace == unit.Namespace
                                     && definition.Name == name)
                .ToArray();
            if (sameNamespace.Length == 1)
            {
                return sameNamespace[0].InternalName;
            }

            var openedNamespace = definitions
                .Where(definition => definition.Kind == kind
                                     && unit.OpenNamespaces.Contains(definition.Namespace)
                                     && definition.Name == name)
                .ToArray();
            if (openedNamespace.Length == 1)
            {
                return openedNamespace[0].InternalName;
            }

            var global = definitions
                .Where(definition => definition.Kind == kind && definition.Name == name)
                .ToArray();
            return global.Length == 1 ? global[0].InternalName : null;
        }

        public string? ResolveQualified(SourceUnit unit, IReadOnlyList<string> path, DefinitionKind kind)
        {
            if (path.Count == 0)
            {
                return null;
            }

            if (path.Count == 1)
            {
                return ResolveUnqualified(unit, path[0], kind);
            }

            var name = path[^1];
            var namespaceSegments = path.Take(path.Count - 1).Select(NormalizePathSegment).ToArray();
            var candidateNamespace = string.Join(".", namespaceSegments);

            var matches = definitions
                .Where(definition => definition.Kind == kind
                                     && NamespaceMatches(definition.Namespace, candidateNamespace, unit)
                                     && definition.Name == name)
                .Select(definition => definition.InternalName)
                .ToArray();
            return matches.Length == 1 ? matches[0] : null;
        }

        private static bool NamespaceMatches(string definitionNamespace, string candidateNamespace, SourceUnit unit)
        {
            return definitionNamespace == candidateNamespace
                   || definitionNamespace == unit.Namespace + "." + candidateNamespace
                   || unit.OpenNamespaces.Any(openNamespace => definitionNamespace == openNamespace + "." + candidateNamespace);
        }
    }

    private enum DefinitionKind
    {
        Type,
        Function,
        Constant,
    }

    private sealed record TextReplacement(int Start, int End, string Text);
}

public sealed record PhysicalNamespaceSourceFile(string Path, string Source);

public sealed record PhysicalNamespaceSourceGroup(
    IReadOnlyList<PhysicalNamespaceSourceFile> Files,
    string RootNamespace,
    string SourceRoot);
