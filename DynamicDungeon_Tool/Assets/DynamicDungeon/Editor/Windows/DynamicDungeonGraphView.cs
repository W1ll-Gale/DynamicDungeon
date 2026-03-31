using System.Collections.Generic;
using DynamicDungeon.Runtime.Graph;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Windows
{
    public sealed class DynamicDungeonGraphView : GraphView
    {
        private readonly GridBackground _gridBackground;

        private GenGraph _graph;

        public GenGraph Graph
        {
            get
            {
                return _graph;
            }
        }

        public DynamicDungeonGraphView()
        {
            GraphView currentGraphView = this;

            style.flexGrow = 1.0f;

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            currentGraphView.AddManipulator(new ContentDragger());
            currentGraphView.AddManipulator(new SelectionDragger());
            currentGraphView.AddManipulator(new RectangleSelector());

            _gridBackground = new GridBackground();
            Insert(0, _gridBackground);
            _gridBackground.style.position = Position.Absolute;
            _gridBackground.style.left = 0.0f;
            _gridBackground.style.top = 0.0f;
            _gridBackground.style.right = 0.0f;
            _gridBackground.style.bottom = 0.0f;
        }

        public void LoadGraph(GenGraph graph)
        {
            ClearGraph();
            _graph = graph;
        }

        public void ClearGraph()
        {
            List<GraphElement> elementsToRemove = new List<GraphElement>();

            foreach (GraphElement element in graphElements)
            {
                elementsToRemove.Add(element);
            }

            DeleteElements(elementsToRemove);
            _graph = null;
        }
    }
}
