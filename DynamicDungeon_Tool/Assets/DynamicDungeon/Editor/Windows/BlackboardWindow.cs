using System;
using DynamicDungeon.Runtime.Graph;

namespace DynamicDungeon.Editor.Windows
{
    internal sealed class BlackboardWindow : FloatingPanelWindow
    {
        private readonly BlackboardPanel _panel;

        public BlackboardWindow(FloatingWindowLayout layout, Action onGraphMutated)
            : base("Blackboard", layout)
        {
            name = "DynamicDungeonBlackboardWindow";

            _panel = new BlackboardPanel(onGraphMutated);
            contentContainer.Add(_panel);
        }

        public void SetGraph(GenGraph graph)
        {
            _panel.SetGraph(graph);
        }
    }
}
