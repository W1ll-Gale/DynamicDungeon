using System.Collections.Generic;
using DynamicDungeon.ConstraintDungeon.Solver;
using DynamicDungeon.ConstraintDungeon.Utils;
using UnityEngine;

namespace DynamicDungeon.ConstraintDungeon
{
    public sealed class TemplateCatalog
    {
        public sealed class PreparedVariant
        {
            public RoomVariant Variant;
            public readonly Dictionary<DoorAnchor, List<Vector2Int>> DoorBasePositions = new Dictionary<DoorAnchor, List<Vector2Int>>();
            public readonly Dictionary<string, List<DoorAnchor>> CompatibleAnchorsByOtherDoorKey = new Dictionary<string, List<DoorAnchor>>();
        }

        public readonly List<GameObject> AllTemplates = new List<GameObject>();
        public readonly Dictionary<GameObject, List<RoomVariant>> Cache = new Dictionary<GameObject, List<RoomVariant>>(ReferenceEqualityComparer<GameObject>.Instance);
        public readonly Dictionary<GameObject, TemplateMetadata> Metadata = new Dictionary<GameObject, TemplateMetadata>(ReferenceEqualityComparer<GameObject>.Instance);
        public readonly Dictionary<RoomVariant, PreparedVariant> PreparedVariants = new Dictionary<RoomVariant, PreparedVariant>();
        public readonly List<GameObject> RoomTemplates = new List<GameObject>();
        public readonly List<GameObject> CorridorTemplates = new List<GameObject>();
        public readonly List<GameObject> EntranceTemplates = new List<GameObject>();
        public readonly ValidationReport Report = new ValidationReport();
        private readonly Dictionary<string, List<DoorAnchor>> compatibleDoorsByKey = new Dictionary<string, List<DoorAnchor>>();
        private readonly Dictionary<DoorAnchor, List<Vector2Int>> doorBasePositions = new Dictionary<DoorAnchor, List<Vector2Int>>();

        public IEnumerable<string> Errors => Report.Errors;
        public IEnumerable<string> Warnings => Report.Warnings;
        public bool HasErrors => !Report.IsValid;

        public List<Vector2Int> GetDoorBasePositions(DoorAnchor anchor)
        {
            return doorBasePositions.TryGetValue(anchor, out List<Vector2Int> positions)
                ? positions
                : anchor.GetPossibleBasePositions();
        }

        public IReadOnlyList<DoorAnchor> GetCompatibleDoors(DoorAnchor anchor)
        {
            return compatibleDoorsByKey.TryGetValue(GetOppositeCompatibilityKey(anchor), out List<DoorAnchor> anchors)
                ? anchors
                : System.Array.Empty<DoorAnchor>();
        }

        internal bool TryGetCompatibleDoors(RoomVariant variant, DoorAnchor otherDoor, out IReadOnlyList<DoorAnchor> anchors)
        {
            if (variant != null &&
                PreparedVariants.TryGetValue(variant, out PreparedVariant prepared) &&
                prepared.CompatibleAnchorsByOtherDoorKey.TryGetValue(GetCompatibilityKey(otherDoor), out List<DoorAnchor> compatibleAnchors))
            {
                anchors = compatibleAnchors;
                return true;
            }

            if (variant != null && PreparedVariants.ContainsKey(variant))
            {
                anchors = System.Array.Empty<DoorAnchor>();
                return true;
            }

            anchors = System.Array.Empty<DoorAnchor>();
            return false;
        }

        public int CompatibleDoorIndexCount => compatibleDoorsByKey.Count;

        internal void RegisterTemplate(GameObject template, List<RoomVariant> variants)
        {
            AllTemplates.Add(template);

            foreach (RoomVariant variant in variants)
            {
                PreparedVariant prepared = new PreparedVariant { Variant = variant };
                PreparedVariants[variant] = prepared;

                foreach (DoorAnchor anchor in variant.anchors)
                {
                    List<Vector2Int> positions = anchor.GetPossibleBasePositions();
                    prepared.DoorBasePositions[anchor] = positions;
                    doorBasePositions[anchor] = positions;

                    string key = GetCompatibilityKey(anchor);
                    if (!compatibleDoorsByKey.TryGetValue(key, out List<DoorAnchor> anchors))
                    {
                        anchors = new List<DoorAnchor>();
                        compatibleDoorsByKey[key] = anchors;
                    }

                    anchors.Add(anchor);

                    string otherDoorKey = GetOppositeCompatibilityKey(anchor);
                    if (!prepared.CompatibleAnchorsByOtherDoorKey.TryGetValue(otherDoorKey, out List<DoorAnchor> compatibleAnchors))
                    {
                        compatibleAnchors = new List<DoorAnchor>();
                        prepared.CompatibleAnchorsByOtherDoorKey[otherDoorKey] = compatibleAnchors;
                    }

                    compatibleAnchors.Add(anchor);
                }
            }
        }

        private static string GetCompatibilityKey(DoorAnchor anchor)
        {
            return $"{anchor.socketType}|{anchor.size}|{anchor.direction}";
        }

        private static string GetOppositeCompatibilityKey(DoorAnchor anchor)
        {
            return $"{anchor.socketType}|{anchor.size}|{anchor.GetOppositeDirection()}";
        }
    }

    public static class TemplatePreparer
    {
        public static TemplateCatalog PrepareForFlow(DungeonFlow flow)
        {
            TemplateCatalog catalog = new TemplateCatalog();
            HashSet<GameObject> templates = new HashSet<GameObject>(ReferenceEqualityComparer<GameObject>.Instance);

            if (flow == null)
            {
                return catalog;
            }

            foreach (RoomNode node in flow.nodes)
            {
                AddTemplates(node.allowedTemplates, templates);
            }

            foreach (DefaultTemplateMapping mapping in flow.defaultTemplates)
            {
                AddTemplates(mapping.templates, templates);
            }

            BuildCache(templates, catalog);
            return catalog;
        }

        public static TemplateCatalog PrepareForOrganic(OrganicGenerationSettings settings)
        {
            TemplateCatalog catalog = new TemplateCatalog();
            HashSet<GameObject> templates = new HashSet<GameObject>(ReferenceEqualityComparer<GameObject>.Instance);

            if (settings == null)
            {
                return catalog;
            }

            settings.EnsureValidState();

            AddTemplate(settings.startPrefab, templates);
            AddTemplate(settings.endPrefab, templates);

            if (settings.templates != null)
            {
                foreach (TemplateEntry entry in settings.templates)
                {
                    if (entry != null && entry.enabled)
                    {
                        AddTemplate(entry.prefab, templates);
                    }
                }
            }

            BuildCache(templates, catalog);
            return catalog;
        }

        private static void AddTemplate(GameObject template, HashSet<GameObject> templates)
        {
            if (template != null)
            {
                templates.Add(template);
            }
        }

        private static void AddTemplates(List<GameObject> source, HashSet<GameObject> templates)
        {
            if (source == null)
            {
                return;
            }

            foreach (GameObject template in source)
            {
                if (template != null)
                {
                    templates.Add(template);
                }
            }
        }

        private static void BuildCache(HashSet<GameObject> templates, TemplateCatalog catalog)
        {
            foreach (GameObject template in templates)
            {
                RoomTemplateComponent component = template.GetComponent<RoomTemplateComponent>();
                if (component == null)
                {
                    catalog.Report.AddError($"Template '{template.name}' has no RoomTemplateComponent.");
                    continue;
                }

#if UNITY_EDITOR
                RoomTemplateBaker.Bake(component);
#endif

                if (component.floorMap == null)
                {
                    catalog.Report.AddError($"Template '{template.name}' is missing a floor tilemap.");
                    continue;
                }

                if (component.bakedData == null || component.bakedData.cells.Count == 0)
                {
                    catalog.Report.AddError($"Template '{template.name}' has no baked floor or wall cells.");
                    continue;
                }

                if (component.bakedData.anchors.Count == 0)
                {
                    catalog.Report.AddWarning($"Template '{template.name}' has no door anchors.");
                }

                List<RoomVariant> variants = component.bakedData.GenerateVariants();
                if (variants.Count == 0)
                {
                    catalog.Report.AddError($"Template '{template.name}' produced no usable room variants.");
                    continue;
                }

                catalog.Cache[template] = variants;
                catalog.RegisterTemplate(template, variants);

                TemplateMetadata metadata = new TemplateMetadata { name = template.name, type = component.roomType };
                catalog.Metadata[template] = metadata;

                if (component.roomType == RoomType.Corridor)
                {
                    catalog.CorridorTemplates.Add(template);
                }
                else if (component.roomType == RoomType.Entrance)
                {
                    catalog.EntranceTemplates.Add(template);
                }
                else
                {
                    catalog.RoomTemplates.Add(template);
                }
            }
        }
    }

    public static class DungeonTemplatePreparer
    {
        public static TemplateCatalog PrepareForFlow(DungeonFlow flow)
        {
            return TemplatePreparer.PrepareForFlow(flow);
        }

        public static TemplateCatalog PrepareForOrganic(OrganicGenerationSettings settings)
        {
            return TemplatePreparer.PrepareForOrganic(settings);
        }
    }
}
