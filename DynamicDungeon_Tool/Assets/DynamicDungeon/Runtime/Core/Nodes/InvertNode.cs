using UnityEngine;

[CreateAssetMenu(fileName = "InvertNode", menuName = "DynamicDungeon/Nodes/Modifier/Invert")]
public sealed class InvertNode : GenNodeBase
{
    public const string WorldInputPortName = "World In";
    public const string WorldOutputPortName = "World Out";

    public override string NodeTitle => "Invert";
    public override string NodeCategory => "Modify";
    public override string NodeDescription => "Inverts the latest Float Layer on the incoming world using 1 - value.";
    public override string PreferredPreviewPortName => WorldOutputPortName;

    protected override void DefinePorts()
    {
        AddInputPort(WorldInputPortName, PortDataKind.World, PortCapacity.Single, true, "World containing the target float layer.");
        AddOutputPort(WorldOutputPortName, PortDataKind.World, PortCapacity.Multi, "World with the inverted layer.");
    }

    public override NodeExecutionResult Execute(NodeExecutionContext context)
    {
        if (!context.TryGetWorld(WorldInputPortName, out GenMap inputWorld) || inputWorld == null)
        {
            Debug.LogWarning($"[{NodeTitle}] No input world connected.");
            return NodeExecutionResult.Empty;
        }

        if (!inputWorld.TryGetLatestFloatLayer(out FloatLayer _, out string layerId))
        {
            Debug.LogWarning($"[{NodeTitle}] No Float Layer was available on the input world.");
            return NodeExecutionResult.From(WorldOutputPortName, NodeValue.World(inputWorld));
        }

        GenMap outputWorld = inputWorld.Clone();
        FloatLayer layer = outputWorld.GetFloatLayer(layerId);

        for (int x = 0; x < outputWorld.Width; x++)
        {
            for (int y = 0; y < outputWorld.Height; y++)
                layer.SetValue(x, y, 1f - layer.GetValue(x, y));
        }

        return NodeExecutionResult.From(WorldOutputPortName, NodeValue.World(outputWorld));
    }
}
