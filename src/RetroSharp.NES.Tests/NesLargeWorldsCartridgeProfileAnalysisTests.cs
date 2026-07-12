namespace RetroSharp.NES.Tests;

using Xunit;

public sealed class NesLargeWorldsCartridgeProfileAnalysisTests
{
    [Fact]
    public void Forced_mmc3_tvrom_link_uses_the_accepted_header_and_physical_sizes()
    {
        const string source = """
                              void Main() {
                                  return;
                              }
                              """;

        var rom = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTests(source);

        Assert.Equal(16 + 64 * 1_024 + 16 * 1_024, rom.Length);
        Assert.Equal(
            new byte[]
            {
                (byte)'N', (byte)'E', (byte)'S', 0x1A,
                0x04, 0x02, 0x48, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            },
            rom[..16]);

        var prg = rom.AsSpan(16, 64 * 1_024);
        Assert.InRange(ReadWord(prg, prg.Length - 4), 0xE000, 0xFFF9);
        Assert.Equal(0x78, prg[6 * 8 * 1_024]);
    }

    [Fact]
    public void Mmc3_tvrom_layout_models_all_eight_physical_prg_banks()
    {
        var layout = NesCartridgeLayout.Create(NesCartridgeProfile.Mmc3Tvrom, useFourScreenNametables: false);

        Assert.Equal(8, layout.PrgSections.Count);
        Assert.Equal(NesPrgSectionKind.WorldR6, layout.PrgSections[0].Kind);
        Assert.Equal(NesPrgSectionKind.PinnedR7, layout.PrgSections[1].Kind);
        Assert.Equal(NesPrgSectionKind.BootR7, layout.PrgSections[2].Kind);
        Assert.All(layout.PrgSections.Skip(3).Take(3), section => Assert.Equal(NesPrgSectionKind.WorldR6, section.Kind));
        Assert.All(layout.PrgSections.Skip(6), section => Assert.Equal(NesPrgSectionKind.FixedRuntime, section.Kind));
        Assert.Equal(
            Enumerable.Range(0, 8).Select(bank => bank * 8 * 1_024),
            layout.PrgSections.Select(section => section.PhysicalOffset));
        Assert.All(layout.PrgSections, section => Assert.Equal(8 * 1_024, section.Size));
    }

    [Fact]
    public void Forced_mmc3_tvrom_link_bootstraps_fixed_mode_banks_chr_and_disabled_irqs()
    {
        const string source = """
                              void Main() {
                                  return;
                              }
                              """;

        var rom = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTests(source);
        var prg = rom.AsSpan(16, 64 * 1_024);
        var fixedRuntime = prg[(6 * 8 * 1_024)..];

        foreach (var bank in new[] { 0, 1, 3, 4, 5 })
        {
            var marker = (byte)(0xA0 + bank);
            Assert.All(prg.Slice(bank * 8 * 1_024, 8 * 1_024).ToArray(), value => Assert.Equal(marker, value));
        }
        Assert.All(
            prg.Slice(2 * 8 * 1_024 + 4_128, 8 * 1_024 - 4_128).ToArray(),
            value => Assert.Equal(0xA2, value));

        Assert.True(ContainsSequence(fixedRuntime, [0xA9, 0x00, 0x8D, 0x00, 0xE0]), "Reset must disable and acknowledge MMC3 IRQs.");
        Assert.True(ContainsSequence(fixedRuntime, Mmc3RegisterWrite(register: 0, value: 0)));
        Assert.True(ContainsSequence(fixedRuntime, Mmc3RegisterWrite(register: 1, value: 2)));
        Assert.True(ContainsSequence(fixedRuntime, Mmc3RegisterWrite(register: 2, value: 4)));
        Assert.True(ContainsSequence(fixedRuntime, Mmc3RegisterWrite(register: 3, value: 5)));
        Assert.True(ContainsSequence(fixedRuntime, Mmc3RegisterWrite(register: 4, value: 6)));
        Assert.True(ContainsSequence(fixedRuntime, Mmc3RegisterWrite(register: 5, value: 7)));
        Assert.True(ContainsSequence(fixedRuntime, [.. Mmc3RegisterWrite(register: 6, value: 0), 0x8D, 0x24, 0x03]));
        Assert.True(ContainsSequence(fixedRuntime, [.. Mmc3RegisterWrite(register: 7, value: 2), 0x8D, 0x25, 0x03]));
        Assert.True(ContainsSequence(fixedRuntime, [.. Mmc3RegisterWrite(register: 7, value: 1), 0x8D, 0x25, 0x03]));
        Assert.False(ContainsSequence(fixedRuntime, [0x8D, 0x00, 0xA0]), "TVROM hardwires four-screen nametables and must not write MMC3 mirroring.");

        Assert.True(ContainsSequence(fixedRuntime, Mmc3BankHelper(register: 6, shadowAddress: 0x0324)));
        Assert.True(ContainsSequence(fixedRuntime, Mmc3BankHelper(register: 7, shadowAddress: 0x0325)));
    }

    [Fact]
    public void Forced_mmc3_tvrom_vectors_target_fixed_reset_and_interrupt_handlers()
    {
        const string source = """
                              void Main() {
                                  return;
                              }
                              """;

        var rom = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTests(source);
        var prg = rom.AsSpan(16, 64 * 1_024);
        var nmiVector = ReadWord(prg, prg.Length - 6);
        var resetVector = ReadWord(prg, prg.Length - 4);
        var irqVector = ReadWord(prg, prg.Length - 2);

        Assert.InRange(nmiVector, 0xC000, 0xFFF9);
        Assert.InRange(resetVector, 0xE000, 0xFFF9);
        Assert.InRange(irqVector, 0xC000, 0xFFF9);
        Assert.Equal(0x48, ByteAtCpuAddress(prg, nmiVector));
        Assert.True(ContainsSequence(prg, IncrementWordSequence(NesPackedCameraRuntime.FrameCounterLow)));
        Assert.Equal(0x78, ByteAtCpuAddress(prg, resetVector));
        Assert.Equal(0x40, ByteAtCpuAddress(prg, irqVector));
    }

    private static byte[] IncrementWordSequence(ushort lowAddress) =>
    [
        0xEE, (byte)lowAddress, (byte)(lowAddress >> 8),
        0xD0, 0x03,
        0xEE, (byte)(lowAddress + 1), (byte)((lowAddress + 1) >> 8),
    ];

    [Fact]
    public void Generated_mmc3_smoke_contains_four_distinct_nametable_probes()
    {
        var leftColumn = string.Join(", ", Enumerable.Range(0, 60).Select(row => row == 0 ? 1 : row == 30 ? 3 : 0));
        var rightColumn = string.Join(", ", Enumerable.Range(0, 60).Select(row => row == 0 ? 2 : row == 30 ? 4 : 0));
        var source = $$"""
                       void Main() {
                           Video.Init();
                           World.Column(0, {{leftColumn}});
                           World.Column(32, {{rightColumn}});
                           World.Map(64, 0, 60);
                           Camera.Init(64, 0, 60);
                           u8 y = 0;
                           while (true) {
                               Video.WaitVBlank();
                               Camera.SetPosition(0, y);
                               Camera.Apply();
                           }
                       }
                       """;

        var rom = RetroSharp.NES.NesRomCompiler.CompileSourceForMmc3TvromTests(
            source,
            sdkLibraryImports: [RetroSharp.Sdk.SdkImportResolver.Portable2D]);
        var expectedNametables = new byte[4 * 1_024];
        expectedNametables[0] = 1;
        expectedNametables[1 * 1_024] = 2;
        expectedNametables[2 * 1_024] = 3;
        expectedNametables[3 * 1_024] = 4;

        Assert.True(
            ContainsSequence(rom.AsSpan(16 + 2 * 8 * 1_024 + 32, 4 * 1_024), expectedNametables),
            "The generated AprNes smoke must source four distinct TVROM nametables from boot-only R7.");
    }

    [Fact]
    public void Mapper_selection_is_not_exposed_as_a_public_source_compiler_argument()
    {
        var publicParameters = typeof(RetroSharp.NES.NesRomCompiler)
            .GetMethods()
            .Where(method => method.IsPublic)
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(publicParameters, parameter => parameter.Contains("mapper", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(publicParameters, parameter => parameter.Contains("profile", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Full_stage1_layout_selects_the_mmc3_four_screen_profile()
    {
        const int currentNoAudioPrgBytes = 41_907;
        const int rawVisualAndCollisionBytes = 24_960;
        const int initialNametableBytes = 4_096;
        const int paletteBytes = 32;
        const int flagRowPointerBytes = 2 * 40;
        var attributeRows = CeilingDivide(40, 4);
        var attributeBlocksPerRow = CeilingDivide(312, 4);
        var columnAttributeBytes = attributeRows * attributeBlocksPerRow;

        Assert.Equal(10, attributeRows);
        Assert.Equal(78, attributeBlocksPerRow);
        Assert.Equal(780, columnAttributeBytes);

        var fixedRuntimeWithoutAudio = currentNoAudioPrgBytes
                                       - rawVisualAndCollisionBytes
                                       - initialNametableBytes
                                       - paletteBytes
                                       - flagRowPointerBytes
                                       - columnAttributeBytes;
        Assert.Equal(11_959, fixedRuntimeWithoutAudio);

        const int prgBaseAddress = 0x8000;
        const int measuredMusicDataEndAddress = 0x134FC;
        const int musicBytes = 4_126;
        var fixedRuntimeWithAudioBeforeMusic = measuredMusicDataEndAddress - prgBaseAddress - musicBytes;
        var audioRuntimeBytes = fixedRuntimeWithAudioBeforeMusic - currentNoAudioPrgBytes;
        Assert.Equal(299, audioRuntimeBytes);
        var fixedRuntimeWithAudio = fixedRuntimeWithoutAudio + audioRuntimeBytes;
        Assert.Equal(12_258, fixedRuntimeWithAudio);

        const int fixedRegionBaseAddress = 0xC000;
        var dpcmPlacements = PlaceDpcmBlocks(fixedRegionBaseAddress, fixedRuntimeWithAudio, 1_153, 129);
        Assert.Equal(
            new[]
            {
                new DpcmPlacement(StartAddress: 0xF000, EndAddress: 0xF480, Length: 1_153),
                new DpcmPlacement(StartAddress: 0xF4C0, EndAddress: 0xF540, Length: 129),
            },
            dpcmPlacements);
        var alignedDpcmBytes = dpcmPlacements[^1].EndAddress + 1 - (fixedRegionBaseAddress + fixedRuntimeWithAudio);
        Assert.Equal(1_375, alignedDpcmBytes);

        const int vectorBytes = 6;
        var fixedBankBytes = fixedRuntimeWithAudio + alignedDpcmBytes + vectorBytes;
        var pinnedRuntimeDataBytes = flagRowPointerBytes + columnAttributeBytes + musicBytes + 26;
        var bootDataBytes = initialNametableBytes + paletteBytes;
        var nonWorldDataBytes = pinnedRuntimeDataBytes + bootDataBytes;
        const int worldPackBytes = 7_920;
        const int chrBytes = 3_056;

        Assert.Equal(13_639, fixedBankBytes);
        Assert.Equal(5_012, pinnedRuntimeDataBytes);
        Assert.Equal(4_128, bootDataBytes);
        Assert.Equal(9_140, nonWorldDataBytes);
        var reconstructedPrgBytes = fixedBankBytes + nonWorldDataBytes + worldPackBytes;
        Assert.Equal(30_699, reconstructedPrgBytes);
        Assert.Equal(2_069, 32 * 1_024 - reconstructedPrgBytes);
        Assert.True(reconstructedPrgBytes < 32 * 1_024, "This reconstruction is a feasible NROM lower bound, not proof of final production fit.");
        Assert.Equal(2_745, 16 * 1_024 - fixedBankBytes);
        Assert.Equal(3_180, 8 * 1_024 - pinnedRuntimeDataBytes);
        Assert.Equal(4_064, 8 * 1_024 - bootDataBytes);
        Assert.Equal(272, 8 * 1_024 - worldPackBytes);
        Assert.Equal(13_328, 16 * 1_024 - chrBytes);

        var uxromPinnedRuntimeBytes = worldPackBytes + musicBytes + 26 + columnAttributeBytes + paletteBytes;
        Assert.Equal(12_884, uxromPinnedRuntimeBytes);
        Assert.True(uxromPinnedRuntimeBytes + flagRowPointerBytes < 16 * 1_024);

        var candidates = new[]
        {
            new Candidate("MMC1", Mapper: 1, SwitchableWindows: 1, CanonicalChrRom: true, CanonicalFourScreen: false, Backend: "ADNES"),
            new Candidate("UxROM", Mapper: 2, SwitchableWindows: 1, CanonicalChrRom: false, CanonicalFourScreen: false, Backend: "ADNES"),
            new Candidate("MMC3/TVROM", Mapper: 4, SwitchableWindows: 2, CanonicalChrRom: true, CanonicalFourScreen: true, Backend: "AprNes"),
        };

        var selected = SelectCanonicalProfile(candidates);
        Assert.Equal("MMC3/TVROM", selected.Name);
        Assert.Equal(4, selected.Mapper);
        Assert.Equal(2, selected.SwitchableWindows);

        var hypotheticalOneWindowProfile = selected with
        {
            Name = "Hypothetical canonical one-window profile",
            SwitchableWindows = 1,
        };
        Assert.Equal(
            hypotheticalOneWindowProfile,
            SelectCanonicalProfile([candidates[0], candidates[1], hypotheticalOneWindowProfile]));

        var header = InesHeader(prgBanks16K: 4, chrBanks8K: 2, mapper: selected.Mapper, fourScreen: true);
        Assert.Equal(new byte[]
        {
            (byte)'N', (byte)'E', (byte)'S', 0x1A,
            0x04, 0x02, 0x48, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        }, header);

        var switchableBanks = new[]
        {
            new Bank(0, "$8000-$9FFF via R6", worldPackBytes, 8 * 1_024),
            new Bank(1, "$A000-$BFFF via R7, pinned after boot", pinnedRuntimeDataBytes, 8 * 1_024),
            new Bank(2, "$A000-$BFFF via R7 during boot only", bootDataBytes, 8 * 1_024),
        };
        var fixedRegion = new Region("$C000-$FFFF in physical banks 6-7", fixedBankBytes, 16 * 1_024);
        Assert.Equal(272, switchableBanks[0].CapacityBytes - switchableBanks[0].UsedBytes);
        Assert.Equal(3_180, switchableBanks[1].CapacityBytes - switchableBanks[1].UsedBytes);
        Assert.Equal(4_064, switchableBanks[2].CapacityBytes - switchableBanks[2].UsedBytes);
        Assert.Equal(2_745, fixedRegion.CapacityBytes - fixedRegion.UsedBytes);

        var adrPath = RepositoryFile("docs/NesLargeWorldsCartridgeProfile.md", requireExists: false);
        Assert.True(File.Exists(adrPath), "The accepted NES Large Worlds cartridge-profile ADR is missing.");
        var adr = File.ReadAllText(adrPath);
        Assert.Contains(
            "Status: **accepted for LW-0.3 on 2026-07-10; forced linker/runtime foundation",
            adr,
            StringComparison.Ordinal);
        Assert.Contains("MMC3/TVROM", adr, StringComparison.Ordinal);
        Assert.Contains("`$8000-$9FFF`", adr, StringComparison.Ordinal);
        Assert.Contains("`$A000-$BFFF`", adr, StringComparison.Ordinal);
        Assert.Contains("`$C000-$FFFF`", adr, StringComparison.Ordinal);
        Assert.Contains("30,699", adr, StringComparison.Ordinal);
        Assert.Contains("13,639", adr, StringComparison.Ordinal);
        Assert.Contains("9,140", adr, StringComparison.Ordinal);
        Assert.Contains("7,920", adr, StringComparison.Ordinal);
        Assert.Contains("3,056", adr, StringComparison.Ordinal);
        Assert.Contains("0x48", adr, StringComparison.Ordinal);
        Assert.Contains("AprNesDebugSessionTests.Load_rom_accepts_mapper4_and_steps_instruction", adr, StringComparison.Ordinal);
        Assert.Contains("Auto_backend_uses_aprnes_for_mapper4", adr, StringComparison.Ordinal);
        Assert.Contains("issue #247", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mapper 0", adr, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IRQ HUD", adr, StringComparison.Ordinal);
    }

    [Fact]
    public void World_bank_calls_restore_the_entry_bank_across_nesting_and_interrupts()
    {
        var state = new BankState(currentWorldBank: 3);

        state.WithWorldBank(4, () =>
        {
            Assert.Equal(4, state.CurrentWorldBank);
            state.RunBankNeutralNmi();
            Assert.Equal(4, state.CurrentWorldBank);

            state.WithWorldBank(5, () =>
            {
                Assert.Equal(5, state.CurrentWorldBank);
                state.RunBankNeutralNmi();
            });

            Assert.Equal(4, state.CurrentWorldBank);
        });

        Assert.Equal(3, state.CurrentWorldBank);
        Assert.Equal(new[] { 4, 5, 4, 3 }, state.BankWrites);
    }

    private static int CeilingDivide(int value, int divisor) => (value + divisor - 1) / divisor;

    private static ushort ReadWord(ReadOnlySpan<byte> bytes, int offset) =>
        (ushort)(bytes[offset] | bytes[offset + 1] << 8);

    private static byte ByteAtCpuAddress(ReadOnlySpan<byte> prg, ushort address) =>
        prg[6 * 8 * 1_024 + address - 0xC000];

    private static byte[] Mmc3RegisterWrite(byte register, byte value) =>
        [0xA9, register, 0x8D, 0x00, 0x80, 0xA9, value, 0x8D, 0x01, 0x80];

    private static byte[] Mmc3BankHelper(byte register, ushort shadowAddress) => register == 6
        ?
        [
            0x48,
            0xAD, 0x80, 0x03,
            0xF0, 0x03,
            0xEE, 0x75, 0x03,
            0xA9, register,
            0x8D, 0x00, 0x80,
            0x68,
            0x8D, (byte)(shadowAddress & 0xFF), (byte)(shadowAddress >> 8),
            0x8D, 0x01, 0x80,
            0x60,
        ]
        :
        [
            0x48,
            0xA9, register,
            0x8D, 0x00, 0x80,
            0x68,
            0x8D, (byte)(shadowAddress & 0xFF), (byte)(shadowAddress >> 8),
            0x8D, 0x01, 0x80,
            0x60,
        ];

    private static bool ContainsSequence(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> sequence) =>
        bytes.IndexOf(sequence) >= 0;

    private static DpcmPlacement[] PlaceDpcmBlocks(int baseAddress, int startOffset, params int[] blockLengths)
    {
        var cursor = startOffset;
        var placements = new List<DpcmPlacement>();
        foreach (var length in blockLengths.OrderDescending())
        {
            cursor = CeilingDivide(cursor, 64) * 64;
            var startAddress = baseAddress + cursor;
            cursor += length;
            placements.Add(new DpcmPlacement(startAddress, baseAddress + cursor - 1, length));
        }

        return placements.ToArray();
    }

    private static Candidate SelectCanonicalProfile(IEnumerable<Candidate> candidates) =>
        Assert.Single(candidates, candidate =>
            candidate.CanonicalChrRom &&
            candidate.CanonicalFourScreen &&
            candidate.Backend == "AprNes");

    private static byte[] InesHeader(int prgBanks16K, int chrBanks8K, int mapper, bool fourScreen)
    {
        var header = new byte[16];
        header[0] = (byte)'N';
        header[1] = (byte)'E';
        header[2] = (byte)'S';
        header[3] = 0x1A;
        header[4] = (byte)prgBanks16K;
        header[5] = (byte)chrBanks8K;
        header[6] = (byte)((mapper << 4) | (fourScreen ? 0x08 : 0x00));
        header[7] = (byte)(mapper & 0xF0);
        return header;
    }

    private static string RepositoryFile(string relativePath, bool requireExists = true)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate) || (!requireExists && Directory.Exists(Path.GetDirectoryName(candidate))))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not find repository path '{relativePath}'.");
    }

    private sealed class BankState(int currentWorldBank)
    {
        private readonly Stack<int> savedBanks = [];

        public int CurrentWorldBank { get; private set; } = currentWorldBank;

        public List<int> BankWrites { get; } = [];

        public void WithWorldBank(int bank, Action operation)
        {
            savedBanks.Push(CurrentWorldBank);
            Select(bank);
            operation();
            Select(savedBanks.Pop());
        }

        public void RunBankNeutralNmi()
        {
            // The v1 NMI handler is fixed and may read the pinned audio window,
            // but it neither reads nor writes the R6 world window.
        }

        private void Select(int bank)
        {
            CurrentWorldBank = bank;
            BankWrites.Add(bank);
        }
    }

    private readonly record struct Candidate(
        string Name,
        int Mapper,
        int SwitchableWindows,
        bool CanonicalChrRom,
        bool CanonicalFourScreen,
        string Backend);

    private readonly record struct DpcmPlacement(int StartAddress, int EndAddress, int Length);

    private readonly record struct Bank(int PhysicalBank, string Window, int UsedBytes, int CapacityBytes);

    private readonly record struct Region(string Name, int UsedBytes, int CapacityBytes);
}
