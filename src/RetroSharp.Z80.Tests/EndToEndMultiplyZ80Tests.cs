using System;
using System.Linq;
using RetroSharp.SemanticAnalysis;
using RetroSharp.Generation.Intermediate;
using RetroSharp.Z80;
using Sixty502DotNet;
using Konamiman.Z80dotNet;
using Xunit;
using CSharpFunctionalExtensions;
using Sixty502DotNet.Shared;

namespace RetroSharp.Z80.Tests;

public class EndToEndMultiplyZ80Tests
{
    [Fact]
    public void Main_return_two_times_three_sets_HL_to_6()
    {
        // Build semantic model: void main() { return 2 * 3; }
        var two = new ConstantNode(2);
        var three = new ConstantNode(3);
        var mul = new BinaryExpressionNode(two, three, RetroSharp.Core.Operator.Get("*"));
        var ret = new ReturnNode(Maybe.From<ExpressionNode>(mul));
        var block = new BlockNode(new System.Collections.Generic.List<StatementNode> { ret });
        var main = new FunctionNode("main", block, new System.Collections.Generic.List<string>());
        var program = new ProgramNode(new System.Collections.Generic.List<FunctionNode> { main });

        // Generate IR
        var ir = new V2IntermediateCodeGenerator().Generate(program);

        // Generate Z80 assembly
        var gen = new Z80Generator();
        var asmResult = gen.Generate(ir);
        Assert.True(asmResult.IsSuccess, asmResult.IsFailure ? asmResult.Error : "");
        var asm = asmResult.Value.Assembly;
        Console.WriteLine("==== ASM ====");
        Console.WriteLine(asm);

        // Assemble to binary with Sixty502DotNet
        var assembler = new Z80Assembler();
        var assembled = assembler.Assemble(asm);
        Assert.True(assembled.IsSuccess, assembled.IsFailure ? assembled.Error : "");
        var bin = assembled.Value.ProgramBinary;

        // Resolve entry point
        var debug = assembled.Value.DebugInfo;
        var entryOffset = debug.Where(d => (d.LineText?.Trim() ?? string.Empty).Equals("main:", StringComparison.Ordinal))
                               .Select(d => d.ProgramCounter)
                               .DefaultIfEmpty(0)
                               .First();

        // Setup CPU and run from origin 0
        var cpu = new Z80Processor();
        cpu.Reset();
        cpu.Memory.SetContents(0, bin);

        var entry = (ushort)entryOffset;
        const ushort haltAddr = 0xF000;
        cpu.Memory[haltAddr] = 0x76; // HALT
        const ushort s0 = 0xFF00;
        cpu.Memory[s0] = (byte)(haltAddr & 0xFF);
        cpu.Memory[s0 + 1] = (byte)(haltAddr >> 8);
        cpu.Registers.SP = unchecked((short)s0);
        cpu.Registers.PC = entry;

        // Step bounded number of instructions until HALT
        const int MaxSteps = 20000;
        for (int i = 0; i < MaxSteps; i++)
        {
            cpu.ExecuteNextInstruction();
            if (cpu.IsHalted) break;
        }

        // Assert HL == 6
        var hl = (cpu.Registers.H << 8) | cpu.Registers.L;
        Assert.Equal(6, hl);
    }

    [Fact]
    public void Parser_pipeline_main_returns_three_times_two_sets_HL_to_6()
    {
var src = @"i16 main() { return 3 * 2; }";
        var hl = Z80.Tests.Support.Z80E2E.RunHL(src, maxSteps: 20000);
        Assert.Equal(6, hl);
    }

    [Fact]
    public void Parser_pipeline_function_returns_n_times_n_minus_1_for_3_is_6()
    {
var src = @"i16 f(i16 n){ return n * (n - 1); } i16 main(){ return f(3); }";
        var hl = Z80.Tests.Support.Z80E2E.RunHL(src, maxSteps: 20000);
        Assert.Equal(6, hl);
    }

    [Fact]
    public void Parser_pipeline_function_returns_n_minus_1_for_3_is_2()
    {
var src = @"i16 f(i16 n){ return n - 1; } i16 main(){ return f(3); }";
        var hl = Z80.Tests.Support.Z80E2E.RunHL(src, maxSteps: 20000);
        Assert.Equal(2, hl);
    }

    [Fact]
    public void Parser_pipeline_function_returns_n_for_3_is_3()
    {
var src = @"i16 f(i16 n){ return n; } i16 main(){ return f(3); }";
        var hl = Z80.Tests.Support.Z80E2E.RunHL(src, maxSteps: 20000);
        Assert.Equal(3, hl);
    }
}

