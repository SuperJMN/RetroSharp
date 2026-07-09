[intrinsic("camera_init")]
extern void portable2d_camera_init(i16 mapWidth, i16 streamY, i16 streamHeight);

[intrinsic("camera_set_position")]
extern void portable2d_camera_set_position(i16 x, i16 y);

[intrinsic("camera_apply")]
extern void portable2d_camera_apply();

[intrinsic("camera_vertical_scroll_max")]
extern i16 portable2d_camera_vertical_scroll_max();

[intrinsic("camera_aabb_tiles")]
extern i16 portable2d_camera_aabb_tiles(i16 worldId, i16 screenX, i16 worldY, i16 width, i16 height, i16 flags);

[intrinsic("camera_aabb_hit_top")]
extern i16 portable2d_camera_aabb_hit_top(i16 worldId, i16 screenX, i16 worldY, i16 width, i16 height, i16 flags);

[intrinsic("camera_screen_aabb_tiles")]
extern i16 portable2d_camera_screen_aabb_tiles(i16 worldId, i16 screenX, i16 screenY, i16 width, i16 height, i16 flags);

[intrinsic("camera_screen_aabb_hit_top")]
extern i16 portable2d_camera_screen_aabb_hit_top(i16 worldId, i16 screenX, i16 screenY, i16 width, i16 height, i16 flags);

class Camera
{
    static inline void Init(i16 mapWidth, i16 streamY, i16 streamHeight)
    {
        portable2d_camera_init(mapWidth, streamY, streamHeight);
    }

    static inline void SetPosition(i16 x, i16 y)
    {
        portable2d_camera_set_position(x, y);
    }

    static inline void Apply()
    {
        portable2d_camera_apply();
    }

    // Maximum camera Y (in pixels) the world can scroll to on this target without exposing area
    // below the map: worldHeight - screenHeight, clamped to >= 0. Folds to a per-target constant
    // (e.g. 0 when the world exactly fills the screen), so callers can clamp their own camera Y and
    // keep sprite/background alignment consistent.
    static inline i16 VerticalScrollMax()
    {
        return portable2d_camera_vertical_scroll_max();
    }

    static inline i16 AabbTiles(i16 screenX, i16 worldY, i16 width, i16 height, i16 flags)
    {
        return portable2d_camera_aabb_tiles("default", screenX, worldY, width, height, flags);
    }

    static inline i16 AabbHitTop(i16 screenX, i16 worldY, i16 width, i16 height, i16 flags)
    {
        return portable2d_camera_aabb_hit_top("default", screenX, worldY, width, height, flags);
    }

    static inline i16 ScreenAabbTiles(i16 screenX, i16 screenY, i16 width, i16 height, i16 flags)
    {
        return portable2d_camera_screen_aabb_tiles("default", screenX, screenY, width, height, flags);
    }

    static inline i16 ScreenAabbHitTop(i16 screenX, i16 screenY, i16 width, i16 height, i16 flags)
    {
        return portable2d_camera_screen_aabb_hit_top("default", screenX, screenY, width, height, flags);
    }
}
