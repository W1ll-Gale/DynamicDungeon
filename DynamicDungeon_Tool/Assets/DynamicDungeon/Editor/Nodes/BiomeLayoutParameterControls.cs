using System;
using System.Collections.Generic;
using System.Globalization;
using DynamicDungeon.Editor.Utilities;
using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Nodes;
using DynamicDungeon.Runtime.Semantic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Nodes
{
    internal static class BiomeLayoutParameterControls
    {
        public static VisualElement CreateBiomeLayoutRulesControl(CustomParameterControlContext context)
        {
            return new BiomeLayoutRulesField(context);
        }

        public static VisualElement CreateLogicalIdRulesControl(CustomParameterControlContext context)
        {
            return new LogicalIdRulesField(context);
        }

        private sealed class BiomeLayoutRulesField : VisualElement, ICustomParameterValueControl
        {
            private readonly string _parameterName;
            private readonly Action<string, string> _onValueChanged;
            private readonly Label _summaryLabel;
            private readonly VisualElement _entryList;
            private readonly VisualElement _constraintList;

            private BiomeLayoutRules _rules;
            private bool _isRefreshing;

            public BiomeLayoutRulesField(CustomParameterControlContext context)
            {
                _parameterName = context.ParameterName;
                _onValueChanged = context.OnValueChanged;
                _rules = ParseLayoutRules(context.ParameterValue);

                style.flexDirection = FlexDirection.Column;
                style.flexGrow = 1.0f;

                VisualElement header = CreateHeader(context.LabelText, "Add Biome", AddEntry);
                _summaryLabel = new Label();
                _summaryLabel.style.marginLeft = 6.0f;
                _summaryLabel.style.color = new Color(0.76f, 0.76f, 0.76f, 1.0f);
                _summaryLabel.style.flexGrow = 1.0f;
                header.Insert(1, _summaryLabel);
                Add(header);

                _entryList = new VisualElement();
                _entryList.style.flexDirection = FlexDirection.Column;
                _entryList.style.marginTop = 4.0f;
                Add(_entryList);

                VisualElement constraintHeader = CreateHeader("Constraints", "Add Constraint", AddConstraint);
                constraintHeader.style.marginTop = 6.0f;
                Add(constraintHeader);

                _constraintList = new VisualElement();
                _constraintList.style.flexDirection = FlexDirection.Column;
                _constraintList.style.marginTop = 4.0f;
                Add(_constraintList);

                Refresh();
            }

            public void SetValueWithoutNotify(string value)
            {
                _rules = ParseLayoutRules(value);
                Refresh();
            }

            private void AddEntry()
            {
                List<BiomeLayoutEntry> entries = GetEntries();
                entries.Add(new BiomeLayoutEntry
                {
                    Enabled = true,
                    Weight = 1.0f,
                    MinSize = 0,
                    MaxSize = 0
                });
                _rules.Entries = entries.ToArray();
                ApplyChanges();
            }

            private void AddConstraint()
            {
                List<BiomeLayoutConstraint> constraints = GetConstraints();
                constraints.Add(new BiomeLayoutConstraint
                {
                    Enabled = true,
                    Type = BiomeLayoutConstraintType.Required,
                    Size = 0
                });
                _rules.Constraints = constraints.ToArray();
                ApplyChanges();
            }

            private void Refresh()
            {
                _isRefreshing = true;
                _entryList.Clear();
                _constraintList.Clear();

                BiomeLayoutEntry[] entries = _rules != null && _rules.Entries != null ? _rules.Entries : Array.Empty<BiomeLayoutEntry>();
                int entryIndex;
                for (entryIndex = 0; entryIndex < entries.Length; entryIndex++)
                {
                    _entryList.Add(CreateEntryRow(entryIndex, entries[entryIndex]));
                }

                if (entries.Length == 0)
                {
                    _entryList.Add(CreateEmptyLabel("No biome entries. Add at least one weighted biome."));
                }

                BiomeLayoutConstraint[] constraints = _rules != null && _rules.Constraints != null ? _rules.Constraints : Array.Empty<BiomeLayoutConstraint>();
                int constraintIndex;
                for (constraintIndex = 0; constraintIndex < constraints.Length; constraintIndex++)
                {
                    _constraintList.Add(CreateConstraintRow(constraintIndex, constraints[constraintIndex]));
                }

                if (constraints.Length == 0)
                {
                    _constraintList.Add(CreateEmptyLabel("No constraints configured."));
                }

                _summaryLabel.text = entries.Length.ToString(CultureInfo.InvariantCulture) +
                                     " biomes, " +
                                     constraints.Length.ToString(CultureInfo.InvariantCulture) +
                                     " constraints";
                _isRefreshing = false;
            }

            private VisualElement CreateEntryRow(int entryIndex, BiomeLayoutEntry entry)
            {
                BiomeLayoutEntry safeEntry = entry ?? new BiomeLayoutEntry();
                VisualElement row = CreateRow();

                Toggle enabledToggle = new Toggle();
                enabledToggle.tooltip = "Enable this biome entry";
                enabledToggle.style.width = 22.0f;
                enabledToggle.SetValueWithoutNotify(safeEntry.Enabled);
                enabledToggle.RegisterValueChangedCallback(evt =>
                {
                    safeEntry.Enabled = evt.newValue;
                    ApplyChanges();
                });
                row.Add(enabledToggle);

                ObjectField biomeField = new ObjectField();
                biomeField.objectType = typeof(BiomeAsset);
                biomeField.allowSceneObjects = false;
                biomeField.style.flexGrow = 1.0f;
                biomeField.style.minWidth = 150.0f;
                biomeField.SetValueWithoutNotify(LoadAsset(safeEntry.Biome, typeof(BiomeAsset)));
                biomeField.RegisterValueChangedCallback(evt =>
                {
                    safeEntry.Biome = GetAssetGuid(evt.newValue);
                    ApplyChanges();
                });
                row.Add(biomeField);

                FloatField weightField = CreateFloatField("Weight", safeEntry.Weight, 56.0f, value =>
                {
                    safeEntry.Weight = Mathf.Max(0.0f, value);
                    ApplyChanges();
                });
                row.Add(weightField);

                IntegerField minField = CreateIntegerField("Min", safeEntry.MinSize, 48.0f, value =>
                {
                    safeEntry.MinSize = Mathf.Max(0, value);
                    ApplyChanges();
                });
                row.Add(minField);

                IntegerField maxField = CreateIntegerField("Max", safeEntry.MaxSize, 48.0f, value =>
                {
                    safeEntry.MaxSize = Mathf.Max(0, value);
                    ApplyChanges();
                });
                row.Add(maxField);

                Button removeButton = CreateIconButton("-", "Remove biome entry", () =>
                {
                    List<BiomeLayoutEntry> entries = GetEntries();
                    if (entryIndex >= 0 && entryIndex < entries.Count)
                    {
                        entries.RemoveAt(entryIndex);
                        _rules.Entries = entries.ToArray();
                        ApplyChanges();
                    }
                });
                row.Add(removeButton);
                return row;
            }

            private VisualElement CreateConstraintRow(int constraintIndex, BiomeLayoutConstraint constraint)
            {
                BiomeLayoutConstraint safeConstraint = constraint ?? new BiomeLayoutConstraint();
                VisualElement row = CreateRow();

                Toggle enabledToggle = new Toggle();
                enabledToggle.tooltip = "Enable this constraint";
                enabledToggle.style.width = 22.0f;
                enabledToggle.SetValueWithoutNotify(safeConstraint.Enabled);
                enabledToggle.RegisterValueChangedCallback(evt =>
                {
                    safeConstraint.Enabled = evt.newValue;
                    ApplyChanges();
                });
                row.Add(enabledToggle);

                EnumField typeField = new EnumField(safeConstraint.Type);
                typeField.style.width = 126.0f;
                typeField.SetValueWithoutNotify(safeConstraint.Type);
                typeField.RegisterValueChangedCallback(evt =>
                {
                    safeConstraint.Type = (BiomeLayoutConstraintType)evt.newValue;
                    ApplyChanges();
                });
                row.Add(typeField);

                ObjectField biomeField = new ObjectField();
                biomeField.objectType = typeof(BiomeAsset);
                biomeField.allowSceneObjects = false;
                biomeField.style.flexGrow = 1.0f;
                biomeField.style.minWidth = 150.0f;
                biomeField.SetValueWithoutNotify(LoadAsset(safeConstraint.Biome, typeof(BiomeAsset)));
                biomeField.RegisterValueChangedCallback(evt =>
                {
                    safeConstraint.Biome = GetAssetGuid(evt.newValue);
                    ApplyChanges();
                });
                row.Add(biomeField);

                IntegerField sizeField = CreateIntegerField("Size", safeConstraint.Size, 52.0f, value =>
                {
                    safeConstraint.Size = Mathf.Max(0, value);
                    ApplyChanges();
                });
                row.Add(sizeField);

                Button removeButton = CreateIconButton("-", "Remove constraint", () =>
                {
                    List<BiomeLayoutConstraint> constraints = GetConstraints();
                    if (constraintIndex >= 0 && constraintIndex < constraints.Count)
                    {
                        constraints.RemoveAt(constraintIndex);
                        _rules.Constraints = constraints.ToArray();
                        ApplyChanges();
                    }
                });
                row.Add(removeButton);
                return row;
            }

            private List<BiomeLayoutEntry> GetEntries()
            {
                return new List<BiomeLayoutEntry>(_rules != null && _rules.Entries != null ? _rules.Entries : Array.Empty<BiomeLayoutEntry>());
            }

            private List<BiomeLayoutConstraint> GetConstraints()
            {
                return new List<BiomeLayoutConstraint>(_rules != null && _rules.Constraints != null ? _rules.Constraints : Array.Empty<BiomeLayoutConstraint>());
            }

            private void ApplyChanges()
            {
                if (_isRefreshing)
                {
                    return;
                }

                string value = JsonUtility.ToJson(_rules ?? new BiomeLayoutRules());
                _onValueChanged?.Invoke(_parameterName, value);
                Refresh();
            }
        }

        private sealed class LogicalIdRulesField : VisualElement, ICustomParameterValueControl
        {
            private readonly string _parameterName;
            private readonly Action<string, string> _onValueChanged;
            private readonly Label _summaryLabel;
            private readonly VisualElement _ruleList;

            private LogicalIdRuleSet _ruleSet;
            private bool _isRefreshing;

            public LogicalIdRulesField(CustomParameterControlContext context)
            {
                _parameterName = context.ParameterName;
                _onValueChanged = context.OnValueChanged;
                _ruleSet = ParseLogicalRules(context.ParameterValue);

                style.flexDirection = FlexDirection.Column;
                style.flexGrow = 1.0f;

                VisualElement header = CreateHeader(context.LabelText, "Add Rule", AddRule);
                _summaryLabel = new Label();
                _summaryLabel.style.marginLeft = 6.0f;
                _summaryLabel.style.color = new Color(0.76f, 0.76f, 0.76f, 1.0f);
                _summaryLabel.style.flexGrow = 1.0f;
                header.Insert(1, _summaryLabel);
                Add(header);

                _ruleList = new VisualElement();
                _ruleList.style.flexDirection = FlexDirection.Column;
                _ruleList.style.marginTop = 4.0f;
                Add(_ruleList);

                Refresh();
            }

            public void SetValueWithoutNotify(string value)
            {
                _ruleSet = ParseLogicalRules(value);
                Refresh();
            }

            private void AddRule()
            {
                List<LogicalIdRule> rules = GetRules();
                rules.Add(new LogicalIdRule
                {
                    Enabled = true,
                    MaskSlot = 0,
                    SourceLogicalId = -1,
                    TargetLogicalId = 1
                });
                _ruleSet.Rules = rules.ToArray();
                ApplyChanges();
            }

            private void Refresh()
            {
                _isRefreshing = true;
                _ruleList.Clear();

                LogicalIdRule[] rules = _ruleSet != null && _ruleSet.Rules != null ? _ruleSet.Rules : Array.Empty<LogicalIdRule>();
                int ruleIndex;
                for (ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
                {
                    _ruleList.Add(CreateRuleRow(ruleIndex, rules[ruleIndex]));
                }

                if (rules.Length == 0)
                {
                    _ruleList.Add(CreateEmptyLabel("No rewrite rules configured."));
                }

                _summaryLabel.text = rules.Length.ToString(CultureInfo.InvariantCulture) + " ordered rules";
                _isRefreshing = false;
            }

            private VisualElement CreateRuleRow(int ruleIndex, LogicalIdRule rule)
            {
                LogicalIdRule safeRule = rule ?? new LogicalIdRule();
                TileSemanticRegistry registry = TileSemanticRegistry.GetOrLoad();
                VisualElement row = CreateRow();

                Toggle enabledToggle = new Toggle();
                enabledToggle.tooltip = "Enable this rule";
                enabledToggle.style.width = 22.0f;
                enabledToggle.SetValueWithoutNotify(safeRule.Enabled);
                enabledToggle.RegisterValueChangedCallback(evt =>
                {
                    safeRule.Enabled = evt.newValue;
                    ApplyChanges();
                });
                row.Add(enabledToggle);

                IntegerField maskField = CreateIntegerField("Mask", Mathf.Clamp(safeRule.MaskSlot, 0, 4), 48.0f, value =>
                {
                    safeRule.MaskSlot = Mathf.Clamp(value, 0, 4);
                    ApplyChanges();
                });
                maskField.tooltip = "0 means always. 1-4 use the matching mask input slot.";
                row.Add(maskField);

                Button sourceButton = null;
                sourceButton = new Button(() => OpenLogicalIdPopup(sourceButton, safeRule, true));
                sourceButton.style.minWidth = 126.0f;
                sourceButton.style.flexGrow = 1.0f;
                sourceButton.tooltip = "Source logical ID. Any matches whatever the current value is.";
                sourceButton.text = BuildSourceLabel(safeRule.SourceLogicalId, registry);
                row.Add(sourceButton);

                Label arrow = new Label("->");
                arrow.style.unityTextAlign = TextAnchor.MiddleCenter;
                arrow.style.width = 24.0f;
                row.Add(arrow);

                Button targetButton = null;
                targetButton = new Button(() => OpenLogicalIdPopup(targetButton, safeRule, false));
                targetButton.style.minWidth = 126.0f;
                targetButton.style.flexGrow = 1.0f;
                targetButton.tooltip = "Target logical ID written when this rule matches.";
                targetButton.text = BuildLogicalIdLabel(safeRule.TargetLogicalId, registry);
                row.Add(targetButton);

                Button upButton = CreateIconButton("^", "Move rule earlier", () => MoveRule(ruleIndex, -1));
                row.Add(upButton);

                Button downButton = CreateIconButton("v", "Move rule later", () => MoveRule(ruleIndex, 1));
                row.Add(downButton);

                Button removeButton = CreateIconButton("-", "Remove rule", () =>
                {
                    List<LogicalIdRule> rules = GetRules();
                    if (ruleIndex >= 0 && ruleIndex < rules.Count)
                    {
                        rules.RemoveAt(ruleIndex);
                        _ruleSet.Rules = rules.ToArray();
                        ApplyChanges();
                    }
                });
                row.Add(removeButton);

                return row;
            }

            private void OpenLogicalIdPopup(Button sourceButton, LogicalIdRule rule, bool isSource)
            {
                if (sourceButton == null || rule == null)
                {
                    return;
                }

                TileSemanticRegistry registry = TileSemanticRegistry.GetOrLoad();
                if (isSource)
                {
                    GenericMenu menu = new GenericMenu();
                    menu.AddItem(new GUIContent("Any"), rule.SourceLogicalId < 0, () =>
                    {
                        rule.SourceLogicalId = -1;
                        ApplyChanges();
                    });
                    menu.AddSeparator(string.Empty);
                    AddLogicalIdMenuItems(menu, registry, rule.SourceLogicalId, selected =>
                    {
                        rule.SourceLogicalId = Mathf.Max(0, selected);
                        ApplyChanges();
                    });
                    menu.DropDown(sourceButton.worldBound);
                    return;
                }

                if (registry != null && registry.Entries != null && registry.Entries.Count > 0)
                {
                    RegistryDropdown.ShowLogicalIdPopup(sourceButton.worldBound, rule.TargetLogicalId, registry, selected =>
                    {
                        rule.TargetLogicalId = Mathf.Max(0, selected);
                        ApplyChanges();
                    });
                    return;
                }

                GenericMenu fallbackMenu = new GenericMenu();
                int fallbackValue = Mathf.Max(0, rule.TargetLogicalId);
                fallbackMenu.AddDisabledItem(new GUIContent("Registry unavailable"));
                fallbackMenu.AddItem(new GUIContent("Keep " + fallbackValue.ToString(CultureInfo.InvariantCulture)), true, () => { });
                fallbackMenu.DropDown(sourceButton.worldBound);
            }

            private void MoveRule(int ruleIndex, int direction)
            {
                List<LogicalIdRule> rules = GetRules();
                int targetIndex = ruleIndex + direction;
                if (ruleIndex < 0 || ruleIndex >= rules.Count || targetIndex < 0 || targetIndex >= rules.Count)
                {
                    return;
                }

                LogicalIdRule rule = rules[ruleIndex];
                rules.RemoveAt(ruleIndex);
                rules.Insert(targetIndex, rule);
                _ruleSet.Rules = rules.ToArray();
                ApplyChanges();
            }

            private List<LogicalIdRule> GetRules()
            {
                return new List<LogicalIdRule>(_ruleSet != null && _ruleSet.Rules != null ? _ruleSet.Rules : Array.Empty<LogicalIdRule>());
            }

            private void ApplyChanges()
            {
                if (_isRefreshing)
                {
                    return;
                }

                string value = JsonUtility.ToJson(_ruleSet ?? new LogicalIdRuleSet());
                _onValueChanged?.Invoke(_parameterName, value);
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
            addButton.style.minWidth = 92.0f;
            header.Add(addButton);
            return header;
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

        private static IntegerField CreateIntegerField(string label, int value, float width, Action<int> onChanged)
        {
            IntegerField field = new IntegerField(label);
            field.style.width = width + 42.0f;
            field.style.minWidth = width + 42.0f;
            field.Query<Label>().ForEach(labelElement => labelElement.style.minWidth = 32.0f);
            field.SetValueWithoutNotify(value);
            field.RegisterValueChangedCallback(evt => onChanged?.Invoke(evt.newValue));
            return field;
        }

        private static FloatField CreateFloatField(string label, float value, float width, Action<float> onChanged)
        {
            FloatField field = new FloatField(label);
            field.style.width = width + 52.0f;
            field.style.minWidth = width + 52.0f;
            field.Query<Label>().ForEach(labelElement => labelElement.style.minWidth = 44.0f);
            field.SetValueWithoutNotify(value);
            field.RegisterValueChangedCallback(evt => onChanged?.Invoke(evt.newValue));
            return field;
        }

        private static BiomeLayoutRules ParseLayoutRules(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new BiomeLayoutRules();
            }

            try
            {
                return JsonUtility.FromJson<BiomeLayoutRules>(value) ?? new BiomeLayoutRules();
            }
            catch
            {
                return new BiomeLayoutRules();
            }
        }

        private static LogicalIdRuleSet ParseLogicalRules(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new LogicalIdRuleSet();
            }

            try
            {
                return JsonUtility.FromJson<LogicalIdRuleSet>(value) ?? new LogicalIdRuleSet();
            }
            catch
            {
                return new LogicalIdRuleSet();
            }
        }

        private static UnityEngine.Object LoadAsset(string guid, Type assetType)
        {
            if (string.IsNullOrWhiteSpace(guid) || assetType == null)
            {
                return null;
            }

            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath(path, assetType);
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

        private static string BuildSourceLabel(int sourceLogicalId, TileSemanticRegistry registry)
        {
            return sourceLogicalId < 0 ? "Any" : BuildLogicalIdLabel(sourceLogicalId, registry);
        }

        private static string BuildLogicalIdLabel(int logicalId, TileSemanticRegistry registry)
        {
            if (logicalId < 0)
            {
                return "Any";
            }

            return RegistryDropdown.BuildLogicalIdLabel((ushort)Mathf.Clamp(logicalId, 0, ushort.MaxValue), registry);
        }

        private static void AddLogicalIdMenuItems(GenericMenu menu, TileSemanticRegistry registry, int currentLogicalId, Action<int> onSelect)
        {
            if (registry == null || registry.Entries == null || registry.Entries.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("Registry unavailable"));
                return;
            }

            List<TileEntry> entries = new List<TileEntry>(registry.Entries);
            entries.Sort((left, right) => left.LogicalId.CompareTo(right.LogicalId));
            int index;
            for (index = 0; index < entries.Count; index++)
            {
                TileEntry entry = entries[index];
                if (entry == null)
                {
                    continue;
                }

                int logicalId = entry.LogicalId;
                string label = BuildLogicalIdLabel(logicalId, registry);
                menu.AddItem(new GUIContent(label), currentLogicalId == logicalId, () => onSelect?.Invoke(logicalId));
            }
        }
    }
}
