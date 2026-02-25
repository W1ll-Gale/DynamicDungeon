using UnityEngine;

public static class GraphLayerUtility
{
    public static bool TryGetLayerId(
        GenGraph graph,
        GraphLayerReference layerReference,
        PortDataKind expectedKind,
        string ownerName,
        string usageLabel,
        out string layerId)
    {
        layerId = null;

        if (graph == null)
        {
            Debug.LogWarning($"[{ownerName}] No graph was available while resolving {usageLabel}.");
            return false;
        }

        if (!layerReference.IsAssigned)
        {
            Debug.LogWarning($"[{ownerName}] {usageLabel} is not assigned.");
            return false;
        }

        if (!graph.TryGetLayer(layerReference.LayerId, out GraphLayerDefinition layer))
        {
            Debug.LogWarning($"[{ownerName}] {usageLabel} no longer exists on graph '{graph.name}'.");
            return false;
        }

        if (layer.Kind != expectedKind)
        {
            Debug.LogWarning(
                $"[{ownerName}] {usageLabel} expects a {expectedKind} layer, but '{layer.DisplayName}' is {layer.Kind}.");
            return false;
        }

        layerId = layer.LayerId;
        return true;
    }

    public static string GetDisplayName(GenGraph graph, GraphLayerReference layerReference, string fallback = "Unassigned Layer")
    {
        if (graph != null && layerReference.IsAssigned && graph.TryGetLayer(layerReference.LayerId, out GraphLayerDefinition layer))
            return layer.DisplayName;

        return fallback;
    }
}
