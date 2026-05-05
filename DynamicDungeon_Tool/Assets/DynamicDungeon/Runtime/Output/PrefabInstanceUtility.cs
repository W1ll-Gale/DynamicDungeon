using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DynamicDungeon.Runtime.Output
{
    public static class PrefabInstanceUtility
    {
        public static GameObject InstantiatePrefab(GameObject prefab, Transform parent, bool unpackPrefabInstanceInEditor)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                if (unpackPrefabInstanceInEditor &&
                    instance != null &&
                    PrefabUtility.IsPartOfPrefabInstance(instance))
                {
                    PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                }

                return instance;
            }
#endif

            return Object.Instantiate(prefab, parent);
        }

        public static void DestroyObject(Object obj)
        {
            if (obj == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Object.DestroyImmediate(obj);
                return;
            }
#endif

            Object.Destroy(obj);
        }
    }
}
