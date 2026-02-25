using UnityEngine;
using UnityEngine.Tilemaps;

[AddComponentMenu("DynamicDungeon/Dungeon Generator")]
public sealed class DungeonGeneratorComponent : MonoBehaviour
{
    [Header("Graph")]
    [SerializeField] private GenGraph _graph;

    [Header("Tilemap Target")]
    [SerializeField] private Tilemap _tilemap;
    [SerializeField, GraphLayerReference(PortDataKind.IntLayer, false)] private GraphLayerReference _tileLayer;
    [SerializeField] private Vector3Int _tilemapOffset = Vector3Int.zero;

    [Header("Execution Overrides")]
    [SerializeField] private bool _overrideWorldSize = false;
    [SerializeField, Min(1)] private int _worldWidth = 128;
    [SerializeField, Min(1)] private int _worldHeight = 128;
    [SerializeField] private bool _overrideSeed = false;
    [SerializeField] private long _seedOverride = 0L;

    [Header("Settings")]
    [SerializeField] private bool _generateOnStart = true;
    [SerializeField] private bool _clearTilemapBeforeGenerate = true;

    private GenMap _lastGeneratedMap;

    public GenMap LastGeneratedMap => _lastGeneratedMap;

    private void Start()
    {
        if (_generateOnStart) Generate();
    }

    [ContextMenu("Generate")]
    public void Generate()
    {
        if (!ValidateRequirements()) return;

        GraphExecutionContext executionContext = GraphExecutionContext.FromGraph(
            _graph,
            _overrideWorldSize ? _worldWidth : (int?)null,
            _overrideWorldSize ? _worldHeight : (int?)null,
            _overrideSeed ? _seedOverride : (long?)null);

        GraphProcessor processor = new GraphProcessor(_graph);
        GraphProcessorResult result = processor.Execute(executionContext);

        if (!result.IsSuccess)
        {
            Debug.LogError($"[DungeonGenerator] Graph execution failed: {result.ErrorMessage}", this);
            return;
        }

        if (!result.TryGetPrimaryGraphOutput(out NodeValue outputValue) ||
            !outputValue.TryGetWorld(out GenMap world) ||
            world == null)
        {
            Debug.LogError(
                "[DungeonGenerator] Graph did not produce a valid final world output. " +
                "Make sure the graph has exactly one connected Graph Output node.",
                this);
            return;
        }

        if (!GraphLayerUtility.TryGetLayerId(_graph, _tileLayer, PortDataKind.IntLayer, nameof(DungeonGeneratorComponent), "Tile Layer", out string tileLayerId))
            return;

        if (!world.HasIntLayer(tileLayerId))
        {
            Debug.LogError(
                $"[DungeonGenerator] Final world does not contain the Int Layer '{GraphLayerUtility.GetDisplayName(_graph, _tileLayer)}'.",
                this);
            return;
        }

        _lastGeneratedMap = world;

        if (_clearTilemapBeforeGenerate)
            _tilemap.ClearAllTiles();

        ApplyIntLayerToTilemap(world.GetIntLayer(tileLayerId));

        Debug.Log(
            $"[DungeonGenerator] Generated world ({world.Width}x{world.Height}) Seed={world.Seed}",
            this);
    }

    private bool ValidateRequirements()
    {
        bool valid = true;

        if (_graph == null) { Debug.LogError("[DungeonGenerator] No GenGraph assigned.", this); valid = false; }
        if (_tilemap == null) { Debug.LogError("[DungeonGenerator] No Tilemap assigned.", this); valid = false; }
        if (_graph != null && _graph.TileRuleset == null) { Debug.LogError("[DungeonGenerator] Graph has no TileRulesetAsset assigned.", this); valid = false; }
        if (!_tileLayer.IsAssigned) { Debug.LogError("[DungeonGenerator] No tile Int Layer is selected.", this); valid = false; }

        return valid;
    }

    private void ApplyIntLayerToTilemap(IntLayer tileLayer)
    {
        int totalCells = tileLayer.Width * tileLayer.Height;
        Vector3Int[] positions = new Vector3Int[totalCells];
        TileBase[] tiles = new TileBase[totalCells];

        int index = 0;
        for (int x = 0; x < tileLayer.Width; x++)
        {
            for (int y = 0; y < tileLayer.Height; y++)
            {
                positions[index] = new Vector3Int(
                    x + _tilemapOffset.x,
                    y + _tilemapOffset.y,
                    _tilemapOffset.z);

                tiles[index] = _graph.TileRuleset.GetTile(tileLayer.GetValue(x, y));
                index++;
            }
        }

        _tilemap.SetTiles(positions, tiles);
        _tilemap.RefreshAllTiles();
    }
}
