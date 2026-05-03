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

    }
}
