using UnityEngine;

[CreateAssetMenu(fileName = "PerlinNoiseNode", menuName = "DynamicDungeon/Nodes/Generator/Perlin Noise")]
public sealed class PerlinNoiseNode : GenNodeBase
{
    public const string WorldInputPortName = "World In";
    public const string WorldOutputPortName = "World Out";

    [Header("Output")]
    [SerializeField, GraphLayerReference(PortDataKind.FloatLayer, false)] private GraphLayerReference _outputLayer;

    [Header("Noise")]
    [SerializeField, Min(0.01f)] private float _scale = 20f;
    [SerializeField, Range(1, 8)] private int _octaves = 4;
    [SerializeField, Range(0f, 1f)] private float _persistence = 0.5f;
    [SerializeField, Min(1f)] private float _lacunarity = 2f;
    [SerializeField] private Vector2 _offset = Vector2.zero;

    public override string NodeTitle => "Perlin Noise";
    public override string NodeCategory => "Generate";
    public override string NodeDescription =>
        "Reads the incoming world size and seed, then writes a Float Layer asset with Perlin noise values.";
    public override string PreferredPreviewPortName => WorldOutputPortName;

    protected override void DefinePorts()
    {
        AddInputPort(WorldInputPortName, PortDataKind.World, PortCapacity.Single, true, "World providing dimensions and seed.");
        AddOutputPort(WorldOutputPortName, PortDataKind.World, PortCapacity.Multi, "World with a new noise layer.");
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
        outputWorld.SetFloatLayer(BuildNoiseLayer(outputLayerId, outputWorld.Width, outputWorld.Height, outputWorld.Seed));

        return NodeExecutionResult.From(WorldOutputPortName, NodeValue.World(outputWorld));
    }

    private FloatLayer BuildNoiseLayer(string layerId, int width, int height, long seed)
    {
        System.Random rng = new System.Random((int)(seed ^ (seed >> 32)));
        float seedOffX = (float)(rng.NextDouble() * 99999.0);
        float seedOffY = (float)(rng.NextDouble() * 99999.0);

        float maxAmplitude = 0f;
        float amplitude = 1f;
        for (int octave = 0; octave < _octaves; octave++)
        {
            maxAmplitude += amplitude;
            amplitude *= _persistence;
        }

        FloatLayer layer = new FloatLayer(layerId, width, height);

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                float value = 0f;
                float frequency = 1f;
                amplitude = 1f;

                for (int octave = 0; octave < _octaves; octave++)
                {
                    float sampleX = (x / _scale * frequency) + _offset.x + seedOffX;
                    float sampleY = (y / _scale * frequency) + _offset.y + seedOffY;
                    value += Mathf.PerlinNoise(sampleX, sampleY) * amplitude;
                    amplitude *= _persistence;
                    frequency *= _lacunarity;
                }

                layer.SetValue(x, y, value / maxAmplitude);
            }
        }

        return layer;
    }
}
