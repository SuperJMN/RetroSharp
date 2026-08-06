namespace RetroSharp.NES;

using System.Globalization;
using System.Text.Json;
using RetroSharp.Core.Sdk;

/// <summary>
/// One PRG region an author can run out of, with the room that is left in it.
/// <see cref="CapacityBytes"/> is <see cref="UsedBytes"/> plus <see cref="HeadroomBytes"/>, which is
/// how the build itself measures the region rather than a re-derived board constant.
/// </summary>
internal sealed record NesCapacityRegion(
    string Region,
    int UsedBytes,
    int HeadroomBytes,
    int CapacityBytes,
    double UsedPercent);

/// <summary>One placement unit's phase and the R6 banks it landed in.</summary>
internal sealed record NesCapacityPhase(
    string Unit,
    string Phase,
    IReadOnlyList<int> PhysicalBanks,
    int Bytes,
    bool IsHotFramePhase);

/// <summary>One R6 program bank's occupancy.</summary>
internal sealed record NesCapacityBank(int PhysicalBank, int UsedBytes, int CapacityBytes, double UsedPercent);

/// <summary>The banked half of the program: where phases went and what R6 has left.</summary>
internal sealed record NesCapacityBankedProgram(
    NesCapacityRegion Region,
    string? HotPhaseUnit,
    int? HotPhasePhysicalBank,
    int HotPhaseBytes,
    IReadOnlyList<NesCapacityPhase> Phases,
    IReadOnlyList<NesCapacityBank> Banks);

/// <summary>
/// Bytes an author pays for indirectly rather than by writing them. Shared SDK subroutines and
/// outlined user functions are counted, not sized: the build measures no byte total for either, and
/// this report never invents one.
/// </summary>
internal sealed record NesCapacityAttribution(
    int FixedVeneerBytes,
    int PinnedR7Bytes,
    int BootR7Bytes,
    int ProgramR6Bytes,
    int ResidentChrBytes,
    int SharedSdkSubroutines,
    int SharedSdkSubroutineCallSites,
    int OutlinedUserFunctions,
    int OutlinedUserFunctionCallSites);

/// <summary>One user function and the bytes inline expansion spends beyond a single body.</summary>
internal sealed record NesCapacityDuplicationHolder(
    string Function,
    string Phase,
    int EmittedCopies,
    int DuplicatedBytes,
    int BodyBytes,
    int CallsPerFrame,
    bool RepeatsPerFrame);

/// <summary>
/// What inline expansion and bank placement spent on repetition. <see cref="Coverage"/> names what
/// this figure cannot see, so it is never read as total recoverable headroom.
/// </summary>
internal sealed record NesCapacityDuplication(
    int InlineDuplicatedBytes,
    int BankDuplicatedSharedBytes,
    IReadOnlyList<NesCapacityDuplicationHolder> TopFunctions,
    string Coverage);

/// <summary>One CPU-work window, named so a cycle figure is never read against the wrong budget.</summary>
internal sealed record NesCapacityCpuWindow(
    string Window,
    long CapacityCycles,
    long KnownLowerCycles,
    long? KnownUpperCycles,
    string Status,
    double? UsedPercent);

/// <summary>
/// Peak per-frame CPU work, as an observation. This report never gates on it.
/// <see cref="UncalibratedContributors"/> names the work the model cannot cost yet, which is why a
/// window's status can be <c>incomplete</c> even when its known cycles fit.
/// </summary>
internal sealed record NesCapacityCpuWork(
    string Profile,
    string Unit,
    IReadOnlyList<NesCapacityCpuWindow> Windows,
    IReadOnlyList<string> UncalibratedContributors);

/// <summary>A region close enough to full that the next change is likely to move placement.</summary>
internal sealed record NesCapacityWarning(
    string Category,
    string Region,
    int HeadroomBytes,
    int CapacityBytes,
    double HeadroomPercent,
    double ThresholdPercent,
    string Message);

/// <summary>
/// What a finished NES build spent, where it spent it, and how much room is left. Diagnostic only:
/// nothing here fails a build, and no field is a gate.
/// </summary>
internal sealed record NesCapacityReport(
    string Schema,
    string Target,
    string SelectedProfile,
    int PrgRomSizeBytes,
    int ChrRomSizeBytes,
    NesCapacityRegion FixedRegion,
    NesCapacityBankedProgram? BankedProgram,
    NesCapacityAttribution Attribution,
    NesCapacityDuplication Duplication,
    NesCapacityCpuWork CpuWork,
    IReadOnlyList<NesCapacityWarning> Warnings,
    IReadOnlyList<string> Notes);

/// <summary>
/// Projects a finished NES build into the capacity and placement report an author reads to see how
/// close the cartridge is to full and which phase is about to move.
/// </summary>
/// <remarks>
/// Everything here already exists on <see cref="NesRomBuildReport"/>; this type only names it,
/// relates the two headroom figures to the regions that own them, and raises a near-cliff warning.
/// It never measures, never changes placement, and never fails a build: the only build-time
/// failures in this area stay <see cref="NesProgramBankCapacityException"/> and
/// <see cref="NesFramePlan.RequireVideoSafeBudget"/>.
/// </remarks>
internal static class NesCapacityReportProjection
{
    internal const string Schema = "retrosharp.nes-capacity/v1";

    /// <summary>Headroom below this share of a region is reported as a near-cliff warning.</summary>
    internal const double NearCliffHeadroomPercent = 5.0;

    internal const string FixedRegionName = "fixed-prg";

    internal const string BankedRegionName = "program-r6";

    internal const string NearCliffCategory = "near-cliff";

    private const int TopDuplicationHolders = 5;

    private const string DuplicationCoverage =
        "Lower bound. Pure functions the compiler answered from a generated ROM table never reach " +
        "the user-function path, so they are absent from this accounting; reported duplication is " +
        "not total recoverable headroom.";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static string Serialize(NesRomBuildResult result) => Serialize(Create(result));

    internal static string Serialize(NesCapacityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, Json);
    }

    internal static NesCapacityReport Create(NesRomBuildResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var report = result.Report;
        var fixedRegion = Region(FixedRegionName, report.FixedPayloadBytes, report.FixedHeadroomBytes);
        var banked = BankedProgram(report.BankPlacement);

        return new NesCapacityReport(
            Schema,
            "nes",
            report.SelectedProfile,
            report.PrgRomSize,
            report.ChrRomSize,
            fixedRegion,
            banked,
            Attribution(report),
            Duplication(report),
            CpuWork(report.CpuWork),
            Warnings(fixedRegion, banked?.Region),
            Notes(report));
    }

    private static NesCapacityRegion Region(string name, int usedBytes, int headroomBytes)
    {
        var capacity = checked(usedBytes + headroomBytes);
        return new NesCapacityRegion(name, usedBytes, headroomBytes, capacity, Percent(usedBytes, capacity) ?? 0);
    }

    private static NesCapacityBankedProgram? BankedProgram(NesProgramBankPlacementReport? placement)
    {
        if (placement is null)
        {
            return null;
        }

        var used = placement.Banks.Sum(bank => bank.UsedBytes);
        return new NesCapacityBankedProgram(
            Region(BankedRegionName, used, placement.ProgramR6HeadroomBytes),
            placement.HotPhaseUnitName,
            placement.HotPhasePhysicalBank,
            placement.HotPhaseBytes,
            placement.Phases
                .Select(phase => new NesCapacityPhase(
                    phase.UnitName,
                    phase.Phase.ToString(),
                    phase.PhysicalBanks,
                    phase.Bytes,
                    string.Equals(phase.UnitName, placement.HotPhaseUnitName, StringComparison.Ordinal)))
                .ToArray(),
            placement.Banks
                .Select(bank => new NesCapacityBank(
                    bank.PhysicalBank,
                    bank.UsedBytes,
                    bank.CapacityBytes,
                    Percent(bank.UsedBytes, bank.CapacityBytes) ?? 0))
                .ToArray());
    }

    private static NesCapacityAttribution Attribution(NesRomBuildReport report) => new(
        report.FixedVeneerBytes,
        report.PinnedR7Bytes,
        report.BootR7Bytes,
        report.ProgramR6Bytes,
        report.ResidentChrBytes,
        report.SharedSdkSubroutines.Count,
        report.SharedSdkSubroutines.Sum(subroutine => subroutine.CallSites),
        report.OutlinedUserFunctions.Count,
        report.OutlinedUserFunctions.Sum(function => function.CallSites));

    private static NesCapacityDuplication Duplication(NesRomBuildReport report) => new(
        report.UserFunctionCalls.DuplicatedBytes,
        report.BankPlacement?.DuplicatedSharedBytes ?? 0,
        report.UserFunctionCalls.Functions
            .Where(function => function.DuplicatedBytes > 0)
            .OrderByDescending(function => function.DuplicatedBytes)
            .ThenBy(function => function.Name, StringComparer.Ordinal)
            .Take(TopDuplicationHolders)
            .Select(function => new NesCapacityDuplicationHolder(
                function.Name,
                function.Phase.ToString(),
                function.EmittedCopies,
                function.DuplicatedBytes,
                function.EmittedBodyBytes,
                function.CallsPerFrame,
                function.RepeatsPerFrame))
            .ToArray(),
        DuplicationCoverage);

    private static NesCapacityCpuWork CpuWork(SdkCpuWorkReport work)
    {
        var windows = new List<NesCapacityCpuWindow>
        {
            new(
                SdkCpuWorkWindowIds.Frame,
                work.FrameWindow,
                work.KnownLower,
                work.KnownUpper,
                work.Status,
                Percent(work.KnownUpper ?? work.KnownLower, work.FrameWindow)),
        };
        windows.AddRange(work.Windows
            .Where(window => !string.Equals(window.Id, SdkCpuWorkWindowIds.Frame, StringComparison.Ordinal))
            .Select(window => new NesCapacityCpuWindow(
                window.Id,
                window.Capacity,
                window.KnownLower,
                window.KnownUpper,
                window.Status,
                Percent(window.KnownUpper ?? window.KnownLower, window.Capacity))));

        return new NesCapacityCpuWork(
            work.Profile,
            work.Unit,
            windows,
            work.Unknowns.Select(unknown => unknown.Id).ToArray());
    }

    private static IReadOnlyList<NesCapacityWarning> Warnings(
        NesCapacityRegion fixedRegion,
        NesCapacityRegion? bankedRegion) =>
        new[] { fixedRegion, bankedRegion }
            .OfType<NesCapacityRegion>()
            .Select(NearCliff)
            .OfType<NesCapacityWarning>()
            .ToArray();

    /// <summary>
    /// A region whose remaining share has fallen under <see cref="NearCliffHeadroomPercent"/>. This
    /// is an early warning, never a gate: spilling past a full region is designed behaviour, so the
    /// build still succeeds.
    /// </summary>
    private static NesCapacityWarning? NearCliff(NesCapacityRegion region)
    {
        if (region.CapacityBytes <= 0)
        {
            return null;
        }

        var headroomPercent = Percent(region.HeadroomBytes, region.CapacityBytes) ?? 0;
        if (headroomPercent >= NearCliffHeadroomPercent)
        {
            return null;
        }

        return new NesCapacityWarning(
            NearCliffCategory,
            region.Region,
            region.HeadroomBytes,
            region.CapacityBytes,
            headroomPercent,
            NearCliffHeadroomPercent,
            $"NES {region.Region} region has {Number(region.HeadroomBytes)} of {Number(region.CapacityBytes)} " +
            $"bytes left ({headroomPercent.ToString("0.##", CultureInfo.InvariantCulture)}%), below the " +
            $"{NearCliffHeadroomPercent.ToString("0.##", CultureInfo.InvariantCulture)}% near-cliff share. " +
            "The next change of any size is likely to move placement or escalate the board. This is a " +
            "warning, not an error: the build succeeded.");
    }

    private static IReadOnlyList<string> Notes(NesRomBuildReport report)
    {
        var notes = new List<string>
        {
            "Diagnostic only. Nothing in this report fails or gates a build.",
            "Region capacity is used plus headroom as the build measured it, not a board constant.",
            "cpuWork is an observation, never a gate. The frame window is the whole NTSC frame and " +
            "video-safe is the VBlank window; both capacities are the ones the build already uses. Known " +
            "cycles cover only calibrated contributors, so an incomplete status means real cost is higher " +
            "than the figure shown and is not the video-safe cost the frame plan enforces. Read each " +
            "figure against its own window.",
            "callsPerFrame is static: it is not path sensitive and does not multiply loop iterations, so " +
            "it is a lower bound where repeatsPerFrame is set and an upper bound over mutually exclusive " +
            "branches otherwise.",
            "The build measures no size for a shared SDK subroutine or an outlined user-function body, so " +
            "attribution names how many exist and how many call sites they serve rather than claiming bytes " +
            "for them.",
        };

        if (report.BankPlacement is null)
        {
            notes.Add(
                "The program is entirely fixed-resident, so there is no phase-to-bank map and no R6 " +
                "program region to report.");
        }

        if (!report.UserFunctionCalls.HasFrameLoop)
        {
            notes.Add("This program has no frame loop, so every user-function call is one-shot.");
        }

        return notes;
    }

    private static double? Percent(long value, long capacity) =>
        capacity <= 0 ? null : Math.Round(value * 100.0 / capacity, 2, MidpointRounding.AwayFromZero);

    private static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
