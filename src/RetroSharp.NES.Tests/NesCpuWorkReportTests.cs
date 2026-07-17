namespace RetroSharp.NES.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Sdk;
using Xunit;
using static RetroSharp.NES.Tests.NesTestAssets;

public sealed class NesCpuWorkReportTests
{
    [Fact]
    public void Build_report_exposes_initial_nes_cpu_work_projection()
    {
        var baseDirectory = WriteSpritePng(
            "hero.png",
            8,
            8,
            Rows(8, 8, "01111110", "01222210", "01233210"));
        var build = CompileWithReport(SpriteFrameDelimitedSource, baseDirectory);
        var report = build.Report.CpuWork;

        Assert.Equal("nes", report.Target);
        Assert.Equal(build.Report.SelectedProfile, report.Profile);
        Assert.Equal("cpu-cycles", report.Unit);
        Assert.Equal(29_780, report.FrameWindow);
        Assert.Equal(513, report.KnownLower);
        Assert.Equal(514, report.KnownUpper);
        Assert.Equal(SdkCpuWorkStatuses.Incomplete, report.Status);

        var transfer = Assert.Single(report.Contributors, contributor => contributor.Id == SdkCpuWorkContributorIds.SpritePublishTransfer);
        Assert.Equal(SdkCpuWorkContributorCategories.TargetRuntime, transfer.Category);
        Assert.Equal("one retained sprite publication transfer", transfer.Basis);
        Assert.Equal(1, transfer.Count);
        Assert.Equal(513, transfer.UnitLower);
        Assert.Equal(514, transfer.UnitUpper);
        Assert.Equal(513, transfer.TotalLower);
        Assert.Equal(514, transfer.TotalUpper);
        Assert.Equal(SdkCpuWorkContributorIds.SpritePublish, transfer.DetailOf);

        Assert.Contains(report.Unknowns, unknown => unknown.Id == SdkCpuWorkContributorIds.SpritePublish);
        Assert.Contains(report.Unknowns, unknown => unknown.Id == SdkCpuWorkContributorIds.ActorPhaseDraw);
        Assert.Contains(report.Unknowns, unknown => unknown.Id == SdkCpuWorkContributorIds.TargetStructArrayAddress);
        Assert.Contains(report.Unknowns, unknown => unknown.Id == SdkCpuWorkContributorIds.UserDynamicLoop);
    }

    [Fact]
    public void Build_report_omits_oam_transfer_when_no_sprite_publication_can_run()
    {
        var build = CompileWithReport(FrameDelimitedSource);
        var report = build.Report.CpuWork;

        Assert.Equal(0, report.KnownLower);
        Assert.Equal(0, report.KnownUpper);
        Assert.DoesNotContain(report.Contributors, contributor => contributor.Id == SdkCpuWorkContributorIds.SpritePublishTransfer);
        Assert.Equal(SdkCpuWorkStatuses.Incomplete, report.Status);
    }

    [Fact]
    public void Arbitrary_user_loops_remain_unknown_instead_of_exact_cpu_work()
    {
        var build = CompileWithReport(UserLoopSource);
        var report = build.Report.CpuWork;

        Assert.DoesNotContain(report.Contributors, contributor => contributor.Id == SdkCpuWorkContributorIds.UserDynamicLoop);
        Assert.Contains(report.Unknowns, unknown => unknown.Id == SdkCpuWorkContributorIds.UserDynamicLoop);
        Assert.Equal(SdkCpuWorkStatuses.Incomplete, report.Status);
    }

    private const string FrameDelimitedSource = """
                                                void Main() {
                                                    Video.Init();
                                                    while (true) {
                                                        Video.WaitVBlank();
                                                    }
                                                }
                                                """;

    private const string SpriteFrameDelimitedSource = """
                                                      void Main() {
                                                          Video.Init();
                                                          Sprite.Asset(hero, "hero.png", 8, 8);
                                                          while (true) {
                                                              Video.WaitVBlank();
                                                              Sprite.Draw(hero, 24, 32, 0, false, 2);
                                                          }
                                                      }
                                                      """;

    private const string UserLoopSource = """
                                          void Main() {
                                              Video.Init();
                                              u8 i = 0;
                                              while (i != 10) {
                                                  i += 1;
                                              }
                                              while (true) {
                                                  Video.WaitVBlank();
                                              }
                                          }
                                          """;

    private static RetroSharp.NES.NesRomBuildResult CompileWithReport(string source, string? baseDirectory = null) =>
        RetroSharp.NES.NesRomCompiler.CompileSourceWithReport(
            source,
            baseDirectory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
}
