using System.Text;

namespace RetroSharp.Cli;

/// <summary>
/// Resolves output paths (project-relative and default extensions) and writes compiled bytes or
/// sidecar text to disk, creating parent directories as needed.
/// </summary>
internal static class BuildOutputWriter
{
    internal static string? ResolveProjectOutputPath(string projectDirectory, string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return null;
        }

        return Path.IsPathRooted(outputPath)
            ? Path.GetFullPath(outputPath)
            : Path.GetFullPath(Path.Combine(projectDirectory, outputPath));
    }

    internal static void WriteBytes(string outputPath, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(outputPath, bytes);
    }

    internal static void WriteText(string outputPath, string text)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outputPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    internal static string DefaultOutputPath(RetroSharpBuildInput buildInput, string extension)
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
}
