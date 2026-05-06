using System;
using System.Collections.Generic;
using System.Globalization;
using DynamicDungeon.Editor.Utilities;
using DynamicDungeon.Runtime.Semantic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Nodes
{
    internal static class SpatialQueryParameterControls
    {
        public static VisualElement CreateContextualQueryConditionsControl(CustomParameterControlContext context)
        {
            return new ContextualQueryConditionsField(context);
        }

        public static VisualElement CreateNeighbourhoodLogicalIdControl(CustomParameterControlContext context)
        {
            return new LogicalIdPickerField(context);
        }

        public static VisualElement CreateNeighbourhoodTagControl(CustomParameterControlContext context)
        {
            return new TagPickerField(context);
        }

        private sealed class ContextualQueryConditionsField : VisualElement, ICustomParameterValueControl
        {
            private readonly string _parameterName;
            private readonly Action<string, string> _onValueChanged;
            private readonly Label _summaryLabel;
            private readonly HelpBox _warningBox;
            private readonly Button _editButton;

            private string _value;

            public ContextualQueryConditionsField(CustomParameterControlContext context)
            {
                _parameterName = context.ParameterName;
                _onValueChanged = context.OnValueChanged;
                _value = context.ParameterValue ?? string.Empty;

                style.flexDirection = FlexDirection.Column;
                style.flexGrow = 1.0f;

                VisualElement topRow = new VisualElement();
                topRow.style.flexDirection = FlexDirection.Row;
                topRow.style.alignItems = Align.Center;

                Label label = new Label(context.LabelText);
                label.style.minWidth = 92.0f;
                label.style.marginRight = 6.0f;
                label.style.unityTextAlign = TextAnchor.MiddleLeft;
                topRow.Add(label);

                _summaryLabel = new Label();
                _summaryLabel.style.flexGrow = 1.0f;
                _summaryLabel.style.flexShrink = 1.0f;
                _summaryLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
                _summaryLabel.style.color = new Color(0.85f, 0.85f, 0.85f, 1.0f);
                topRow.Add(_summaryLabel);

                _editButton = new Button(OpenEditorPopup);
                _editButton.text = "Edit Query";
                _editButton.style.marginLeft = 6.0f;
                topRow.Add(_editButton);

                _warningBox = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
                _warningBox.style.marginTop = 4.0f;

                Add(topRow);
                Add(_warningBox);

                RefreshVisuals();
            }

            public void SetValueWithoutNotify(string value)
            {
                _value = value ?? string.Empty;
                RefreshVisuals();
            }

            private void OpenEditorPopup()
            {
                Rect popupRect = _editButton.worldBound;
                UnityEditor.PopupWindow.Show(
                    popupRect,
                    new ContextualQueryPopup(
                        _value,
                        updatedValue =>
                        {
                            _value = updatedValue ?? string.Empty;
                            RefreshVisuals();
                            _onValueChanged?.Invoke(_parameterName, _value);
                        }));
            }

            private void RefreshVisuals()
            {
                TileSemanticRegistry registry = TileSemanticRegistry.GetOrLoad();
                List<SpatialQueryAuthoringUtility.EditableNeighbourCondition> conditions = SpatialQueryAuthoringUtility.ParseConditions(_value);
                _summaryLabel.text = SpatialQueryAuthoringUtility.BuildSummary(conditions, registry);

                bool showWarning = registry == null;
                _warningBox.style.display = showWarning ? DisplayStyle.Flex : DisplayStyle.None;
                if (showWarning)
                {
                    _warningBox.text = "Tile semantic registry unavailable. Tag conditions can still be edited manually.";
                }
            }
        }

        private sealed class LogicalIdPickerField : VisualElement, ICustomParameterValueControl
        {
            private readonly string _parameterName;
            private readonly Action<string, string> _onValueChanged;
            private readonly Label _label;
            private readonly Button _button;
            private readonly IntegerField _integerField;

            private int _logicalId;

            public LogicalIdPickerField(CustomParameterControlContext context)
            {
                _parameterName = context.ParameterName;
                _onValueChanged = context.OnValueChanged;
                _logicalId = ParseInt(context.ParameterValue);

                style.flexDirection = FlexDirection.Row;
                style.alignItems = Align.Center;
                style.flexGrow = 1.0f;

                _label = new Label(context.LabelText);
                _label.style.minWidth = 92.0f;
                _label.style.marginRight = 6.0f;
                _label.style.unityTextAlign = TextAnchor.MiddleLeft;
                Add(_label);

                TileSemanticRegistry registry = TileSemanticRegistry.GetOrLoad();
                if (registry != null && registry.Entries != null && registry.Entries.Count > 0)
                {
                    _button = new Button(() => OpenLogicalIdPopup(registry));
                    _button.style.flexGrow = 1.0f;
                    _button.style.flexShrink = 1.0f;
                    Add(_button);
                    RefreshButtonLabel(registry);
                    return;
                }

                _integerField = new IntegerField();
                _integerField.style.flexGrow = 1.0f;
                _integerField.SetValueWithoutNotify(_logicalId);
                _integerField.RegisterValueChangedCallback(
                    changeEvent =>
                    {
                        _logicalId = Mathf.Max(0, changeEvent.newValue);
                        _integerField.SetValueWithoutNotify(_logicalId);
                        _onValueChanged?.Invoke(_parameterName, _logicalId.ToString(CultureInfo.InvariantCulture));
                    });
                Add(_integerField);
            }

            public void SetValueWithoutNotify(string value)
            {
                _logicalId = ParseInt(value);
                if (_button != null)
                {
                    RefreshButtonLabel(TileSemanticRegistry.GetOrLoad());
                }

                if (_integerField != null)
                {
                    _integerField.SetValueWithoutNotify(_logicalId);
                }
            }

            private void OpenLogicalIdPopup(TileSemanticRegistry registry)
            {
                RegistryDropdown.ShowLogicalIdPopup(_button.worldBound, _logicalId, registry, selectedLogicalId =>
                {
                    _logicalId = Mathf.Max(0, selectedLogicalId);
                    RefreshButtonLabel(registry);
                    _onValueChanged?.Invoke(_parameterName, _logicalId.ToString(CultureInfo.InvariantCulture));
                });
            }

            private void RefreshButtonLabel(TileSemanticRegistry registry)
            {
                if (_button != null)
                {
                    _button.text = SpatialQueryAuthoringUtility.BuildLogicalIdLabel(_logicalId, registry);
                }
            }
        }

        private sealed class TagPickerField : VisualElement, ICustomParameterValueControl
        {
            private readonly string _parameterName;
            private readonly Action<string, string> _onValueChanged;
            private readonly Label _label;
            private readonly Button _button;
            private readonly TextField _textField;
            private readonly HelpBox _warningBox;

            private string _tagName;

            public TagPickerField(CustomParameterControlContext context)
            {
                _parameterName = context.ParameterName;
                _onValueChanged = context.OnValueChanged;
                _tagName = context.ParameterValue ?? string.Empty;

                style.flexDirection = FlexDirection.Column;
                style.flexGrow = 1.0f;

                VisualElement row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;

                _label = new Label(context.LabelText);
                _label.style.minWidth = 92.0f;
                _label.style.marginRight = 6.0f;
                _label.style.unityTextAlign = TextAnchor.MiddleLeft;
                row.Add(_label);

                TileSemanticRegistry registry = TileSemanticRegistry.GetOrLoad();
                if (registry != null && registry.AllTags != null && registry.AllTags.Count > 0)
                {
                    _button = new Button(() => OpenTagPopup(registry));
                    _button.style.flexGrow = 1.0f;
                    _button.style.flexShrink = 1.0f;
                    row.Add(_button);
                    Add(row);
                    RefreshButtonLabel();
                    return;
                }

                _textField = new TextField();
                _textField.style.flexGrow = 1.0f;
                _textField.SetValueWithoutNotify(_tagName);
                _textField.RegisterValueChangedCallback(
                    changeEvent =>
                    {
                        _tagName = changeEvent.newValue ?? string.Empty;
                        _onValueChanged?.Invoke(_parameterName, _tagName);
                    });
                row.Add(_textField);
                Add(row);

                _warningBox = new HelpBox("Tile semantic registry unavailable. Enter tag names manually.", HelpBoxMessageType.Warning);
                _warningBox.style.marginTop = 4.0f;
                Add(_warningBox);
            }

            public void SetValueWithoutNotify(string value)
            {
                _tagName = value ?? string.Empty;

                if (_button != null)
                {
                    RefreshButtonLabel();
                }

                if (_textField != null)
                {
                    _textField.SetValueWithoutNotify(_tagName);
                }
            }

            private void OpenTagPopup(TileSemanticRegistry registry)
            {
                RegistryDropdown.ShowTagPopup(_button.worldBound, _tagName, registry, selectedTag =>
                {
                    _tagName = selectedTag ?? string.Empty;
                    RefreshButtonLabel();
                    _onValueChanged?.Invoke(_parameterName, _tagName);
                });
            }

            private void RefreshButtonLabel()
            {
                if (_button != null)
                {
                    _button.text = string.IsNullOrWhiteSpace(_tagName) ? "Select Tag" : "#" + _tagName;
                }
            }
        }

        private sealed class ContextualQueryPopup : PopupWindowContent
        {
            private readonly Action<string> _onChanged;

            private List<SpatialQueryAuthoringUtility.EditableNeighbourCondition> _conditions;
            private int _selectedConditionIndex;
            private Vector2 _scrollPosition;
            private bool _presetMatchById;
            private int _presetLogicalId;
            private string _presetTagName;

            public ContextualQueryPopup(string serialisedConditions, Action<string> onChanged)
            {
                _onChanged = onChanged;
                _conditions = SpatialQueryAuthoringUtility.ParseConditions(serialisedConditions);
                _selectedConditionIndex = _conditions.Count > 0 ? 0 : -1;
                _presetMatchById = true;
                _presetTagName = string.Empty;

                if (_conditions.Count > 0)
                {
                    SpatialQueryAuthoringUtility.EditableNeighbourCondition firstCondition = _conditions[0];
                    _presetMatchById = firstCondition.MatchById;
                    _presetLogicalId = firstCondition.LogicalId;
                    _presetTagName = firstCondition.TagName ?? string.Empty;
                }
            }

            public override Vector2 GetWindowSize()
            {
                return new Vector2(680.0f, 560.0f);
            }

            public override void OnGUI(Rect rect)
            {
                TileSemanticRegistry registry = TileSemanticRegistry.GetOrLoad();

                Rect contentRect = new Rect(rect.x + 8.0f, rect.y + 8.0f, rect.width - 16.0f, rect.height - 16.0f);
                GUILayout.BeginArea(contentRect);
                EditorGUILayout.LabelField("Contextual Query Builder", EditorStyles.boldLabel);
                EditorGUILayout.Space(2.0f);
                DrawWarning(registry);
                DrawPresetSection(registry);
                EditorGUILayout.Space(6.0f);
                DrawToolbar();
                EditorGUILayout.Space(4.0f);
                DrawConditionList(registry);
                GUILayout.EndArea();
            }

            private void DrawWarning(TileSemanticRegistry registry)
            {
                if (registry == null)
                {
                    EditorGUILayout.HelpBox("Tile semantic registry unavailable. Tag matchers will use manual text entry.", MessageType.Warning);
                }
            }

            private void DrawPresetSection(TileSemanticRegistry registry)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Pattern Presets", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Choose a matcher once, then stamp it onto a common spatial pattern.", EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(2.0f);

                int selectedMode = _presetMatchById ? 0 : 1;
                int updatedMode = GUILayout.Toolbar(selectedMode, new[] { "Logical ID", "Tag" });
                bool updatedMatchById = updatedMode == 0;
                if (updatedMatchById != _presetMatchById)
                {
                    _presetMatchById = updatedMatchById;
                    editorWindow?.Repaint();
                }

                if (_presetMatchById)
                {
                    DrawLogicalIdSelector(_presetLogicalId, registry, "Preset Matcher", selectedLogicalId =>
                    {
                        int clampedLogicalId = Mathf.Max(0, selectedLogicalId);
                        if (_presetLogicalId != clampedLogicalId)
                        {
                            _presetLogicalId = clampedLogicalId;
                            editorWindow?.Repaint();
                        }
                    });
                }
                else
                {
                    DrawTagSelector(_presetTagName, registry, "Preset Matcher", selectedTag =>
                    {
                        string updatedTagName = selectedTag ?? string.Empty;
                        if (!string.Equals(_presetTagName, updatedTagName, StringComparison.Ordinal))
                        {
                            _presetTagName = updatedTagName;
                            editorWindow?.Repaint();
                        }
                    });
                }

                EditorGUILayout.Space(4.0f);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Exact Tile", GUILayout.Height(24.0f)))
                {
                    ApplyPreset(SpatialQueryPreset.ExactTile);
                }

                if (GUILayout.Button("4-Way Surrounded", GUILayout.Height(24.0f)))
                {
                    ApplyPreset(SpatialQueryPreset.FourWaySurrounded);
                }

                if (GUILayout.Button("8-Way Surrounded", GUILayout.Height(24.0f)))
                {
                    ApplyPreset(SpatialQueryPreset.EightWaySurrounded);
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Horizontal Run", GUILayout.Height(24.0f)))
                {
                    ApplyPreset(SpatialQueryPreset.HorizontalRun);
                }

                if (GUILayout.Button("Vertical Run", GUILayout.Height(24.0f)))
                {
                    ApplyPreset(SpatialQueryPreset.VerticalRun);
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            private void DrawToolbar()
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField("Condition Actions", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("Add Condition", GUILayout.Height(24.0f)))
                {
                    _conditions.Add(new SpatialQueryAuthoringUtility.EditableNeighbourCondition());
                    _selectedConditionIndex = _conditions.Count - 1;
                    ApplyChanges();
                }

                using (new EditorGUI.DisabledScope(_selectedConditionIndex < 0 || _selectedConditionIndex >= _conditions.Count))
                {
                    if (GUILayout.Button("Duplicate Selected", GUILayout.Height(24.0f)))
                    {
                        SpatialQueryAuthoringUtility.EditableNeighbourCondition sourceCondition = _conditions[_selectedConditionIndex];
                        _conditions.Insert(_selectedConditionIndex + 1, sourceCondition.Clone());
                        _selectedConditionIndex++;
                        ApplyChanges();
                    }
                }

                if (GUILayout.Button("Clear All", GUILayout.Height(24.0f)))
                {
                    _conditions.Clear();
                    _selectedConditionIndex = -1;
                    ApplyChanges();
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            private void DrawConditionList(TileSemanticRegistry registry)
            {
                EditorGUILayout.LabelField("Conditions", EditorStyles.boldLabel);

                if (_conditions.Count == 0)
                {
                    EditorGUILayout.HelpBox("No conditions configured. Add a condition or apply a preset.", MessageType.Info);
                    return;
                }

                _scrollPosition = GUILayout.BeginScrollView(
                    _scrollPosition,
                    false,
                    true,
                    GUIStyle.none,
                    GUI.skin.verticalScrollbar);

                int conditionIndex;
                for (conditionIndex = 0; conditionIndex < _conditions.Count; conditionIndex++)
                {
                    DrawConditionRow(conditionIndex, registry);
                    GUILayout.Space(4.0f);
                }

                GUILayout.EndScrollView();
            }

            private void DrawConditionRow(int conditionIndex, TileSemanticRegistry registry)
            {
                SpatialQueryAuthoringUtility.EditableNeighbourCondition condition = _conditions[conditionIndex];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                bool isSelected = _selectedConditionIndex == conditionIndex;
                bool newIsSelected = GUILayout.Toggle(isSelected, string.Empty, GUILayout.Width(18.0f));
                if (newIsSelected && !isSelected)
                {
                    _selectedConditionIndex = conditionIndex;
                }

                EditorGUILayout.LabelField("Condition " + (conditionIndex + 1).ToString(CultureInfo.InvariantCulture), EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Remove", GUILayout.Width(76.0f)))
                {
                    _conditions.RemoveAt(conditionIndex);
                    if (_selectedConditionIndex >= _conditions.Count)
                    {
                        _selectedConditionIndex = _conditions.Count - 1;
                    }

                    ApplyChanges();
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    return;
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField(BuildConditionSummary(condition, registry), EditorStyles.miniLabel);

                EditorGUILayout.Space(2.0f);
                EditorGUILayout.LabelField("Offset", EditorStyles.miniBoldLabel);
                Vector2Int updatedOffset = DrawOffsetFields(condition.Offset);
                if (updatedOffset != condition.Offset)
                {
                    condition.Offset = updatedOffset;
                    ApplyChanges();
                }

                EditorGUILayout.Space(2.0f);
                EditorGUILayout.LabelField("Match Mode", EditorStyles.miniBoldLabel);
                int selectedMode = condition.MatchById ? 0 : 1;
                int updatedMode = GUILayout.Toolbar(selectedMode, new[] { "Logical ID", "Tag" });
                bool updatedMatchById = updatedMode == 0;
                if (updatedMatchById != condition.MatchById)
                {
                    condition.MatchById = updatedMatchById;
                    ApplyChanges();
                }

                EditorGUILayout.Space(2.0f);
                EditorGUILayout.LabelField("Matcher", EditorStyles.miniBoldLabel);
                if (condition.MatchById)
                {
                    DrawLogicalIdSelector(condition.LogicalId, registry, string.Empty, selectedLogicalId =>
                    {
                        int clampedLogicalId = Mathf.Max(0, selectedLogicalId);
                        if (condition.LogicalId != clampedLogicalId)
                        {
                            condition.LogicalId = clampedLogicalId;
                            ApplyChanges();
                        }
                    });
                }
                else
                {
                    DrawTagSelector(condition.TagName, registry, string.Empty, selectedTag =>
                    {
                        string updatedTagName = selectedTag ?? string.Empty;
                        if (!string.Equals(condition.TagName ?? string.Empty, updatedTagName, StringComparison.Ordinal))
                        {
                            condition.TagName = updatedTagName;
                            ApplyChanges();
                        }
                    });
                }

                EditorGUILayout.EndVertical();
            }

            private void ApplyPreset(SpatialQueryPreset preset)
            {
                _conditions = SpatialQueryAuthoringUtility.CreatePreset(preset, _presetMatchById, _presetLogicalId, _presetTagName);
                _selectedConditionIndex = _conditions.Count > 0 ? 0 : -1;
                ApplyChanges();
            }

            private void ApplyChanges()
            {
                _onChanged?.Invoke(SpatialQueryAuthoringUtility.SerialiseConditions(_conditions));
                editorWindow?.Repaint();
            }

            private static void DrawLogicalIdSelector(int logicalId, TileSemanticRegistry registry, string label, Action<int> onChanged)
            {
                if (registry != null && registry.Entries != null && registry.Entries.Count > 0)
                {
                    Rect popupRect = EditorGUILayout.GetControlRect();
                    Rect buttonRect = string.IsNullOrEmpty(label) ? popupRect : EditorGUI.PrefixLabel(popupRect, new GUIContent(label));
                    string buttonLabel = SpatialQueryAuthoringUtility.BuildLogicalIdLabel(logicalId, registry);
                    if (GUI.Button(buttonRect, buttonLabel, EditorStyles.popup))
                    {
                        RegistryDropdown.ShowLogicalIdPopup(buttonRect, logicalId, registry, selectedLogicalId => onChanged?.Invoke(selectedLogicalId));
                    }

                    return;
                }

                int updatedLogicalId = Mathf.Max(0, EditorGUILayout.DelayedIntField(label, logicalId));
                if (updatedLogicalId != logicalId)
                {
                    onChanged?.Invoke(updatedLogicalId);
                }
            }

            private static void DrawTagSelector(string tagName, TileSemanticRegistry registry, string label, Action<string> onChanged)
            {
                string currentTagName = tagName ?? string.Empty;
                if (registry != null && registry.AllTags != null && registry.AllTags.Count > 0)
                {
                    Rect popupRect = EditorGUILayout.GetControlRect();
                    Rect buttonRect = string.IsNullOrEmpty(label) ? popupRect : EditorGUI.PrefixLabel(popupRect, new GUIContent(label));
                    string buttonLabel = string.IsNullOrWhiteSpace(currentTagName) ? "Select Tag" : "#" + currentTagName;
                    if (GUI.Button(buttonRect, buttonLabel, EditorStyles.popup))
                    {
                        RegistryDropdown.ShowTagPopup(buttonRect, currentTagName, registry, selectedTag => onChanged?.Invoke(selectedTag ?? string.Empty));
                    }

                    return;
                }

                string updatedTagName = EditorGUILayout.DelayedTextField(label, currentTagName);
                if (!string.Equals(updatedTagName, currentTagName, StringComparison.Ordinal))
                {
                    onChanged?.Invoke(updatedTagName);
                }
            }

            private static Vector2Int DrawOffsetFields(Vector2Int offset)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label("X", GUILayout.Width(14.0f));
                int updatedX = EditorGUILayout.DelayedIntField(offset.x, GUILayout.Width(72.0f));
                GUILayout.Space(8.0f);
                GUILayout.Label("Y", GUILayout.Width(14.0f));
                int updatedY = EditorGUILayout.DelayedIntField(offset.y, GUILayout.Width(72.0f));
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                return new Vector2Int(updatedX, updatedY);
            }

            private static string BuildConditionSummary(SpatialQueryAuthoringUtility.EditableNeighbourCondition condition, TileSemanticRegistry registry)
            {
                string matcherSummary = condition.MatchById
                    ? SpatialQueryAuthoringUtility.BuildLogicalIdLabel(condition.LogicalId, registry)
                    : string.IsNullOrWhiteSpace(condition.TagName) ? "Any Tag" : "#" + condition.TagName;
                return "(" + condition.Offset.x.ToString(CultureInfo.InvariantCulture) + ", " + condition.Offset.y.ToString(CultureInfo.InvariantCulture) + ") - " + matcherSummary;
            }
        }

        private static int ParseInt(string value)
        {
            int parsedValue;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue))
            {
                return Mathf.Max(0, parsedValue);
            }

            return 0;
        }
    }
}
