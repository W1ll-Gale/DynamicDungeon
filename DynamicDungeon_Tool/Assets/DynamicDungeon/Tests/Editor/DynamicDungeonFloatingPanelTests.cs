using DynamicDungeon.Editor.Windows;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using NUnit.Framework;
using UnityEngine;

namespace DynamicDungeon.Tests.Editor
{
    public sealed class DynamicDungeonFloatingPanelTests
    {
        [Test]
        public void AutoSaveDefaultsOnAndPersistsTogglePreference()
        {
            bool hadOriginalPreference = DynamicDungeonEditorWindow.HasAutoSavePreferenceForTesting();
            bool originalPreference = DynamicDungeonEditorWindow.LoadAutoSavePreferenceForTesting();

            try
            {
                DynamicDungeonEditorWindow.DeleteAutoSavePreferenceForTesting();
                Assert.That(DynamicDungeonEditorWindow.LoadAutoSavePreferenceForTesting(), Is.True);

                DynamicDungeonEditorWindow.SaveAutoSavePreferenceForTesting(false);
                Assert.That(DynamicDungeonEditorWindow.LoadAutoSavePreferenceForTesting(), Is.False);

                DynamicDungeonEditorWindow.SaveAutoSavePreferenceForTesting(true);
                Assert.That(DynamicDungeonEditorWindow.LoadAutoSavePreferenceForTesting(), Is.True);
            }
            finally
            {
                if (hadOriginalPreference)
                {
                    DynamicDungeonEditorWindow.SaveAutoSavePreferenceForTesting(originalPreference);
                }
                else
                {
                    DynamicDungeonEditorWindow.DeleteAutoSavePreferenceForTesting();
                }
            }
        }

        [Test]
        public void PanelViewSettingsRoundTripPersistsVisibilityChoices()
        {
            DynamicDungeonEditorWindow.PanelViewSettings originalSettings =
                Clone(DynamicDungeonEditorWindow.LoadPanelViewSettingsForTesting());

            try
            {
                DynamicDungeonEditorWindow.SavePanelViewSettingsForTesting(
                    new DynamicDungeonEditorWindow.PanelViewSettings
                    {
                        IsBlackboardVisible = false,
                        IsGraphSettingsVisible = true,
                        IsMiniMapVisible = false,
                        IsBlackboardCollapsed = true,
                        IsGraphSettingsCollapsed = false,
                        IsMiniMapCollapsed = true
                    });

                DynamicDungeonEditorWindow.PanelViewSettings reloadedSettings =
                    DynamicDungeonEditorWindow.LoadPanelViewSettingsForTesting();

                Assert.That(reloadedSettings.IsBlackboardVisible, Is.False);
                Assert.That(reloadedSettings.IsGraphSettingsVisible, Is.True);
                Assert.That(reloadedSettings.IsMiniMapVisible, Is.False);
                Assert.That(reloadedSettings.IsBlackboardCollapsed, Is.True);
                Assert.That(reloadedSettings.IsGraphSettingsCollapsed, Is.False);
                Assert.That(reloadedSettings.IsMiniMapCollapsed, Is.True);
            }
            finally
            {
                DynamicDungeonEditorWindow.SavePanelViewSettingsForTesting(originalSettings);
            }
        }

        [Test]
        public void DefaultPanelLayoutsDockBlackboardLeftAndSettingsRight()
        {
            FloatingWindowLayout blackboardLayout =
                DynamicDungeonEditorWindow.CreateDefaultBlackboardLayoutForTesting();
            FloatingWindowLayout graphSettingsLayout =
                DynamicDungeonEditorWindow.CreateDefaultGraphSettingsLayoutForTesting();
            FloatingWindowLayout miniMapLayout =
                DynamicDungeonEditorWindow.CreateDefaultMiniMapLayoutForTesting();

            Rect parentRect = new Rect(0.0f, 0.0f, 1000.0f, 700.0f);
            Rect blackboardRect = blackboardLayout.GetRect(parentRect);
            Rect graphSettingsRect = graphSettingsLayout.GetRect(parentRect);
            Rect miniMapRect = miniMapLayout.GetRect(parentRect);

            Assert.That(blackboardLayout.DockToLeft, Is.True);
            Assert.That(graphSettingsLayout.DockToLeft, Is.False);
            Assert.That(miniMapLayout.DockToLeft, Is.False);
            Assert.That(miniMapLayout.DockToTop, Is.False);
            Assert.That(blackboardRect.x, Is.EqualTo(8.0f).Within(0.001f));
            Assert.That(graphSettingsRect.xMax, Is.EqualTo(parentRect.width - 8.0f).Within(0.001f));
            Assert.That(miniMapRect.xMax, Is.EqualTo(parentRect.width - 8.0f).Within(0.001f));
            Assert.That(miniMapRect.yMax, Is.EqualTo(parentRect.height - 8.0f).Within(0.001f));
        }

        [Test]
        public void BlackboardPanelMaintainsAddRenameReorderAndDeleteFlows()
        {
            int mutationCount = 0;
            DynamicDungeonGraphView graphView = new DynamicDungeonGraphView();
            BlackboardPanel panel = new BlackboardPanel(() => mutationCount++);
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();

            try
            {
                graphView.Add(panel);
                panel.SetGraph(graph);
                panel.AddExposedPropertyForTesting();
                panel.AddExposedPropertyForTesting();

                Assert.That(graph.ExposedProperties.Count, Is.EqualTo(2));

                ExposedProperty firstProperty = graph.ExposedProperties[0];
                graphView.LoadGraph(graph);
                graphView.CreateExposedPropertyNodeFromBlackboardForTesting(firstProperty.PropertyId, new Vector2(120.0f, 60.0f));

                panel.RenamePropertyForTesting(firstProperty, "SeedStrength");

                Assert.That(firstProperty.PropertyName, Is.EqualTo("SeedStrength"));
                Assert.That(graph.Nodes[0].NodeName, Is.EqualTo("SeedStrength"));
                Assert.That(
                    ExposedPropertyNodeUtility.GetPropertyName(graph.Nodes[0]),
                    Is.EqualTo("SeedStrength"));

                string secondPropertyId = graph.ExposedProperties[1].PropertyId;
                panel.MovePropertyForTesting(secondPropertyId, -1);

                Assert.That(graph.ExposedProperties[0].PropertyId, Is.EqualTo(secondPropertyId));

                string usedPropertyId = graph.ExposedProperties[1].PropertyId;
                panel.DeletePropertyForTesting(usedPropertyId);
                Assert.That(graph.ExposedProperties.Count, Is.EqualTo(2));

                panel.DeletePropertyForTesting(usedPropertyId);
                Assert.That(graph.ExposedProperties.Count, Is.EqualTo(1));
                Assert.That(graph.Nodes.Count, Is.EqualTo(0));
                Assert.That(mutationCount, Is.GreaterThanOrEqualTo(4));
            }
            finally
            {
                Object.DestroyImmediate(graph);
            }
        }

        private static DynamicDungeonEditorWindow.PanelViewSettings Clone(
            DynamicDungeonEditorWindow.PanelViewSettings settings)
        {
            return JsonUtility.FromJson<DynamicDungeonEditorWindow.PanelViewSettings>(
                       JsonUtility.ToJson(settings))
                   ?? new DynamicDungeonEditorWindow.PanelViewSettings();
        }
    }
}
