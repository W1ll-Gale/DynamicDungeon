using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
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
        }

        private static readonly Dictionary<Type, Dictionary<string, ParameterMetadata>> _metadataByNodeType =
            new Dictionary<Type, Dictionary<string, ParameterMetadata>>();

        private static Type _currentNodeType;

        public static void SetNodeTypeContext(Type nodeType)
        {
            _currentNodeType = nodeType;
        }

        public static VisualElement CreateControl(SerializedParameter parameter, Action<string, string> onValueChanged)
        {
            if (parameter == null)
            {
                return new Label("Missing parameter");
            }

            string parameterName = parameter.Name ?? string.Empty;
            string parameterValue = parameter.Value ?? string.Empty;
            ParameterMetadata metadata = ResolveMetadata(_currentNodeType, parameterName);

            if (metadata != null && metadata.ValueType != null && metadata.ValueType.IsEnum)
            {
                return CreateEnumControl(parameterName, parameterValue, metadata.ValueType, onValueChanged);
            }

            bool parsedBoolean;
            if (bool.TryParse(parameterValue, out parsedBoolean))
            {
                return CreateToggleControl(parameterName, parsedBoolean, onValueChanged);
            }

            float parsedFloat;
            bool looksLikeFloat = float.TryParse(
                parameterValue,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out parsedFloat);

            if (metadata != null && metadata.RangeAttribute != null && looksLikeFloat)
            {
                return CreateSliderControl(parameterName, parsedFloat, metadata.RangeAttribute, onValueChanged);
            }

            if (looksLikeFloat ||
                parameterName.EndsWith("Min", StringComparison.OrdinalIgnoreCase) ||
                parameterName.EndsWith("Max", StringComparison.OrdinalIgnoreCase))
            {
                return CreateFloatControl(parameterName, parsedFloat, parameterValue, onValueChanged);
            }

            return CreateTextControl(parameterName, parameterValue, onValueChanged);
        }

        private static VisualElement CreateEnumControl(string parameterName, string parameterValue, Type enumType, Action<string, string> onValueChanged)
        {
            List<string> choices = new List<string>(Enum.GetNames(enumType));
            DropdownField dropdownField = new DropdownField(ObjectNames.NicifyVariableName(parameterName), choices, 0);
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

        private static VisualElement CreateToggleControl(string parameterName, bool parameterValue, Action<string, string> onValueChanged)
        {
            Toggle toggle = new Toggle(ObjectNames.NicifyVariableName(parameterName));
            toggle.SetValueWithoutNotify(parameterValue);
            toggle.RegisterValueChangedCallback(
                changeEvent =>
                {
                    string serialisedValue = changeEvent.newValue ? bool.TrueString.ToLowerInvariant() : bool.FalseString.ToLowerInvariant();
                    onValueChanged?.Invoke(parameterName, serialisedValue);
                });

            return toggle;
        }

        private static VisualElement CreateSliderControl(string parameterName, float parameterValue, RangeAttribute rangeAttribute, Action<string, string> onValueChanged)
        {
            Slider slider = new Slider(ObjectNames.NicifyVariableName(parameterName), rangeAttribute.min, rangeAttribute.max);
            slider.SetValueWithoutNotify(Mathf.Clamp(parameterValue, rangeAttribute.min, rangeAttribute.max));
            slider.RegisterValueChangedCallback(
                changeEvent =>
                {
                    string serialisedValue = changeEvent.newValue.ToString(CultureInfo.InvariantCulture);
                    onValueChanged?.Invoke(parameterName, serialisedValue);
                });

            return slider;
        }

        private static VisualElement CreateFloatControl(string parameterName, float parsedFloatValue, string rawValue, Action<string, string> onValueChanged)
        {
            FloatField floatField = new FloatField(ObjectNames.NicifyVariableName(parameterName));
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

        private static VisualElement CreateTextControl(string parameterName, string parameterValue, Action<string, string> onValueChanged)
        {
            TextField textField = new TextField(ObjectNames.NicifyVariableName(parameterName));
            textField.SetValueWithoutNotify(parameterValue);
            textField.RegisterValueChangedCallback(
                changeEvent =>
                {
                    onValueChanged?.Invoke(parameterName, changeEvent.newValue ?? string.Empty);
                });

            return textField;
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
                metadataByName.Add(key, metadata);
            }

            return metadataByName;
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
