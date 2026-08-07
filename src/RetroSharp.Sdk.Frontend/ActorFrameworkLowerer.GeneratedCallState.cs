namespace RetroSharp.Sdk;

public static partial class ActorFrameworkLowerer
{
    private sealed class GeneratedCallState
    {
        private readonly Dictionary<string, int> activationCallCounts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> projectileRequestCallCounts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> effectRequestCallCounts = new(StringComparer.Ordinal);
        private readonly List<ActorSpawnActivation> activations = [];

        public IReadOnlyList<ActorSpawnActivation> Activations => activations;

        public string NextActivationPrefix(ActorSpawnLayer spawnLayer, int screenWidth, int poolCapacity)
        {
            activationCallCounts.TryGetValue(spawnLayer.RuntimeName, out var count);
            activationCallCounts[spawnLayer.RuntimeName] = count + 1;
            var prefix = $"{spawnLayer.RuntimeName}_call{count}";
            activations.Add(new ActorSpawnActivation(spawnLayer, prefix, screenWidth, poolCapacity));
            return prefix;
        }

        public string NextProjectileRequestPrefix(ProjectilePool pool)
        {
            projectileRequestCallCounts.TryGetValue(pool.Name, out var count);
            projectileRequestCallCounts[pool.Name] = count + 1;
            return $"__{pool.Name}_request_call{count}";
        }

        public string NextEffectRequestPrefix(EffectPool pool, string purpose)
        {
            var key = $"{pool.Name}:{purpose}";
            effectRequestCallCounts.TryGetValue(key, out var count);
            effectRequestCallCounts[key] = count + 1;
            return $"__{pool.Name}_{purpose}_call{count}";
        }
    }

    private sealed record ActorSpawnActivation(
        ActorSpawnLayer Layer,
        string Prefix,
        int ScreenWidth,
        int PoolCapacity);
}
