using System;
using System.Collections.Generic;
using System.Reflection;
using DynamicDungeon.Runtime.Semantic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DynamicDungeon.Runtime.Output
{
    public sealed class TilemapLayerWriter
    {
        private const string TilemapNamePrefix = "Tilemap_";

        private readonly Dictionary<string, Tilemap> _tilemapsByLayerName = new Dictionary<string, Tilemap>(StringComparer.Ordinal);

        public void EnsureTimelapsCreated(Grid grid, IReadOnlyList<TilemapLayerDefinition> definitions)
        {
            if (grid == null)
            {
                throw new ArgumentNullException(nameof(grid));
            }

            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            int index;
            for (index = 0; index < definitions.Count; index++)
            {
                TilemapLayerDefinition definition = definitions[index];
                if (definition == null)
                {
                    continue;
                }

                Tilemap tilemap = GetOrCreateTilemap(grid.transform, definition);
                ConfigureTilemap(tilemap, definition);
                _tilemapsByLayerName[definition.LayerName] = tilemap;
            }
        }

        public void ClearAll()
        {
            foreach (KeyValuePair<string, Tilemap> pair in _tilemapsByLayerName)
            {
                Tilemap tilemap = pair.Value;
                if (tilemap != null)
                {
                    tilemap.ClearAllTiles();
                }
            }
        }

        public void WriteTile(Vector3Int position, TileBase tile, TilemapLayerDefinition layer)
        {
            if (tile == null)
            {
                throw new ArgumentNullException(nameof(tile));
            }

            if (layer == null)
            {
                throw new ArgumentNullException(nameof(layer));
            }

            Tilemap tilemap;
            if (!_tilemapsByLayerName.TryGetValue(layer.LayerName, out tilemap) || tilemap == null)
            {
                throw new InvalidOperationException("Tilemap layer '" + layer.LayerName + "' has not been created yet.");
            }

            tilemap.SetTile(position, tile);
        }

        private static Tilemap GetOrCreateTilemap(Transform parent, TilemapLayerDefinition definition)
        {
            string objectName = TilemapNamePrefix + definition.LayerName;
            Transform existingChild = parent.Find(objectName);

            GameObject tilemapObject;
            if (existingChild != null)
            {
                tilemapObject = existingChild.gameObject;
            }
            else
            {
                tilemapObject = new GameObject(objectName);
                tilemapObject.transform.SetParent(parent, false);
                tilemapObject.transform.localPosition = Vector3.zero;
                tilemapObject.transform.localRotation = Quaternion.identity;
                tilemapObject.transform.localScale = Vector3.one;
            }

            Tilemap tilemap = tilemapObject.GetComponent<Tilemap>();
            if (tilemap == null)
            {
                tilemap = tilemapObject.AddComponent<Tilemap>();
            }

            TilemapRenderer tilemapRenderer = tilemapObject.GetComponent<TilemapRenderer>();
            if (tilemapRenderer == null)
            {
                tilemapRenderer = tilemapObject.AddComponent<TilemapRenderer>();
            }

            return tilemap;
        }

        private static void ConfigureTilemap(Tilemap tilemap, TilemapLayerDefinition definition)
        {
            TilemapRenderer tilemapRenderer = tilemap.GetComponent<TilemapRenderer>();
            tilemapRenderer.sortingOrder = definition.SortOrder;

            if (definition.ComponentsToAdd == null)
            {
                return;
            }

            int componentIndex;
            for (componentIndex = 0; componentIndex < definition.ComponentsToAdd.Count; componentIndex++)
            {
                string componentTypeName = definition.ComponentsToAdd[componentIndex];
                if (string.IsNullOrWhiteSpace(componentTypeName))
                {
                    continue;
                }

                Type componentType = ResolveComponentType(componentTypeName);
                if (componentType == null)
                {
                    Debug.LogWarning("Tilemap layer '" + definition.LayerName + "' could not resolve component type '" + componentTypeName + "'.");
                    continue;
                }

                if (!typeof(Component).IsAssignableFrom(componentType))
                {
                    Debug.LogWarning("Tilemap layer '" + definition.LayerName + "' component type '" + componentTypeName + "' is not a Unity Component.");
                    continue;
                }

                Component component = tilemap.GetComponent(componentType);
                if (component == null)
                {
                    component = tilemap.gameObject.AddComponent(componentType);
                    ApplyComponentDefaults(component);
                }
            }
        }

        private static Type ResolveComponentType(string componentTypeName)
        {
            Type directType = Type.GetType(componentTypeName, false);
            if (directType != null)
            {
                return directType;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            int assemblyIndex;
            for (assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++)
            {
                Assembly assembly = assemblies[assemblyIndex];
                Type resolvedType = assembly.GetType(componentTypeName, false);
                if (resolvedType != null)
                {
                    return resolvedType;
                }
            }

            return null;
        }

        private static void ApplyComponentDefaults(Component component)
        {
            Rigidbody2D rigidbody2D = component as Rigidbody2D;
            if (rigidbody2D != null)
            {
                rigidbody2D.bodyType = RigidbodyType2D.Static;
                return;
            }

            CompositeCollider2D compositeCollider2D = component as CompositeCollider2D;
            if (compositeCollider2D != null)
            {
                TilemapCollider2D tilemapCollider2D = compositeCollider2D.GetComponent<TilemapCollider2D>();
                if (tilemapCollider2D != null)
                {
                    tilemapCollider2D.compositeOperation = Collider2D.CompositeOperation.Merge;
                }
            }
        }
    }
}
