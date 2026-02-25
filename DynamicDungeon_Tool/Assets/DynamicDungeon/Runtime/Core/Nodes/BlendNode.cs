using UnityEngine;

public enum BlendMode
{
    Lerp,
    Add,
    Multiply,
    Subtract,
    Min,
    Max,
}

[CreateAssetMenu(fileName = "BlendNode", menuName = "DynamicDungeon/Nodes/Modifier/Blend")]
public sealed class BlendNode : GenNodeBase
{
    public const string WorldAPortName = "World A";
    public const string WorldBPortName = "World B";
    public const string WorldOutputPortName = "World Out";

    [Header("Output Layer")]
    [SerializeField, GraphLayerReference(PortDataKind.FloatLayer, false)] private GraphLayerReference _outputLayer;

    [Header("Blend Settings")]
    [SerializeField] private BlendMode _blendMode = BlendMode.Lerp;
    [SerializeField, Range(0f, 1f)] private float _blendWeight = 0.5f;

    public override string NodeTitle => "Blend";
    public override string NodeCategory => "Combine";
    public override string NodeDescription => "Blends the latest Float Layer from each input world and writes the result to a chosen Float Layer.";
    public override string PreferredPreviewPortName => WorldOutputPortName;

    protected override void DefinePorts()
    {
        AddInputPort(WorldAPortName, PortDataKind.World, PortCapacity.Single, true, "Primary world.");
        AddInputPort(WorldBPortName, PortDataKind.World, PortCapacity.Single, true, "Secondary world.");
        AddOutputPort(WorldOutputPortName, PortDataKind.World, PortCapacity.Multi, "Blended output world.");
    }

    public override NodeExecutionResult Execute(NodeExecutionContext context)
    {
        if (!context.TryGetWorld(WorldAPortName, out GenMap worldA) || worldA == null)
        {
            Debug.LogWarning($"[{NodeTitle}] World A not connected.");
            return NodeExecutionResult.Empty;
        }

        if (!context.TryGetWorld(WorldBPortName, out GenMap worldB) || worldB == null)
        {
            Debug.LogWarning($"[{NodeTitle}] World B not connected.");
            return NodeExecutionResult.Empty;
        }

        if (!worldA.TryGetLatestFloatLayer(out FloatLayer layerA, out _))
        {
            Debug.LogWarning($"[{NodeTitle}] World A does not contain a Float Layer to blend.");
            return NodeExecutionResult.From(WorldOutputPortName, NodeValue.World(worldA));
        }

        if (!worldB.TryGetLatestFloatLayer(out FloatLayer layerB, out _))
        {
            Debug.LogWarning($"[{NodeTitle}] World B does not contain a Float Layer to blend.");
            return NodeExecutionResult.From(WorldOutputPortName, NodeValue.World(worldA));
        }

        if (!GraphLayerUtility.TryGetLayerId(context.Execution.Graph, _outputLayer, PortDataKind.FloatLayer, NodeTitle, "Output Layer", out string outputLayerId))
            return NodeExecutionResult.From(WorldOutputPortName, NodeValue.World(worldA));

        if (worldA.Width != worldB.Width || worldA.Height != worldB.Height)
        {
            Debug.LogError($"[{NodeTitle}] Input world dimensions do not match.");
            return NodeExecutionResult.From(WorldOutputPortName, NodeValue.World(worldA));
        }

        GenMap outputWorld = worldA.Clone();
        FloatLayer blended = new FloatLayer(outputLayerId, outputWorld.Width, outputWorld.Height);

        for (int x = 0; x < outputWorld.Width; x++)
        {
            for (int y = 0; y < outputWorld.Height; y++)
            {
                float a = layerA.GetValue(x, y);
                float b = layerB.GetValue(x, y);
                blended.SetValue(x, y, Blend(a, b));
            }
        }

        outputWorld.SetFloatLayer(blended);
        return NodeExecutionResult.From(WorldOutputPortName, NodeValue.World(outputWorld));
    }

    private float Blend(float a, float b)
    {
        switch (_blendMode)
        {
            case BlendMode.Lerp: return Mathf.Lerp(a, b, _blendWeight);
            case BlendMode.Add: return Mathf.Clamp01(a + b);
            case BlendMode.Multiply: return a * b;
            case BlendMode.Subtract: return Mathf.Clamp01(a - b);
            case BlendMode.Min: return Mathf.Min(a, b);
            case BlendMode.Max: return Mathf.Max(a, b);
            default: return a;
        }
    }
}
