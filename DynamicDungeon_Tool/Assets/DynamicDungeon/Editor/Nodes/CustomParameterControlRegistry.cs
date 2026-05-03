using System;
using DynamicDungeon.Runtime.Nodes;
using UnityEngine.UIElements;

namespace DynamicDungeon.Editor.Nodes
{
    internal interface ICustomParameterValueControl
    {
        void SetValueWithoutNotify(string value);
    }

    internal readonly struct CustomParameterControlContext
    {
        public readonly Type NodeType;
        public readonly string ParameterName;
        public readonly string LabelText;
        public readonly string ParameterValue;
        public readonly string DefaultValue;
        public readonly Action<string, string> OnValueChanged;

        public CustomParameterControlContext(
            Type nodeType,
            string parameterName,
            string labelText,
            string parameterValue,
            string defaultValue,
            Action<string, string> onValueChanged)
        {
            NodeType = nodeType;
            ParameterName = parameterName ?? string.Empty;
            LabelText = labelText ?? string.Empty;
            ParameterValue = parameterValue ?? string.Empty;
            DefaultValue = defaultValue;
            OnValueChanged = onValueChanged;
        }
    }

    internal static class CustomParameterControlRegistry
    {
        public static bool TryCreateControl(CustomParameterControlContext context, out VisualElement control)
        {
            control = null;

            if (context.NodeType == typeof(ContextualQueryNode) &&
                string.Equals(context.ParameterName, "conditions", StringComparison.OrdinalIgnoreCase))
            {
                control = SpatialQueryParameterControls.CreateContextualQueryConditionsControl(context);
                return control != null;
            }

            if (context.NodeType == typeof(NeighbourhoodCheckNode))
            {
                if (string.Equals(context.ParameterName, "logicalId", StringComparison.OrdinalIgnoreCase))
                {
                    control = SpatialQueryParameterControls.CreateNeighbourhoodLogicalIdControl(context);
                    return control != null;
                }

                if (string.Equals(context.ParameterName, "tagName", StringComparison.OrdinalIgnoreCase))
                {
                    control = SpatialQueryParameterControls.CreateNeighbourhoodTagControl(context);
                    return control != null;
                }
            }

            if (context.NodeType == typeof(BiomeLayoutNode) &&
                string.Equals(context.ParameterName, "rules", StringComparison.OrdinalIgnoreCase))
            {
                control = BiomeLayoutParameterControls.CreateBiomeLayoutRulesControl(context);
                return control != null;
            }

            if ((context.NodeType == typeof(LogicalIdRuleOverlayNode) ||
                 context.NodeType == typeof(LogicalIdRuleStackNode)) &&
                string.Equals(context.ParameterName, "rules", StringComparison.OrdinalIgnoreCase))
            {
                control = BiomeLayoutParameterControls.CreateLogicalIdRulesControl(context);
                return control != null;
            }

            if (context.NodeType == typeof(BiomeOverrideStackNode) &&
                string.Equals(context.ParameterName, "rules", StringComparison.OrdinalIgnoreCase))
            {
                control = StackRuleParameterControls.CreateBiomeOverrideRulesControl(context);
                return control != null;
            }

            if (context.NodeType == typeof(PlacementSetNode) &&
                string.Equals(context.ParameterName, "rules", StringComparison.OrdinalIgnoreCase))
            {
                control = StackRuleParameterControls.CreatePlacementSetRulesControl(context);
                return control != null;
            }

            if (context.NodeType == typeof(PrefabStamperNode) &&
                string.Equals(context.ParameterName, "prefabVariants", StringComparison.OrdinalIgnoreCase))
            {
                control = PrefabStampVariantParameterControls.CreatePrefabVariantsControl(context);
                return control != null;
            }

            return false;
        }
    }
}
