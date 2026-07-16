namespace RetroSharp.Sdk;

public static partial class ActorFrameworkLowerer
{
    private sealed class EffectState
    {
        private readonly Dictionary<string, EffectPool> pools = new(StringComparer.Ordinal);
        private readonly List<EffectPool> poolsInOrder = [];
        private readonly Dictionary<string, EffectDef> defs = new(StringComparer.Ordinal);
        private readonly List<EffectDef> defsInOrder = [];

        public IReadOnlyList<EffectPool> Pools => poolsInOrder;
        public IReadOnlyList<EffectDef> Definitions => defsInOrder;
        public bool HasDirectives => pools.Count != 0 || defs.Count != 0;

        public void AddPool(EffectPool pool)
        {
            if (!pools.TryAdd(pool.Name, pool))
            {
                throw new InvalidOperationException($"Effects.Pool for '{pool.Name}' is already declared.");
            }

            poolsInOrder.Add(pool);
        }

        public EffectPool Pool(string name)
        {
            if (pools.TryGetValue(name, out var pool))
            {
                return pool;
            }

            throw new InvalidOperationException($"Unknown effect pool '{name}'. Declare Effects.Pool({name}, ...).");
        }

        public bool TryPool(string name, out EffectPool pool)
        {
            return pools.TryGetValue(name, out pool!);
        }

        public void AddDefinition(EffectDef def)
        {
            if (!defs.TryAdd(def.Name, def))
            {
                throw new InvalidOperationException($"Effects.Def for '{def.Name}' is already declared.");
            }

            defsInOrder.Add(def);
        }

        public EffectDef Definition(string name)
        {
            if (defs.TryGetValue(name, out var def))
            {
                return def;
            }

            throw new InvalidOperationException($"Unknown effect kind '{name}'. Declare Effects.Def({name}, ...).");
        }
    }
}
