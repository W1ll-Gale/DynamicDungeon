using System.Collections.Generic;
using System.Text;

public sealed class GraphCompileResult
{
    private readonly List<GraphDiagnostic> _diagnostics;
    private readonly List<GenNodeBase> _executionOrder;

    public bool IsSuccess { get; }
    public IReadOnlyList<GraphDiagnostic> Diagnostics => _diagnostics;
    public IReadOnlyList<GenNodeBase> ExecutionOrder => _executionOrder;

    private GraphCompileResult(
        bool isSuccess,
        List<GraphDiagnostic> diagnostics,
        List<GenNodeBase> executionOrder)
    {
        IsSuccess = isSuccess;
        _diagnostics = diagnostics ?? new List<GraphDiagnostic>();
        _executionOrder = executionOrder ?? new List<GenNodeBase>();
    }

    public static GraphCompileResult Success(
        List<GenNodeBase> executionOrder,
        List<GraphDiagnostic> diagnostics = null)
        => new GraphCompileResult(true, diagnostics, executionOrder);

    public static GraphCompileResult Failure(
        List<GraphDiagnostic> diagnostics,
        List<GenNodeBase> partialOrder = null)
        => new GraphCompileResult(false, diagnostics, partialOrder);

    public string BuildSummary()
    {
        if (_diagnostics.Count == 0)
            return IsSuccess ? "Graph compile succeeded." : "Graph compile failed.";

        StringBuilder builder = new StringBuilder();
        builder.AppendLine(IsSuccess ? "Graph compile succeeded with diagnostics:" : "Graph compile failed:");
        foreach (GraphDiagnostic diagnostic in _diagnostics)
            builder.AppendLine(diagnostic.ToString());
        return builder.ToString().TrimEnd();
    }
}
