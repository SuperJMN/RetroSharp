namespace RetroSharp.Cli;

/// <summary>
/// Parses the option-only argument list for the `gbs-to-gbapu` subcommand into a
/// <see cref="RetroSharp.GameBoy.GameBoyGbsToGbApuOptions"/>.
/// </summary>
internal static class GbsToGbApuCommandLineParser
{
    internal static RetroSharp.GameBoy.GameBoyGbsToGbApuOptions Parse(string[] args)
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

    private static int ParsePositiveInt(string value, string option)
    {
        if (!int.TryParse(value, out var parsed) || parsed < 1)
        {
            throw new ArgumentException($"{option} requires a positive integer.");
        }

        return parsed;
    }

    private static long ParseNonNegativeLong(string value, string option)
    {
        if (!long.TryParse(value, out var parsed) || parsed < 0)
        {
            throw new ArgumentException($"{option} requires a non-negative integer.");
        }

        return parsed;
    }
}
