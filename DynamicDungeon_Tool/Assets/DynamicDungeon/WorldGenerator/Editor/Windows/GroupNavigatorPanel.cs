using System;
using System.Collections.Generic;
using System.Globalization;
using DynamicDungeon.Runtime.Graph;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Windows
{
    internal sealed class GroupNavigatorPanel : VisualElement
    {
        private readonly DynamicDungeonGraphView _graphView;
        private readonly VisualElement _list;
        private GenGraph _graph;

        public GroupNavigatorPanel(DynamicDungeonGraphView graphView)
        {
            _graphView = graphView;
            style.flexDirection = FlexDirection.Column;
            style.flexGrow = 1.0f;
            style.paddingLeft = 8.0f;
            style.paddingRight = 8.0f;
            style.paddingTop = 8.0f;
            style.paddingBottom = 8.0f;

            _list = new VisualElement();
            _list.style.flexDirection = FlexDirection.Column;
            _list.style.flexGrow = 1.0f;
            Add(_list);
        }

        public void SetGraph(GenGraph graph)
        {
            _graph = graph;
            Refresh();
        }

        public void Refresh()
        {
            _list.Clear();

            if (_graph == null || _graphView == null)
            {
                _list.Add(CreateEmptyLabel("No graph loaded."));
                return;
            }

            IReadOnlyList<GroupNavigationItem> groups = _graphView.GetGroupNavigationItems();
            if (groups.Count == 0)
            {
                _list.Add(CreateEmptyLabel("No groups yet."));
                return;
            }

            int groupIndex;
            for (groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                _list.Add(CreateGroupRow(groups[groupIndex]));
            }
        }

        private VisualElement CreateGroupRow(GroupNavigationItem item)
        {
            Button row = new Button(() => _graphView.SelectAndFrameGroup(item.GroupId));
            row.name = "GroupNavigatorRow_" + item.GroupId;
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4.0f;
            row.style.paddingLeft = 6.0f;
            row.style.paddingRight = 6.0f;
            row.style.paddingTop = 5.0f;
            row.style.paddingBottom = 5.0f;
            row.tooltip = "Frame group '" + item.Title + "'";

            Label title = new Label(item.Title);
            title.style.flexGrow = 1.0f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.unityTextAlign = TextAnchor.MiddleLeft;
            row.Add(title);

            Label count = new Label(item.NodeCount.ToString(CultureInfo.InvariantCulture));
            count.style.minWidth = 28.0f;
            count.style.unityTextAlign = TextAnchor.MiddleRight;
            count.style.color = new Color(0.72f, 0.72f, 0.72f, 1.0f);
            row.Add(count);

            return row;
        }

        private static Label CreateEmptyLabel(string text)
        {
            Label label = new Label(text ?? string.Empty);
            label.style.color = new Color(0.68f, 0.68f, 0.68f, 1.0f);
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }
    }
}
