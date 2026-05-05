using System;
using System.Collections.Generic;
using System.Globalization;
using DynamicDungeon.Runtime.Semantic;
using UnityEngine;

namespace DynamicDungeon.Editor.Nodes
{
    internal enum SpatialQueryPreset
    {
        ExactTile,
        FourWaySurrounded,
        EightWaySurrounded,
        HorizontalRun,
        VerticalRun
    }

    internal static class SpatialQueryAuthoringUtility
    {
        [Serializable]
        private sealed class NeighbourConditionRecord
        {
            public Vector2Int Offset = Vector2Int.zero;
            public bool MatchById = true;
            public int LogicalId;
            public string TagName = string.Empty;
        }

        [Serializable]
        private sealed class NeighbourConditionCollection
        {
            public NeighbourConditionRecord[] Entries = Array.Empty<NeighbourConditionRecord>();
        }

        internal sealed class EditableNeighbourCondition
        {
            public Vector2Int Offset = Vector2Int.zero;
            public bool MatchById = true;
            public int LogicalId;
            public string TagName = string.Empty;

            public EditableNeighbourCondition Clone()
            {
                EditableNeighbourCondition clone = new EditableNeighbourCondition();
                clone.Offset = Offset;
                clone.MatchById = MatchById;
                clone.LogicalId = LogicalId;
                clone.TagName = TagName ?? string.Empty;
                return clone;
            }
        }

        public static List<EditableNeighbourCondition> ParseConditions(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return new List<EditableNeighbourCondition>();
            }

            try
            {
                NeighbourConditionCollection collection = JsonUtility.FromJson<NeighbourConditionCollection>(rawJson);
                if (collection == null || collection.Entries == null || collection.Entries.Length == 0)
                {
                    return new List<EditableNeighbourCondition>();
                }

                List<EditableNeighbourCondition> conditions = new List<EditableNeighbourCondition>(collection.Entries.Length);
                int index;
                for (index = 0; index < collection.Entries.Length; index++)
                {
                    NeighbourConditionRecord record = collection.Entries[index];
                    EditableNeighbourCondition condition = new EditableNeighbourCondition();

                    if (record != null)
                    {
                        condition.Offset = record.Offset;
                        condition.MatchById = record.MatchById;
                        condition.LogicalId = Mathf.Max(0, record.LogicalId);
                        condition.TagName = record.TagName ?? string.Empty;
                    }

                    conditions.Add(condition);
                }

                return conditions;
            }
            catch
            {
                return new List<EditableNeighbourCondition>();
            }
        }

        public static string SerialiseConditions(IReadOnlyList<EditableNeighbourCondition> conditions)
        {
            NeighbourConditionCollection collection = new NeighbourConditionCollection();
            if (conditions == null || conditions.Count == 0)
            {
                collection.Entries = Array.Empty<NeighbourConditionRecord>();
                return JsonUtility.ToJson(collection);
            }

            NeighbourConditionRecord[] entries = new NeighbourConditionRecord[conditions.Count];
            int index;
            for (index = 0; index < conditions.Count; index++)
            {
                EditableNeighbourCondition condition = conditions[index] ?? new EditableNeighbourCondition();
                NeighbourConditionRecord record = new NeighbourConditionRecord();
                record.Offset = condition.Offset;
                record.MatchById = condition.MatchById;
                record.LogicalId = Mathf.Max(0, condition.LogicalId);
                record.TagName = condition.TagName ?? string.Empty;
                entries[index] = record;
            }

            collection.Entries = entries;
            return JsonUtility.ToJson(collection);
        }

        public static List<EditableNeighbourCondition> CreatePreset(SpatialQueryPreset preset, bool matchById, int logicalId, string tagName)
        {
            Vector2Int[] offsets;
            if (preset == SpatialQueryPreset.ExactTile)
            {
                offsets = new[] { Vector2Int.zero };
            }
            else if (preset == SpatialQueryPreset.FourWaySurrounded)
            {
                offsets = new[]
                {
                    Vector2Int.zero,
                    Vector2Int.up,
                    Vector2Int.down,
                    Vector2Int.left,
                    Vector2Int.right
                };
            }
            else if (preset == SpatialQueryPreset.EightWaySurrounded)
            {
                offsets = new[]
                {
                    Vector2Int.zero,
                    new Vector2Int(-1, -1),
                    new Vector2Int(0, -1),
                    new Vector2Int(1, -1),
                    new Vector2Int(-1, 0),
                    new Vector2Int(1, 0),
                    new Vector2Int(-1, 1),
                    new Vector2Int(0, 1),
                    new Vector2Int(1, 1)
                };
            }
            else if (preset == SpatialQueryPreset.HorizontalRun)
            {
                offsets = new[]
                {
                    Vector2Int.zero,
                    Vector2Int.left,
                    Vector2Int.right
                };
            }
            else
            {
                offsets = new[]
                {
                    Vector2Int.zero,
                    Vector2Int.up,
                    Vector2Int.down
                };
            }

            List<EditableNeighbourCondition> conditions = new List<EditableNeighbourCondition>(offsets.Length);
            int index;
            for (index = 0; index < offsets.Length; index++)
            {
                EditableNeighbourCondition condition = new EditableNeighbourCondition();
                condition.Offset = offsets[index];
                condition.MatchById = matchById;
                condition.LogicalId = Mathf.Max(0, logicalId);
                condition.TagName = tagName ?? string.Empty;
                conditions.Add(condition);
            }

            return conditions;
        }

        public static string BuildSummary(IReadOnlyList<EditableNeighbourCondition> conditions, TileSemanticRegistry registry)
        {
            if (conditions == null || conditions.Count == 0)
            {
                return "No conditions";
            }

            string countText = conditions.Count.ToString(CultureInfo.InvariantCulture) + " conditions";
            EditableNeighbourCondition centreCondition = FindConditionAtOffset(conditions, Vector2Int.zero);
            string centreText = centreCondition != null
                ? "centre=" + BuildMatcherLabel(centreCondition, registry)
                : string.Empty;

            string neighbourText = string.Empty;
            if (TryBuildNeighbourSummary(conditions, registry, new[]
                {
                    Vector2Int.up,
                    Vector2Int.down,
                    Vector2Int.left,
                    Vector2Int.right
                },
                "4-way neighbours",
                out neighbourText))
            {
                return CombineSummaryParts(countText, centreText, neighbourText);
            }

            if (TryBuildNeighbourSummary(conditions, registry, new[]
                {
                    new Vector2Int(-1, -1),
                    new Vector2Int(0, -1),
                    new Vector2Int(1, -1),
                    new Vector2Int(-1, 0),
                    new Vector2Int(1, 0),
                    new Vector2Int(-1, 1),
                    new Vector2Int(0, 1),
                    new Vector2Int(1, 1)
                },
                "8-way neighbours",
                out neighbourText))
            {
                return CombineSummaryParts(countText, centreText, neighbourText);
            }

            return CombineSummaryParts(countText, centreText, "custom pattern");
        }

        public static string BuildMatcherLabel(bool matchById, int logicalId, string tagName, TileSemanticRegistry registry)
        {
            if (matchById)
            {
                return BuildLogicalIdLabel(logicalId, registry);
            }

            string safeTagName = tagName ?? string.Empty;
            return safeTagName.Length == 0 ? "Tag" : "#" + safeTagName;
        }

        public static string BuildMatcherLabel(EditableNeighbourCondition condition, TileSemanticRegistry registry)
        {
            if (condition == null)
            {
                return "Unspecified";
            }

            return BuildMatcherLabel(condition.MatchById, condition.LogicalId, condition.TagName, registry);
        }

        public static string BuildLogicalIdLabel(int logicalId, TileSemanticRegistry registry)
        {
            ushort resolvedLogicalId = (ushort)Mathf.Clamp(logicalId, 0, ushort.MaxValue);
            if (registry != null && registry.TryGetEntry(resolvedLogicalId, out TileEntry entry) && entry != null)
            {
                string displayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? "Unnamed" : entry.DisplayName;
                return displayName + " (" + logicalId.ToString(CultureInfo.InvariantCulture) + ")";
            }

            return logicalId.ToString(CultureInfo.InvariantCulture);
        }

        private static EditableNeighbourCondition FindConditionAtOffset(IReadOnlyList<EditableNeighbourCondition> conditions, Vector2Int offset)
        {
            int index;
            for (index = 0; index < conditions.Count; index++)
            {
                EditableNeighbourCondition condition = conditions[index];
                if (condition != null && condition.Offset == offset)
                {
                    return condition;
                }
            }

            return null;
        }

        private static bool TryBuildNeighbourSummary(
            IReadOnlyList<EditableNeighbourCondition> conditions,
            TileSemanticRegistry registry,
            IReadOnlyList<Vector2Int> requiredOffsets,
            string label,
            out string neighbourSummary)
        {
            neighbourSummary = string.Empty;
            EditableNeighbourCondition referenceCondition = null;

            int offsetIndex;
            for (offsetIndex = 0; offsetIndex < requiredOffsets.Count; offsetIndex++)
            {
                EditableNeighbourCondition condition = FindConditionAtOffset(conditions, requiredOffsets[offsetIndex]);
                if (condition == null)
                {
                    return false;
                }

                if (referenceCondition == null)
                {
                    referenceCondition = condition;
                    continue;
                }

                if (!TargetsMatch(referenceCondition, condition))
                {
                    return false;
                }
            }

            if (referenceCondition == null)
            {
                return false;
            }

            neighbourSummary = label + "=" + BuildMatcherLabel(referenceCondition, registry);
            return true;
        }

        private static bool TargetsMatch(EditableNeighbourCondition left, EditableNeighbourCondition right)
        {
            if (left == null || right == null || left.MatchById != right.MatchById)
            {
                return false;
            }

            if (left.MatchById)
            {
                return left.LogicalId == right.LogicalId;
            }

            return string.Equals(left.TagName ?? string.Empty, right.TagName ?? string.Empty, StringComparison.Ordinal);
        }

        private static string CombineSummaryParts(string first, string second, string third)
        {
            List<string> parts = new List<string>(3);
            if (!string.IsNullOrWhiteSpace(first))
            {
                parts.Add(first);
            }

            if (!string.IsNullOrWhiteSpace(second))
            {
                parts.Add(second);
            }

            if (!string.IsNullOrWhiteSpace(third))
            {
                parts.Add(third);
            }

            return string.Join(" • ", parts);
        }
    }
}
