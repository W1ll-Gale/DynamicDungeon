using System;
using System.Collections.Generic;
using System.Globalization;
using DynamicDungeon.Runtime.Placement;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Nodes
{
    internal static class PrefabStampVariantParameterControls
    {
        public static VisualElement CreatePrefabVariantsControl(CustomParameterControlContext context)
        {
            return new PrefabVariantsField(context);
        }

        private sealed class PrefabVariantsField : VisualElement, ICustomParameterValueControl
        {
            private readonly string _parameterName;
            private readonly Action<string, string> _onValueChanged;
            private readonly Label _summaryLabel;
            private readonly VisualElement _variantList;

            private PrefabStampVariantSet _variantSet;
            private bool _isRefreshing;

            public PrefabVariantsField(CustomParameterControlContext context)
            {
                _parameterName = context.ParameterName;
                _onValueChanged = context.OnValueChanged;
                _variantSet = ParseVariantSet(context.ParameterValue);

                style.flexDirection = FlexDirection.Column;
                style.flexGrow = 1.0f;

                VisualElement header = CreateHeader(context.LabelText, AddVariant);
                _summaryLabel = new Label();
                _summaryLabel.style.marginLeft = 6.0f;
                _summaryLabel.style.color = new Color(0.76f, 0.76f, 0.76f, 1.0f);
                _summaryLabel.style.flexGrow = 1.0f;
                header.Insert(1, _summaryLabel);
                Add(header);

                _variantList = new VisualElement();
                _variantList.style.flexDirection = FlexDirection.Column;
                _variantList.style.marginTop = 4.0f;
                Add(_variantList);

                Refresh();
            }

            public void SetValueWithoutNotify(string value)
            {
                _variantSet = ParseVariantSet(value);
                Refresh();
            }

            private void AddVariant()
            {
                List<PrefabStampVariant> variants = GetVariants();
                variants.Add(new PrefabStampVariant
                {
                    Weight = 1.0f
                });
                _variantSet.Variants = variants.ToArray();
                ApplyChanges();
            }

            private void Refresh()
            {
                _isRefreshing = true;
                _variantList.Clear();

                PrefabStampVariant[] variants = _variantSet != null && _variantSet.Variants != null
                    ? _variantSet.Variants
                    : Array.Empty<PrefabStampVariant>();

                int variantIndex;
                for (variantIndex = 0; variantIndex < variants.Length; variantIndex++)
                {
                    _variantList.Add(CreateVariantRow(variantIndex, variants[variantIndex]));
                }

                if (variants.Length == 0)
                {
                    _variantList.Add(CreateEmptyLabel("No prefab variants configured."));
                }

                _summaryLabel.text = variants.Length.ToString(CultureInfo.InvariantCulture) + " variants";
                _isRefreshing = false;
            }

            private VisualElement CreateVariantRow(int variantIndex, PrefabStampVariant variant)
            {
                PrefabStampVariant safeVariant = variant ?? new PrefabStampVariant();
                VisualElement row = CreateRow();

                ObjectField prefabField = new ObjectField();
                prefabField.objectType = typeof(GameObject);
                prefabField.allowSceneObjects = false;
                prefabField.style.flexGrow = 1.0f;
                prefabField.style.minWidth = 170.0f;
                prefabField.SetValueWithoutNotify(LoadAsset(safeVariant.Prefab));
                prefabField.RegisterValueChangedCallback(evt =>
                {
                    safeVariant.Prefab = GetAssetGuid(evt.newValue);
                    ApplyChanges();
                });
                row.Add(prefabField);

                FloatField weightField = new FloatField("Weight");
                weightField.style.width = 108.0f;
                weightField.style.minWidth = 108.0f;
                weightField.Query<Label>().ForEach(label => label.style.minWidth = 44.0f);
                weightField.SetValueWithoutNotify(Mathf.Max(0.0f, safeVariant.Weight));
                weightField.RegisterValueChangedCallback(evt =>
                {
                    safeVariant.Weight = Mathf.Max(0.0f, evt.newValue);
                    ApplyChanges();
                });
                row.Add(weightField);

                Button removeButton = CreateIconButton("-", "Remove prefab variant", () =>
                {
                    List<PrefabStampVariant> variants = GetVariants();
                    if (variantIndex >= 0 && variantIndex < variants.Count)
                    {
                        variants.RemoveAt(variantIndex);
                        _variantSet.Variants = variants.ToArray();
                        ApplyChanges();
                    }
                });
                row.Add(removeButton);
                return row;
            }

            private List<PrefabStampVariant> GetVariants()
            {
                return new List<PrefabStampVariant>(_variantSet != null && _variantSet.Variants != null
                    ? _variantSet.Variants
                    : Array.Empty<PrefabStampVariant>());
            }

            private void ApplyChanges()
            {
                if (_isRefreshing)
                {
                    return;
                }

                string value = JsonUtility.ToJson(_variantSet ?? new PrefabStampVariantSet());
                _onValueChanged?.Invoke(_parameterName, value);
                Refresh();
            }
        }

        private static VisualElement CreateHeader(string labelText, Action onAdd)
        {
            VisualElement header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;

            Label label = new Label(labelText);
            label.style.minWidth = 92.0f;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            header.Add(label);

            Button addButton = new Button(onAdd);
            addButton.text = "Add Prefab";
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

        private static PrefabStampVariantSet ParseVariantSet(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new PrefabStampVariantSet();
            }

            try
            {
                return JsonUtility.FromJson<PrefabStampVariantSet>(value) ?? new PrefabStampVariantSet();
            }
            catch
            {
                return new PrefabStampVariantSet();
            }
        }

        private static UnityEngine.Object LoadAsset(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid))
            {
                return null;
            }

            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
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
