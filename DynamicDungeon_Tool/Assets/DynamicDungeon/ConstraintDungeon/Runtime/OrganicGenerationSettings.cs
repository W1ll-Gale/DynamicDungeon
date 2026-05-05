using System.Collections.Generic;
using UnityEngine;

namespace DynamicDungeon.ConstraintDungeon
{
    public enum DungeonGenerationMode
    {
        FlowGraph,
        OrganicGrowth
    }

    [System.Serializable]
    public sealed class TemplateEntry
    {
        public GameObject prefab;
        public bool enabled = true;
        [Min(0f)] public float weight = 1f;
        [Min(0)] public int requiredMinimumCount = 0;
        [Min(0)] public int maximumCount = 0;
        public bool useDirectionBias = false;
        public FacingDirection preferredDirection = FacingDirection.East;
        [Range(0f, 1f)] public float directionBias = 0.5f;
    }

    public enum OrganicBranchingBias
    {
        Balanced,
        Straighter,
        Branchier
    }

    [CreateAssetMenu(fileName = "NewOrganicGrowthProfile", menuName = ConstraintDungeonMenuPaths.OrganicGrowthProfileAssetMenu)]
    public sealed class OrganicGenerationSettings : ScriptableObject
    {
        public GameObject startPrefab;
        public GameObject endPrefab;
        public List<TemplateEntry> templates = new List<TemplateEntry>();
        [Min(0)] public int targetRoomCount = 10;
        public bool useRoomCountRange = false;
        [Min(0)] public int minRoomCount = 10;
        [Min(0)] public int maxRoomCount = 10;
        [Range(0f, 1f)] public float corridorChance = 0.5f;
        [Min(0)] public int maxCorridorChain = 3;
        [Range(0f, 1f)] public float branchingProbability = 0.3f;
        public OrganicBranchingBias branchingBias = OrganicBranchingBias.Balanced;
        [Range(0f, 1f)] public float branchingBiasStrength = 0.5f;
        public bool useDirectionalGrowthHeuristic = false;
        public FacingDirection preferredGrowthDirection = FacingDirection.East;
        [Range(0f, 1f)] public float directionalGrowthBias = 0.75f;
        public bool useGlobalTemplateDirectionBias = false;
        public FacingDirection globalTemplateDirection = FacingDirection.East;
        [Range(0f, 1f)] public float globalTemplateDirectionBias = 0.5f;

        public void EnsureValidState()
        {
            if (templates == null)
            {
                templates = new List<TemplateEntry>();
            }

            minRoomCount = Mathf.Max(0, minRoomCount);
            maxRoomCount = Mathf.Max(minRoomCount, maxRoomCount);
        }

        public bool HasAnyTemplate()
        {
            EnsureValidState();

            if (IsUnityObjectAlive(startPrefab) || IsUnityObjectAlive(endPrefab))
            {
                return true;
            }

            foreach (TemplateEntry entry in templates)
            {
                if (entry != null && IsUnityObjectAlive(entry.prefab))
                {
                    return true;
                }
            }

            return false;
        }

        public TemplateEntry GetEntry(GameObject prefab)
        {
            if (!IsAssigned(prefab) || templates == null)
            {
                return null;
            }

            foreach (TemplateEntry entry in templates)
            {
                if (entry != null && ReferenceEquals(entry.prefab, prefab))
                {
                    return entry;
                }
            }

            return null;
        }

        public float GetWeight(GameObject prefab)
        {
            TemplateEntry entry = GetEntry(prefab);
            return entry != null ? Mathf.Max(0f, entry.weight) : 0f;
        }

        public float GetDirectionalWeight(GameObject prefab, FacingDirection growthDirection)
        {
            float baseWeight = GetWeight(prefab);
            TemplateEntry entry = GetEntry(prefab);
            if (entry == null || baseWeight <= 0f)
            {
                return baseWeight;
            }

            if (entry.useDirectionBias)
            {
                return ApplyDirectionBias(baseWeight, entry.preferredDirection, entry.directionBias, growthDirection);
            }

            if (useGlobalTemplateDirectionBias)
            {
                return ApplyDirectionBias(baseWeight, globalTemplateDirection, globalTemplateDirectionBias, growthDirection);
            }

            return baseWeight;
        }

        private static float ApplyDirectionBias(float baseWeight, FacingDirection preferredDirection, float directionBias, FacingDirection growthDirection)
        {
            float bias = Mathf.Clamp01(directionBias);
            return preferredDirection == growthDirection
                ? baseWeight * Mathf.Lerp(1f, 3f, bias)
                : baseWeight * Mathf.Lerp(1f, 0.25f, bias);
        }

        public int GetResolvedTargetRoomCount(System.Random random)
        {
            if (!useRoomCountRange)
            {
                return Mathf.Max(0, targetRoomCount);
            }

            int min = Mathf.Max(0, minRoomCount);
            int max = Mathf.Max(min, maxRoomCount);
            return random != null ? random.Next(min, max + 1) : min;
        }

        public int GetMaximumTargetRoomCount()
        {
            return useRoomCountRange ? Mathf.Max(minRoomCount, maxRoomCount) : Mathf.Max(0, targetRoomCount);
        }

        public int GetMinimumTargetRoomCount()
        {
            return useRoomCountRange ? Mathf.Min(minRoomCount, maxRoomCount) : Mathf.Max(0, targetRoomCount);
        }

        public int GetRequiredMinimum(GameObject prefab)
        {
            TemplateEntry entry = GetEntry(prefab);
            return entry != null ? Mathf.Max(0, entry.requiredMinimumCount) : 0;
        }

        public int GetMaximumCount(GameObject prefab)
        {
            TemplateEntry entry = GetEntry(prefab);
            return entry != null ? Mathf.Max(0, entry.maximumCount) : 0;
        }

        public bool IsEnabledForRandomSelection(GameObject prefab)
        {
            TemplateEntry entry = GetEntry(prefab);
            return entry != null && entry.enabled && entry.weight > 0f;
        }

        public ValidationReport Validate()
        {
            EnsureValidState();

            ValidationReport report = new ValidationReport();
            if (targetRoomCount < 0)
            {
                report.AddError("Target room count cannot be negative.");
            }

            if (useRoomCountRange && maxRoomCount < minRoomCount)
            {
                report.AddError("Maximum room count must be greater than or equal to minimum room count.");
            }

            int requiredCountedRooms = 0;
            int explicitCountedCapacity = GetExplicitCountedCapacity();
            bool hasRepeatableCountedTemplateCandidate = false;
            int maximumTargetRoomCount = GetMaximumTargetRoomCount();
            int minimumTargetRoomCount = GetMinimumTargetRoomCount();

            foreach (TemplateEntry entry in templates)
            {
                if (entry == null || !IsAssigned(entry.prefab))
                {
                    continue;
                }

                int requiredMinimum = Mathf.Max(0, entry.requiredMinimumCount);
                int maximumCount = Mathf.Max(0, entry.maximumCount);
                bool countedTemplate = IsCountedTemplate(entry.prefab);
                if (countedTemplate && entry.enabled && entry.weight > 0f && !ReferenceEquals(endPrefab, entry.prefab))
                {
                    hasRepeatableCountedTemplateCandidate = true;
                }

                if (requiredMinimum <= 0)
                {
                    continue;
                }

                if (!countedTemplate)
                {
                    report.AddWarning($"Template '{entry.prefab.name}' has a required minimum but is a corridor, so it does not count toward the target room total.");
                }

                if (!entry.enabled)
                {
                    report.AddError($"Template '{entry.prefab.name}' has a required minimum but is disabled.");
                }

                if (entry.weight <= 0f)
                {
                    report.AddError($"Template '{entry.prefab.name}' has a required minimum but its random-selection weight is zero.");
                }

                if (maximumCount > 0 && requiredMinimum > maximumCount)
                {
                    report.AddError($"Template '{entry.prefab.name}' requires {requiredMinimum} placement(s), but its maximum count is {maximumCount}.");
                }

                if (ReferenceEquals(endPrefab, entry.prefab) && countedTemplate && requiredMinimum > 1)
                {
                    report.AddError($"End template '{entry.prefab.name}' can only satisfy one required counted room placement.");
                }

                if (countedTemplate)
                {
                    requiredCountedRooms += requiredMinimum;
                }
            }

            if (maximumTargetRoomCount > explicitCountedCapacity && !hasRepeatableCountedTemplateCandidate)
            {
                report.AddError("Organic generation needs at least one enabled repeatable non-corridor template with positive weight when the target room count is larger than the assigned start/end room capacity.");
            }

            if (requiredCountedRooms > Mathf.Max(0, minimumTargetRoomCount))
            {
                report.AddError($"Required counted room minimums total {requiredCountedRooms}, which exceeds the minimum target room count of {Mathf.Max(0, minimumTargetRoomCount)}.");
            }

            AddSocketCompatibilityWarnings(report);

            return report;
        }

        public static bool IsAssigned(GameObject prefab)
        {
            return !ReferenceEquals(prefab, null);
        }

        private static bool IsUnityObjectAlive(GameObject prefab)
        {
            return prefab != null;
        }

        private static bool IsCountedTemplate(GameObject prefab)
        {
            if (prefab == null)
            {
                return false;
            }

            RoomTemplateComponent component = prefab.GetComponent<RoomTemplateComponent>();
            return component != null && component.roomType != RoomType.Corridor;
        }

        private int GetExplicitCountedCapacity()
        {
            int capacity = 0;
            if (IsCountedTemplate(startPrefab))
            {
                capacity++;
            }

            if (IsCountedTemplate(endPrefab) && !ReferenceEquals(startPrefab, endPrefab))
            {
                capacity++;
            }

            return capacity;
        }

        private void AddSocketCompatibilityWarnings(ValidationReport report)
        {
            List<GameObject> prefabs = new List<GameObject>();
            AddPrefabIfMissing(prefabs, startPrefab);
            AddPrefabIfMissing(prefabs, endPrefab);

            foreach (TemplateEntry entry in templates)
            {
                if (entry != null && entry.enabled)
                {
                    AddPrefabIfMissing(prefabs, entry.prefab);
                }
            }

            List<DoorSignature> signatures = new List<DoorSignature>();
            foreach (GameObject prefab in prefabs)
            {
                RoomTemplateComponent component = prefab != null ? prefab.GetComponent<RoomTemplateComponent>() : null;
                if (component == null)
                {
                    continue;
                }

#if UNITY_EDITOR
                RoomTemplateBaker.Bake(component);
#endif

                if (component.bakedData == null || component.bakedData.anchors == null)
                {
                    continue;
                }

                foreach (DoorAnchor anchor in component.bakedData.anchors)
                {
                    signatures.Add(new DoorSignature(prefab, anchor.socketType, anchor.size, anchor.direction));
                }
            }

            HashSet<string> warned = new HashSet<string>();
            foreach (DoorSignature signature in signatures)
            {
                bool hasMatch = false;
                foreach (DoorSignature candidate in signatures)
                {
                    bool canUseCandidate = !ReferenceEquals(candidate.Prefab, signature.Prefab) || CanTemplateConnectToAnotherCopy(signature.Prefab);
                    if (canUseCandidate &&
                        candidate.SocketType == signature.SocketType &&
                        candidate.Size == signature.Size &&
                        candidate.Direction == GetOppositeDirection(signature.Direction))
                    {
                        hasMatch = true;
                        break;
                    }
                }

                string warningKey = $"{signature.Prefab.GetInstanceID()}|{signature.SocketType}|{signature.Size}|{signature.Direction}";
                if (!hasMatch && warned.Add(warningKey))
                {
                    report.AddWarning($"Template '{signature.Prefab.name}' has a {signature.Direction} {signature.SocketType} size {signature.Size} door with no compatible opposite door in the organic pool.");
                }
            }
        }

        private bool CanTemplateConnectToAnotherCopy(GameObject prefab)
        {
            TemplateEntry entry = GetEntry(prefab);
            return entry != null &&
                   entry.enabled &&
                   entry.weight > 0f &&
                   Mathf.Max(0, entry.maximumCount) != 1;
        }

        private static void AddPrefabIfMissing(List<GameObject> prefabs, GameObject prefab)
        {
            if (!IsAssigned(prefab))
            {
                return;
            }

            foreach (GameObject existing in prefabs)
            {
                if (ReferenceEquals(existing, prefab))
                {
                    return;
                }
            }

            prefabs.Add(prefab);
        }

        private static FacingDirection GetOppositeDirection(FacingDirection direction)
        {
            return direction switch
            {
                FacingDirection.North => FacingDirection.South,
                FacingDirection.South => FacingDirection.North,
                FacingDirection.East => FacingDirection.West,
                FacingDirection.West => FacingDirection.East,
                _ => FacingDirection.North
            };
        }

        private readonly struct DoorSignature
        {
            public readonly GameObject Prefab;
            public readonly string SocketType;
            public readonly int Size;
            public readonly FacingDirection Direction;

            public DoorSignature(GameObject prefab, string socketType, int size, FacingDirection direction)
            {
                Prefab = prefab;
                SocketType = socketType;
                Size = size;
                Direction = direction;
            }
        }
    }
}
