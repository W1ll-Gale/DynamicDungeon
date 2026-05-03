using DynamicDungeon.Runtime.Graph;

namespace DynamicDungeon.Editor.Windows
{
    internal sealed class GroupNavigatorWindow : FloatingPanelWindow
    {
        private readonly GroupNavigatorPanel _panel;

        public GroupNavigatorWindow(FloatingWindowLayout layout, DynamicDungeonGraphView graphView)
            : base("Groups", layout)
        {
            name = "DynamicDungeonGroupNavigatorWindow";
            _panel = new GroupNavigatorPanel(graphView);
            contentContainer.Add(_panel);
        }

        public void SetGraph(GenGraph graph)
        {
            _panel.SetGraph(graph);
        }

        public void Refresh()
        {
            _panel.Refresh();
        }
    }
}
