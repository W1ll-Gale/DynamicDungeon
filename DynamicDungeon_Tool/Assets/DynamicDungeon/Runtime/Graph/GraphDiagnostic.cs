namespace DynamicDungeon.Runtime.Graph
{
    public readonly struct GraphDiagnostic
    {
        public readonly DiagnosticSeverity Severity;
        public readonly string Message;
        public readonly string NodeId;
        public readonly string PortName;

        public GraphDiagnostic(DiagnosticSeverity severity, string message, string nodeId, string portName)
        {
            Severity = severity;
            Message = message;
            NodeId = nodeId;
            PortName = portName;
        }
    }
}
