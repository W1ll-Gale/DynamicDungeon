using System;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Graph;

namespace DynamicDungeon.Runtime.Core
{
    public readonly struct ExecutionResult
    {
        public readonly bool IsSuccess;
        public readonly string ErrorMessage;
        public readonly WorldSnapshot Snapshot;
        public readonly bool WasCancelled;
        public readonly IReadOnlyList<GraphDiagnostic> Diagnostics;

        public ExecutionResult(bool isSuccess, string errorMessage, WorldSnapshot snapshot, bool wasCancelled, IReadOnlyList<GraphDiagnostic> diagnostics = null)
        {
            IsSuccess = isSuccess;
            ErrorMessage = errorMessage;
            Snapshot = snapshot;
            WasCancelled = wasCancelled;
            Diagnostics = diagnostics ?? Array.Empty<GraphDiagnostic>();
        }
    }
}
