using System;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Graph;

namespace DynamicDungeon.Runtime.Core
{
    public static class ManagedBlackboardDiagnosticUtility
    {
        private const string DiagnosticsKey = "__RuntimeDiagnostics";

        public static void AppendWarning(ManagedBlackboard managedBlackboard, string message, string nodeId, string portName)
        {
            AppendDiagnostic(managedBlackboard, new GraphDiagnostic(DiagnosticSeverity.Warning, message, nodeId, portName));
        }

        public static IReadOnlyList<GraphDiagnostic> ReadDiagnosticsSnapshot(ManagedBlackboard managedBlackboard)
        {
            if (managedBlackboard == null)
            {
                return Array.Empty<GraphDiagnostic>();
            }

            List<GraphDiagnostic> diagnostics;
            if (!managedBlackboard.Read(DiagnosticsKey, out diagnostics) || diagnostics == null || diagnostics.Count == 0)
            {
                return Array.Empty<GraphDiagnostic>();
            }

            return diagnostics.ToArray();
        }

        private static void AppendDiagnostic(ManagedBlackboard managedBlackboard, GraphDiagnostic diagnostic)
        {
            if (managedBlackboard == null)
            {
                return;
            }

            List<GraphDiagnostic> diagnostics;
            if (!managedBlackboard.Read(DiagnosticsKey, out diagnostics) || diagnostics == null)
            {
                diagnostics = new List<GraphDiagnostic>();
                managedBlackboard.Write(DiagnosticsKey, diagnostics);
            }

            int index;
            for (index = 0; index < diagnostics.Count; index++)
            {
                GraphDiagnostic existingDiagnostic = diagnostics[index];
                if (existingDiagnostic.Severity == diagnostic.Severity &&
                    string.Equals(existingDiagnostic.Message, diagnostic.Message, StringComparison.Ordinal) &&
                    string.Equals(existingDiagnostic.NodeId, diagnostic.NodeId, StringComparison.Ordinal) &&
                    string.Equals(existingDiagnostic.PortName, diagnostic.PortName, StringComparison.Ordinal))
                {
                    return;
                }
            }

            diagnostics.Add(diagnostic);
        }
    }
}
