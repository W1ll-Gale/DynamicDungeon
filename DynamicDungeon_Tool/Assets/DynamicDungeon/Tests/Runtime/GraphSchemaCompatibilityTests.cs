using System;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Graph;
using NUnit.Framework;
using UnityEngine;

namespace DynamicDungeon.Tests.Runtime
{
    public sealed class GraphSchemaCompatibilityTests
    {
        [Test]
        public void CurrentSchemaGraphValidatesSuccessfully()
        {
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();

            try
            {
                graph.SchemaVersion = GraphSchemaVersion.Current;

                string errorMessage;
                bool isValid = GraphOutputUtility.TryValidateCurrentSchema(graph, out errorMessage);

                Assert.That(isValid, Is.True);
                Assert.That(errorMessage, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void LegacySchemaGraphFailsValidationWithClearError()
        {
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();

            try
            {
                graph.SchemaVersion = GraphSchemaVersion.Current - 1;

                string errorMessage;
                bool isValid = GraphOutputUtility.TryValidateCurrentSchema(graph, out errorMessage);

                Assert.That(isValid, Is.False);
                Assert.That(errorMessage, Does.Contain("no longer supported"));
                Assert.That(errorMessage, Does.Contain("schema v" + GraphSchemaVersion.Current));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void CompileRejectsLegacySchemaGraphWithClearDiagnostic()
        {
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();

            try
            {
                graph.SchemaVersion = 0;
                graph.WorldWidth = 8;
                graph.WorldHeight = 8;
                graph.DefaultSeed = 123L;
                GraphOutputUtility.EnsureSingleOutputNode(graph);

                GraphCompileResult compileResult = GraphCompiler.Compile(graph);

                Assert.That(compileResult.IsSuccess, Is.False);
                Assert.That(compileResult.Plan, Is.Null);
                Assert.That(ContainsError(compileResult.Diagnostics, "no longer supported"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        private static bool ContainsError(IReadOnlyList<GraphDiagnostic> diagnostics, string messageFragment)
        {
            int index;
            for (index = 0; index < diagnostics.Count; index++)
            {
                GraphDiagnostic diagnostic = diagnostics[index];
                if (diagnostic.Severity == DiagnosticSeverity.Error &&
                    diagnostic.Message != null &&
                    diagnostic.Message.IndexOf(messageFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
