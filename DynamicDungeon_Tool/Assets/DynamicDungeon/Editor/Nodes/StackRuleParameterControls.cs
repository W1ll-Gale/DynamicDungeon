using System;
using System.Collections.Generic;
using System.Globalization;
using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Nodes;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Nodes
{
    internal static class StackRuleParameterControls
    {
        public static VisualElement CreateBiomeOverrideRulesControl(CustomParameterControlContext context)
        {
            return new BiomeOverrideRulesField(context);
        }

        public static VisualElement CreatePlacementSetRulesControl(CustomParameterControlContext context)
        {
            return new PlacementSetRulesField(context);
        }

        public static bool HasMissingBiomeAssets(BiomeOverrideStackRuleSet ruleSet)
        {
            BiomeOverrideStackRule[] rules = ruleSet != null && ruleSet.Rules != null
                ? ruleSet.Rules
                : Array.Empty<BiomeOverrideStackRule>();

            int ruleIndex;
            for (ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
            {
                BiomeOverrideStackRule rule = rules[ruleIndex];
                if (rule != null && rule.Enabled && LoadAsset<BiomeAsset>(rule.OverrideBiome) == null)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool HasMissingPlacementPrefabs(PlacementSetRuleSet ruleSet)
        {
            PlacementSetRule[] rules = ruleSet != null && ruleSet.Rules != null
                ? ruleSet.Rules
                : Array.Empty<PlacementSetRule>();

            int ruleIndex;
            for (ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
            {
                PlacementSetRule rule = rules[ruleIndex];
                if (rule != null && rule.Enabled && LoadAsset<GameObject>(rule.Prefab) == null)
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class BiomeOverrideRulesField : VisualElement, ICustomParameterValueControl
        {
            private readonly string _parameterName;
            private readonly Action<string, string> _onValueChanged;
            private readonly Label _summaryLabel;
            private readonly VisualElement _ruleList;
            private BiomeOverrideStackRuleSet _ruleSet;
            private bool _isRefreshing;

            public BiomeOverrideRulesField(CustomParameterControlContext context)
            {
                _parameterName = context.ParameterName;
                _onValueChanged = context.OnValueChanged;
                _ruleSet = ParseBiomeRules(context.ParameterValue);

                style.flexDirection = FlexDirection.Column;
                style.flexGrow = 1.0f;

                VisualElement header = CreateHeader(context.LabelText, "Add Override", AddRule);
                _summaryLabel = CreateSummaryLabel();
                header.Insert(1, _summaryLabel);
                Add(header);

                _ruleList = CreateList();
                Add(_ruleList);
                Refresh();
            }

            public void SetValueWithoutNotify(string value)
            {
                _ruleSet = ParseBiomeRules(value);
                Refresh();
            }

            private void AddRule()
            {
                List<BiomeOverrideStackRule> rules = GetRules();
                rules.Add(new BiomeOverrideStackRule { Enabled = true, MaskSlot = 1, Probability = 1.0f });
                _ruleSet.Rules = rules.ToArray();
                ApplyChanges();
            }

            private void Refresh()
            {
                _isRefreshing = true;
                _ruleList.Clear();
                BiomeOverrideStackRule[] rules = _ruleSet != null && _ruleSet.Rules != null
                    ? _ruleSet.Rules
                    : Array.Empty<BiomeOverrideStackRule>();

                int ruleIndex;
                for (ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
                {
                    _ruleList.Add(CreateRuleRow(ruleIndex, rules[ruleIndex]));
                }

                if (rules.Length == 0)
                {
                    _ruleList.Add(CreateEmptyLabel("No biome overrides configured."));
                }

                string suffix = HasMissingBiomeAssets(_ruleSet) ? " - missing assets" : string.Empty;
                _summaryLabel.text = rules.Length.ToString(CultureInfo.InvariantCulture) + " ordered overrides" + suffix;
                _summaryLabel.style.color = HasMissingBiomeAssets(_ruleSet)
                    ? new Color(1.0f, 0.48f, 0.32f, 1.0f)
                    : new Color(0.76f, 0.76f, 0.76f, 1.0f);
                _isRefreshing = false;
            }

            private VisualElement CreateRuleRow(int ruleIndex, BiomeOverrideStackRule rule)
            {
                BiomeOverrideStackRule safeRule = rule ?? new BiomeOverrideStackRule();
                VisualElement row = CreateRow();

                row.Add(CreateEnabledToggle(safeRule.Enabled, value =>
                {
                    safeRule.Enabled = value;
                    ApplyChanges();
                }));

                IntegerField maskField = CreateIntegerField("Mask", Mathf.Max(1, safeRule.MaskSlot), 92.0f, value =>
                {
                    safeRule.MaskSlot = Mathf.Max(1, value);
                    ApplyChanges();
                });
                maskField.tooltip = "One-based mask input slot.";
                row.Add(maskField);

                ObjectField biomeField = new ObjectField();
                biomeField.objectType = typeof(BiomeAsset);
                biomeField.allowSceneObjects = false;
                biomeField.style.flexGrow = 1.0f;
                biomeField.style.minWidth = 150.0f;
                biomeField.tooltip = "Biome asset written when the mask row matches.";
                biomeField.SetValueWithoutNotify(LoadAsset<BiomeAsset>(safeRule.OverrideBiome));
                biomeField.RegisterValueChangedCallback(evt =>
                {
                    safeRule.OverrideBiome = GetAssetGuid(evt.newValue);
                    ApplyChanges();
                });
                row.Add(biomeField);

                FloatField probabilityField = CreateFloatField("P", Mathf.Clamp01(safeRule.Probability), 70.0f, value =>
                {
                    safeRule.Probability = Mathf.Clamp01(value);
                    ApplyChanges();
                });
                probabilityField.tooltip = "Probability of applying this override.";
                row.Add(probabilityField);

                AddMoveRemoveButtons(row, ruleIndex, GetRules, rules => _ruleSet.Rules = rules.ToArray(), ApplyChanges);
                return row;
            }

            private List<BiomeOverrideStackRule> GetRules()
            {
                return new List<BiomeOverrideStackRule>(_ruleSet != null && _ruleSet.Rules != null
                    ? _ruleSet.Rules
                    : Array.Empty<BiomeOverrideStackRule>());
            }

            private void ApplyChanges()
            {
                if (_isRefreshing)
                {
                    return;
                }

                _onValueChanged?.Invoke(_parameterName, JsonUtility.ToJson(_ruleSet ?? new BiomeOverrideStackRuleSet()));
                Refresh();
            }
        }

        private sealed class PlacementSetRulesField : VisualElement, ICustomParameterValueControl
        {
            private readonly string _parameterName;
            private readonly Action<string, string> _onValueChanged;
            private readonly Label _summaryLabel;
            private readonly VisualElement _ruleList;
            private PlacementSetRuleSet _ruleSet;
            private bool _isRefreshing;

            public PlacementSetRulesField(CustomParameterControlContext context)
            {
                _parameterName = context.ParameterName;
                _onValueChanged = context.OnValueChanged;
                _ruleSet = ParsePlacementRules(context.ParameterValue);

                style.flexDirection = FlexDirection.Column;
                style.flexGrow = 1.0f;

                VisualElement header = CreateHeader(context.LabelText, "Add Placement", AddRule);
                _summaryLabel = CreateSummaryLabel();
                header.Insert(1, _summaryLabel);
                Add(header);

                _ruleList = CreateList();
                Add(_ruleList);
                Refresh();
            }

            public void SetValueWithoutNotify(string value)
            {
                _ruleSet = ParsePlacementRules(value);
                Refresh();
            }

            private void AddRule()
            {
                List<PlacementSetRule> rules = GetRules();
                rules.Add(new PlacementSetRule { Enabled = true, WeightSlot = 1, Density = 1.0f });
                _ruleSet.Rules = rules.ToArray();
                ApplyChanges();
            }

            private void Refresh()
            {
                _isRefreshing = true;
                _ruleList.Clear();
                PlacementSetRule[] rules = _ruleSet != null && _ruleSet.Rules != null
                    ? _ruleSet.Rules
                    : Array.Empty<PlacementSetRule>();

                int ruleIndex;
                for (ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
                {
                    _ruleList.Add(CreateRuleRow(ruleIndex, rules[ruleIndex]));
                }

                if (rules.Length == 0)
                {
                    _ruleList.Add(CreateEmptyLabel("No placement rows configured."));
                }

                string suffix = HasMissingPlacementPrefabs(_ruleSet) ? " - missing prefabs" : string.Empty;
                _summaryLabel.text = rules.Length.ToString(CultureInfo.InvariantCulture) + " placement rows" + suffix;
                _summaryLabel.style.color = HasMissingPlacementPrefabs(_ruleSet)
                    ? new Color(1.0f, 0.48f, 0.32f, 1.0f)
                    : new Color(0.76f, 0.76f, 0.76f, 1.0f);
                _isRefreshing = false;
            }

            private VisualElement CreateRuleRow(int ruleIndex, PlacementSetRule rule)
            {
                PlacementSetRule safeRule = rule ?? new PlacementSetRule();
                VisualElement block = new VisualElement();
                block.style.flexDirection = FlexDirection.Column;
                block.style.marginBottom = 6.0f;

                VisualElement firstRow = CreateRow();
                firstRow.Add(CreateEnabledToggle(safeRule.Enabled, value =>
                {
                    safeRule.Enabled = value;
                    ApplyChanges();
                }));

                IntegerField weightField = CreateIntegerField("Weight", Mathf.Max(1, safeRule.WeightSlot), 104.0f, value =>
                {
                    safeRule.WeightSlot = Mathf.Max(1, value);
                    ApplyChanges();
                });
                weightField.tooltip = "One-based weight map input slot.";
                firstRow.Add(weightField);

                ObjectField prefabField = new ObjectField();
                prefabField.objectType = typeof(GameObject);
                prefabField.allowSceneObjects = false;
                prefabField.style.flexGrow = 1.0f;
                prefabField.style.minWidth = 150.0f;
                prefabField.tooltip = "Prefab stamp asset.";
                prefabField.SetValueWithoutNotify(LoadAsset<GameObject>(safeRule.Prefab));
                prefabField.RegisterValueChangedCallback(evt =>
                {
                    safeRule.Prefab = GetAssetGuid(evt.newValue);
                    ApplyChanges();
                });
                firstRow.Add(prefabField);

                AddMoveRemoveButtons(firstRow, ruleIndex, GetRules, rules => _ruleSet.Rules = rules.ToArray(), ApplyChanges);
                block.Add(firstRow);

                VisualElement secondRow = CreateRow();
                secondRow.style.marginLeft = 24.0f;
                secondRow.Add(CreateFloatField("Threshold", Mathf.Clamp01(safeRule.Threshold), 118.0f, value =>
                {
                    safeRule.Threshold = Mathf.Clamp01(value);
                    ApplyChanges();
                }));
                secondRow.Add(CreateFloatField("Density", Mathf.Max(0.0f, safeRule.Density), 108.0f, value =>
                {
                    safeRule.Density = Mathf.Max(0.0f, value);
                    ApplyChanges();
                }));
                secondRow.Add(CreateIntegerField("Limit", Mathf.Max(0, safeRule.PointCount), 90.0f, value =>
                {
                    safeRule.PointCount = Mathf.Max(0, value);
                    ApplyChanges();
                }));
                secondRow.Add(CreateIntegerField("X", safeRule.OffsetX, 62.0f, value =>
                {
                    safeRule.OffsetX = value;
                    ApplyChanges();
                }));
                secondRow.Add(CreateIntegerField("Y", safeRule.OffsetY, 62.0f, value =>
                {
                    safeRule.OffsetY = value;
                    ApplyChanges();
                }));
                secondRow.Add(CreateToggle("Mirror X", safeRule.MirrorX, value =>
                {
                    safeRule.MirrorX = value;
                    ApplyChanges();
                }));
                secondRow.Add(CreateToggle("Mirror Y", safeRule.MirrorY, value =>
                {
                    safeRule.MirrorY = value;
                    ApplyChanges();
                }));
                secondRow.Add(CreateToggle("Rotate", safeRule.AllowRotation, value =>
                {
                    safeRule.AllowRotation = value;
                    ApplyChanges();
                }));
                block.Add(secondRow);
                return block;
            }

            private List<PlacementSetRule> GetRules()
            {
                return new List<PlacementSetRule>(_ruleSet != null && _ruleSet.Rules != null
                    ? _ruleSet.Rules
                    : Array.Empty<PlacementSetRule>());
            }

            private void ApplyChanges()
            {
                if (_isRefreshing)
                {
                    return;
                }

                _onValueChanged?.Invoke(_parameterName, JsonUtility.ToJson(_ruleSet ?? new PlacementSetRuleSet()));
                Refresh();
            }
        }

        private static VisualElement CreateHeader(string labelText, string buttonText, Action onAdd)
        {
            VisualElement header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;

            Label label = new Label(labelText);
            label.style.minWidth = 92.0f;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            header.Add(label);

            Button addButton = new Button(onAdd);
            addButton.text = buttonText;
            addButton.style.minWidth = 104.0f;
            header.Add(addButton);
            return header;
        }

        private static Label CreateSummaryLabel()
        {
            Label label = new Label();
            label.style.marginLeft = 6.0f;
            label.style.flexGrow = 1.0f;
            return label;
        }

        private static VisualElement CreateList()
        {
            VisualElement list = new VisualElement();
            list.style.flexDirection = FlexDirection.Column;
            list.style.marginTop = 4.0f;
            return list;
        }

        private static VisualElement CreateRow()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 3.0f;
            row.style.flexGrow = 1.0f;
            return row;
        }

        private static Label CreateEmptyLabel(string text)
        {
            Label label = new Label(text);
            label.style.marginLeft = 98.0f;
            label.style.color = new Color(0.62f, 0.62f, 0.62f, 1.0f);
            return label;
        }

        private static Toggle CreateEnabledToggle(bool value, Action<bool> onChanged)
        {
            Toggle toggle = new Toggle();
            toggle.tooltip = "Enable this row";
            toggle.style.width = 22.0f;
            toggle.SetValueWithoutNotify(value);
            toggle.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
            return toggle;
        }

        private static Toggle CreateToggle(string label, bool value, Action<bool> onChanged)
        {
            Toggle toggle = new Toggle(label);
            toggle.style.marginLeft = 3.0f;
            toggle.SetValueWithoutNotify(value);
            toggle.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
            return toggle;
        }

        private static IntegerField CreateIntegerField(string label, int value, float width, Action<int> onChanged)
        {
            IntegerField field = new IntegerField(label);
            field.style.width = width;
            field.style.minWidth = width;
            field.Query<Label>().ForEach(labelElement => labelElement.style.minWidth = Mathf.Min(58.0f, width * 0.48f));
            field.SetValueWithoutNotify(value);
            field.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
            return field;
        }

        private static FloatField CreateFloatField(string label, float value, float width, Action<float> onChanged)
        {
            FloatField field = new FloatField(label);
            field.style.width = width;
            field.style.minWidth = width;
            field.Query<Label>().ForEach(labelElement => labelElement.style.minWidth = Mathf.Min(64.0f, width * 0.55f));
            field.SetValueWithoutNotify(value);
            field.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
            return field;
        }

        private static void AddMoveRemoveButtons<TRule>(
            VisualElement row,
            int ruleIndex,
            Func<List<TRule>> getRules,
            Action<List<TRule>> setRules,
            Action applyChanges)
        {
            row.Add(CreateIconButton("^", "Move row earlier", () =>
            {
                List<TRule> rules = getRules();
                MoveRule(rules, ruleIndex, -1);
                setRules(rules);
                applyChanges();
            }));
            row.Add(CreateIconButton("v", "Move row later", () =>
            {
                List<TRule> rules = getRules();
                MoveRule(rules, ruleIndex, 1);
                setRules(rules);
                applyChanges();
            }));
            row.Add(CreateIconButton("-", "Remove row", () =>
            {
                List<TRule> rules = getRules();
                if (ruleIndex >= 0 && ruleIndex < rules.Count)
                {
                    rules.RemoveAt(ruleIndex);
                    setRules(rules);
                    applyChanges();
                }
            }));
        }

        private static void MoveRule<TRule>(List<TRule> rules, int ruleIndex, int direction)
        {
            int targetIndex = ruleIndex + direction;
            if (rules == null || ruleIndex < 0 || ruleIndex >= rules.Count || targetIndex < 0 || targetIndex >= rules.Count)
            {
                return;
            }

            TRule rule = rules[ruleIndex];
            rules.RemoveAt(ruleIndex);
            rules.Insert(targetIndex, rule);
        }

        private static Button CreateIconButton(string text, string tooltip, Action onClick)
        {
            Button button = new Button(onClick);
            button.text = text;
            button.tooltip = tooltip;
            button.style.width = 24.0f;
            button.style.minWidth = 24.0f;
            button.style.paddingLeft = 0.0f;
            button.style.paddingRight = 0.0f;
            button.style.marginLeft = 3.0f;
            return button;
        }

        private static BiomeOverrideStackRuleSet ParseBiomeRules(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new BiomeOverrideStackRuleSet();
            }

            try
            {
                return JsonUtility.FromJson<BiomeOverrideStackRuleSet>(value) ?? new BiomeOverrideStackRuleSet();
            }
            catch
            {
                return new BiomeOverrideStackRuleSet();
            }
        }

        private static PlacementSetRuleSet ParsePlacementRules(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new PlacementSetRuleSet();
            }

            try
            {
                return JsonUtility.FromJson<PlacementSetRuleSet>(value) ?? new PlacementSetRuleSet();
            }
            catch
            {
                return new PlacementSetRuleSet();
            }
        }

        private static T LoadAsset<T>(string guid) where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                return null;
            }

            string path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrWhiteSpace(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
        }

        private static string GetAssetGuid(UnityEngine.Object asset)
        {
            if (asset == null)
            {
                return string.Empty;
            }

            string path = AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrWhiteSpace(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
        }
    }
}
