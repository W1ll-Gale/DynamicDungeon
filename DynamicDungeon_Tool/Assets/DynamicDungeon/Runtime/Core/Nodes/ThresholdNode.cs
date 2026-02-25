using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class ThresholdBand
{
    [Range(0f, 1f)] public float MaxValue = 0.5f;
    public int TileId = 0;
}

[CreateAssetMenu(fileName = "ThresholdNode", menuName = "DynamicDungeon/Nodes/Modifier/Threshold")]
public sealed class ThresholdNode : GenNodeBase
{
    public const string WorldInputPortName = "World In";
    public const string WorldOutputPortName = "World Out";

    [Header("Output Layer")]
    [SerializeField, GraphLayerReference(PortDataKind.IntLayer, false)] private GraphLayerReference _outputLayer;

    [Header("Bands (lowest MaxValue wins)")]
    [SerializeField]
    [NonReorderable]
    private List<ThresholdBand> _bands = new List<ThresholdBand>
    {
        new ThresholdBand { MaxValue = 0.45f, TileId = 0 },
        new ThresholdBand { MaxValue = 1.00f, TileId = 1 },
    };

    public override string NodeTitle => "Threshold";
    public override string NodeCategory => "Convert";
    public override string NodeDescription => "Reads the latest Float Layer on the input world and writes an Int Layer using ordered threshold bands.";
    public override string PreferredPreviewPortName => WorldOutputPortName;

    protected override void DefinePorts()
    {
        AddInputPort(WorldInputPortName, PortDataKind.World, PortCapacity.Single, true, "World containing the source float layer.");
        AddOutputPort(WorldOutputPortName, PortDataKind.World, PortCapacity.Multi, "World with the generated tile layer.");
    }

    public override NodeExecutionResult Execute(NodeExecutionContext context)
    {
        if (!context.TryGetWorld(WorldInputPortName, out GenMap inputWorld) || inputWorld == null)
        {
            Debug.LogWarning($"[{NodeTitle}] No input world connected.");
            return NodeExecutionResult.Empty;
        }

        if (!GraphLayerUtility.TryGetLayerId(context.Execution.Graph, _outputLayer, PortDataKind.IntLayer, NodeTitle, "Output Layer", out string outputLayerId))
            return NodeExecutionResult.From(WorldOutputPortName, NodeValue.World(inputWorld));

        if (!inputWorld.TryGetLatestFloatLayer(out FloatLayer floatLayer, out _))
        {
            Debug.LogWarning($"[{NodeTitle}] No Float Layer was available on the input world.");
            return NodeExecutionResult.From(WorldOutputPortName, NodeValue.World(inputWorld));
        }

        if (_bands == null || _bands.Count == 0)
        {
            Debug.LogWarning($"[{NodeTitle}] No threshold bands configured.");
            return NodeExecutionResult.From(WorldOutputPortName, NodeValue.World(inputWorld));
        }

        List<ThresholdBand> sortedBands = new List<ThresholdBand>(_bands);
        sortedBands.Sort((a, b) => a.MaxValue.CompareTo(b.MaxValue));

        GenMap outputWorld = inputWorld.Clone();
        IntLayer tileLayer = new IntLayer(outputLayerId, outputWorld.Width, outputWorld.Height);

        for (int x = 0; x < outputWorld.Width; x++)
        {
            for (int y = 0; y < outputWorld.Height; y++)
            {
                float value = floatLayer.GetValue(x, y);
                tileLayer.SetValue(x, y, ResolveId(value, sortedBands));
            }
        }

        outputWorld.SetIntLayer(tileLayer);
        return NodeExecutionResult.From(WorldOutputPortName, NodeValue.World(outputWorld));
    }

    private static int ResolveId(float value, List<ThresholdBand> sortedBands)
    {
        for (int index = 0; index < sortedBands.Count; index++)
        {
            if (value <= sortedBands[index].MaxValue)
                return sortedBands[index].TileId;
        }

        return sortedBands[sortedBands.Count - 1].TileId;
    }
}
