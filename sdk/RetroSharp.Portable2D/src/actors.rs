class Actors
{
    static inline [sdk_role("actor_pool")] void Pool(i16 name, i16 capacity)
    {
    }

    static inline [sdk_role("actor_spawn_layer")] void SpawnLayer(i16 pool, i16 mapPath, i16 layerName)
    {
    }

    static inline [sdk_role("actor_spawn_window")] void SpawnWindow(i16 pool, i16 mapPath, i16 layerName, i16 left, i16 width)
    {
    }
}

class Enemies
{
    static inline [sdk_role("actor_enemy_def")] void Def(i16 name)
    {
    }

    static inline [sdk_role("actor_enemy_behavior")] u8 Behavior(u8 kind) => 0;

    static inline [sdk_role("actor_enemy_speed")] u8 Speed(u8 kind) => 0;

    static inline [sdk_role("actor_enemy_hp")] u8 Hp(u8 kind) => 0;

    static inline [sdk_role("actor_enemy_cooldown")] u8 Cooldown(u8 kind) => 0;

    static inline [sdk_role("actor_enemy_contact_damage")] u8 ContactDamage(u8 kind) => 0;

    static inline [sdk_role("actor_enemy_hitbox_width")] u8 HitboxWidth(u8 kind) => 0;

    static inline [sdk_role("actor_enemy_hitbox_height")] u8 HitboxHeight(u8 kind) => 0;
}
