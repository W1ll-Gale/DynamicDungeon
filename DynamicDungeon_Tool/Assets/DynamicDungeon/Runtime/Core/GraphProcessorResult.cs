using System.Collections.Generic;

public sealed class GraphProcessorResult
{
    private Dictionary<string, IReadOnlyDictionary<string, NodeValue>> _nodeOutputs =
        new Dictionary<string, IReadOnlyDictionary<string, NodeValue>>();

    private List<GraphDiagnostic> _diagnostics = new List<GraphDiagnostic>();
    private NodeValue _primaryGraphOutput;

    public bool IsSuccess { get; private set; }
    public string ErrorMessage { get; private set; }
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, NodeValue>> NodeOutputs => _nodeOutputs;
    public NodeValue PrimaryGraphOutput => _primaryGraphOutput;
    public IReadOnlyList<GraphDiagnostic> Diagnostics => _diagnostics;

    public static GraphProcessorResult Success(
        Dictionary<string, IReadOnlyDictionary<string, NodeValue>> nodeOutputs,
        NodeValue primaryGraphOutput,
        IEnumerable<GraphDiagnostic> diagnostics = null)
    {
        GraphProcessorResult result = new GraphProcessorResult();
        result.IsSuccess = true;
        result._nodeOutputs = nodeOutputs ?? new Dictionary<string, IReadOnlyDictionary<string, NodeValue>>();
        result._primaryGraphOutput = primaryGraphOutput;
        if (diagnostics != null) result._diagnostics = new List<GraphDiagnostic>(diagnostics);
        return result;
    }

    public static GraphProcessorResult Failure(
        string errorMessage,
        IEnumerable<GraphDiagnostic> diagnostics = null)
    {
        GraphProcessorResult result = new GraphProcessorResult();
        result.IsSuccess = false;
        result.ErrorMessage = errorMessage;
        if (diagnostics != null) result._diagnostics = new List<GraphDiagnostic>(diagnostics);
        return result;
    }

    public bool TryGetNodeOutput(string nodeId, string portName, out NodeValue value)
    {
        value = null;
        if (!_nodeOutputs.TryGetValue(nodeId, out IReadOnlyDictionary<string, NodeValue> ports))
            return false;

        return ports.TryGetValue(portName, out value);
    }

    public bool TryGetPrimaryGraphOutput(out NodeValue value)
    {
        value = _primaryGraphOutput;
        return value != null;
    }
}
