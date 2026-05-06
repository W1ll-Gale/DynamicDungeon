using System.Collections.Generic;
using DynamicDungeon.Editor.Windows;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Nodes
{
    public sealed class GenEdgeConnectorListener : IEdgeConnectorListener
    {
        private readonly List<Edge> _edgesToCreate = new List<Edge>();
        private readonly List<GraphElement> _elementsToRemove = new List<GraphElement>();

        public void OnDropOutsidePort(Edge edge, Vector2 position)
        {
            DynamicDungeonGraphView graphView = null;
            if (edge != null)
            {
                Port anchorPort = edge.output != null ? edge.output : edge.input;
                graphView = anchorPort != null ? anchorPort.GetFirstAncestorOfType<DynamicDungeonGraphView>() : null;
                if (graphView != null)
                {
                    graphView.OpenFilteredNodeSearch(graphView.WorldToLocal(position), anchorPort);
                }
            }
        }

        public void OnDrop(GraphView graphView, Edge edge)
        {
            if (graphView == null || edge == null)
            {
                return;
            }

            _edgesToCreate.Clear();
            _edgesToCreate.Add(edge);

            _elementsToRemove.Clear();
            CollectConflictingConnections(edge.input, edge, _elementsToRemove);
            CollectConflictingConnections(edge.output, edge, _elementsToRemove);

            if (_elementsToRemove.Count > 0)
            {
                graphView.DeleteElements(_elementsToRemove);
            }

            GraphViewChange graphViewChange = new GraphViewChange
            {
                edgesToCreate = _edgesToCreate
            };

            if (graphView.graphViewChanged != null)
            {
                graphViewChange = graphView.graphViewChanged(graphViewChange);
            }

            List<Edge> createdEdges = graphViewChange.edgesToCreate;
            if (createdEdges == null)
            {
                return;
            }

            int edgeIndex;
            for (edgeIndex = 0; edgeIndex < createdEdges.Count; edgeIndex++)
            {
                Edge createdEdge = createdEdges[edgeIndex];
                if (createdEdge == null)
                {
                    continue;
                }

                DynamicDungeonGraphView dynamicDungeonGraphView = graphView as DynamicDungeonGraphView;
                Edge edgeToAdd = dynamicDungeonGraphView != null
                    ? dynamicDungeonGraphView.NormaliseCreatedEdge(createdEdge)
                    : createdEdge;

                if (!ReferenceEquals(edgeToAdd, createdEdge))
                {
                    if (createdEdge.output != null)
                    {
                        createdEdge.output.Disconnect(createdEdge);
                    }

                    if (createdEdge.input != null)
                    {
                        createdEdge.input.Disconnect(createdEdge);
                    }

                    if (createdEdge.parent != null)
                    {
                        createdEdge.RemoveFromHierarchy();
                    }
                }

                graphView.AddElement(edgeToAdd);
            }
        }

        private static void CollectConflictingConnections(Port port, Edge draggedEdge, ICollection<GraphElement> elementsToRemove)
        {
            if (port == null || port.capacity != Port.Capacity.Single)
            {
                return;
            }

            foreach (Edge connection in port.connections)
            {
                if (connection == null || ReferenceEquals(connection, draggedEdge) || elementsToRemove.Contains(connection))
                {
                    continue;
                }

                elementsToRemove.Add(connection);
            }
        }
    }
}
