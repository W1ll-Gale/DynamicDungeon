namespace DynamicDungeon.Editor.Shared
{
    public enum SharedDiagnosticSeverity
    {
        Error,
        Warning
    }

    public readonly struct SharedGraphDiagnostic
    {
        public readonly SharedDiagnosticSeverity Severity;
        public readonly string Message;
        public readonly string ElementId;
        public readonly string Detail;

        public SharedGraphDiagnostic(SharedDiagnosticSeverity severity, string message, string elementId, string detail = null)
        {
            Severity = severity;
            Message = message;
            ElementId = elementId;
            Detail = detail;
        }
    }
}
