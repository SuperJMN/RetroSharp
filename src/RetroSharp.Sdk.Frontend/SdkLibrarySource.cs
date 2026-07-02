namespace RetroSharp.Sdk;

using Antlr4.Runtime;
using RetroSharp.Core.Sdk;
using RetroSharp.Parser;

public static class SdkLibrarySource
{
    public static string ForTarget(TargetIntrinsicCatalog catalog)
    {
        var prefix = $"__retrosharp_{catalog.TargetId}";
        var cameraAabbExterns = "";
        var cameraAabbMethods = "";
        if (catalog.TryResolve("camera_aabb_tiles", out var cameraAabbTiles)
            && cameraAabbTiles.Operation == TargetIntrinsicOperation.CameraAabbTiles)
        {
            cameraAabbExterns += $$"""
                 [target("{{catalog.TargetId}}")]
                 [intrinsic("camera_aabb_tiles")]
                 extern i16 {{prefix}}_camera_aabb_tiles(i16 worldId, i16 screenX, i16 worldY, i16 width, i16 height, i16 flags);

                 """;
            cameraAabbMethods += $$"""

                     static inline i16 AabbTiles(i16 screenX, i16 worldY, i16 width, i16 height, i16 flags)
                     {
                         return {{prefix}}_camera_aabb_tiles("default", screenX, worldY, width, height, flags);
                     }
                 """;
        }

        if (catalog.TryResolve("camera_aabb_hit_top", out var cameraAabbHitTop)
            && cameraAabbHitTop.Operation == TargetIntrinsicOperation.CameraAabbHitTop)
        {
            cameraAabbExterns += $$"""
                 [target("{{catalog.TargetId}}")]
                 [intrinsic("camera_aabb_hit_top")]
                 extern i16 {{prefix}}_camera_aabb_hit_top(i16 worldId, i16 screenX, i16 worldY, i16 width, i16 height, i16 flags);

                 """;
            cameraAabbMethods += $$"""

                     static inline i16 AabbHitTop(i16 screenX, i16 worldY, i16 width, i16 height, i16 flags)
                     {
                         return {{prefix}}_camera_aabb_hit_top("default", screenX, worldY, width, height, flags);
                     }
                 """;
        }

        if (catalog.TryResolve("camera_screen_aabb_tiles", out var cameraScreenAabbTiles)
            && cameraScreenAabbTiles.Operation == TargetIntrinsicOperation.CameraScreenAabbTiles)
        {
            cameraAabbExterns += $$"""
                 [target("{{catalog.TargetId}}")]
                 [intrinsic("camera_screen_aabb_tiles")]
                 extern i16 {{prefix}}_camera_screen_aabb_tiles(i16 worldId, i16 screenX, i16 screenY, i16 width, i16 height, i16 flags);

                 """;
            cameraAabbMethods += $$"""

                     static inline i16 ScreenAabbTiles(i16 screenX, i16 screenY, i16 width, i16 height, i16 flags)
                     {
                         return {{prefix}}_camera_screen_aabb_tiles("default", screenX, screenY, width, height, flags);
                     }
                 """;
        }

        if (catalog.TryResolve("camera_screen_aabb_hit_top", out var cameraScreenAabbHitTop)
            && cameraScreenAabbHitTop.Operation == TargetIntrinsicOperation.CameraScreenAabbHitTop)
        {
            cameraAabbExterns += $$"""
                 [target("{{catalog.TargetId}}")]
                 [intrinsic("camera_screen_aabb_hit_top")]
                 extern i16 {{prefix}}_camera_screen_aabb_hit_top(i16 worldId, i16 screenX, i16 screenY, i16 width, i16 height, i16 flags);

                 """;
            cameraAabbMethods += $$"""

                     static inline i16 ScreenAabbHitTop(i16 screenX, i16 screenY, i16 width, i16 height, i16 flags)
                     {
                         return {{prefix}}_camera_screen_aabb_hit_top("default", screenX, screenY, width, height, flags);
                     }
                 """;
        }

        var library = $$"""
                 const {{MarkerName(catalog.TargetId)}} = 1;

                 enum Button { A, B, Select, Start, Right, Left, Up, Down }

                 [target("{{catalog.TargetId}}")]
                 [intrinsic("wait_frame")]
                 extern void {{prefix}}_wait_frame();

                 [target("{{catalog.TargetId}}")]
                 [intrinsic("poll_input")]
                 extern void {{prefix}}_poll_input();

                 [target("{{catalog.TargetId}}")]
                 [intrinsic("audio_init")]
                 extern void {{prefix}}_audio_init();

                 [target("{{catalog.TargetId}}")]
                 [intrinsic("audio_update")]
                 extern void {{prefix}}_audio_update();

                 [target("{{catalog.TargetId}}")]
                 [intrinsic("camera_set_position")]
                 extern void {{prefix}}_camera_set_position(i16 x, i16 y);

                 [target("{{catalog.TargetId}}")]
                 [intrinsic("camera_apply")]
                 extern void {{prefix}}_camera_apply();

                 {{cameraAabbExterns}}
                 class Video
                 {
                     static inline void WaitVBlank()
                     {
                         {{prefix}}_wait_frame();
                     }
                 }

                 class Input
                 {
                     static inline void Poll()
                     {
                         {{prefix}}_poll_input();
                     }
                 }

                 class Audio
                 {
                     static inline void Init()
                     {
                         {{prefix}}_audio_init();
                     }

                     static inline void Update()
                     {
                         {{prefix}}_audio_update();
                     }
                 }

                 class Camera
                 {
                     static inline void SetPosition(i16 x, i16 y)
                     {
                         {{prefix}}_camera_set_position(x, y);
                     }

                     static inline void Apply()
                     {
                         {{prefix}}_camera_apply();
                     }
                 {{cameraAabbMethods}}
                 }

                 """;

        // Capability-gated library member: only the targets that catalog the
        // world_tile_flags_at intrinsic (Game Boy today; NES lacks the world
        // tile-flag collision query) expose World.TileFlagsAt(...).
        if (catalog.TryResolve("world_tile_flags_at", out _))
        {
            library += $$"""
                 [target("{{catalog.TargetId}}")]
                 [intrinsic("world_tile_flags_at")]
                 extern i16 {{prefix}}_world_tile_flags_at(i16 x, i16 y);

                 class World
                 {
                     static inline i16 TileFlagsAt(i16 x, i16 y)
                     {
                         return {{prefix}}_world_tile_flags_at(x, y);
                     }
                 }

                 """;
        }

        if (catalog.TryResolve("sprite_draw", out var spriteDraw)
            && spriteDraw.Operation == TargetIntrinsicOperation.DrawLogicalSprite)
        {
            library += $$"""
                 [target("{{catalog.TargetId}}")]
                 [intrinsic("sprite_draw")]
                 extern void {{prefix}}_sprite_draw(i16 spriteId, i16 x, i16 y, i16 frame, bool flipX, i16 paletteSlot);

                 class Sprite
                 {
                     static inline void Draw(i16 spriteId, i16 x, i16 y, i16 frame, bool flipX = false, i16 paletteSlot = 0)
                     {
                         {{prefix}}_sprite_draw(spriteId, x, y, frame, flipX, paletteSlot);
                     }
                 }

                 """;
        }

        // Capability-gated library member: targets that catalog music_play/music_stop
        // (Game Boy and NES today) expose Music.Play(...) / Music.Stop() over their BGM
        // target intrinsics. Music.Asset(...) is not a class member and still lowers
        // through the SDK module.
        if (catalog.TryResolve("music_play", out var musicPlay)
            && musicPlay.Operation == TargetIntrinsicOperation.PlayMusic
            && catalog.TryResolve("music_stop", out var musicStop)
            && musicStop.Operation == TargetIntrinsicOperation.StopMusic)
        {
            library += $$"""
                 [target("{{catalog.TargetId}}")]
                 [intrinsic("music_play")]
                 extern void {{prefix}}_music_play(i16 theme);

                 [target("{{catalog.TargetId}}")]
                 [intrinsic("music_stop")]
                 extern void {{prefix}}_music_stop();

                 class Music
                 {
                     static inline void Play(i16 theme)
                     {
                         {{prefix}}_music_play(theme);
                     }

                     static inline void Stop()
                     {
                         {{prefix}}_music_stop();
                     }
                 }

                 """;
        }

        return library;
    }

    public static string Merge(
        TargetIntrinsicCatalog catalog,
        string source,
        SdkLibraryImportMode importMode = SdkLibraryImportMode.LegacyAutoImport,
        SdkLibraryRegistry? registry = null,
        IReadOnlyList<string>? libraryImportPaths = null)
    {
        if (source.Contains(MarkerName(catalog.TargetId), StringComparison.Ordinal))
        {
            return source;
        }

        registry ??= SdkLibraryRegistry.Default;
        var (imports, importPaths, body) = SplitLeadingImports(source);
        var libraries = importPaths
            .Concat(libraryImportPaths ?? [])
            .Select(importPath => ResolveLibrary(registry, importPath))
            .DistinctBy(library => library.ImportPath)
            .ToList();

        if (importMode == SdkLibraryImportMode.LegacyAutoImport
            && !libraries.Any(library => library.ImportPath == SdkImportResolver.Portable2D)
            && registry.TryResolve(SdkImportResolver.Portable2D, out var portable2D))
        {
            libraries.Insert(0, portable2D!);
        }

        var librarySourceSets = libraries
            .Select(library => library.SourceSetForTarget(catalog))
            .ToList();
        var plainLibrarySource = string.Concat(librarySourceSets
            .Where(sourceSet => sourceSet.PhysicalNamespace is null)
            .Select(sourceSet => sourceSet.Source));
        var physicalLibrarySourceGroups = librarySourceSets
            .Select(sourceSet => sourceSet.PhysicalNamespace)
            .OfType<PhysicalNamespaceSourceGroup>()
            .ToArray();
        if (physicalLibrarySourceGroups.Length == 0)
        {
            return imports + plainLibrarySource + body;
        }

        var rewrittenPhysicalSource = PhysicalNamespaceSourceComposer.Compose(
            physicalLibrarySourceGroups,
            [new PhysicalNamespaceSourceFile("__retrosharp_importer.rs", body)]);
        return imports + plainLibrarySource + rewrittenPhysicalSource;
    }

    private static SdkLibrary ResolveLibrary(SdkLibraryRegistry registry, string importPath)
    {
        if (!registry.TryResolve(importPath, out var library))
        {
            throw new InvalidOperationException($"Unknown import '{importPath}'.");
        }

        return library!;
    }

    private static (string Imports, IReadOnlyList<string> ImportPaths, string Body) SplitLeadingImports(string source)
    {
        var lexer = new RetroSharpLexer(CharStreams.fromString(source));
        var tokens = lexer.GetAllTokens();
        var tokenIndex = 0;
        var importEnd = 0;
        var importPaths = new List<string>();

        while (tokenIndex < tokens.Count && tokens[tokenIndex].Text == "import")
        {
            tokenIndex++;
            var path = "";
            while (tokenIndex < tokens.Count && tokens[tokenIndex].Text != ";")
            {
                path += tokens[tokenIndex].Text;
                tokenIndex++;
            }

            if (tokenIndex >= tokens.Count)
            {
                return (source, [], string.Empty);
            }

            importPaths.Add(path);
            importEnd = tokens[tokenIndex].StopIndex + 1;
            tokenIndex++;
        }

        return (source[..importEnd], importPaths, source[importEnd..]);
    }

    private static string MarkerName(string targetId)
    {
        return $"__retrosharp_sdk_library_{targetId}";
    }
}
