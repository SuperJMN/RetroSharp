namespace RetroSharp.GameBoy.Tests;

using RetroSharp.Core.Sdk;
using RetroSharp.Sdk;
using Xunit;
using static RetroSharp.GameBoy.Tests.GameBoyTestSupport;

public sealed class GameBoyCpuWorkReportTests
{
    [Fact]
    public void Build_report_exposes_initial_game_boy_cpu_work_projection()
    {
        var baseDirectory = WriteSpriteJsonAsset(
            "player.sprite.json",
            SpriteJson(Rows(8, 16, "01230123", "32103210")));
        var build = CompileWithReport(SpriteFrameDelimitedSource, baseDirectory);
        var report = build.Report.CpuWork;

        Assert.Equal("gb", report.Target);
        Assert.Equal(build.Report.SelectedProfile, report.Profile);
        Assert.Equal("t-cycles", report.Unit);
        Assert.Equal(70_224, report.FrameWindow);
        Assert.Equal(640, report.KnownLower);
        Assert.Equal(640, report.KnownUpper);
        Assert.Equal(SdkCpuWorkStatuses.Incomplete, report.Status);

        var transfer = Assert.Single(report.Contributors, contributor => contributor.Id == SdkCpuWorkContributorIds.SpritePublishTransfer);
        Assert.Equal(SdkCpuWorkContributorCategories.TargetRuntime, transfer.Category);
        Assert.Equal("one retained sprite publication transfer", transfer.Basis);
        Assert.Equal(1, transfer.Count);
        Assert.Equal(640, transfer.UnitLower);
        Assert.Equal(640, transfer.UnitUpper);
        Assert.Equal(640, transfer.TotalLower);
        Assert.Equal(640, transfer.TotalUpper);
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
                                                          Sprite.Asset(player, "player.sprite.json");
                                                          while (true) {
                                                              Video.WaitVBlank();
                                                              Sprite.Draw(player, 72, 80, 0, false, 1);
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

    private static RetroSharp.GameBoy.GameBoyRomBuildResult CompileWithReport(string source, string? baseDirectory = null) =>
        RetroSharp.GameBoy.GameBoyRomCompiler.CompileSourceWithReport(
            source,
            baseDirectory,
            sdkLibraryImports: [SdkImportResolver.Portable2D]);
}
