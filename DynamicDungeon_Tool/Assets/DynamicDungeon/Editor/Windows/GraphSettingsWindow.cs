using System;
using DynamicDungeon.Editor.Shared;
using DynamicDungeon.Runtime.Graph;

namespace DynamicDungeon.Editor.Windows
{
    internal sealed class GraphSettingsWindow : FloatingPanelWindow
    {
        private readonly GraphSettingsPanel _panel;

        public GraphSettingsWindow(FloatingWindowLayout layout, Action onDimensionsOrSeedChanged, Action onGraphMutated)
            : base("Graph Settings", layout)
        {
            name = "DynamicDungeonGraphSettingsWindow";

            _panel = new GraphSettingsPanel(onDimensionsOrSeedChanged, onGraphMutated);
            contentContainer.Add(_panel);
        }

        public void SetGraph(GenGraph graph)
        {
            _panel.SetGraph(graph);
        }
    }
}
