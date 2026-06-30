namespace RetroSharp.Core.Tests;

using Xunit;

public sealed class CompileTimeOperandIntrinsicsReferenceTests
{
    [Fact]
    public void Architecture_roadmap_links_to_compile_time_operand_intrinsic_design_note()
    {
        var roadmap = File.ReadAllText(RepositoryFile("docs/ArchitectureRoadmap.md"));

        Assert.Contains("docs/CompileTimeOperandIntrinsics.md", roadmap, StringComparison.Ordinal);
    }

    [Fact]
    public void Compile_time_operand_intrinsic_design_note_records_descriptor_role_decision()
    {
        var design = File.ReadAllText(RepositoryFile("docs/CompileTimeOperandIntrinsics.md"));

        Assert.Contains("# Compile-Time Operand Intrinsics", design, StringComparison.Ordinal);
        Assert.Contains("TargetIntrinsicDescriptor", design, StringComparison.Ordinal);
        Assert.Contains("AssetRef", design, StringComparison.Ordinal);
        Assert.Contains("ConstPaletteSlot", design, StringComparison.Ordinal);
        Assert.Contains("EnumFlags", design, StringComparison.Ordinal);
        Assert.Contains("WorldId", design, StringComparison.Ordinal);
        Assert.Contains("StreamMapColumn", design, StringComparison.Ordinal);
        Assert.Contains("StreamMapRow", design, StringComparison.Ordinal);
    }

    private static string RepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not find repository file '{relativePath}'.");
    }
}
