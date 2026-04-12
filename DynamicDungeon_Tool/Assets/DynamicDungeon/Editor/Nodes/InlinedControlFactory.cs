using System;
using System.Collections.Generic;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;
using System.Globalization;
using System.Reflection;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Nodes
{
    public static class InlinedControlFactory
    {
        private sealed class ParameterMetadata
        {
            public Type ValueType;
            public RangeAttribute RangeAttribute;
            public string TooltipText;
            public string DisplayName;
            public bool UseNeighbourCountRuleEditor;
        }

        private sealed class LabelledRuleField : VisualElement
        {
            public Label LabelElement;
            public VisualElement InputElement;
        }

        private sealed class NeighbourCountRuleField : VisualElement
        {
            private readonly string _parameterName;
            private readonly Action<string, string> _onValueChanged;
            private readonly Button[] _countButtons;
            private string _value;

            public NeighbourCountRuleField(string parameterName, string initialValue, Action<string, string> onValueChanged)
            {
                _parameterName = parameterName ?? string.Empty;
                _onValueChanged = onValueChanged;
                _countButtons = new Button[9];

                style.flexDirection = FlexDirection.Row;
                style.flexGrow = 1.0f;
                style.flexShrink = 1.0f;

                VisualElement buttonRow = new VisualElement();
                buttonRow.style.flexDirection = FlexDirection.Row;
                buttonRow.style.alignItems = Align.Center;
                buttonRow.style.flexWrap = Wrap.Wrap;
                buttonRow.style.flexGrow = 1.0f;

                int neighbourCount;
                for (neighbourCount = 0; neighbourCount <= 8; neighbourCount++)
                {
                    int capturedCount = neighbourCount;
                    Button button = new Button(() => ToggleCount(capturedCount));
                    button.text = capturedCount.ToString(CultureInfo.InvariantCulture);
                    button.style.minWidth = 24.0f;
                    button.style.height = 20.0f;
                    button.style.paddingLeft = 0.0f;
                    button.style.paddingRight = 0.0f;
                    button.style.marginTop = 0.0f;
                    button.style.marginBottom = 0.0f;
                    button.style.marginLeft = 0.0f;
                    button.style.marginRight = 4.0f;
                    _countButtons[capturedCount] = button;
                    buttonRow.Add(button);
                }

                Add(buttonRow);
                SetValueWithoutNotify(initialValue);
            }

            public void SetValueWithoutNotify(string value)
            {
                _value = NormaliseNeighbourCountRule(value);
                RefreshButtonStyles();
            }

            private void ToggleCount(int neighbourCount)
            {
                HashSet<int> selectedCounts = ParseNeighbourCountSelections(_value);
                if (!selectedCounts.Add(neighbourCount))
                {
                    selectedCounts.Remove(neighbourCount);
                }

                _value = SerialiseNeighbourCountSelections(selectedCounts);
                RefreshButtonStyles();
                _onValueChanged?.Invoke(_parameterName, _value);
            }

            private void RefreshButtonStyles()
            {
                HashSet<int> selectedCounts = ParseNeighbourCountSelections(_value);

                int neighbourCount;
                for (neighbourCount = 0; neighbourCount < _countButtons.Length; neighbourCount++)
                {
                    Button button = _countButtons[neighbourCount];
                    bool isSelected = selectedCounts.Contains(neighbourCount);
                    button.style.backgroundColor = isSelected
                        ? new StyleColor(new Color(0.24f, 0.48f, 0.74f, 1.0f))
                        : new StyleColor(new Color(0.18f, 0.18f, 0.18f, 1.0f));
                    button.style.color = isSelected
                        ? new StyleColor(Color.white)
                        : new StyleColor(new Color(0.82f, 0.82f, 0.82f, 1.0f));
                    button.style.borderTopColor = isSelected
                        ? new StyleColor(new Color(0.44f, 0.68f, 0.95f, 1.0f))
                        : new StyleColor(new Color(0.28f, 0.28f, 0.28f, 1.0f));
                    button.style.borderBottomColor = button.style.borderTopColor;
                    button.style.borderLeftColor = button.style.borderTopColor;
                    button.style.borderRightColor = button.style.borderTopColor;
                }
            }
        }

        private static readonly Dictionary<Type, Dictionary<string, ParameterMetadata>> _metadataByNodeType =
            new Dictionary<Type, Dictionary<string, ParameterMetadata>>();

        private static Type _currentNodeType;

        public static void SetNodeTypeContext(Type nodeType)
        {
            _currentNodeType = nodeType;
        }

        public static VisualElement CreateControl(SerializedParameter parameter, string defaultValue, Action<string, string> onValueChanged)
        {
            if (parameter == null)
            {
                return new Label("Missing parameter");
            }

            string parameterName = parameter.Name ?? string.Empty;
            string parameterValue = parameter.Value ?? string.Empty;
            ParameterMetadata metadata = ResolveMetadata(_currentNodeType, parameterName);
            string labelText = ResolveDisplayName(parameterName, metadata);
            VisualElement field;

            if (metadata != null && metadata.UseNeighbourCountRuleEditor)
            {
                field = CreateNeighbourCountRuleControl(parameterName, labelText, parameterValue, onValueChanged);
                return WrapControl(parameterName, field, metadata, defaultValue, onValueChanged);
            }

            if (metadata != null && metadata.ValueType != null && metadata.ValueType.IsEnum)
            {
                field = CreateEnumControl(parameterName, labelText, parameterValue, metadata.ValueType, onValueChanged);
                return WrapControl(parameterName, field, metadata, defaultValue, onValueChanged);
            }

            bool parsedBoolean;
            if (bool.TryParse(parameterValue, out parsedBoolean))
            {
                field = CreateToggleControl(parameterName, labelText, parsedBoolean, onValueChanged);
                return WrapControl(parameterName, field, metadata, defaultValue, onValueChanged);
            }

            bool isIntegerParameter = metadata != null && metadata.ValueType == typeof(int);
            int parsedInt;
            int.TryParse(
                parameterValue,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out parsedInt);

            if (isIntegerParameter && metadata.RangeAttribute != null)
            {
                field = CreateIntegerSliderControl(parameterName, labelText, parsedInt, metadata.RangeAttribute, onValueChanged);
                return WrapControl(parameterName, field, metadata, defaultValue, onValueChanged);
            }

            if (isIntegerParameter)
            {
                field = CreateIntegerControl(parameterName, labelText, parsedInt, parameterValue, onValueChanged);
                return WrapControl(parameterName, field, metadata, defaultValue, onValueChanged);
            }

            float parsedFloat;
            bool looksLikeFloat = float.TryParse(
                parameterValue,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out parsedFloat);

            if (metadata != null && metadata.RangeAttribute != null && looksLikeFloat)
            {
                field = CreateSliderControl(parameterName, labelText, parsedFloat, metadata.RangeAttribute, onValueChanged);
                return WrapControl(parameterName, field, metadata, defaultValue, onValueChanged);
            }

            if (looksLikeFloat ||
                parameterName.EndsWith("Min", StringComparison.OrdinalIgnoreCase) ||
                parameterName.EndsWith("Max", StringComparison.OrdinalIgnoreCase))
            {
                field = CreateFloatControl(parameterName, labelText, parsedFloat, parameterValue, onValueChanged);
                return WrapControl(parameterName, field, metadata, defaultValue, onValueChanged);
            }

            field = CreateTextControl(parameterName, labelText, parameterValue, onValueChanged);
            return WrapControl(parameterName, field, metadata, defaultValue, onValueChanged);
        }

        private static VisualElement CreateNeighbourCountRuleControl(string parameterName, string labelText, string parameterValue, Action<string, string> onValueChanged)
        {
            LabelledRuleField container = new LabelledRuleField();
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.Center;
            container.style.flexGrow = 1.0f;
            container.style.flexShrink = 1.0f;

            Label label = new Label(labelText);
            label.style.minWidth = 92.0f;
            label.style.marginRight = 6.0f;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;

            NeighbourCountRuleField ruleField = new NeighbourCountRuleField(parameterName, parameterValue, onValueChanged);
            ruleField.style.flexGrow = 1.0f;
            ruleField.style.flexShrink = 1.0f;

            container.LabelElement = label;
            container.InputElement = ruleField;
            container.Add(label);
            container.Add(ruleField);
            return container;
        }

        private static VisualElement CreateEnumControl(string parameterName, string labelText, string parameterValue, Type enumType, Action<string, string> onValueChanged)
        {
            List<string> choices = new List<string>(Enum.GetNames(enumType));
            DropdownField dropdownField = new DropdownField(labelText, choices, 0);
            int selectedIndex = choices.IndexOf(parameterValue);
            if (selectedIndex < 0)
            {
                selectedIndex = 0;
            }

            dropdownField.value = choices[selectedIndex];
            dropdownField.RegisterValueChangedCallback(
                changeEvent =>
                {
                    onValueChanged?.Invoke(parameterName, changeEvent.newValue ?? string.Empty);
                });

            return dropdownField;
        }

        private static VisualElement CreateToggleControl(string parameterName, string labelText, bool parameterValue, Action<string, string> onValueChanged)
        {
            Toggle toggle = new Toggle(labelText);
            toggle.SetValueWithoutNotify(parameterValue);
            toggle.RegisterValueChangedCallback(
                changeEvent =>
                {
                    string serialisedValue = changeEvent.newValue ? bool.TrueString.ToLowerInvariant() : bool.FalseString.ToLowerInvariant();
                    onValueChanged?.Invoke(parameterName, serialisedValue);
                });

            return toggle;
        }

        private static VisualElement CreateIntegerSliderControl(string parameterName, string labelText, int parameterValue, RangeAttribute rangeAttribute, Action<string, string> onValueChanged)
        {
            int minValue = Mathf.RoundToInt(rangeAttribute.min);
            int maxValue = Mathf.RoundToInt(rangeAttribute.max);
            SliderInt slider = new SliderInt(labelText, minValue, maxValue);
            slider.SetValueWithoutNotify(Mathf.Clamp(parameterValue, minValue, maxValue));
            slider.RegisterValueChangedCallback(
                changeEvent =>
                {
                    string serialisedValue = changeEvent.newValue.ToString(CultureInfo.InvariantCulture);
                    onValueChanged?.Invoke(parameterName, serialisedValue);
                });

            return slider;
        }

        private static VisualElement CreateSliderControl(string parameterName, string labelText, float parameterValue, RangeAttribute rangeAttribute, Action<string, string> onValueChanged)
        {
            Slider slider = new Slider(labelText, rangeAttribute.min, rangeAttribute.max);
            slider.SetValueWithoutNotify(Mathf.Clamp(parameterValue, rangeAttribute.min, rangeAttribute.max));
            slider.RegisterValueChangedCallback(
                changeEvent =>
                {
                    string serialisedValue = changeEvent.newValue.ToString(CultureInfo.InvariantCulture);
                    onValueChanged?.Invoke(parameterName, serialisedValue);
                });

            return slider;
        }

        private static VisualElement CreateIntegerControl(string parameterName, string labelText, int parsedIntValue, string rawValue, Action<string, string> onValueChanged)
        {
            IntegerField integerField = new IntegerField(labelText);
            int initialValue = parsedIntValue;
            if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out initialValue))
            {
                initialValue = 0;
            }

            integerField.SetValueWithoutNotify(initialValue);
            integerField.RegisterValueChangedCallback(
                changeEvent =>
                {
                    string serialisedValue = changeEvent.newValue.ToString(CultureInfo.InvariantCulture);
                    onValueChanged?.Invoke(parameterName, serialisedValue);
                });

            return integerField;
        }

        private static VisualElement CreateFloatControl(string parameterName, string labelText, float parsedFloatValue, string rawValue, Action<string, string> onValueChanged)
        {
            FloatField floatField = new FloatField(labelText);
            float initialValue = parsedFloatValue;
            if (!float.TryParse(rawValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out initialValue))
            {
                initialValue = 0.0f;
            }

            floatField.SetValueWithoutNotify(initialValue);
            floatField.RegisterValueChangedCallback(
                changeEvent =>
                {
                    string serialisedValue = changeEvent.newValue.ToString(CultureInfo.InvariantCulture);
                    onValueChanged?.Invoke(parameterName, serialisedValue);
                });

            return floatField;
        }

        private static VisualElement CreateTextControl(string parameterName, string labelText, string parameterValue, Action<string, string> onValueChanged)
        {
            TextField textField = new TextField(labelText);
            textField.SetValueWithoutNotify(parameterValue);
            textField.RegisterValueChangedCallback(
                changeEvent =>
                {
                    onValueChanged?.Invoke(parameterName, changeEvent.newValue ?? string.Empty);
                });

            return textField;
        }

        private static VisualElement WrapControl(string parameterName, VisualElement field, ParameterMetadata metadata, string defaultValue, Action<string, string> onValueChanged)
        {
            string tooltipText = BuildTooltipText(parameterName, metadata, defaultValue);
            ApplyTooltip(field, tooltipText);

            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            field.style.flexGrow = 1.0f;
            field.style.flexShrink = 1.0f;
            row.Add(field);
            row.AddManipulator(new ContextualMenuManipulator(
                menuPopulateEvent =>
                {
                    if (defaultValue == null)
                    {
                        menuPopulateEvent.menu.AppendAction(
                            "Reset Parameter",
                            _ => { },
                            _ => DropdownMenuAction.Status.Disabled);
                        return;
                    }

                    menuPopulateEvent.menu.AppendAction(
                        "Reset Parameter",
                        _ => ApplyResetValue(field, parameterName, defaultValue, metadata, onValueChanged));
                }));

            return row;
        }

        private static void ApplyResetValue(VisualElement field, string parameterName, string defaultValue, ParameterMetadata metadata, Action<string, string> onValueChanged)
        {
            if (defaultValue == null || field == null)
            {
                return;
            }

            if (field is DropdownField dropdownField)
            {
                string valueToApply = defaultValue;
                if (!dropdownField.choices.Contains(valueToApply) && dropdownField.choices.Count > 0)
                {
                    valueToApply = dropdownField.choices[0];
                }

                dropdownField.SetValueWithoutNotify(valueToApply);
                onValueChanged?.Invoke(parameterName, valueToApply ?? string.Empty);
                return;
            }

            if (field is Toggle toggle)
            {
                bool parsedBoolean;
                if (!bool.TryParse(defaultValue, out parsedBoolean))
                {
                    parsedBoolean = false;
                }

                toggle.SetValueWithoutNotify(parsedBoolean);
                onValueChanged?.Invoke(parameterName, parsedBoolean ? bool.TrueString.ToLowerInvariant() : bool.FalseString.ToLowerInvariant());
                return;
            }

            if (field is Slider slider)
            {
                float parsedFloat;
                if (!float.TryParse(defaultValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedFloat))
                {
                    parsedFloat = 0.0f;
                }

                if (metadata != null && metadata.RangeAttribute != null)
                {
                    parsedFloat = Mathf.Clamp(parsedFloat, metadata.RangeAttribute.min, metadata.RangeAttribute.max);
                }

                slider.SetValueWithoutNotify(parsedFloat);
                onValueChanged?.Invoke(parameterName, parsedFloat.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (field is SliderInt sliderInt)
            {
                int parsedInt;
                if (!int.TryParse(defaultValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedInt))
                {
                    parsedInt = 0;
                }

                sliderInt.SetValueWithoutNotify(parsedInt);
                onValueChanged?.Invoke(parameterName, parsedInt.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (field is NeighbourCountRuleField neighbourCountRuleField)
            {
                neighbourCountRuleField.SetValueWithoutNotify(defaultValue);
                onValueChanged?.Invoke(parameterName, NormaliseNeighbourCountRule(defaultValue));
                return;
            }

            if (field is FloatField floatField)
            {
                float parsedFloat;
                if (!float.TryParse(defaultValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsedFloat))
                {
                    parsedFloat = 0.0f;
                }

                floatField.SetValueWithoutNotify(parsedFloat);
                onValueChanged?.Invoke(parameterName, parsedFloat.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (field is IntegerField integerField)
            {
                int parsedInt;
                if (!int.TryParse(defaultValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedInt))
                {
                    parsedInt = 0;
                }

                integerField.SetValueWithoutNotify(parsedInt);
                onValueChanged?.Invoke(parameterName, parsedInt.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (field is TextField textField)
            {
                textField.SetValueWithoutNotify(defaultValue);
                onValueChanged?.Invoke(parameterName, defaultValue);
            }
        }

        private static ParameterMetadata ResolveMetadata(Type nodeType, string parameterName)
        {
            if (nodeType == null || string.IsNullOrWhiteSpace(parameterName))
            {
                return null;
            }

            Dictionary<string, ParameterMetadata> metadataByName;
            if (!_metadataByNodeType.TryGetValue(nodeType, out metadataByName))
            {
                metadataByName = BuildMetadata(nodeType);
                _metadataByNodeType.Add(nodeType, metadataByName);
            }

            ParameterMetadata metadata;
            if (metadataByName.TryGetValue(NormaliseName(parameterName), out metadata))
            {
                return metadata;
            }

            return null;
        }

        private static Dictionary<string, ParameterMetadata> BuildMetadata(Type nodeType)
        {
            Dictionary<string, ParameterMetadata> metadataByName = new Dictionary<string, ParameterMetadata>(StringComparer.OrdinalIgnoreCase);
            BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            FieldInfo[] fields = nodeType.GetFields(bindingFlags);
            int fieldIndex;
            for (fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
            {
                FieldInfo field = fields[fieldIndex];
                string key = NormaliseName(field.Name);
                if (metadataByName.ContainsKey(key))
                {
                    continue;
                }

                ParameterMetadata metadata = new ParameterMetadata();
                metadata.ValueType = field.FieldType;
                metadata.RangeAttribute = field.GetCustomAttribute<RangeAttribute>();
                metadata.TooltipText = GetTooltipText(field);
                metadata.DisplayName = GetDisplayName(field);
                metadata.UseNeighbourCountRuleEditor = field.GetCustomAttribute<NeighbourCountRuleAttribute>() != null;
                metadataByName.Add(key, metadata);
            }

            PropertyInfo[] properties = nodeType.GetProperties(bindingFlags);
            int propertyIndex;
            for (propertyIndex = 0; propertyIndex < properties.Length; propertyIndex++)
            {
                PropertyInfo property = properties[propertyIndex];
                string key = NormaliseName(property.Name);
                if (metadataByName.ContainsKey(key))
                {
                    continue;
                }

                ParameterMetadata metadata = new ParameterMetadata();
                metadata.ValueType = property.PropertyType;
                metadata.RangeAttribute = property.GetCustomAttribute<RangeAttribute>();
                metadata.TooltipText = GetTooltipText(property);
                metadata.DisplayName = GetDisplayName(property);
                metadata.UseNeighbourCountRuleEditor = property.GetCustomAttribute<NeighbourCountRuleAttribute>() != null;
                metadataByName.Add(key, metadata);
            }

            return metadataByName;
        }

        private static string BuildTooltipText(string parameterName, ParameterMetadata metadata, string defaultValue)
        {
            List<string> lines = new List<string>();

            string descriptionText = metadata != null ? metadata.TooltipText : string.Empty;
            if (!string.IsNullOrWhiteSpace(descriptionText))
            {
                lines.Add(descriptionText);
            }

            List<string> details = new List<string>();

            if (metadata != null && metadata.RangeAttribute != null)
            {
                details.Add(
                    "Range " +
                    metadata.RangeAttribute.min.ToString(CultureInfo.InvariantCulture) +
                    " to " +
                    metadata.RangeAttribute.max.ToString(CultureInfo.InvariantCulture));
            }

            if (defaultValue != null)
            {
                details.Add("Default " + FormatParameterValue(defaultValue, metadata));
            }

            if (details.Count > 0)
            {
                lines.Add(string.Join("  •  ", details));
            }

            if (lines.Count == 0)
            {
                lines.Add(ResolveDisplayName(parameterName, metadata));
            }

            return string.Join("\n", lines);
        }

        private static void ApplyTooltip(VisualElement field, string tooltipText)
        {
            if (field == null || string.IsNullOrWhiteSpace(tooltipText))
            {
                return;
            }

            if (field is LabelledRuleField labelledRuleField)
            {
                if (labelledRuleField.LabelElement != null)
                {
                    labelledRuleField.LabelElement.tooltip = tooltipText;
                }

                return;
            }

            Label label = field.Q<Label>(className: BaseField<string>.labelUssClassName);
            if (label != null)
            {
                label.tooltip = tooltipText;
            }
        }

        private static string GetTooltipText(MemberInfo memberInfo)
        {
            if (memberInfo == null)
            {
                return string.Empty;
            }

            TooltipAttribute tooltipAttribute = memberInfo.GetCustomAttribute<TooltipAttribute>();
            if (tooltipAttribute != null && !string.IsNullOrWhiteSpace(tooltipAttribute.tooltip))
            {
                return tooltipAttribute.tooltip;
            }

            DescriptionAttribute descriptionAttribute = memberInfo.GetCustomAttribute<DescriptionAttribute>();
            if (descriptionAttribute != null && !string.IsNullOrWhiteSpace(descriptionAttribute.Description))
            {
                return descriptionAttribute.Description;
            }

            return string.Empty;
        }

        private static string GetDisplayName(MemberInfo memberInfo)
        {
            if (memberInfo == null)
            {
                return string.Empty;
            }

            InspectorNameAttribute inspectorNameAttribute = memberInfo.GetCustomAttribute<InspectorNameAttribute>();
            if (inspectorNameAttribute != null && !string.IsNullOrWhiteSpace(inspectorNameAttribute.displayName))
            {
                return inspectorNameAttribute.displayName;
            }

            return string.Empty;
        }

        private static string ResolveDisplayName(string parameterName, ParameterMetadata metadata)
        {
            if (metadata != null && !string.IsNullOrWhiteSpace(metadata.DisplayName))
            {
                return metadata.DisplayName;
            }

            return ObjectNames.NicifyVariableName(parameterName);
        }

        private static string FormatParameterValue(string value, ParameterMetadata metadata)
        {
            if (metadata != null && metadata.UseNeighbourCountRuleEditor)
            {
                return FormatNeighbourCountRule(value);
            }

            return value ?? string.Empty;
        }

        private static string FormatNeighbourCountRule(string value)
        {
            HashSet<int> selectedCounts = ParseNeighbourCountSelections(value);
            if (selectedCounts.Count == 0)
            {
                return "none";
            }

            List<int> orderedCounts = new List<int>(selectedCounts);
            orderedCounts.Sort();

            List<string> parts = new List<string>(orderedCounts.Count);
            int index;
            for (index = 0; index < orderedCounts.Count; index++)
            {
                parts.Add(orderedCounts[index].ToString(CultureInfo.InvariantCulture));
            }

            return string.Join(", ", parts);
        }

        private static string NormaliseNeighbourCountRule(string value)
        {
            return SerialiseNeighbourCountSelections(ParseNeighbourCountSelections(value));
        }

        private static HashSet<int> ParseNeighbourCountSelections(string value)
        {
            HashSet<int> selectedCounts = new HashSet<int>();
            if (string.IsNullOrEmpty(value))
            {
                return selectedCounts;
            }

            int characterIndex;
            for (characterIndex = 0; characterIndex < value.Length; characterIndex++)
            {
                char character = value[characterIndex];
                if (character < '0' || character > '8')
                {
                    continue;
                }

                selectedCounts.Add(character - '0');
            }

            return selectedCounts;
        }

        private static string SerialiseNeighbourCountSelections(HashSet<int> selectedCounts)
        {
            if (selectedCounts == null || selectedCounts.Count == 0)
            {
                return string.Empty;
            }

            List<int> orderedCounts = new List<int>(selectedCounts);
            orderedCounts.Sort();

            char[] serialisedCharacters = new char[orderedCounts.Count];
            int index;
            for (index = 0; index < orderedCounts.Count; index++)
            {
                serialisedCharacters[index] = (char)('0' + orderedCounts[index]);
            }

            return new string(serialisedCharacters);
        }

        private static string NormaliseName(string name)
        {
            string safeName = name ?? string.Empty;
            while (safeName.StartsWith("_", StringComparison.Ordinal))
            {
                safeName = safeName.Substring(1);
            }

            return safeName;
        }
    }
}
