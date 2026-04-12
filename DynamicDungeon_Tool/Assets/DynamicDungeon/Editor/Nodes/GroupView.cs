using System;
using DynamicDungeon.Runtime.Graph;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Nodes
{
    public sealed class GroupView : Group
    {
        private readonly GenGraph _graph;
        private readonly GenGroupData _groupData;
        private readonly Action _afterMutation;

        private bool _suppressSync;

        public string GroupId
        {
            get
            {
                return _groupData != null ? _groupData.GroupId : string.Empty;
            }
        }

        public GroupView(GenGraph graph, GenGroupData groupData, Action afterMutation) : base()
        {
            _graph = graph;
            _groupData = groupData;
            _afterMutation = afterMutation;

            _suppressSync = true;
            title = groupData.Title ?? string.Empty;
            SetPosition(groupData.Position);
            _suppressSync = false;

            RegisterCallback<FocusOutEvent>(OnGroupFocusOut);
        }

        public override void SetPosition(Rect newPos)
        {
            base.SetPosition(newPos);

            if (_suppressSync || _graph == null || _groupData == null)
            {
                return;
            }

            if (_groupData.Position == newPos)
            {
                return;
            }

            Undo.RecordObject(_graph, "Move Group");
            _groupData.Position = newPos;
            EditorUtility.SetDirty(_graph);
            _afterMutation?.Invoke();
        }

        private void OnGroupFocusOut(FocusOutEvent focusOutEvent)
        {
            if (_suppressSync || _graph == null || _groupData == null)
            {
                return;
            }

            string currentTitle = title ?? string.Empty;
            if (string.Equals(_groupData.Title, currentTitle, StringComparison.Ordinal))
            {
                return;
            }

            Undo.RecordObject(_graph, "Rename Group");
            _groupData.Title = currentTitle;
            EditorUtility.SetDirty(_graph);
            _afterMutation?.Invoke();
        }
    }
}
