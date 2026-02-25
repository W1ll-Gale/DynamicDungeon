using System;

public enum GraphDiagnosticSeverity
{
    Info,
    Warning,
    Error
}

[Serializable]
public sealed class GraphDiagnostic
{
    public GraphDiagnosticSeverity Severity { get; }
    public string Message { get; }
    public string NodeId { get; }
    public string ConnectionId { get; }

    public GraphDiagnostic(
        GraphDiagnosticSeverity severity,
        string message,
        string nodeId = "",
        string connectionId = "")
    {
        Severity = severity;
        Message = message ?? string.Empty;
        NodeId = nodeId ?? string.Empty;
        ConnectionId = connectionId ?? string.Empty;
    }

    public override string ToString()
    {
        string location = string.Empty;
        if (!string.IsNullOrEmpty(NodeId)) location += $" Node={NodeId}";
        if (!string.IsNullOrEmpty(ConnectionId)) location += $" Connection={ConnectionId}";
        return $"[{Severity}] {Message}{location}";
    }
}
