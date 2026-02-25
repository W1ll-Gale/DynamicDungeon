using UnityEditor;
using UnityEngine;

public static class GraphAuthoringUtility
{
    public static void EnsureBootstrapGraph(GenGraph graph)
    {
        if (graph == null)
            return;

        bool dirty = false;

        if (graph.Layers.Count == 0)
        {
            graph.EnsureLayer("Height", PortDataKind.FloatLayer);
            graph.EnsureLayer("Tiles", PortDataKind.IntLayer);
            dirty = true;
        }

        if (graph.Nodes.Count == 0)
        {
            EmptyGridNode worldNode = ScriptableObject.CreateInstance<EmptyGridNode>();
            worldNode.name = nameof(EmptyGridNode);
            worldNode.EditorPosition = new Vector2(140f, 220f);

            GraphOutputNode outputNode = ScriptableObject.CreateInstance<GraphOutputNode>();
            outputNode.name = nameof(GraphOutputNode);
            outputNode.EditorPosition = new Vector2(500f, 220f);

            AssetDatabase.AddObjectToAsset(worldNode, graph);
            AssetDatabase.AddObjectToAsset(outputNode, graph);

            graph.AddNode(worldNode);
            graph.AddNode(outputNode);
            graph.AddConnection(
                worldNode.NodeId,
                worldNode.GetOutputPortById(worldNode.OutputPorts[0].PortId).PortId,
                outputNode.NodeId,
                outputNode.GetInputPortById(outputNode.InputPorts[0].PortId).PortId);

            dirty = true;
        }

        if (dirty)
        {
            EditorUtility.SetDirty(graph);
            AssetDatabase.SaveAssets();
        }
    }
}
