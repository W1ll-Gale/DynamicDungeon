using UnityEngine;

[CreateAssetMenu(fileName = "CellularAutomataNode", menuName = "DynamicDungeon/Nodes/Generator/Cellular Automata")]
public sealed class CellularAutomataNode : GenNodeBase
{
    public const string WorldInputPortName = "World In";
    public const string WorldOutputPortName = "World Out";

    [Header("Output")]
    [SerializeField, GraphLayerReference(PortDataKind.FloatLayer, false)] private GraphLayerReference _outputLayer;

    [Header("Initial Fill")]
    [SerializeField, Range(0f, 1f)] private float _fillProbability = 0.52f;

    [Header("Smoothing")]
    [SerializeField, Range(1, 15)] private int _iterations = 5;
    [SerializeField, Range(0, 8)] private int _birthThreshold = 4;
    [SerializeField, Range(0, 8)] private int _surviveThreshold = 3;

    [Header("Border")]
    [SerializeField] private bool _solidBorder = true;

    public override string NodeTitle => "Cellular Automata";
    public override string NodeCategory => "Generate";
    public override string NodeDescription => "Writes a Float Layer asset using cellular automata, which is useful for caves and organic terrain masks.";
    public override string PreferredPreviewPortName => WorldOutputPortName;

    protected override void DefinePorts()
    {
        AddInputPort(WorldInputPortName, PortDataKind.World, PortCapacity.Single, true, "World providing dimensions and seed.");
        AddOutputPort(WorldOutputPortName, PortDataKind.World, PortCapacity.Multi, "World with a cave layer.");
    }

    public override NodeExecutionResult Execute(NodeExecutionContext context)
    {
        if (!context.TryGetWorld(WorldInputPortName, out GenMap inputWorld) || inputWorld == null)
        {
            Debug.LogWarning($"[{NodeTitle}] No input world connected.");
            return NodeExecutionResult.Empty;
        }

        if (!GraphLayerUtility.TryGetLayerId(context.Execution.Graph, _outputLayer, PortDataKind.FloatLayer, NodeTitle, "Output Layer", out string outputLayerId))
            return NodeExecutionResult.From(WorldOutputPortName, NodeValue.World(inputWorld));

        GenMap outputWorld = inputWorld.Clone();
        outputWorld.SetFloatLayer(RunCA(outputLayerId, outputWorld.Width, outputWorld.Height, outputWorld.Seed));

        return NodeExecutionResult.From(WorldOutputPortName, NodeValue.World(outputWorld));
    }

    private FloatLayer RunCA(string layerId, int width, int height, long seed)
    {
        System.Random rng = new System.Random((int)(seed ^ (seed >> 32)));
        bool[,] grid = InitialFill(width, height, rng);

        for (int iteration = 0; iteration < _iterations; iteration++)
            grid = Smooth(grid, width, height);

        return BoolGridToFloatLayer(layerId, grid, width, height);
    }

    private bool[,] InitialFill(int width, int height, System.Random rng)
    {
        bool[,] grid = new bool[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                bool isBorder = _solidBorder && (x == 0 || x == width - 1 || y == 0 || y == height - 1);
                grid[x, y] = !isBorder && (rng.NextDouble() < _fillProbability);
            }
        }

        return grid;
    }

    private bool[,] Smooth(bool[,] grid, int width, int height)
    {
        bool[,] next = new bool[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                bool isBorder = _solidBorder && (x == 0 || x == width - 1 || y == 0 || y == height - 1);
                if (isBorder)
                {
                    next[x, y] = false;
                    continue;
                }

                int wallCount = CountWallNeighbours(grid, x, y, width, height);
                next[x, y] = grid[x, y] ? wallCount < _birthThreshold : wallCount < _surviveThreshold;
            }
        }

        return next;
    }

    private int CountWallNeighbours(bool[,] grid, int cx, int cy, int width, int height)
    {
        int count = 0;
        for (int nx = cx - 1; nx <= cx + 1; nx++)
        {
            for (int ny = cy - 1; ny <= cy + 1; ny++)
            {
                if (nx == cx && ny == cy) continue;
                if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                {
                    count++;
                    continue;
                }

                if (!grid[nx, ny]) count++;
            }
        }

        return count;
    }

    private FloatLayer BoolGridToFloatLayer(string layerId, bool[,] grid, int width, int height)
    {
        FloatLayer layer = new FloatLayer(layerId, width, height);
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
                layer.SetValue(x, y, grid[x, y] ? 1f : 0f);
        }
        return layer;
    }
}
