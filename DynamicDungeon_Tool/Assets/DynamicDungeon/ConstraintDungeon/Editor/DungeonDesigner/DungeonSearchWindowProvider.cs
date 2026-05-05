using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using DynamicDungeon.ConstraintDungeon;
using UnityEditor;

namespace DynamicDungeon.ConstraintDungeon.Editor.DungeonDesigner
{
    public class DungeonSearchWindowProvider : ScriptableObject, ISearchWindowProvider
    {
        private DungeonGraphView _graphView;
        private EditorWindow _window;
        private Texture2D _indentationIcon;

        public void Initialise(DungeonGraphView graphView, EditorWindow window)
        {
            _graphView = graphView;
            _window = window;
            
            // Transparent icon for indentation
            _indentationIcon = new Texture2D(1, 1);
            _indentationIcon.SetPixel(0, 0, new Color(0, 0, 0, 0));
            _indentationIcon.Apply();
        }

        public List<SearchTreeEntry> CreateSearchTree(SearchWindowContext context)
        {
            List<SearchTreeEntry> tree = new List<SearchTreeEntry>
            {
                new SearchTreeGroupEntry(new GUIContent("Create Room"), 0),
                
                new SearchTreeGroupEntry(new GUIContent("Standard Rooms"), 1),
                new SearchTreeEntry(new GUIContent("Normal Room", _indentationIcon)) { level = 2, userData = RoomType.Room },
                new SearchTreeEntry(new GUIContent("Hub Room", _indentationIcon)) { level = 2, userData = RoomType.Hub },
                new SearchTreeEntry(new GUIContent("Boss Room", _indentationIcon)) { level = 2, userData = RoomType.Boss },
                
                new SearchTreeGroupEntry(new GUIContent("Specialised Nodes"), 1),
                new SearchTreeEntry(new GUIContent("Entrance (Spawn)", _indentationIcon)) { level = 2, userData = RoomType.Entrance },
                new SearchTreeEntry(new GUIContent("Exit", _indentationIcon)) { level = 2, userData = RoomType.Exit },
                new SearchTreeEntry(new GUIContent("Shop", _indentationIcon)) { level = 2, userData = RoomType.Shop },
                new SearchTreeEntry(new GUIContent("Reward Room", _indentationIcon)) { level = 2, userData = RoomType.Reward },
                new SearchTreeEntry(new GUIContent("Boss Foyer", _indentationIcon)) { level = 2, userData = RoomType.BossFoyer }
            };

            return tree;
        }

        public bool OnSelectEntry(SearchTreeEntry searchTreeEntry, SearchWindowContext context)
        {
            // Coordinate mapping: Convert screen-space mouse to graph-space
            VisualElement root = _window.rootVisualElement;
            Vector2 windowMousePosition = root.ChangeCoordinatesTo(root.parent, context.screenMousePosition - _window.position.position);
            Vector2 graphMousePosition = _graphView.contentViewContainer.WorldToLocal(windowMousePosition);

            _graphView.AddNode((RoomType)searchTreeEntry.userData, graphMousePosition);
            return true;
        }
    }
}
