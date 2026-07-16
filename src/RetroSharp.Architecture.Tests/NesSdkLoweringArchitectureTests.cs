namespace RetroSharp.Architecture.Tests;

using RetroSharp.NES;

public sealed class NesSdkLoweringArchitectureTests
{
    private static readonly string[] PurposeNamedModules =
    [
        "src/RetroSharp.NES/NesCartridgeLayout.cs",
        "src/RetroSharp.NES/NesRuntimeCompiler.cs",
        "src/RetroSharp.NES/NesSdkOperationLowerer.cs",
        "src/RetroSharp.NES/NesSdkLoweringContext.cs",
        "src/RetroSharp.NES/NesSdkStreamReader.cs",
        "src/RetroSharp.NES/PrgBuilder.cs",
    ];

    private static readonly string[] RuntimeFeatureModules =
    [
        "src/RetroSharp.NES/NesRuntimeCompiler.Audio.cs",
        "src/RetroSharp.NES/NesRuntimeCompiler.Calls.cs",
        "src/RetroSharp.NES/NesRuntimeCompiler.ControlFlow.cs",
        "src/RetroSharp.NES/NesRuntimeCompiler.Expressions.cs",
        "src/RetroSharp.NES/NesRuntimeCompiler.Functions.cs",
        "src/RetroSharp.NES/NesRuntimeCompiler.Storage.cs",
    ];

    private static readonly (string TestName, string FocusedFile)[] FocusedLoweringTests =
    [
        ("Compiles_wait_frame_library_helper_over_nes_intrinsic_like_sdk_operation", "NesSdkFrameInputLoweringTests.cs"),
        ("Compiles_input_poll_library_helper_over_nes_intrinsic_like_sdk_operation", "NesSdkFrameInputLoweringTests.cs"),
        ("Nes_video_wait_vblank_waits_for_the_next_vblank_edge", "NesSdkFrameInputLoweringTests.cs"),
        ("Nes_video_wait_vblank_applies_pending_camera_scroll_before_sprite_dma", "NesSdkFrameInputLoweringTests.cs"),
        ("Button_enum_members_are_accepted_by_input_facade", "NesSdkFrameInputLoweringTests.cs"),
        ("Bare_button_identifiers_are_rejected_by_input_facade", "NesSdkFrameInputLoweringTests.cs"),
        ("Input_facade_predicates_lower_like_explicit_numeric_checks", "NesSdkFrameInputLoweringTests.cs"),
        ("Compiles_tick_input_helpers_to_nes_controller_state", "NesSdkFrameInputLoweringTests.cs"),
        ("Golden_sprite_draw_emission_is_pinned_nes", "NesSdkSpriteLoweringTests.cs"),
        ("Compiles_logical_sprite_draw_to_nes_oam_and_chr_data", "NesSdkSpriteLoweringTests.cs"),
        ("Golden_collision_aabb_emission_is_pinned_nes", "NesSdkCollisionLoweringTests.cs"),
        ("World_camera_hit_top_materializes_y_304_and_minus_one_through_a_x_on_nes", "NesSdkCollisionLoweringTests.cs"),
        ("Screen_camera_hit_top_keeps_byte_semantics_and_zero_extends_word_results_on_nes", "NesSdkCollisionLoweringTests.cs"),
        ("World_camera_hit_top_rejects_unsafe_byte_narrowing_on_tall_nes_world", "NesSdkCollisionLoweringTests.cs"),
        ("World_camera_hit_top_keeps_legacy_byte_destination_for_32_row_nes_world", "NesSdkCollisionLoweringTests.cs"),
        ("Collision_aabb_via_compile_time_operand_intrinsic_is_byte_identical_nes", "NesSdkCollisionLoweringTests.cs"),
        ("Screen_collision_aabb_via_compile_time_operand_intrinsic_is_byte_identical_nes", "NesSdkCollisionLoweringTests.cs"),
        ("Camera_relative_collision_uses_absolute_camera_tile_after_scroll_wrap", "NesSdkCollisionLoweringTests.cs"),
        ("Compiles_horizontal_camera_path_from_world_map_to_nes_scroll", "NesSdkCameraStreamingLoweringTests.cs"),
        ("Nes_lowers_explicit_stream_map_row_to_ppu_row_writes", "NesSdkCameraStreamingLoweringTests.cs"),
        ("Nes_accepts_world_rows_beyond_four_screen_surface_for_runtime_row_streaming", "NesSdkCameraStreamingLoweringTests.cs"),
        ("Four_screen_horizontal_streaming_prepares_the_next_offscreen_column", "NesSdkCameraStreamingLoweringTests.cs"),
        ("Nes_streams_runtime_rows_when_vertical_camera_crosses_tall_world_boundary", "NesSdkCameraStreamingLoweringTests.cs"),
        ("Nes_automatic_camera_row_streaming_uses_the_callers_vblank", "NesSdkCameraStreamingLoweringTests.cs"),
        ("Nes_runtime_row_streaming_writes_contiguous_ppudata_segments", "NesSdkCameraStreamingLoweringTests.cs"),
        ("Nes_runtime_row_streaming_is_split_across_vblanks", "NesSdkCameraStreamingLoweringTests.cs"),
        ("Compiles_four_screen_camera_path_from_world_map_to_nes_scroll", "NesSdkCameraStreamingLoweringTests.cs"),
        ("Nes_vertical_camera_large_delta_steps_one_pixel_instead_of_jumping", "NesSdkCameraStreamingLoweringTests.cs"),
        ("Accepts_vertical_camera_stream_area_taller_than_four_screen_buffer", "NesSdkCameraStreamingLoweringTests.cs"),
        ("Rejects_vertical_camera_stream_start_outside_four_screen_buffer", "NesSdkCameraStreamingLoweringTests.cs"),
    ];

    [Fact]
    public void Nes_sdk_lowerer_owns_emission_without_delegating_to_runtime_compiler()
    {
        ArchitectureSymbolAssertions.AssertSdkOperationOwnership(
            typeof(NesRomCompiler).Assembly,
            "RetroSharp.NES.NesSdkOperationLowerer",
            "RetroSharp.NES.NesRuntimeCompiler");
    }

    [Fact]
    public void Nes_cartridge_runtime_stream_and_prg_modules_are_physically_separate()
    {
        var root = RepositoryRoot();

        foreach (var path in PurposeNamedModules)
        {
            Assert.True(File.Exists(Path.Combine(root, path)), $"NES purpose-named module '{path}' must exist.");
        }

        foreach (var path in RuntimeFeatureModules)
        {
            var fullPath = Path.Combine(root, path);
            Assert.True(File.Exists(fullPath), $"NES runtime feature module '{path}' must exist.");
            Assert.Contains("partial class NesRuntimeCompiler", File.ReadAllText(fullPath), StringComparison.Ordinal);
        }

        var romBuilderSource = File.ReadAllText(Path.Combine(root, "src/RetroSharp.NES/NesRomBuilder.cs"));
        Assert.DoesNotContain("class NesRuntimeCompiler", romBuilderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("class PrgBuilder", romBuilderSource, StringComparison.Ordinal);
        Assert.DoesNotContain("record NesCartridgeLayout", romBuilderSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Sdk_lowering_regressions_live_in_the_focused_nes_suites()
    {
        var root = RepositoryRoot();
        var monolithicSource = File.ReadAllText(Path.Combine(root, "src/RetroSharp.NES.Tests/NesRomCompilerTests.cs"));

        foreach (var (testName, focusedFile) in FocusedLoweringTests)
        {
            var focusedSource = File.ReadAllText(Path.Combine(root, "src/RetroSharp.NES.Tests", focusedFile));
            Assert.DoesNotContain($"void {testName}(", monolithicSource, StringComparison.Ordinal);
            Assert.Contains($"void {testName}(", focusedSource, StringComparison.Ordinal);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RetroSharp.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
