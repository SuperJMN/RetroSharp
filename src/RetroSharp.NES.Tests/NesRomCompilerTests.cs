namespace RetroSharp.NES.Tests;

using RetroSharp.GameBoy;
using RetroSharp.NES;
using RetroSharp.Parser;
using RetroSharp.Sdk;
using Xunit;
using static RetroSharp.NES.Tests.NesTestAssets;

public partial class NesRomCompilerTests
{
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

    private static string WriteSpriteAsset(string fileName, string contents)
    {
        var directory = Path.Combine(Path.GetTempPath(), "RetroSharp.NES.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), contents);
        return directory;
    }

    private static bool ContainsSequence(IReadOnlyList<byte> bytes, IReadOnlyList<byte> sequence)
    {
        return IndexOfSequence(bytes, sequence) >= 0;
    }

    private static int CountOccurrences(IReadOnlyList<byte> bytes, IReadOnlyList<byte> sequence)
    {
        var count = 0;
        for (var i = 0; i <= bytes.Count - sequence.Count; i++)
        {
            var match = true;
            for (var j = 0; j < sequence.Count; j++)
            {
                if (bytes[i + j] != sequence[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                count++;
            }
        }

        return count;
    }

    private static int IndexOfSequence(IReadOnlyList<byte> bytes, IReadOnlyList<byte> sequence)
    {
        for (var i = 0; i <= bytes.Count - sequence.Count; i++)
        {
            var match = true;
            for (var j = 0; j < sequence.Count; j++)
            {
                if (bytes[i + j] != sequence[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }

    private static int ReadLittleEndian16(IReadOnlyList<byte> bytes, int offset)
    {
        return bytes[offset] | bytes[offset + 1] << 8;
    }

    private static string Fingerprint(byte[] bytes)
    {
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }

    private static string CollisionHitContractSource(int height, int solidRow, string body)
    {
        var visual = string.Join(", ", Enumerable.Repeat("0", height));
        var flags = string.Join(", ", Enumerable.Range(0, height).Select(row => row == solidRow ? "1" : "0"));
        return $$"""
                 void Main() {
                     World.Column(0, {{visual}});
                     World.Flags(0, {{flags}});
                     World.Map(1, 0, {{height}});
                     Camera.Init(1, 0, {{height}});
                     Camera.SetPosition(0, 1);
                     Camera.Apply();
                     {{body}}
                 }
                 """;
    }

    private static string BytesAround(IReadOnlyList<byte> bytes, int offset)
    {
        var start = Math.Max(0, offset - 8);
        var length = Math.Min(bytes.Count - start, 17);
        return $"0x{offset:X4} [" + string.Join(" ", bytes.Skip(start).Take(length).Select(value => value.ToString("X2"))) + "]";
    }

    private static bool ContainsAbsoluteJumpTo(IReadOnlyList<byte> bytes, int target, int startInclusive, int endExclusive)
    {
        for (var i = Math.Max(0, startInclusive); i <= Math.Min(bytes.Count, endExclusive) - 3; i++)
        {
            if (bytes[i] == 0x4C && ReadLittleEndian16(bytes, i + 1) == target)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAbsoluteJumpAfter(IReadOnlyList<byte> bytes, int startInclusive, int endExclusive, int minimumTarget)
    {
        for (var i = Math.Max(0, startInclusive); i <= Math.Min(bytes.Count, endExclusive) - 3; i++)
        {
            if (bytes[i] == 0x4C && ReadLittleEndian16(bytes, i + 1) > minimumTarget)
            {
                return true;
            }
        }

        return false;
    }

    private static int ReadRelativeTarget(IReadOnlyList<byte> bytes, int branchOffset)
    {
        return 0x8000 + branchOffset + 2 + unchecked((sbyte)bytes[branchOffset + 1]);
    }
}
