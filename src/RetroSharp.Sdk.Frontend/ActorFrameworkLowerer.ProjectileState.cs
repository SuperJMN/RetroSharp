namespace RetroSharp.Sdk;

public static partial class ActorFrameworkLowerer
{
    private sealed class ProjectileState
    {
        private readonly Dictionary<string, ProjectilePool> pools = new(StringComparer.Ordinal);
        private readonly List<ProjectilePool> poolsInOrder = [];
        private readonly Dictionary<string, ProjectileDef> defs = new(StringComparer.Ordinal);
        private readonly List<ProjectileDef> defsInOrder = [];

        public IReadOnlyList<ProjectilePool> Pools => poolsInOrder;
        public IReadOnlyList<ProjectileDef> Definitions => defsInOrder;
        public bool HasDirectives => pools.Count != 0 || defs.Count != 0;

        public void AddPool(ProjectilePool pool)
        {
            if (!pools.TryAdd(pool.Name, pool))
            {
                throw new InvalidOperationException($"Projectiles.Pool for '{pool.Name}' is already declared.");
            }

            poolsInOrder.Add(pool);
        }

        public ProjectilePool Pool(string name) => pools[name];

        public bool TryPool(string name, out ProjectilePool pool)
        {
            return pools.TryGetValue(name, out pool!);
        }

        public void AddDefinition(ProjectileDef def)
        {
            if (!defs.TryAdd(def.Name, def))
            {
                throw new InvalidOperationException($"Projectiles.Def for '{def.Name}' is already declared.");
            }

            defsInOrder.Add(def);
        }

        public ProjectileDef Definition(string name)
        {
            if (defs.TryGetValue(name, out var def))
            {
                return def;
            }

            throw new InvalidOperationException($"Unknown projectile kind '{name}'. Declare Projectiles.Def({name}, ...).");
        }
    }
}
