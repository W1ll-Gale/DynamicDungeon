using DynamicDungeon.ConstraintDungeon.Editor.DungeonDesigner;
using DynamicDungeon.Editor.Shared;
using NUnit.Framework;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace DynamicDungeon.ConstraintDungeon.Tests.EditMode
{
    public sealed class DungeonDesignerSharedShellTests
    {
        [Test]
        public void SharedMiniMapCanAttachToDungeonGraphView()
        {
            DungeonGraphView graphView = new DungeonGraphView(null);
            DungeonFlow flow = CreateFlowWithCorridorLink();

            try
            {
                graphView.LoadFlow(flow);
                MiniMapWindow miniMapWindow = new MiniMapWindow(
                    new FloatingWindowLayout(),
                    graphView,
                    new MiniMapGraphCallbacks
                    {
                        RegisterViewTransformChanged = callback => graphView.SetViewTransformChangedCallback(callback),
                        GetViewportState = graphView.GetViewportState,
                        ShouldIncludeElement = element => element is Node,
                        GetElementId = element => element is Node node ? node.viewDataKey : null,
                        FocusElement = graphView.FocusElement
                    });

                Assert.That(miniMapWindow.RefreshCountForTesting, Is.GreaterThan(0));
                Assert.That(graphView.FocusElement("entrance"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(flow);
            }
        }

        [Test]
        public void DiagnosticsCanFocusDungeonCorridorLink()
        {
            DungeonGraphView graphView = new DungeonGraphView(null);
            DungeonFlow flow = CreateFlowWithCorridorLink();

            try
            {
                graphView.LoadFlow(flow);
                string linkElementId = DungeonGraphView.BuildLinkElementId("entrance", "exit");

                Assert.That(graphView.ResolveElementName(linkElementId), Does.Contain("Entrance"));
                Assert.That(graphView.FocusElement(linkElementId), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(flow);
            }
        }

        private static DungeonFlow CreateFlowWithCorridorLink()
        {
            DungeonFlow flow = ScriptableObject.CreateInstance<DungeonFlow>();
            flow.nodes.Add(new RoomNode("entrance")
            {
                displayName = "Entrance",
                type = RoomType.Entrance,
                position = new Vector2(0.0f, 0.0f)
            });
            flow.nodes.Add(new RoomNode("corridor")
            {
                displayName = "Connector",
                type = RoomType.Corridor,
                position = new Vector2(200.0f, 0.0f)
            });
            flow.nodes.Add(new RoomNode("exit")
            {
                displayName = "Exit",
                type = RoomType.Exit,
                position = new Vector2(400.0f, 0.0f)
            });
            flow.edges.Add(new RoomEdge("entrance", "corridor"));
            flow.edges.Add(new RoomEdge("corridor", "exit"));
            return flow;
        }
    }
}
