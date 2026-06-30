namespace RetroSharp.Sdk;

using RetroSharp.Core.Sdk;

public static class SdkLibrarySource
{
    public static string ForTarget(TargetIntrinsicCatalog catalog)
    {
        var prefix = $"__retrosharp_{catalog.TargetId}";
        return $$"""
                 const {{MarkerName(catalog.TargetId)}} = 1;

                 [target("{{catalog.TargetId}}")]
                 [intrinsic("wait_frame")]
                 extern void {{prefix}}_wait_frame();

                 [target("{{catalog.TargetId}}")]
                 [intrinsic("poll_input")]
                 extern void {{prefix}}_poll_input();

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

                 """;
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
