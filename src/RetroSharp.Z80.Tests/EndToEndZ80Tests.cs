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

public class EndToEndZ80Tests
{
    [Fact]
    public void Main_return_constant_three_sets_HL_to_3()
    {
        // Build semantic model: program { void main() { return 3; } }
        var retExpr = new ConstantNode(3);
        var ret = new ReturnNode(Maybe.From<ExpressionNode>(retExpr));
        var block = new BlockNode(new System.Collections.Generic.List<StatementNode> { ret });
        var main = new FunctionNode("i16", "main", block, new System.Collections.Generic.List<string>());
        var program = new ProgramNode(new System.Collections.Generic.List<FunctionNode> { main });

        // Generate IR
        var ir = new V2IntermediateCodeGenerator().Generate(program);

        // Generate Z80 assembly
        var gen = new Z80Generator();
        var asmResult = gen.Generate(ir);
        Assert.True(asmResult.IsSuccess, asmResult.IsFailure ? asmResult.Error : "");
        var asm = asmResult.Value.Assembly;

        // Assemble to binary with Sixty502DotNet
        var assembler = new Z80Assembler();
        var assembled = assembler.Assemble(asm);
        Assert.True(assembled.IsSuccess, assembled.IsFailure ? assembled.Error : "");
        var bin = assembled.Value.ProgramBinary;

        // Resolve entry point: try to find PC of line "main:" in debug info; fallback to 0
        var debug = assembled.Value.DebugInfo;
        var entryOffset = debug.Where(d => (d.LineText?.Trim() ?? string.Empty).Equals("main:", StringComparison.Ordinal))
                               .Select(d => d.ProgramCounter)
                               .DefaultIfEmpty(0)
                               .First();

        // Load program at origin 0
        // Setup CPU and memory
        var cpu = new Z80Processor();
        cpu.Reset();
        cpu.Memory.SetContents(0, bin);

        // Setup return to HALT and jump to entry
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

        // Assert HL == 3
        var hlValue = (cpu.Registers.H << 8) | cpu.Registers.L;
        Assert.Equal(3, hlValue);
    }
}

