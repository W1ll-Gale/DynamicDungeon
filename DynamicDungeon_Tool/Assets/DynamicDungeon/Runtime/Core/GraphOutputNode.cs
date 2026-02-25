using UnityEngine;

[CreateAssetMenu(fileName = "GraphOutputNode", menuName = "DynamicDungeon/Nodes/Output/Graph Output")]
public sealed class GraphOutputNode : GenNodeBase
{
    public const string WorldInputPortName = "World In";

    public override string NodeTitle => "Graph Output";
    public override string NodeCategory => "Output";
    public override string NodeDescription => "Ends the graph and exposes the final world for runtime consumers.";
    public override string PreferredPreviewInputPortName => WorldInputPortName;

    protected override void DefinePorts()
    {
        AddInputPort(WorldInputPortName, PortDataKind.World, PortCapacity.Single, true, "World to expose as a graph output.");
    }

    public override NodeExecutionResult Execute(NodeExecutionContext context)
    {
        if (!context.TryGetWorld(WorldInputPortName, out GenMap world) || world == null)
        {
            Debug.LogWarning($"[{NodeTitle}] No input world connected.");
            return NodeExecutionResult.Empty;
        }

        return NodeExecutionResult.Empty;
    }
}
