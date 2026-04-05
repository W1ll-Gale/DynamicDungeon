using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace DynamicDungeon.Editor.Nodes
{
    public sealed class GenEdgeConnectorListener : IEdgeConnectorListener
    {
        private readonly List<Edge> _edgesToCreate = new List<Edge>();
        private readonly List<GraphElement> _elementsToRemove = new List<GraphElement>();

        public void OnDropOutsidePort(Edge edge, Vector2 position)
        {
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

                graphView.AddElement(createdEdge);
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
