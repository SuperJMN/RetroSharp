namespace RetroSharp.GameBoy;

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

public sealed record GameBoyGbsHeader(
    int Version,
    int SubsongCount,
    int DefaultSubsong,
    ushort LoadAddress,
    ushort InitAddress,
    ushort PlayAddress,
    ushort StackPointer,
    byte TimerModulo,
    byte TimerControl,
    string Title,
    string Author,
    string Copyright);

internal sealed record GameBoyGbsTraceOptions(
    string InputPath,
    int Subsong,
    int Seconds,
    string GbsPlayPath);

internal interface IGameBoyGbsTraceSource
{
    IReadOnlyList<string> Capture(GameBoyGbsTraceOptions options);
}

internal static class GameBoyGbsFile
{
    public static GameBoyGbsHeader ReadHeader(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("GBS export requires --in <file.gbs>.");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Could not find GBS file '{path}'.", path);
        }

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 0x70 ||
            bytes[0] != (byte)'G' ||
            bytes[1] != (byte)'B' ||
            bytes[2] != (byte)'S')
        {
            throw new InvalidOperationException($"'{Path.GetFileName(path)}' is not an uncompressed GBS file.");
        }

        return new GameBoyGbsHeader(
            Version: bytes[3],
            SubsongCount: bytes[4],
            DefaultSubsong: bytes[5],
            LoadAddress: ReadUInt16(bytes, 0x06),
            InitAddress: ReadUInt16(bytes, 0x08),
            PlayAddress: ReadUInt16(bytes, 0x0A),
            StackPointer: ReadUInt16(bytes, 0x0C),
            TimerModulo: bytes[0x0E],
            TimerControl: bytes[0x0F],
            Title: ReadFixedString(bytes, 0x10, 32),
            Author: ReadFixedString(bytes, 0x30, 32),
            Copyright: ReadFixedString(bytes, 0x50, 32));
    }

    private static ushort ReadUInt16(byte[] bytes, int offset)
    {
        return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
    }

    private static string ReadFixedString(byte[] bytes, int offset, int length)
    {
        var end = offset;
        while (end < offset + length && bytes[end] != 0)
        {
            end++;
        }

        return Encoding.ASCII.GetString(bytes, offset, end - offset);
    }
}

internal sealed class GbsPlayTraceSource : IGameBoyGbsTraceSource
{
    public IReadOnlyList<string> Capture(GameBoyGbsTraceOptions options)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(options.GbsPlayPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        process.StartInfo.ArgumentList.Add("-q");
        process.StartInfo.ArgumentList.Add("-o");
        process.StartInfo.ArgumentList.Add("iodumper");
        process.StartInfo.ArgumentList.Add("-t");
        process.StartInfo.ArgumentList.Add(options.Seconds.ToString(CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("0");
        process.StartInfo.ArgumentList.Add("-g");
        process.StartInfo.ArgumentList.Add("0");
        process.StartInfo.ArgumentList.Add(options.InputPath);
        process.StartInfo.ArgumentList.Add(options.Subsong.ToString(CultureInfo.InvariantCulture));
        process.StartInfo.ArgumentList.Add(options.Subsong.ToString(CultureInfo.InvariantCulture));

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not start gbsplay at '{options.GbsPlayPath}'. Install gbsplay or pass --gbsplay <path>.",
                ex);
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        var timeout = TimeSpan.FromSeconds(Math.Max(30, options.Seconds + 10));
        if (!process.WaitForExit(timeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"gbsplay did not finish within {timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.");
        }

        var output = stdout.GetAwaiter().GetResult();
        var error = stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? $"gbsplay failed with exit code {process.ExitCode.ToString(CultureInfo.InvariantCulture)}."
                    : error.Trim());
        }

        return SplitLines(output);
    }

    private static IReadOnlyList<string> SplitLines(string text)
    {
        using var reader = new StringReader(text);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }
}
