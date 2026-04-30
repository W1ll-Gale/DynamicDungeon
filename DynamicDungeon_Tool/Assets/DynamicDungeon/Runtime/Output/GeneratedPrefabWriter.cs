using System;
using DynamicDungeon.Runtime.Placement;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

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

                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
                else
                {
#if UNITY_EDITOR
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
#else
                    UnityEngine.Object.Destroy(child.gameObject);
#endif
                }
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

            GameObject instance = InstantiatePrefab(prefab);
            Transform instanceTransform = instance.transform;
            instanceTransform.position = worldPosition;
            instanceTransform.rotation = rotation;

            Vector3 baseScale = instanceTransform.localScale;
            instanceTransform.localScale = new Vector3(
                baseScale.x * mirrorScale.x,
                baseScale.y * mirrorScale.y,
                baseScale.z * mirrorScale.z);
        }

        private GameObject InstantiatePrefab(GameObject prefab)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                return (GameObject)PrefabUtility.InstantiatePrefab(prefab, _generatedRoot);
            }
#endif

            return UnityEngine.Object.Instantiate(prefab, _generatedRoot);
        }
    }
}
