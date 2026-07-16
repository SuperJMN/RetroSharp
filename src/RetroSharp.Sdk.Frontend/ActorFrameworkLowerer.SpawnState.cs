namespace RetroSharp.Sdk;

public static partial class ActorFrameworkLowerer
{
    private sealed class SpawnState
    {
        private readonly Dictionary<ActorSpawnLayerKey, ActorSpawnLayer> spawnLayers = [];
        private readonly List<ActorSpawnLayer> spawnLayersInOrder = [];

        public IReadOnlyList<ActorSpawnLayer> Layers => spawnLayersInOrder;
        public bool HasDirectives => spawnLayers.Count != 0;

        public void AddLayer(ActorSpawnLayer spawnLayer)
        {
            var key = ActorSpawnLayerKey.From(spawnLayer.MethodName, spawnLayer.PoolName, spawnLayer.MapPath, spawnLayer.LayerName, spawnLayer.WindowLeft, spawnLayer.WindowWidth);
            if (spawnLayers.ContainsKey(key))
            {
                return;
            }

            var runtimeName = $"__{spawnLayer.PoolName}_spawn_{spawnLayers.Count}";
            var runtimeLayer = spawnLayer with { RuntimeName = runtimeName };
            spawnLayers.Add(key, runtimeLayer);
            spawnLayersInOrder.Add(runtimeLayer);
        }

        public ActorSpawnLayer Layer(ActorSpawnLayerKey key) => spawnLayers[key];

        public IEnumerable<ActorSpawnLayer> LayersFor(string poolName)
        {
            return spawnLayersInOrder.Where(layer => layer.PoolName == poolName);
        }
    }
}
