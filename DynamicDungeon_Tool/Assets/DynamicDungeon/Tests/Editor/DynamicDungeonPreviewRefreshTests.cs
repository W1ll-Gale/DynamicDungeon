using System.Collections;
using System.Reflection;
using DynamicDungeon.Editor.Nodes;
using DynamicDungeon.Editor.Shared;
using DynamicDungeon.Editor.Utilities;
using DynamicDungeon.Editor.Windows;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Nodes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DynamicDungeon.Tests.Editor
{
    public sealed class DynamicDungeonPreviewRefreshTests
    {
        [Test]
        public void NormalParameterEditQueuesDirtyNodeRefresh()
        {
            GenGraph graph;
            GenerationOrchestrator orchestrator;
            GenNodeView nodeView = CreateConstantNodeView(out graph, out orchestrator);

            try
            {
                InvokeParameterValueChanged(nodeView, "floatValue", "0.75");

                Assert.That(orchestrator.PendingDirtyNodeCountForTesting, Is.EqualTo(1));
                Assert.That(orchestrator.IsRefreshDebounceScheduledForTesting, Is.True);
                Assert.That(orchestrator.IsFullRefreshQueuedForTesting, Is.False);
            }
            finally
            {
                orchestrator.Dispose();
                Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void PortShapeParameterEditQueuesFullRefresh()
        {
            GenGraph graph;
            GenerationOrchestrator orchestrator;
            GenNodeView nodeView = CreateConstantNodeView(out graph, out orchestrator);

            try
            {
                InvokeParameterValueChanged(nodeView, "outputType", ChannelType.Int.ToString());

                Assert.That(orchestrator.PendingDirtyNodeCountForTesting, Is.EqualTo(0));
                Assert.That(orchestrator.IsRefreshDebounceScheduledForTesting, Is.True);
                Assert.That(orchestrator.IsFullRefreshQueuedForTesting, Is.True);
            }
            finally
            {
                orchestrator.Dispose();
                Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void DuplicateFullPreviewRefreshRequestsCoalesce()
        {
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();
            DynamicDungeonGraphView graphView = new DynamicDungeonGraphView();
            GenerationOrchestrator orchestrator = new GenerationOrchestrator(graphView, _ => { }, _ => { });

            try
            {
                orchestrator.SetGraph(graph);

                orchestrator.RequestPreviewRefresh();
                orchestrator.RequestPreviewRefresh();

                Assert.That(orchestrator.IsFullRefreshQueuedForTesting, Is.True);
                Assert.That(orchestrator.IsRefreshDebounceScheduledForTesting, Is.True);
                Assert.That(orchestrator.RefreshScheduleCountForTesting, Is.EqualTo(1));
            }
            finally
            {
                orchestrator.Dispose();
                Object.DestroyImmediate(graph);
            }
        }

        [UnityTest]
        public IEnumerator MiniMapDoesNotRefreshWhileIdle()
        {
            DynamicDungeonGraphView graphView = new DynamicDungeonGraphView();
            MiniMapWindow miniMapWindow = new MiniMapWindow(
                DynamicDungeonEditorWindow.CreateDefaultMiniMapLayoutForTesting(),
                graphView,
                new MiniMapGraphCallbacks
                {
                    RegisterViewTransformChanged = callback => graphView.SetViewTransformChangedCallback(callback),
                    GetViewportState = graphView.GetViewportState
                });
            int refreshCountAfterConstruction = miniMapWindow.RefreshCountForTesting;

            yield return null;
            yield return null;

            Assert.That(miniMapWindow.RefreshCountForTesting, Is.EqualTo(refreshCountAfterConstruction));
        }

        private static GenNodeView CreateConstantNodeView(out GenGraph graph, out GenerationOrchestrator orchestrator)
        {
            graph = ScriptableObject.CreateInstance<GenGraph>();
            graph.WorldWidth = 8;
            graph.WorldHeight = 8;

            GenNodeData nodeData = graph.AddNode(typeof(ConstantNode).FullName, "Constant", Vector2.zero);
            GenNodeInstantiationUtility.PopulateDefaultParameters(nodeData, typeof(ConstantNode));

            IGenNode nodeInstance;
            string errorMessage;
            Assert.That(
                GenNodeInstantiationUtility.TryCreateNodeInstance(nodeData, out nodeInstance, out errorMessage),
                Is.True,
                errorMessage);

            DynamicDungeonGraphView graphView = new DynamicDungeonGraphView();
            orchestrator = new GenerationOrchestrator(graphView, _ => { }, _ => { });
            orchestrator.SetGraph(graph);

            return new GenNodeView(
                graph,
                nodeData,
                nodeInstance,
                orchestrator,
                null,
                null,
                () => { });
        }

        private static void InvokeParameterValueChanged(GenNodeView nodeView, string parameterName, string value)
        {
            MethodInfo methodInfo = typeof(GenNodeView).GetMethod(
                "OnParameterValueChanged",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(methodInfo, Is.Not.Null);
            methodInfo.Invoke(nodeView, new object[] { parameterName, value });
        }
    }
}
