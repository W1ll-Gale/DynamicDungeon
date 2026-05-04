using DynamicDungeon.ConstraintDungeon.Solver;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DynamicDungeon.ConstraintDungeon
{
    public static class DungeonRoomInstantiator
    {
        public static GameObject InstantiateRoom(PlacedRoom data, Transform parent, float tileSize = 1f)
        {
            if (data == null || data.sourcePrefab == null)
            {
                return null;
            }

            GameObject instance;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                instance = (GameObject)PrefabUtility.InstantiatePrefab(data.sourcePrefab, parent);
                if (PrefabUtility.IsPartOfPrefabInstance(instance))
                {
                    PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                }
            }
            else
            {
                instance = Object.Instantiate(data.sourcePrefab, parent);
            }
#else
            instance = Object.Instantiate(data.sourcePrefab, parent);
#endif

            InitialiseInstance(instance, data, tileSize);
            return instance;
        }

        public static void InitialiseInstance(GameObject instance, PlacedRoom data, float tileSize = 1f)
        {
            if (instance == null || data == null)
            {
                return;
            }

            instance.name = data.node.displayName;
            instance.transform.localPosition = new Vector3(
                data.position.x - data.variant.pivotOffset.x,
                data.position.y - data.variant.pivotOffset.y,
                0) * tileSize;

            Vector3 scale = Vector3.one;
            if (data.variant.mirrored)
            {
                scale.x = -1;
            }

            instance.transform.localScale = scale;
            instance.transform.localRotation = Quaternion.Euler(0, 0, -90f * data.variant.rotation);

            RoomTemplateComponent component = instance.GetComponent<RoomTemplateComponent>();
            if (component != null)
            {
                component.InitialiseRoom(data);
                DestroyComponent(component);
            }
        }

        public static void DestroyObject(GameObject obj)
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

        private static void DestroyComponent(Component component)
        {
            if (component == null)
            {
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Object.DestroyImmediate(component);
                return;
            }
#endif

            Object.Destroy(component);
        }
    }
}
