using Sixty502DotNet.Shared;

void PrintSection(string header, string body)
{
    Console.WriteLine(header);
    Console.WriteLine(body);
    Console.WriteLine();
}

string ReadInputFile(string s) => File.ReadAllText(s);

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

static (string? InputPath, string? OutputPath, string Target) ParseCommandLine(string[] args)
{
    string? inputPath = null;
    string? outputPath = null;
    var target = "z80";

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
            default:
                if (args[i].StartsWith("-", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"Unknown option '{args[i]}'.");
                }

                inputPath ??= args[i];
                break;
        }
    }

    return (inputPath, outputPath, target);
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

var options = ParseCommandLine(args);
if (options.InputPath is null)
{
    Console.Error.WriteLine("No source file has been specified");
    return 1;
}

var path = options.InputPath;

if (options.Target == "nes")
{
    try
    {
        var source = ReadInputFile(path);
        var rom = RetroSharp.NES.NesRomCompiler.CompileSource(source, Path.GetDirectoryName(Path.GetFullPath(path)));
        var outputPath = options.OutputPath ?? Path.ChangeExtension(path, ".nes");
        File.WriteAllBytes(outputPath, rom);
        Console.Error.WriteLine($"Wrote NES ROM: {outputPath}");
        return 0;
    }
    catch (Exception ex)
    {
        PrintError(ex.Message);
        return 1;
    }
}

if (options.Target is "gb" or "gameboy")
{
    try
    {
        var source = ReadInputFile(path);
        var rom = RetroSharp.GameBoy.GameBoyRomCompiler.CompileSource(source, Path.GetDirectoryName(Path.GetFullPath(path)));
        var outputPath = options.OutputPath ?? Path.ChangeExtension(path, ".gb");
        File.WriteAllBytes(outputPath, rom);
        Console.Error.WriteLine($"Wrote Game Boy ROM: {outputPath}");
        return 0;
    }
    catch (Exception ex)
    {
        PrintError(ex.Message);
        return 1;
    }
}

if (options.Target != "z80")
{
    Console.Error.WriteLine($"Unknown target '{options.Target}'. Supported targets: z80, nes, gb");
    return 1;
}

return Result
    .Try(() => ReadInputFile(path))
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
