using System.Collections.Generic;
using DynamicDungeon.Runtime.Core;

namespace DynamicDungeon.Runtime.Graph
{
    public sealed class GraphCompileResult
    {
        public bool IsSuccess;
        public IReadOnlyList<GraphDiagnostic> Diagnostics;
        public ExecutionPlan Plan;
        public string OutputChannelName;
        public bool HasConnectedOutput;

        public GraphCompileResult(bool isSuccess, IReadOnlyList<GraphDiagnostic> diagnostics, ExecutionPlan plan, string outputChannelName, bool hasConnectedOutput)
        {
            IsSuccess = isSuccess;
            Diagnostics = diagnostics;
            Plan = plan;
            OutputChannelName = outputChannelName;
            HasConnectedOutput = hasConnectedOutput;
        }
    }
}
