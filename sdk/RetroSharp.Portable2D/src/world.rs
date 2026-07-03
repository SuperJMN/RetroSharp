[target("gb")]
[intrinsic("world_tile_flags_at")]
extern i16 portable2d_world_tile_flags_at(i16 x, i16 y);

class World
{
    static inline [resource("world_column")] void Column()
    {
    }

    static inline [resource("world_flags")] void Flags()
    {
    }

    static inline [resource("world_map")] void Map(i16 width, i16 streamY, i16 height)
    {
    }

    static inline [resource("world_load")] void Load(i16 path)
    {
    }

    static inline [target("gb")] i16 TileFlagsAt(i16 x, i16 y)
    {
        return portable2d_world_tile_flags_at(x, y);
    }
}
