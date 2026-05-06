using System;
using System.Collections.Generic;
using System.Reflection;
using DynamicDungeon.Editor.Nodes;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Nodes;
using DynamicDungeon.Runtime.Semantic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Tests.Editor
{
    public sealed class SpatialQueryEditorTests
    {
        [TearDown]
        public void TearDown()
        {
            ResetRegistryCache();
        }

        [Test]
        public void ContextualQueryConditionsRoundTripPreservesSchemaData()
        {
            string json =
                "{\"Entries\":[" +
                "{\"Offset\":{\"x\":0,\"y\":0},\"MatchById\":true,\"LogicalId\":2,\"TagName\":\"\"}," +
                "{\"Offset\":{\"x\":1,\"y\":0},\"MatchById\":false,\"LogicalId\":0,\"TagName\":\"Water\"}" +
                "]}";

            List<SpatialQueryAuthoringUtility.EditableNeighbourCondition> conditions = SpatialQueryAuthoringUtility.ParseConditions(json);

            Assert.That(conditions, Has.Count.EqualTo(2));
            Assert.That(conditions[0].Offset, Is.EqualTo(Vector2Int.zero));
            Assert.That(conditions[0].MatchById, Is.True);
            Assert.That(conditions[0].LogicalId, Is.EqualTo(2));
            Assert.That(conditions[1].Offset, Is.EqualTo(Vector2Int.right));
            Assert.That(conditions[1].MatchById, Is.False);
            Assert.That(conditions[1].TagName, Is.EqualTo("Water"));

            string reserialisedJson = SpatialQueryAuthoringUtility.SerialiseConditions(conditions);
            List<SpatialQueryAuthoringUtility.EditableNeighbourCondition> reparsedConditions = SpatialQueryAuthoringUtility.ParseConditions(reserialisedJson);

            Assert.That(reparsedConditions, Has.Count.EqualTo(2));
            Assert.That(reparsedConditions[0].Offset, Is.EqualTo(Vector2Int.zero));
            Assert.That(reparsedConditions[0].LogicalId, Is.EqualTo(2));
            Assert.That(reparsedConditions[1].Offset, Is.EqualTo(Vector2Int.right));
            Assert.That(reparsedConditions[1].TagName, Is.EqualTo("Water"));
        }

        [Test]
        public void FourWayPresetCreatesCentreAndCardinalOffsets()
        {
            List<SpatialQueryAuthoringUtility.EditableNeighbourCondition> conditions = SpatialQueryAuthoringUtility.CreatePreset(
                SpatialQueryPreset.FourWaySurrounded,
                true,
                7,
                string.Empty);

            Assert.That(conditions, Has.Count.EqualTo(5));
            CollectionAssert.AreEquivalent(
                new[]
                {
                    Vector2Int.zero,
                    Vector2Int.up,
                    Vector2Int.down,
                    Vector2Int.left,
                    Vector2Int.right
                },
                ExtractOffsets(conditions));

            int index;
            for (index = 0; index < conditions.Count; index++)
            {
                Assert.That(conditions[index].MatchById, Is.True);
                Assert.That(conditions[index].LogicalId, Is.EqualTo(7));
            }
        }

        [Test]
        public void ContextualQueryConditionsUseCustomPopupControl()
        {
            InlinedControlFactory.SetNodeTypeContext(typeof(ContextualQueryNode));
            SerializedParameter parameter = new SerializedParameter("conditions", "{\"Entries\":[]}");

            VisualElement control = InlinedControlFactory.CreateControl(parameter, string.Empty, (_, _) => { });
            Button editButton = FindButtonWithText(control, "Edit Query");

            Assert.That(editButton, Is.Not.Null);
            Assert.That(control.Query<TextField>().ToList().Count, Is.EqualTo(0));
        }

        [Test]
        public void MissingRegistryTagControlShowsFallbackWarning()
        {
            SetRegistryCache(null, true);
            InlinedControlFactory.SetNodeTypeContext(typeof(NeighbourhoodCheckNode));
            SerializedParameter parameter = new SerializedParameter("tagName", "Water");

            VisualElement control = InlinedControlFactory.CreateControl(parameter, string.Empty, (_, _) => { });

            Assert.That(control.Q<HelpBox>(), Is.Not.Null);
        }

        [Test]
        public void RegistryBackedControlsUseFriendlyPopupLabels()
        {
            TileSemanticRegistry registry = ScriptableObject.CreateInstance<TileSemanticRegistry>();

            try
            {
                registry.AllTags.Add("Water");

                TileEntry entry = new TileEntry();
                entry.LogicalId = 2;
                entry.DisplayName = "Wall";
                registry.Entries.Add(entry);

                SetRegistryCache(registry);

                InlinedControlFactory.SetNodeTypeContext(typeof(NeighbourhoodCheckNode));
                VisualElement logicalIdControl = InlinedControlFactory.CreateControl(new SerializedParameter("logicalId", "2"), string.Empty, (_, _) => { });
                VisualElement tagControl = InlinedControlFactory.CreateControl(new SerializedParameter("tagName", "Water"), string.Empty, (_, _) => { });

                Button logicalIdButton = FindButtonWithText(logicalIdControl, "Wall (2)");
                Button tagButton = FindButtonWithText(tagControl, "#Water");

                Assert.That(logicalIdButton, Is.Not.Null);
                Assert.That(tagButton, Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(registry);
            }
        }

        [Test]
        public void NeighbourhoodCheckVisibilitySwitchesBetweenLogicalIdAndTagName()
        {
            NeighbourhoodCheckNode node = new NeighbourhoodCheckNode("query", "Neighbourhood");

            Assert.That(node.IsParameterVisible("logicalId"), Is.True);
            Assert.That(node.IsParameterVisible("tagName"), Is.False);

            node.ReceiveParameter("matchById", "false");

            Assert.That(node.IsParameterVisible("logicalId"), Is.False);
            Assert.That(node.IsParameterVisible("tagName"), Is.True);
        }

        private static List<Vector2Int> ExtractOffsets(IReadOnlyList<SpatialQueryAuthoringUtility.EditableNeighbourCondition> conditions)
        {
            List<Vector2Int> offsets = new List<Vector2Int>(conditions.Count);

            int index;
            for (index = 0; index < conditions.Count; index++)
            {
                offsets.Add(conditions[index].Offset);
            }

            return offsets;
        }

        private static Button FindButtonWithText(VisualElement root, string buttonText)
        {
            List<Button> buttons = root.Query<Button>().ToList();

            int index;
            for (index = 0; index < buttons.Count; index++)
            {
                if (string.Equals(buttons[index].text, buttonText, StringComparison.Ordinal))
                {
                    return buttons[index];
                }
            }

            return null;
        }

        private static void ResetRegistryCache()
        {
            SetRegistryCache(null, false);
        }

        private static void SetRegistryCache(TileSemanticRegistry registry, bool hasAttemptedLoad = true)
        {
            FieldInfo cachedRegistryField = typeof(TileSemanticRegistry).GetField("_cachedRegistry", BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo hasAttemptedLoadField = typeof(TileSemanticRegistry).GetField("_hasAttemptedLoad", BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(cachedRegistryField, Is.Not.Null);
            Assert.That(hasAttemptedLoadField, Is.Not.Null);

            cachedRegistryField.SetValue(null, registry);
            hasAttemptedLoadField.SetValue(null, hasAttemptedLoad);
        }
    }
}
