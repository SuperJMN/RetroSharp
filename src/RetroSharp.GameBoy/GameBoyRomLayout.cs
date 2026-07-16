using System.Globalization;
using System.Text;
using RetroSharp.Core;
using RetroSharp.Core.Sdk;
using RetroSharp.Core.Targeting;
using RetroSharp.Parser;
using RetroSharp.Sdk;

namespace RetroSharp.GameBoy;

internal sealed record GameBoyRomBuildResult(byte[] Rom, GameBoyRomBuildReport Report);

internal sealed record GameBoyRomBuildReport(
    string SelectedProfile,
    int RomSize,
    IReadOnlyList<GameBoyRomBuildSegment> Segments,
    IReadOnlyList<int> OccupiedBanks,
    IReadOnlyDictionary<string, ushort> FixedSymbols);

internal sealed record GameBoyRomBuildSegment(
    string Owner,
    int PhysicalStart,
    int Length,
    int Bank,
    ushort CpuAddress);

internal readonly record struct GameBoyFarAddress(byte Bank, ushort Address);

internal sealed record GameBoyWorldPackSegment(int RelativeOffset, byte Bank, ushort Address, int Length);

internal sealed class GameBoyWorldPackPlacement
{
    private const int BankSize = 16 * 1024;
    private const int WindowStart = 0x4000;
    private const int WindowEnd = 0x8000;

    private GameBoyWorldPackPlacement(
        byte baseBank,
        ushort baseAddress,
        byte[] serializedBytes,
        IReadOnlyList<GameBoyWorldPackSegment> segments)
    {
        BaseBank = baseBank;
        BaseAddress = baseAddress;
        SerializedBytes = serializedBytes;
        Segments = segments;
    }

    public byte BaseBank { get; }

    public ushort BaseAddress { get; }

    public byte[] SerializedBytes { get; }

    public IReadOnlyList<GameBoyWorldPackSegment> Segments { get; }

    public static GameBoyWorldPackPlacement Create(byte baseBank, ushort baseAddress, byte[] serializedBytes)
    {
        ArgumentNullException.ThrowIfNull(serializedBytes);
        if (baseBank is 0 or >= 32)
        {
            throw new InvalidOperationException($"Game Boy WorldPack base bank must be 1..31; received {baseBank}.");
        }

        if (baseAddress is < WindowStart or >= WindowEnd)
        {
            throw new InvalidOperationException($"Game Boy WorldPack base address must be $4000-$7FFF; received ${baseAddress:X4}.");
        }

        if (serializedBytes.Length == 0)
        {
            throw new InvalidOperationException("Game Boy WorldPack placement requires at least one serialized byte.");
        }

        var final = TranslateOffset(baseBank, baseAddress, serializedBytes.Length - 1);
        if (final.Bank >= 32)
        {
            throw new InvalidOperationException(
                $"Game Boy WorldPack placement ends in bank {final.Bank}, but the current MBC1 profile supports banks 1..31.");
        }

        var segments = new List<GameBoyWorldPackSegment>();
        var relativeOffset = 0;
        while (relativeOffset < serializedBytes.Length)
        {
            var location = TranslateOffset(baseBank, baseAddress, relativeOffset);
            var length = Math.Min(serializedBytes.Length - relativeOffset, WindowEnd - location.Address);
            segments.Add(new GameBoyWorldPackSegment(relativeOffset, location.Bank, location.Address, length));
            relativeOffset += length;
        }

        return new GameBoyWorldPackPlacement(baseBank, baseAddress, serializedBytes, segments);
    }

    public GameBoyFarAddress TranslateOffset(int relativeOffset)
    {
        if (relativeOffset < 0 || relativeOffset >= SerializedBytes.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(relativeOffset));
        }

        return TranslateOffset(BaseBank, BaseAddress, relativeOffset);
    }

    private static GameBoyFarAddress TranslateOffset(byte baseBank, ushort baseAddress, int relativeOffset)
    {
        var linear = checked(baseAddress - WindowStart + relativeOffset);
        return new GameBoyFarAddress(
            checked((byte)(baseBank + linear / BankSize)),
            checked((ushort)(WindowStart + linear % BankSize)));
    }
}

internal sealed class GameBoyRomLayout
{
    private const int BankSize = 16 * 1024;
    private const int BankedWindowStart = 0x4000;
    private const int BankedWindowEnd = 0x8000;
    private const int MaxSimpleMbc1BankCount = 32;
    public const int MaxProgramTailBankCount = MaxSimpleMbc1BankCount - 1;

    private GameBoyRomLayout(
        bool usesBankedMusic,
        int programTailBankCount,
        int romSize,
        byte romSizeCode,
        GameBoyWorldPackPlacement? worldPackPlacement,
        IReadOnlyDictionary<string, GameBoyReadOnlyDataPlacement> readOnlyDataPlacements,
        IReadOnlyDictionary<string, GameBoyBankedAudioPlacement> musicPlacements,
        IReadOnlyDictionary<string, GameBoyBankedAudioPlacement> soundEffectPlacements)
    {
        UsesBankedMusic = usesBankedMusic;
        ProgramTailBankCount = programTailBankCount;
        RomSize = romSize;
        RomSizeCode = romSizeCode;
        WorldPackPlacement = worldPackPlacement;
        ReadOnlyDataPlacements = readOnlyDataPlacements;
        MusicPlacements = musicPlacements;
        SoundEffectPlacements = soundEffectPlacements;
    }

    public static GameBoyRomLayout RomOnly { get; } = new(
        usesBankedMusic: false,
        programTailBankCount: 0,
        romSize: 32 * 1024,
        romSizeCode: 0x00,
        worldPackPlacement: null,
        readOnlyDataPlacements: new Dictionary<string, GameBoyReadOnlyDataPlacement>(),
        musicPlacements: new Dictionary<string, GameBoyBankedAudioPlacement>(),
        soundEffectPlacements: new Dictionary<string, GameBoyBankedAudioPlacement>());

    public bool UsesBankedMusic { get; }

    public int ProgramTailBankCount { get; }

    public int RomSize { get; }

    public byte RomSizeCode { get; }

    public GameBoyWorldPackPlacement? WorldPackPlacement { get; }

    public bool UsesBankedWorldPack => WorldPackPlacement is not null;

    public bool UsesBankedReadOnlyData => ReadOnlyDataPlacements.Count != 0;

    public IReadOnlyDictionary<string, GameBoyReadOnlyDataPlacement> ReadOnlyDataPlacements { get; }

    public IReadOnlyDictionary<string, GameBoyBankedAudioPlacement> MusicPlacements { get; }

    public IReadOnlyDictionary<string, GameBoyBankedAudioPlacement> SoundEffectPlacements { get; }

    public bool TryReadOnlyDataPlacement(string label, out GameBoyReadOnlyDataPlacement placement)
    {
        return ReadOnlyDataPlacements.TryGetValue(label, out placement!);
    }

    public GameBoyBankedAudioPlacement MusicPlacement(string name)
    {
        return MusicPlacements.TryGetValue(name, out var placement)
            ? placement
            : throw new InvalidOperationException($"Unknown banked Game Boy music asset '{name}'.");
    }

    public GameBoyBankedAudioPlacement SoundEffectPlacement(string name)
    {
        return SoundEffectPlacements.TryGetValue(name, out var placement)
            ? placement
            : throw new InvalidOperationException($"Unknown banked Game Boy SFX asset '{name}'.");
    }

    public static GameBoyRomLayout CreateBankedMusicLayout(
        GameBoyVideoProgram program,
        IReadOnlyList<GameBoyReadOnlyDataBlob> readOnlyData,
        byte[]? packedWorldBytes,
        int programTailBankCount = 0,
        bool bankReadOnlyData = false)
    {
        var readOnlyPlacements = new Dictionary<string, GameBoyReadOnlyDataPlacement>();
        var placements = new Dictionary<string, GameBoyBankedAudioPlacement>();
        var soundEffectPlacements = new Dictionary<string, GameBoyBankedAudioPlacement>();
        var nextBank = 1 + programTailBankCount;
        var nextAddress = BankedWindowStart;
        GameBoyWorldPackPlacement? worldPackPlacement = null;
        if (packedWorldBytes is not null)
        {
            worldPackPlacement = GameBoyWorldPackPlacement.Create(
                checked((byte)nextBank),
                checked((ushort)nextAddress),
                packedWorldBytes);
            nextBank = worldPackPlacement.Segments[^1].Bank + 1;
            nextAddress = BankedWindowStart;
        }

        if (bankReadOnlyData)
        {
            foreach (var data in readOnlyData)
            {
                if (data.Data.Length > BankSize)
                {
                    throw new InvalidOperationException(
                        $"Game Boy read-only data block '{data.Label}' is {data.Data.Length} bytes, which exceeds one 16 KiB MBC1 bank.");
                }

                if (nextAddress + data.Data.Length > BankedWindowEnd)
                {
                    nextBank++;
                    nextAddress = BankedWindowStart;
                }

                readOnlyPlacements[data.Label] = new GameBoyReadOnlyDataPlacement(
                    data.Label,
                    (byte)nextBank,
                    (ushort)nextAddress,
                    data.Data);
                nextAddress += data.Data.Length;
            }

            if (readOnlyPlacements.Count != 0)
            {
                nextBank++;
            }
        }

        foreach (var asset in program.MusicAssetsInLoadOrder)
        {
            placements[asset.Name] = new GameBoyBankedAudioPlacement(asset.Name, (byte)nextBank, 0x4000, asset.Data);
            nextBank += Math.Max(1, (asset.Data.Length + BankSize - 1) / BankSize);
        }

        foreach (var asset in program.SoundEffectAssetsInLoadOrder)
        {
            soundEffectPlacements[asset.Name] = new GameBoyBankedAudioPlacement(asset.Name, (byte)nextBank, 0x4000, asset.Data);
            nextBank += Math.Max(1, (asset.Data.Length + BankSize - 1) / BankSize);
        }

        var bankCount = RoundBankCount(nextBank);
        if (bankCount > MaxSimpleMbc1BankCount)
        {
            throw new InvalidOperationException(
                $"Generated Game Boy ROM needs {bankCount} banks, but the current transparent MBC1 lowering supports up to {MaxSimpleMbc1BankCount} banks.");
        }

        return new GameBoyRomLayout(
            usesBankedMusic: placements.Count != 0 || soundEffectPlacements.Count != 0,
            programTailBankCount: programTailBankCount,
            romSize: bankCount * BankSize,
            romSizeCode: ToRomSizeCode(bankCount),
            worldPackPlacement: worldPackPlacement,
            readOnlyDataPlacements: readOnlyPlacements,
            musicPlacements: placements,
            soundEffectPlacements: soundEffectPlacements);
    }

    private static int RoundBankCount(int requiredBanks)
    {
        var banks = 2;
        while (banks < requiredBanks)
        {
            banks *= 2;
        }

        return banks;
    }

    private static byte ToRomSizeCode(int bankCount) => bankCount switch
    {
        2 => 0x00,
        4 => 0x01,
        8 => 0x02,
        16 => 0x03,
        32 => 0x04,
        _ => throw new InvalidOperationException($"Unsupported Game Boy ROM bank count '{bankCount}'."),
    };
}

internal sealed record GameBoyReadOnlyDataBlob(string Label, byte[] Data);

internal sealed record GameBoyReadOnlyDataPlacement(string Label, byte Bank, ushort Address, byte[] Data);

internal sealed record GameBoyBankedAudioPlacement(string Name, byte Bank, ushort Address, byte[] Data);
