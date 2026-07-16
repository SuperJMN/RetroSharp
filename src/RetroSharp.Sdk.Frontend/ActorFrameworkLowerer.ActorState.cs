namespace RetroSharp.Sdk;

public static partial class ActorFrameworkLowerer
{
    private sealed class ActorState
    {
        private readonly Dictionary<string, ActorPool> pools = new(StringComparer.Ordinal);
        private readonly List<ActorPool> poolsInOrder = [];
        private readonly Dictionary<string, EnemyDef> enemyDefs = new(StringComparer.Ordinal);
        private readonly List<EnemyDef> enemyDefsInOrder = [];
        private readonly HashSet<ActorFrameworkRole> usedEnemyLookupMethods = [];

        public IReadOnlyList<ActorPool> Pools => poolsInOrder;
        public IReadOnlyList<EnemyDef> EnemyDefs => enemyDefsInOrder;
        public IReadOnlySet<ActorFrameworkRole> UsedEnemyLookupMethods => usedEnemyLookupMethods;
        public bool HasDirectives => pools.Count != 0 || enemyDefs.Count != 0;

        public void AddPool(ActorPool pool)
        {
            if (!pools.TryAdd(pool.Name, pool))
            {
                throw new InvalidOperationException($"Actors.Pool for '{pool.Name}' is already declared.");
            }

            poolsInOrder.Add(pool);
        }

        public ActorPool Pool(string name) => pools[name];

        public bool TryPool(string name, out ActorPool pool)
        {
            return pools.TryGetValue(name, out pool!);
        }

        public void AddEnemyDef(EnemyDef def)
        {
            if (!enemyDefs.TryAdd(def.Name, def))
            {
                throw new InvalidOperationException($"Enemies.Def for '{def.Name}' is already declared.");
            }

            enemyDefsInOrder.Add(def);
        }

        public void RecordEnemyLookupMethod(ActorFrameworkRole role)
        {
            usedEnemyLookupMethods.Add(role);
        }
    }
}
