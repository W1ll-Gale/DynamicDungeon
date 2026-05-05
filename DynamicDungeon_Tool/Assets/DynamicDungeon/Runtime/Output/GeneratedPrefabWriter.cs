using System;
using DynamicDungeon.Runtime.Placement;
using UnityEngine;

namespace DynamicDungeon.Runtime.Output
{
    public sealed class GeneratedPrefabWriter
    {
        private const string RootName = "GeneratedPrefabs";

        private Transform _generatedRoot;

        public void EnsureRoot(Transform parent)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (_generatedRoot != null && _generatedRoot.parent == parent)
            {
                return;
            }

            Transform existing = parent.Find(RootName);
            if (existing != null)
            {
                _generatedRoot = existing;
                return;
            }

            GameObject rootObject = new GameObject(RootName);
            rootObject.transform.SetParent(parent, false);
            _generatedRoot = rootObject.transform;
        }

        public void ClearAll()
        {
            if (_generatedRoot == null)
            {
                return;
            }

            for (int index = _generatedRoot.childCount - 1; index >= 0; index--)
            {
                Transform child = _generatedRoot.GetChild(index);
                if (child == null)
                {
                    continue;
                }

                PrefabInstanceUtility.DestroyObject(child.gameObject);
            }
        }

        public void WritePrefab(GameObject prefab, Vector3 worldPosition, Quaternion rotation, Vector3 mirrorScale)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            if (_generatedRoot == null)
            {
                throw new InvalidOperationException("Generated prefab root has not been created.");
            }

            GameObject instance = PrefabInstanceUtility.InstantiatePrefab(prefab, _generatedRoot, false);
            Transform instanceTransform = instance.transform;
            instanceTransform.position = worldPosition;
            instanceTransform.rotation = rotation;

            Vector3 baseScale = instanceTransform.localScale;
            instanceTransform.localScale = new Vector3(
                baseScale.x * mirrorScale.x,
                baseScale.y * mirrorScale.y,
                baseScale.z * mirrorScale.z);
        }
    }
}
