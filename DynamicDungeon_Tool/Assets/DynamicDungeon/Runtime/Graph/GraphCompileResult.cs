using System.Collections.Generic;
using DynamicDungeon.Runtime.Core;

namespace DynamicDungeon.Runtime.Graph
{
    public sealed class GraphCompileResult
    {
        public bool IsSuccess;
        public IReadOnlyList<GraphDiagnostic> Diagnostics;
        public ExecutionPlan Plan;

        public GraphCompileResult(bool isSuccess, IReadOnlyList<GraphDiagnostic> diagnostics, ExecutionPlan plan)
        {
            IsSuccess = isSuccess;
            Diagnostics = diagnostics;
            Plan = plan;
        }
    }
}
