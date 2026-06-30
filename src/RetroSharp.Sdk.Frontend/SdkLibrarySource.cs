namespace RetroSharp.Sdk;

using RetroSharp.Core.Sdk;

public static class SdkLibrarySource
{
    public static string ForTarget(TargetIntrinsicCatalog catalog)
    {
        var prefix = $"__retrosharp_{catalog.TargetId}";
        var library = $$"""
                 const {{MarkerName(catalog.TargetId)}} = 1;

                 [target("{{catalog.TargetId}}")]
                 [intrinsic("wait_frame")]
                 extern void {{prefix}}_wait_frame();

                 [target("{{catalog.TargetId}}")]
                 [intrinsic("poll_input")]
                 extern void {{prefix}}_poll_input();

                 [target("{{catalog.TargetId}}")]
                 [intrinsic("audio_update")]
                 extern void {{prefix}}_audio_update();

                 [target("{{catalog.TargetId}}")]
                 [intrinsic("camera_set_position")]
                 extern void {{prefix}}_camera_set_position(i16 x, i16 y);

                 [target("{{catalog.TargetId}}")]
                 [intrinsic("camera_apply")]
                 extern void {{prefix}}_camera_apply();

                 class video
                 {
                     static inline void WaitVBlank()
                     {
                         {{prefix}}_wait_frame();
                     }
                 }

                 class input
                 {
                     static inline void Poll()
                     {
                         {{prefix}}_poll_input();
                     }
                 }

                 class audio
                 {
                     static inline void Update()
                     {
                         {{prefix}}_audio_update();
                     }
                 }

                 class camera
                 {
                     static inline void SetPosition(i16 x, i16 y)
                     {
                         {{prefix}}_camera_set_position(x, y);
                     }

                     static inline void Apply()
                     {
                         {{prefix}}_camera_apply();
                     }
                 }

                 """;

        // Capability-gated library member: only the targets that catalog the
        // world_tile_flags_at intrinsic (Game Boy today; NES lacks the world
        // tile-flag collision query) expose world.TileFlagsAt(...).
        if (catalog.TryResolve("world_tile_flags_at", out _))
        {
            library += $$"""
                 [target("{{catalog.TargetId}}")]
                 [intrinsic("world_tile_flags_at")]
                 extern i16 {{prefix}}_world_tile_flags_at(i16 x, i16 y);

                 class world
                 {
                     static inline i16 TileFlagsAt(i16 x, i16 y)
                     {
                         return {{prefix}}_world_tile_flags_at(x, y);
                     }
                 }

                 """;
        }

        return library;
    }

    public static string Merge(TargetIntrinsicCatalog catalog, string source)
    {
        return source.Contains(MarkerName(catalog.TargetId), StringComparison.Ordinal)
            ? source
            : ForTarget(catalog) + source;
    }

    private static string MarkerName(string targetId)
    {
        return $"__retrosharp_sdk_library_{targetId}";
    }
}
