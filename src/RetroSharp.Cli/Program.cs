using System.Text.Json;
using Sixty502DotNet.Shared;

void PrintSection(string header, string body)
{
    Console.WriteLine(header);
    Console.WriteLine(body);
    Console.WriteLine();
}

static string ReadInputFile(string s) => File.ReadAllText(s);

void PrintSuccess() => Console.Error.WriteLine("Success");
void PrintError(string s) => Console.Error.WriteLine(s);
void PrintAsm(RetroSharp.Z80.Core.GeneratedProgram gp) => PrintSection("Assembly:", gp.Assembly);

void PrintRunResult(int hl)
{
    PrintSection("Run result (HL):", hl.ToString());
}

void PrintSource(string source) => PrintSection("Source:", source);

void PrintDiagnostics(IEnumerable<string> diagnostics)
{
    var text = diagnostics.Any() ? string.Join("\n", diagnostics) : "None";
    PrintSection("Diagnostics:", text);
}

Result<RetroSharp.SemanticAnalysis.AnalyzeResult<RetroSharp.SemanticAnalysis.SemanticNode>> Analyze(string source)
{
    var parser = new RetroSharp.Parser.SomeParser();
    var parse = parser.Parse(source);
    if (parse.IsFailure) return Result.Failure<RetroSharp.SemanticAnalysis.AnalyzeResult<RetroSharp.SemanticAnalysis.SemanticNode>>(parse.Error);

    var analyzer = new RetroSharp.SemanticAnalysis.SemanticAnalyzer();
    var analyzed = analyzer.Analyze(parse.Value);
    return Result.Success(analyzed);
}

Result<RetroSharp.Generation.Intermediate.Model.IntermediateCodeProgram> GenerateIR(RetroSharp.SemanticAnalysis.AnalyzeResult<RetroSharp.SemanticAnalysis.SemanticNode> analyzed)
{
    var gen = new RetroSharp.Generation.Intermediate.V2IntermediateCodeGenerator();
    var programNode = (RetroSharp.SemanticAnalysis.ProgramNode)analyzed.Node;
    var ir = gen.Generate(programNode);
    return Result.Success(ir);
}

Result<RetroSharp.Generation.Intermediate.Model.IntermediateCodeProgram> OptimizeIR(RetroSharp.Generation.Intermediate.Model.IntermediateCodeProgram ir)
{
    return RetroSharp.Generation.Intermediate.Model.Transforms.DefaultOptimizationPipeline
        .Apply(ir);
}

Result<RetroSharp.Z80.Core.GeneratedProgram> GenerateAsm(RetroSharp.Generation.Intermediate.Model.IntermediateCodeProgram ir)
{
    var z80 = new RetroSharp.Z80.Z80Generator();
    return z80.Generate(ir);
}

Result<int> RunAsm(RetroSharp.Z80.Core.GeneratedProgram asm)
{
    // Assemble with Sixty502DotNet Z80Assembler and run on Konamiman.Z80dotNet
    var assembler = new Z80Assembler();
    var assembled = assembler.Assemble(asm.Assembly);
    if (assembled.IsFailure) return Result.Failure<int>(assembled.Error);

    var bin = assembled.Value.ProgramBinary;
    // Determine entry PC: try to find "main:" label in debug info
    ushort entryPc = (ushort)assembled.Value.DebugInfo
        .Where(d => (d.LineText?.Trim() ?? string.Empty) == "main:")
        .Select(d => d.ProgramCounter)
        .DefaultIfEmpty(0)
        .First();

    if (entryPc == 0)
    {
        // Fallback: scan assembly text for main: and pick first instruction after
        var lines = asm.Assembly.Split('\n');
        for (int i = 0; i < lines.Length && entryPc == 0; i++)
        {
            if (lines[i].Trim() == "main:")
            {
                for (int j = i + 1; j < lines.Length; j++)
                {
                    var next = lines[j].Trim();
                    if (!string.IsNullOrEmpty(next) && !next.EndsWith(':'))
                    {
                        // Pick first instruction after label; use its first matching debug entry with highest PC
                        var instruction = next.Split('\t')[0];
                        var match = assembled.Value.DebugInfo
                            .Where(d => d.LineText?.Trim().StartsWith(instruction) == true)
                            .OrderBy(d => d.ProgramCounter)
                            .LastOrDefault();
                        if (match != null) entryPc = (ushort)match.ProgramCounter;
                        break;
                    }
                }
            }
        }
    }

    var cpu = new Konamiman.Z80dotNet.Z80Processor();
    cpu.Reset();
    cpu.Memory.SetContents(0, bin);

    const ushort haltAddr = 0xF000;
    cpu.Memory[haltAddr] = 0x76; // HALT
    const ushort s0 = 0xFF00;
    cpu.Memory[s0] = (byte)(haltAddr & 0xFF);
    cpu.Memory[s0 + 1] = (byte)(haltAddr >> 8);
    cpu.Registers.SP = unchecked((short)s0);

    cpu.Registers.PC = entryPc;

    for (int i = 0; i < 20000 && !cpu.IsHalted; i++)
    {
        cpu.ExecuteNextInstruction();
    }

    if (!cpu.IsHalted) return Result.Failure<int>("Z80 execution didn't reach HALT within step bound.");

    int hl = (cpu.Registers.H << 8) | (cpu.Registers.L & 0xFF);
    return Result.Success(hl);
}

void PrintIR(RetroSharp.Generation.Intermediate.Model.IntermediateCodeProgram ir)
{
    var text = RetroSharp.Generation.Intermediate.Model.Visitors.PrettyPrinterVisitor.Print(ir);
    PrintSection("Intermediate code:", text);
}

static (string? InputPath, string? OutputPath, string? Target, IReadOnlyList<string> LibraryPaths) ParseCommandLine(string[] args)
{
    string? inputPath = null;
    string? outputPath = null;
    string? target = null;
    var libraryPaths = new List<string>();

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--target":
                if (i + 1 >= args.Length) throw new ArgumentException("--target requires a value.");
                target = args[++i].ToLowerInvariant();
                break;
            case "--out":
            case "-o":
                if (i + 1 >= args.Length) throw new ArgumentException($"{args[i]} requires a value.");
                outputPath = args[++i];
                break;
            case "--lib-path":
                if (i + 1 >= args.Length) throw new ArgumentException("--lib-path requires a value.");
                libraryPaths.Add(args[++i]);
                break;
            default:
                if (args[i].StartsWith("-", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"Unknown option '{args[i]}'.");
                }

                inputPath ??= args[i];
                break;
        }
    }

    return (inputPath, outputPath, target, libraryPaths);
}

static RetroSharp.Sdk.SdkLibraryRegistry? ResolveSdkLibraryRegistry(IReadOnlyList<string> libraryPaths)
{
    return libraryPaths.Count == 0
        ? null
        : RetroSharp.Sdk.SdkLibraryRegistry.FromDirectories(libraryPaths);
}

static IReadOnlyList<RetroSharpBuildInput> ResolveBuildInputs((string? InputPath, string? OutputPath, string? Target, IReadOnlyList<string> LibraryPaths) options)
{
    if (options.InputPath is null)
    {
        throw new ArgumentException("No source file has been specified");
    }

    return IsProjectFile(options.InputPath)
        ? ResolveProjectBuildInputs(options)
        : [ResolveSourceBuildInput(options)];
}

static RetroSharpBuildInput ResolveSourceBuildInput((string? InputPath, string? OutputPath, string? Target, IReadOnlyList<string> LibraryPaths) options)
{
    var inputPath = options.InputPath ?? throw new ArgumentException("No source file has been specified");
    var fullPath = Path.GetFullPath(inputPath);
    return new RetroSharpBuildInput(
        ReadInputFile(inputPath),
        Path.GetDirectoryName(fullPath),
        options.Target ?? "z80",
        options.OutputPath,
        options.LibraryPaths,
        inputPath);
}

static IReadOnlyList<RetroSharpBuildInput> ResolveProjectBuildInputs((string? InputPath, string? OutputPath, string? Target, IReadOnlyList<string> LibraryPaths) options)
{
    var projectPath = Path.GetFullPath(options.InputPath ?? throw new ArgumentException("No project file has been specified"));
    var projectDirectory = Path.GetDirectoryName(projectPath)
        ?? throw new InvalidOperationException($"Could not resolve directory for RetroSharp project '{projectPath}'.");
    var manifest = ReadProjectManifest(projectPath);
    var sourceItems = manifest.Sources ?? [];
    if (sourceItems.Length == 0)
    {
        throw new InvalidOperationException($"RetroSharp project '{projectPath}' must list at least one source file.");
    }

    var sourceFiles = sourceItems
        .Select(sourcePath => ReadProjectSourceFile(projectDirectory, projectPath, sourcePath))
        .ToArray();
    var source = ComposeProjectSource(projectDirectory, projectPath, manifest, sourceFiles);
    var projectLibraryPaths = (manifest.LibraryPaths ?? [])
        .Select(libraryPath => ResolveProjectItemPath(projectDirectory, projectPath, libraryPath, "library path"))
        .ToArray();
    var libraryPaths = projectLibraryPaths.Concat(options.LibraryPaths).ToArray();
    var targets = ResolveProjectTargets(options, manifest);
    if (options.OutputPath is not null && targets.Length > 1)
    {
        throw new InvalidOperationException("--out can only be used with a single target. Use project outputs for multi-target builds.");
    }

    return targets
        .Select(target => new RetroSharpBuildInput(
            source,
            projectDirectory,
            target,
            options.OutputPath ?? ResolveProjectOutputPath(projectDirectory, ResolveProjectOutput(manifest, target)),
            libraryPaths,
            projectPath))
        .ToArray();
}

static string[] ResolveProjectTargets(
    (string? InputPath, string? OutputPath, string? Target, IReadOnlyList<string> LibraryPaths) options,
    RetroSharpProjectManifest manifest)
{
    if (!string.IsNullOrWhiteSpace(options.Target))
    {
        return [options.Target];
    }

    if (manifest.Targets is { Length: > 0 })
    {
        return manifest.Targets.Select(NormalizeTarget).ToArray();
    }

    return [string.IsNullOrWhiteSpace(manifest.Target) ? "z80" : NormalizeTarget(manifest.Target)];
}

static string NormalizeTarget(string target)
{
    if (string.IsNullOrWhiteSpace(target))
    {
        throw new InvalidOperationException("RetroSharp project declares an empty target.");
    }

    return target.Trim().ToLowerInvariant();
}

static string? ResolveProjectOutput(RetroSharpProjectManifest manifest, string target)
{
    if (manifest.Outputs is not null)
    {
        foreach (var output in manifest.Outputs)
        {
            if (string.Equals(output.Key, target, StringComparison.OrdinalIgnoreCase))
            {
                return output.Value;
            }
        }
    }

    return manifest.Output ?? manifest.OutputPath;
}

static bool IsProjectFile(string path)
{
    var fileName = Path.GetFileName(path);
    return string.Equals(fileName, "retrosharp.json", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".retrosharp.json", StringComparison.OrdinalIgnoreCase);
}

static RetroSharpProjectManifest ReadProjectManifest(string projectPath)
{
    var options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    try
    {
        return JsonSerializer.Deserialize<RetroSharpProjectManifest>(File.ReadAllText(projectPath), options)
            ?? throw new InvalidOperationException($"RetroSharp project '{projectPath}' is empty.");
    }
    catch (JsonException ex)
    {
        throw new InvalidOperationException($"Invalid RetroSharp project '{projectPath}': {ex.Message}", ex);
    }
}

static string ComposeProjectSource(
    string projectDirectory,
    string projectPath,
    RetroSharpProjectManifest manifest,
    IReadOnlyList<RetroSharp.Sdk.PhysicalNamespaceSourceFile> sourceFiles)
{
    if (string.IsNullOrWhiteSpace(manifest.NamespaceMode))
    {
        return string.Concat(sourceFiles.Select(sourceFile => EnsureTrailingNewLine(sourceFile.Source)));
    }

    if (!string.Equals(manifest.NamespaceMode, "physical", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"RetroSharp project '{projectPath}' declares unsupported namespaceMode '{manifest.NamespaceMode}'.");
    }

    var rootNamespace = string.IsNullOrWhiteSpace(manifest.RootNamespace)
        ? DefaultRootNamespace(projectPath)
        : manifest.RootNamespace;
    var sourceRoot = ResolveProjectItemPath(projectDirectory, projectPath, manifest.SourceRoot ?? "src", "sourceRoot");
    return RetroSharp.Sdk.PhysicalNamespaceSourceComposer.Compose(sourceFiles, rootNamespace, sourceRoot);
}

static RetroSharp.Sdk.PhysicalNamespaceSourceFile ReadProjectSourceFile(string projectDirectory, string projectPath, string sourcePath)
{
    var fullSourcePath = ResolveProjectItemPath(projectDirectory, projectPath, sourcePath, "source");
    if (!File.Exists(fullSourcePath))
    {
        throw new InvalidOperationException($"RetroSharp project '{projectPath}' source '{sourcePath}' was not found.");
    }

    var source = File.ReadAllText(fullSourcePath);
    return new RetroSharp.Sdk.PhysicalNamespaceSourceFile(fullSourcePath, source);
}

static string EnsureTrailingNewLine(string source)
{
    return source.EndsWith('\n') ? source : source + System.Environment.NewLine;
}

static string DefaultRootNamespace(string projectPath)
{
    var fileName = Path.GetFileName(projectPath);
    const string projectSuffix = ".retrosharp.json";
    var baseName = fileName.EndsWith(projectSuffix, StringComparison.OrdinalIgnoreCase)
        ? fileName[..^projectSuffix.Length]
        : Path.GetFileNameWithoutExtension(fileName);
    var segments = baseName
        .Split(['-', '_', '.', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(segment => char.ToUpperInvariant(segment[0]) + segment[1..])
        .ToArray();
    var normalized = new string(string.Concat(segments).Where(char.IsLetterOrDigit).ToArray());
    if (string.IsNullOrEmpty(normalized))
    {
        return "RetroSharpProject";
    }

    return char.IsDigit(normalized[0]) ? "_" + normalized : normalized;
}

static string ResolveProjectItemPath(string projectDirectory, string projectPath, string itemPath, string itemKind)
{
    if (string.IsNullOrWhiteSpace(itemPath))
    {
        throw new InvalidOperationException($"RetroSharp project '{projectPath}' declares an empty {itemKind} path.");
    }

    return Path.IsPathRooted(itemPath)
        ? Path.GetFullPath(itemPath)
        : Path.GetFullPath(Path.Combine(projectDirectory, itemPath));
}

static string? ResolveProjectOutputPath(string projectDirectory, string? outputPath)
{
    if (string.IsNullOrWhiteSpace(outputPath))
    {
        return null;
    }

    return Path.IsPathRooted(outputPath)
        ? Path.GetFullPath(outputPath)
        : Path.GetFullPath(Path.Combine(projectDirectory, outputPath));
}

static void WriteOutputBytes(string outputPath, byte[] bytes)
{
    var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }

    File.WriteAllBytes(outputPath, bytes);
}

static string DefaultOutputPath(RetroSharpBuildInput buildInput, string extension)
{
    const string projectSuffix = ".retrosharp.json";
    var fileName = Path.GetFileName(buildInput.PrimaryPath);
    if (fileName.EndsWith(projectSuffix, StringComparison.OrdinalIgnoreCase))
    {
        var directory = Path.GetDirectoryName(buildInput.PrimaryPath);
        var outputName = fileName[..^projectSuffix.Length] + extension;
        return string.IsNullOrEmpty(directory) ? outputName : Path.Combine(directory, outputName);
    }

    return Path.ChangeExtension(buildInput.PrimaryPath, extension);
}

static RetroSharp.GameBoy.GameBoyGbsToGbApuOptions ParseGbsToGbApuCommandLine(string[] args)
{
    string? inputPath = null;
    string? outputPath = null;
    var subsong = 1;
    var seconds = 60;
    long loopCycle = 0;
    var gbsPlayPath = "gbsplay";
    var autoLoop = true;
    var emitJson = false;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--in":
                if (i + 1 >= args.Length) throw new ArgumentException("--in requires a value.");
                inputPath = args[++i];
                break;
            case "--out":
            case "-o":
                if (i + 1 >= args.Length) throw new ArgumentException($"{args[i]} requires a value.");
                outputPath = args[++i];
                break;
            case "--subsong":
                if (i + 1 >= args.Length) throw new ArgumentException("--subsong requires a value.");
                subsong = ParsePositiveInt(args[++i], "--subsong");
                break;
            case "--seconds":
                if (i + 1 >= args.Length) throw new ArgumentException("--seconds requires a value.");
                seconds = ParsePositiveInt(args[++i], "--seconds");
                break;
            case "--loop-cycle":
                if (i + 1 >= args.Length) throw new ArgumentException("--loop-cycle requires a value.");
                loopCycle = ParseNonNegativeLong(args[++i], "--loop-cycle");
                break;
            case "--auto-loop":
                autoLoop = true;
                break;
            case "--no-auto-loop":
                autoLoop = false;
                break;
            case "--emit-json":
                emitJson = true;
                break;
            case "--gbsplay":
                if (i + 1 >= args.Length) throw new ArgumentException("--gbsplay requires a value.");
                gbsPlayPath = args[++i];
                break;
            default:
                throw new ArgumentException($"Unknown gbs-to-gbapu option '{args[i]}'.");
        }
    }

    if (inputPath is null)
    {
        throw new ArgumentException("GBS to GBAPU export requires --in <file.gbs>.");
    }

    if (outputPath is null)
    {
        throw new ArgumentException("GBS to GBAPU export requires --out <file.gbapu.json>.");
    }

    return new RetroSharp.GameBoy.GameBoyGbsToGbApuOptions(
        inputPath,
        outputPath,
        subsong,
        seconds,
        loopCycle,
        gbsPlayPath,
        autoLoop,
        emitJson);
}

static int ParsePositiveInt(string value, string option)
{
    if (!int.TryParse(value, out var parsed) || parsed < 1)
    {
        throw new ArgumentException($"{option} requires a positive integer.");
    }

    return parsed;
}

static long ParseNonNegativeLong(string value, string option)
{
    if (!long.TryParse(value, out var parsed) || parsed < 0)
    {
        throw new ArgumentException($"{option} requires a non-negative integer.");
    }

    return parsed;
}

if (args.Length < 1)
{
    Console.Error.WriteLine("No source file has been specified");
    return 1;
}

if (args[0] == "gbs-to-gbapu")
{
    try
    {
        var exportOptions = ParseGbsToGbApuCommandLine(args[1..]);
        var result = RetroSharp.GameBoy.GameBoyGbsToGbApuExporter.Export(exportOptions);
        Console.Error.WriteLine(
            $"Wrote Game Boy APU trace: {exportOptions.OutputPath} ({result.EventCount} events, {result.DurationCycles / 4194304.0:0.00}s, loop cycle {result.LoopCycle})");
        return 0;
    }
    catch (Exception ex)
    {
        PrintError(ex.Message);
        return 1;
    }
}

if (args[0] == "gbapu-dump")
{
    try
    {
        if (args.Length < 2)
        {
            throw new ArgumentException("gbapu-dump requires a trace path: gbapu-dump <file.gbapu|file.gbapu.json>.");
        }

        var dumpPath = args[1];
        var trace = RetroSharp.GameBoy.GameBoyApuTraceBinary.LooksLikeBinary(dumpPath)
            ? RetroSharp.GameBoy.GameBoyApuTraceBinary.Read(dumpPath)
            : RetroSharp.GameBoy.GameBoyApuTraceFile.Read(dumpPath);

        Console.Error.WriteLine(
            $"; gbapu trace: {trace.Events.Count} events, {trace.DurationCycles / 4194304.0:0.00}s, loopCycle {trace.LoopCycle}, replayHz {trace.Metadata.ReplayHz?.ToString("0.0000") ?? "?"}");
        if (!string.IsNullOrEmpty(trace.Metadata.Title))
        {
            Console.Error.WriteLine($"; title: {trace.Metadata.Title}");
        }

        var absolute = 0L;
        foreach (var traceEvent in trace.Events)
        {
            absolute += traceEvent.DeltaCycles;
            Console.Out.WriteLine($"{absolute:X8} ff{traceEvent.Address & 0xFF:x2}={traceEvent.Value:x2}");
        }

        return 0;
    }
    catch (Exception ex)
    {
        PrintError(ex.Message);
        return 1;
    }
}

if (args[0].StartsWith("gbs-to-", StringComparison.Ordinal))
{
    PrintError($"Unknown command '{args[0]}'.");
    return 1;
}

int BuildInput(RetroSharpBuildInput buildInput)
{
    if (buildInput.Target == "nes")
    {
        try
        {
            var sdkLibraryRegistry = ResolveSdkLibraryRegistry(buildInput.LibraryPaths);
            var rom = RetroSharp.NES.NesRomCompiler.CompileSource(
                buildInput.Source,
                buildInput.BaseDirectory,
                sdkLibraryRegistry: sdkLibraryRegistry);
            var outputPath = buildInput.OutputPath ?? DefaultOutputPath(buildInput, ".nes");
            WriteOutputBytes(outputPath, rom);
            Console.Error.WriteLine($"Wrote NES ROM: {outputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            PrintError(ex.Message);
            return 1;
        }
    }

    if (buildInput.Target is "gb" or "gameboy")
    {
        try
        {
            var sdkLibraryRegistry = ResolveSdkLibraryRegistry(buildInput.LibraryPaths);
            var rom = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSource(
                buildInput.Source,
                buildInput.BaseDirectory,
                sdkLibraryRegistry: sdkLibraryRegistry);
            var outputPath = buildInput.OutputPath ?? DefaultOutputPath(buildInput, ".gb");
            WriteOutputBytes(outputPath, rom);
            Console.Error.WriteLine($"Wrote Game Boy ROM: {outputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            PrintError(ex.Message);
            return 1;
        }
    }

    if (buildInput.Target != "z80")
    {
        Console.Error.WriteLine($"Unknown target '{buildInput.Target}'. Supported targets: z80, nes, gb");
        return 1;
    }

    return Result
        .Try(() => buildInput.Source)
        .Tap(PrintSource)
        .Bind(Analyze)
        .Tap(result => PrintDiagnostics(result.Node.AllErrors))
        .Bind(GenerateIR)
        .Bind(OptimizeIR)
        .Tap(PrintIR)
        .Bind(GenerateAsm)
        .Tap(PrintAsm)
        .Bind(RunAsm)
        .Tap(PrintRunResult)
        .Match(
            _ =>
            {
                PrintSuccess();
                return 0;
            },
            error =>
            {
                PrintError(error);
                return 1;
            });
}

var options = ParseCommandLine(args);
if (options.InputPath is null)
{
    Console.Error.WriteLine("No source file has been specified");
    return 1;
}

IReadOnlyList<RetroSharpBuildInput> buildInputs;
try
{
    buildInputs = ResolveBuildInputs(options);
}
catch (Exception ex)
{
    PrintError(ex.Message);
    return 1;
}

foreach (var buildInput in buildInputs)
{
    var result = BuildInput(buildInput);
    if (result != 0)
    {
        return result;
    }
}

return 0;

file sealed record RetroSharpBuildInput(
    string Source,
    string? BaseDirectory,
    string Target,
    string? OutputPath,
    IReadOnlyList<string> LibraryPaths,
    string PrimaryPath);

file sealed record RetroSharpProjectManifest
{
    public string? Target { get; init; }
    public string[]? Targets { get; init; }
    public string? Output { get; init; }
    public string? OutputPath { get; init; }
    public Dictionary<string, string>? Outputs { get; init; }
    public string[]? Sources { get; init; }
    public string[]? LibraryPaths { get; init; }
    public string? RootNamespace { get; init; }
    public string? SourceRoot { get; init; }
    public string? NamespaceMode { get; init; }
}
