[intrinsic("camera_set_position")]
extern void portable2d_camera_set_position(i16 x, i16 y);

[intrinsic("camera_apply")]
extern void portable2d_camera_apply();

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
    static inline void SetPosition(i16 x, i16 y)
    {
        portable2d_camera_set_position(x, y);
    }

    static inline void Apply()
    {
        portable2d_camera_apply();
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
