using System;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Semantic;
using UnityEditor;
using UnityEngine;

namespace DynamicDungeon.Editor
{
    internal static class DynamicDungeonBuiltInLayerDefaults
    {
        private const string LayerDefinitionFolderPath = "Assets/DynamicDungeon/LayerDefinitions";
        private const string TilemapColliderTypeName = "UnityEngine.Tilemaps.TilemapCollider2D";
        private const string CompositeColliderTypeName = "UnityEngine.CompositeCollider2D";
        private const string Rigidbody2DTypeName = "UnityEngine.Rigidbody2D";

        private static readonly LayerPreset[] _presets = new[]
        {
            new LayerPreset("Solid", new[] { "Solid" }, new[] { TilemapColliderTypeName, CompositeColliderTypeName, Rigidbody2DTypeName }, 0, false, false),
            new LayerPreset("Floor", new[] { "Walkable" }, Array.Empty<string>(), 0, false, false),
            new LayerPreset("Liquid", new[] { "Liquid" }, new[] { TilemapColliderTypeName }, 0, false, true),
            new LayerPreset("Trigger", new[] { "Trigger" }, new[] { TilemapColliderTypeName }, 0, false, true),
            new LayerPreset("Visual", new[] { "Decorative" }, Array.Empty<string>(), 0, false, false),
            new LayerPreset("Default", Array.Empty<string>(), Array.Empty<string>(), 0, true, false)
        };

        internal sealed class LayerPreset
        {
            public readonly string LayerName;
            public readonly string[] RoutingTags;
            public readonly string[] ComponentTypeNames;
            public readonly int SortOrder;
            public readonly bool IsCatchAll;
            public readonly bool UseTriggerCollider;

            public LayerPreset(string layerName, string[] routingTags, string[] componentTypeNames, int sortOrder, bool isCatchAll, bool useTriggerCollider)
            {
                LayerName = layerName;
                RoutingTags = routingTags ?? Array.Empty<string>();
                ComponentTypeNames = componentTypeNames ?? Array.Empty<string>();
                SortOrder = sortOrder;
                IsCatchAll = isCatchAll;
                UseTriggerCollider = useTriggerCollider;
            }
        }

        public static IReadOnlyList<LayerPreset> Presets
        {
            get
            {
                return _presets;
            }
        }

        public static List<TilemapLayerDefinition> CreateOrLoadDefaultLayerAssets()
        {
            DynamicDungeonEditorAssetUtility.EnsureFolderPath(LayerDefinitionFolderPath);

            List<TilemapLayerDefinition> layerDefinitions = new List<TilemapLayerDefinition>(_presets.Length);

            int index;
            for (index = 0; index < _presets.Length; index++)
            {
                LayerPreset preset = _presets[index];
                string assetPath = BuildLayerAssetPath(preset.LayerName);
                UnityEngine.Object existingAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (existingAsset != null && !(existingAsset is TilemapLayerDefinition))
                {
                    throw new InvalidOperationException("Cannot create layer definition at '" + assetPath + "' because a different asset already exists there.");
                }

                TilemapLayerDefinition layerDefinition = AssetDatabase.LoadAssetAtPath<TilemapLayerDefinition>(assetPath);
                if (layerDefinition == null)
                {
                    layerDefinition = ScriptableObject.CreateInstance<TilemapLayerDefinition>();
                    ConfigureDefinition(layerDefinition, preset);
                    AssetDatabase.CreateAsset(layerDefinition, assetPath);
                }
                else
                {
                    ConfigureDefinition(layerDefinition, preset);
                    EditorUtility.SetDirty(layerDefinition);
                }

                layerDefinitions.Add(layerDefinition);
            }

            AssetDatabase.SaveAssets();
            return layerDefinitions;
        }

        public static void ConfigureDefinition(TilemapLayerDefinition layerDefinition, LayerPreset preset)
        {
            if (layerDefinition == null)
            {
                throw new ArgumentNullException(nameof(layerDefinition));
            }

            if (preset == null)
            {
                throw new ArgumentNullException(nameof(preset));
            }

            layerDefinition.LayerName = preset.LayerName;
            layerDefinition.SortOrder = preset.SortOrder;
            layerDefinition.IsCatchAll = preset.IsCatchAll;

            if (layerDefinition.RoutingTags == null)
            {
                layerDefinition.RoutingTags = new List<string>();
            }
            else
            {
                layerDefinition.RoutingTags.Clear();
            }

            int routingTagIndex;
            for (routingTagIndex = 0; routingTagIndex < preset.RoutingTags.Length; routingTagIndex++)
            {
                layerDefinition.RoutingTags.Add(preset.RoutingTags[routingTagIndex]);
            }

            if (layerDefinition.ComponentsToAdd == null)
            {
                layerDefinition.ComponentsToAdd = new List<string>();
            }
            else
            {
                layerDefinition.ComponentsToAdd.Clear();
            }

            int componentIndex;
            for (componentIndex = 0; componentIndex < preset.ComponentTypeNames.Length; componentIndex++)
            {
                layerDefinition.ComponentsToAdd.Add(preset.ComponentTypeNames[componentIndex]);
            }
        }

        private static string BuildLayerAssetPath(string layerName)
        {
            return LayerDefinitionFolderPath + "/" + layerName + "Layer.asset";
        }
    }
}
