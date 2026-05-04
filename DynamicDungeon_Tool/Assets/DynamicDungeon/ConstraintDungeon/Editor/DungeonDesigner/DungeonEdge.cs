using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;
using UnityEngine;
using DynamicDungeon.ConstraintDungeon;
using System.Collections.Generic;

namespace DynamicDungeon.ConstraintDungeon.Editor.DungeonDesigner
{
    public class DungeonEdge : Edge
    {
        public RoomNode associatedCorridor;
        public readonly List<RoomNode> associatedCorridors = new List<RoomNode>();
        public string fromRoomId;
        public string toRoomId;
        private VisualElement _centreBox;
        private Label _connectorLabel;

        public DungeonEdge()
        {
            _centreBox = new VisualElement { name = "edge-box" };
            _centreBox.pickingMode = PickingMode.Position;

            _centreBox.style.width = 27;
            _centreBox.style.height = 27;
            _centreBox.style.backgroundColor = new Color(0.15f, 0.2f, 0.22f, 1f);
            
            _centreBox.style.borderTopWidth = 1;
            _centreBox.style.borderBottomWidth = 1;
            _centreBox.style.borderLeftWidth = 1;
            _centreBox.style.borderRightWidth = 1;

            _centreBox.style.borderTopColor = new Color(0.44f, 0.75f, 1f, 1f);
            _centreBox.style.borderBottomColor = new Color(0.44f, 0.75f, 1f, 1f);
            _centreBox.style.borderLeftColor = new Color(0.44f, 0.75f, 1f, 1f);
            _centreBox.style.borderRightColor = new Color(0.44f, 0.75f, 1f, 1f);

            _centreBox.style.borderTopLeftRadius = 4;
            _centreBox.style.borderTopRightRadius = 4;
            _centreBox.style.borderBottomLeftRadius = 4;
            _centreBox.style.borderBottomRightRadius = 4;
            _centreBox.style.position = Position.Absolute;

            _connectorLabel = new Label("1");
            _connectorLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _connectorLabel.style.fontSize = 11;
            _connectorLabel.style.color = Color.white;
            _connectorLabel.style.flexGrow = 1;
            _centreBox.Add(_connectorLabel);
            
            Add(_centreBox);

            // Periodically update position (better than every frame)
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        public override bool UpdateEdgeControl()
        {
            bool result = base.UpdateEdgeControl();
            UpdateBoxPosition();
            return result;
        }

        public void SetCorridorCount(int count)
        {
            if (_connectorLabel != null)
                _connectorLabel.text = Mathf.Max(0, count).ToString();
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            UpdateBoxPosition();
        }

        public void UpdateBoxPosition()
        {
            if (edgeControl == null) return;

            // EdgeControl points define the spline path
            // We want the midpoint of the straight line or the spline
            Vector2 start = edgeControl.from;
            Vector2 end = edgeControl.to;
            Vector2 centre = (start + end) / 2f;

            _centreBox.style.left = centre.x - (_centreBox.layout.width / 2f);
            _centreBox.style.top = centre.y - (_centreBox.layout.height / 2f);
        }
    }
}
