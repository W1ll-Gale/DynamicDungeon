using UnityEngine;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Output;
using DynamicDungeon.Runtime.Placement;
using UnityEngine.Tilemaps;

namespace DynamicDungeon.ConstraintDungeon
{
#if UNITY_EDITOR
    internal static class RoomTemplateDoorGizmoState
    {
        private static readonly HashSet<int> ActiveEditorInstanceIds = new HashSet<int>();

        public static void SetEditing(RoomTemplateComponent room, bool isEditing)
        {
            if (room == null)
            {
                return;
            }

            int id = room.GetInstanceID();
            if (isEditing)
            {
                ActiveEditorInstanceIds.Add(id);
            }
            else
            {
                ActiveEditorInstanceIds.Remove(id);
            }
        }

        public static bool IsEditing(RoomTemplateComponent room)
        {
            return room != null && ActiveEditorInstanceIds.Contains(room.GetInstanceID());
        }
    }
#endif

    [System.Serializable]
    public struct DoorTileData
    {
        public Vector2Int pos;
        public SocketTypeAsset socket;
        public int autoSize; // Used for hybrid/auto doors only

        public DoorTileData(Vector2Int p, SocketTypeAsset s, int size = 1)
        {
            pos = p;
            socket = s;
            autoSize = size;
        }
    }

    public class RoomTemplateComponent : MonoBehaviour, IPrefabPlacementInstanceProcessor
    {
        [Header("Room Properties")]
        public string roomName;
        public RoomType roomType = RoomType.Room;
        public bool allowRotation = true;
        public bool allowMirroring = true;
        public bool showGizmos = true;
        public bool keepTileOrientation = true;
        
        [Header("Door Settings")]
        public SocketTypeAsset activeSocket;
        public int fallbackDoorSize = 1;
        public bool showDoorSocketLabels = false;
        
        [HideInInspector] public List<DoorTileData> manualDoorPoints = new List<DoorTileData>();
        [HideInInspector] public List<DoorTileData> autoDoorPoints = new List<DoorTileData>();
        [HideInInspector] public bool hasSpawnPoint = false;
        [HideInInspector] public Vector2Int spawnPoint = Vector2Int.zero;
        
        [Header("Tilemap layers")]
        public Tilemap floorMap;
        public Tilemap wallMap;

        [HideInInspector] public RoomTemplateData bakedData = new RoomTemplateData();

#if UNITY_EDITOR
        private void OnValidate()
        {
            Bake();
        }

        public void Bake()
        {
            if (floorMap == null) return;

            bakedData.templateName = roomName;
            bakedData.allowRotation = allowRotation;
            bakedData.allowMirroring = allowMirroring;

            bakedData.cells.Clear();
            BoundsInt bounds = GetCombinedCellBounds();
            foreach (Vector3Int pos in bounds.allPositionsWithin)
            {
                if (floorMap.HasTile(pos))
                    bakedData.cells.Add(new CellData(new Vector2Int(pos.x, pos.y), TileType.Floor));
                else if (wallMap != null && wallMap.HasTile(pos))
                    bakedData.cells.Add(new CellData(new Vector2Int(pos.x, pos.y), TileType.Wall));
            }

            CleanDoorPoints(manualDoorPoints);
            CleanDoorPoints(autoDoorPoints);
            CleanSpawnPoint();
            bakedData.hasSpawnPoint = hasSpawnPoint;
            bakedData.spawnPoint = spawnPoint;

            bakedData.anchors.Clear();
            if (manualDoorPoints.Count == 0 && autoDoorPoints.Count == 0)
            {
                GenerateAutomaticWallAnchors();
                return;
            }

            GroupPointsToAnchors(manualDoorPoints, DoorMode.Absolute);
            GroupPointsToAnchors(autoDoorPoints, DoorMode.Hybrid);
        }

        private BoundsInt GetCombinedCellBounds()
        {
            BoundsInt floorBounds = floorMap.cellBounds;
            if (wallMap == null) return floorBounds;

            BoundsInt wallBounds = wallMap.cellBounds;
            int xMin = Mathf.Min(floorBounds.xMin, wallBounds.xMin);
            int yMin = Mathf.Min(floorBounds.yMin, wallBounds.yMin);
            int xMax = Mathf.Max(floorBounds.xMax, wallBounds.xMax);
            int yMax = Mathf.Max(floorBounds.yMax, wallBounds.yMax);

            return new BoundsInt(xMin, yMin, 0, xMax - xMin, yMax - yMin, 1);
        }

        private void CleanDoorPoints(List<DoorTileData> list)
        {
            if (list == null) return;
            list.RemoveAll(d => 
            {
                Vector3Int vp = new Vector3Int(d.pos.x, d.pos.y, 0);
                bool hasWall = wallMap != null && wallMap.HasTile(vp);
                bool hasFloor = floorMap != null && floorMap.HasTile(vp);
                
                if (!hasWall && !hasFloor) return true;
                
                if (hasFloor && !hasWall)
                {
                    bool adjToAir = false;
                    Vector3Int[] neighbours = { vp + Vector3Int.up, vp + Vector3Int.down, vp + Vector3Int.left, vp + Vector3Int.right };
                    foreach(Vector3Int neighbour in neighbours)
                        if (!floorMap.HasTile(neighbour) && (wallMap == null || !wallMap.HasTile(neighbour))) adjToAir = true;
                    if (!adjToAir) return true;
                }
                return false;
            });
        }

        private void CleanSpawnPoint()
        {
            if (!hasSpawnPoint || floorMap == null)
            {
                return;
            }

            Vector3Int cell = new Vector3Int(spawnPoint.x, spawnPoint.y, 0);
            if (!floorMap.HasTile(cell))
            {
                hasSpawnPoint = false;
                spawnPoint = Vector2Int.zero;
            }
        }

        private void GroupPointsToAnchors(List<DoorTileData> points, DoorMode mode)
        {
            if (points == null || points.Count == 0) return;

            HashSet<Vector2Int> standardPoints = new HashSet<Vector2Int>();
            Dictionary<SocketTypeAsset, HashSet<Vector2Int>> bySocket = new Dictionary<SocketTypeAsset, HashSet<Vector2Int>>();

            foreach(DoorTileData point in points)
            {
                if (point.socket == null) standardPoints.Add(point.pos);
                else
                {
                    if (!bySocket.ContainsKey(point.socket)) bySocket[point.socket] = new HashSet<Vector2Int>();
                    bySocket[point.socket].Add(point.pos);
                }
            }

            ProcessPointGrouping(standardPoints, null, points, mode);

            foreach(KeyValuePair<SocketTypeAsset, HashSet<Vector2Int>> pair in bySocket)
            {
                ProcessPointGrouping(pair.Value, pair.Key, points, mode);
            }
        }

        private void ProcessPointGrouping(HashSet<Vector2Int> remaining, SocketTypeAsset socket, List<DoorTileData> originalPoints, DoorMode mode)
        {
            while (remaining.Count > 0)
            {
                HashSet<Vector2Int>.Enumerator it = remaining.GetEnumerator();
                it.MoveNext();
                Vector2Int start = it.Current;
                remaining.Remove(start);

                List<Vector2Int> group = new List<Vector2Int> { start };
                ExtendGroup(start, remaining, group);

                if (group.Count > 0)
                {
                    DoorAnchor anchor = CreateAnchorFromGroup(group);
                    anchor.socketType = socket != null ? socket.name : "Standard";
                    anchor.mode = mode;

                    if (mode == DoorMode.Absolute)
                    {
                        anchor.size = group.Count;
                    }
                    else
                    {
                        // For Auto doors, find the size stored in the tiles
                        int size = 1;
                        foreach(DoorTileData point in originalPoints) if(point.pos == group[0]) { size = point.autoSize; break; }
                        anchor.size = size;
                    }

                    bakedData.anchors.Add(anchor);
                }
            }
        }

        private void ExtendGroup(Vector2Int start, HashSet<Vector2Int> remaining, List<Vector2Int> group)
        {
            Queue<Vector2Int> queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            while(queue.Count > 0)
            {
                Vector2Int point = queue.Dequeue();
                Vector2Int[] neighbours = { point + Vector2Int.up, point + Vector2Int.down, point + Vector2Int.left, point + Vector2Int.right };
                foreach(Vector2Int neighbour in neighbours)
                {
                    if (remaining.Contains(neighbour))
                    {
                        remaining.Remove(neighbour);
                        group.Add(neighbour);
                        queue.Enqueue(neighbour);
                    }
                }
            }
        }

        private DoorAnchor CreateAnchorFromGroup(List<Vector2Int> group)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach(Vector2Int point in group)
            {
                minX = Mathf.Min(minX, point.x); minY = Mathf.Min(minY, point.y);
                maxX = Mathf.Max(maxX, point.x); maxY = Mathf.Max(maxY, point.y);
            }

            RectInt area = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            
            FacingDirection dir = FacingDirection.North;
            foreach(Vector2Int point in group)
            {
                Vector3Int vp = new Vector3Int(point.x, point.y, 0);
                if (floorMap.HasTile(vp + Vector3Int.down)) { dir = FacingDirection.North; break; }
                if (floorMap.HasTile(vp + Vector3Int.up)) { dir = FacingDirection.South; break; }
                if (floorMap.HasTile(vp + Vector3Int.right)) { dir = FacingDirection.West; break; }
                if (floorMap.HasTile(vp + Vector3Int.left)) { dir = FacingDirection.East; break; }
            }

            return new DoorAnchor
            {
                area = area,
                direction = dir,
                locallyOccupiedCell = area.position
            };
        }

        private void GenerateAutomaticWallAnchors()
        {
            if (wallMap == null) return;

            List<Vector2Int> edgeWalls = new List<Vector2Int>();
            BoundsInt bounds = wallMap.cellBounds;
            foreach (Vector3Int pos in bounds.allPositionsWithin)
            {
                if (wallMap.HasTile(pos))
                {
                    bool adjToFloor = false;
                    Vector3Int[] neighbours = { pos + Vector3Int.up, pos + Vector3Int.down, pos + Vector3Int.left, pos + Vector3Int.right };
                    foreach (Vector3Int neighbour in neighbours) if (floorMap != null && floorMap.HasTile(neighbour)) adjToFloor = true;
                    if (adjToFloor) edgeWalls.Add(new Vector2Int(pos.x, pos.y));
                }
            }

            HashSet<Vector2Int> remaining = new HashSet<Vector2Int>(edgeWalls);
            while (remaining.Count > 0)
            {
                HashSet<Vector2Int>.Enumerator it = remaining.GetEnumerator();
                it.MoveNext();
                Vector2Int start = it.Current;
                remaining.Remove(start);

                List<Vector2Int> group = new List<Vector2Int> { start };
                ExtendGroup(start, remaining, group);

                if (group.Count > 0)
                {
                    DoorAnchor anchor = CreateAnchorFromGroup(group);
                    anchor.mode = DoorMode.Hybrid;
                    anchor.socketType = activeSocket != null ? activeSocket.name : "Standard";
                    anchor.size = fallbackDoorSize;
                    bakedData.anchors.Add(anchor);
                }
            }
        }
#endif

        public void InitialiseRoom(DynamicDungeon.ConstraintDungeon.Solver.PlacedRoom data)
        {
            RoomTemplateRuntimeInitializer.Initialise(this, data);
        }

        public void ProcessPrefabPlacement(PrefabPlacementRecord placement, PrefabPlacementMetadata metadata)
        {
            if (metadata == null || metadata.Type != RoomTemplatePlacementMutation.MetadataType || string.IsNullOrWhiteSpace(metadata.Payload))
            {
                return;
            }

            RoomTemplatePlacementMutation mutation = JsonUtility.FromJson<RoomTemplatePlacementMutation>(metadata.Payload);
            RoomTemplateRuntimeInitializer.ApplyMutation(this, mutation);
            PrefabInstanceUtility.DestroyObject(this);
        }

        private void OnDrawGizmosSelected()
        {
            if (!showGizmos || floorMap == null) return;
            
            // Set matrix so gizmos move and rotate with the tilemap
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Gizmos.matrix = floorMap.transform.localToWorldMatrix;

            DrawRoomPerimeter();
            DrawDoorGizmos();
            DrawSpawnPointGizmo();
            DrawValidityStatus();
            
#if UNITY_EDITOR
            DrawValidityOverlay();
#endif
            Gizmos.matrix = oldMatrix;
        }

#if UNITY_EDITOR
        private void DrawValidityOverlay()
        {
            RoomValidator.ValidationResult result = RoomValidator.Validate(this);
            UnityEditor.Handles.BeginGUI();
            Rect rect = new Rect(60, 10, 240, 60);
            GUI.Box(rect, "");
            GUIStyle style = new GUIStyle(UnityEditor.EditorStyles.boldLabel);
            bool isHovered = rect.Contains(Event.current.mousePosition);
            style.normal.textColor = result.isValid ? (isHovered ? Color.white : Color.green) : Color.red;
            style.fontSize = 14;
            style.alignment = TextAnchor.MiddleCenter;
            GUI.Label(new Rect(rect.x, rect.y + 5, rect.width, 25), result.isValid ? "ROOM VALID" : "ROOM INVALID", style);
            GUIStyle msgStyle = new GUIStyle(UnityEditor.EditorStyles.miniLabel);
            msgStyle.alignment = TextAnchor.MiddleCenter;
            msgStyle.wordWrap = true;
            GUI.Label(new Rect(rect.x + 5, rect.y + 30, rect.width - 10, 30), result.message, msgStyle);
            UnityEditor.Handles.EndGUI();
        }
#endif

        private void DrawSpawnPointGizmo()
        {
            if (!hasSpawnPoint)
            {
                return;
            }

            Vector3 centre = new Vector3(spawnPoint.x + 0.5f, spawnPoint.y + 0.5f, 0);
            Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.85f);
            Gizmos.DrawSphere(centre, 0.22f);
            Gizmos.DrawWireCube(centre, new Vector3(0.8f, 0.8f, 0.1f));
        }

        private void DrawRoomPerimeter()
        {
            Gizmos.color = Color.white;
            BoundsInt bounds = floorMap.cellBounds;
            foreach (Vector3Int pos in bounds.allPositionsWithin)
            {
                if (floorMap.HasTile(pos) || (wallMap != null && wallMap.HasTile(pos)))
                {
                    Vector3Int[] neighbours = { pos + Vector3Int.up, pos + Vector3Int.down, pos + Vector3Int.left, pos + Vector3Int.right };
                    foreach (Vector3Int neighbour in neighbours)
                        if (!floorMap.HasTile(neighbour) && (wallMap == null || !wallMap.HasTile(neighbour)))
                            DrawEdge(pos, neighbour);
                }
            }
        }

        private void DrawEdge(Vector3Int p1, Vector3Int p2)
        {
            // We are using Tilemap local space because of Gizmos.matrix
            Vector3 centre1 = floorMap.CellToLocal(p1) + floorMap.tileAnchor;
            Vector3 centre2 = floorMap.CellToLocal(p2) + floorMap.tileAnchor;
            Vector3 mid = (centre1 + centre2) * 0.5f;
            Vector3 diff = centre2 - centre1;
            Vector3 lineStart, lineEnd;
            if (Mathf.Abs(diff.x) > 0.1f) { lineStart = mid + Vector3.up * 0.5f; lineEnd = mid + Vector3.down * 0.5f; }
            else { lineStart = mid + Vector3.left * 0.5f; lineEnd = mid + Vector3.right * 0.5f; }
            Gizmos.DrawLine(lineStart, lineEnd);
        }

        private void DrawDoorGizmos()
        {
#if UNITY_EDITOR
            // Draw Highlights for points using their socket colors
            foreach (DoorTileData door in manualDoorPoints)
            {
                Gizmos.color = door.socket != null ? door.socket.gizmoColor : Color.cyan;
                Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, 0.4f);
                Gizmos.DrawCube(new Vector3(door.pos.x + 0.5f, door.pos.y + 0.5f, 0), new Vector3(1, 1, 0.1f));
            }

            foreach (DoorTileData door in autoDoorPoints)
            {
                Gizmos.color = door.socket != null ? door.socket.gizmoColor : Color.cyan;
                Gizmos.color = new Color(Gizmos.color.r, Gizmos.color.g, Gizmos.color.b, 0.2f);
                Gizmos.DrawCube(new Vector3(door.pos.x + 0.5f, door.pos.y + 0.5f, 0), new Vector3(1, 1, 0.1f));
            }
#endif

            if (bakedData != null && bakedData.anchors != null)
            {
                foreach (DoorAnchor anchor in bakedData.anchors)
                {
                    SocketTypeAsset asset = null;
                    foreach(DoorTileData point in manualDoorPoints)
                    {
                        if (point.socket != null && point.socket.name == anchor.socketType) { asset = point.socket; break; }
                    }
                    if (asset == null)
                    {
                        foreach(DoorTileData point in autoDoorPoints)
                        {
                            if (point.socket != null && point.socket.name == anchor.socketType) { asset = point.socket; break; }
                        }
                    }
                    if (asset == null && activeSocket != null && activeSocket.name == anchor.socketType) asset = activeSocket;

                    bool isStandard = anchor.socketType == "Standard";
                    Color baseColor = isStandard ? Color.cyan : (asset != null ? asset.gizmoColor : Color.green);

                    if (anchor.mode == DoorMode.Absolute)
                    {
                        Gizmos.color = baseColor;
                        DrawRectOutline(anchor.area);
                    }
                    else
                    {
                        Gizmos.color = baseColor;
                        DrawDashedRect(anchor.area, 0.05f);
                        
                        RectInt previewRect = anchor.area;
                        if (anchor.direction == FacingDirection.North || anchor.direction == FacingDirection.South) previewRect.width = anchor.size;
                        else previewRect.height = anchor.size;
                        
                        Gizmos.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.4f);
                        Gizmos.DrawCube(new Vector3(previewRect.center.x, previewRect.center.y, 0), new Vector3(previewRect.width, previewRect.height, 0.1f));
                    }
                    
                    Gizmos.color = Color.yellow;
                    Vector3 centre = new Vector3(anchor.area.center.x, anchor.area.center.y, 0);
                    Vector3 dir = new Vector3(anchor.GetDirectionVector().x, anchor.GetDirectionVector().y, 0);
                    Gizmos.DrawRay(centre, dir * 1.0f);

#if UNITY_EDITOR
                    if (showDoorSocketLabels && RoomTemplateDoorGizmoState.IsEditing(this))
                    {
                        bool labelHovered = IsDoorLabelHovered(anchor, centre);
                        if (labelHovered)
                        {
                            Gizmos.color = new Color(1f, 1f, 0.25f, 0.35f);
                            Gizmos.DrawCube(new Vector3(anchor.area.center.x, anchor.area.center.y, 0), new Vector3(anchor.area.width, anchor.area.height, 0.12f));
                            Gizmos.color = Color.yellow;
                            DrawRectOutline(anchor.area);
                        }

                        DrawDoorLabel(anchor, centre, baseColor, labelHovered);
                    }
#endif
                }
            }
        }

#if UNITY_EDITOR
        private bool IsDoorLabelHovered(DoorAnchor anchor, Vector3 localCentre)
        {
            return GetDoorLabelRect(anchor, localCentre, out _, out _).Contains(Event.current.mousePosition);
        }

        private void DrawDoorLabel(DoorAnchor anchor, Vector3 localCentre, Color socketColor, bool hovered)
        {
            Rect rect = GetDoorLabelRect(anchor, localCentre, out string label, out GUIStyle style);

            UnityEditor.Handles.BeginGUI();
            Color background = hovered ? new Color(1f, 0.92f, 0.2f, 0.95f) : new Color(0.02f, 0.02f, 0.02f, 0.82f);
            Color border = hovered ? Color.yellow : new Color(socketColor.r, socketColor.g, socketColor.b, 0.95f);
            UnityEditor.EditorGUI.DrawRect(rect, background);
            UnityEditor.EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), border);
            UnityEditor.EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), border);
            UnityEditor.EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), border);
            UnityEditor.EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), border);

            style.normal.textColor = hovered ? Color.black : Color.white;
            GUI.Label(rect, label, style);
            UnityEditor.Handles.EndGUI();
        }

        private Rect GetDoorLabelRect(DoorAnchor anchor, Vector3 localCentre, out string label, out GUIStyle style)
        {
            Vector3 labelPosition = floorMap.transform.TransformPoint(localCentre + new Vector3(0f, 0.35f, 0f));
            string sizeText = anchor.size > 1 ? $" x{anchor.size}" : string.Empty;
            string modeText = anchor.mode == DoorMode.Hybrid ? " auto" : string.Empty;
            label = $"{anchor.socketType}{sizeText}{modeText}";

            style = new GUIStyle(UnityEditor.EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                padding = new RectOffset(5, 5, 2, 3)
            };

            Vector2 guiPoint = UnityEditor.HandleUtility.WorldToGUIPoint(labelPosition);
            Vector2 size = style.CalcSize(new GUIContent(label));
            return new Rect(guiPoint.x - size.x * 0.5f - 4f, guiPoint.y - size.y * 0.5f - 3f, size.x + 8f, size.y + 6f);
        }
#endif

        private void DrawRectOutline(RectInt rect)
        {
            Vector3 bl = new Vector3(rect.xMin, rect.yMin, 0); Vector3 br = new Vector3(rect.xMax, rect.yMin, 0);
            Vector3 tl = new Vector3(rect.xMin, rect.yMax, 0); Vector3 tr = new Vector3(rect.xMax, rect.yMax, 0);
            Gizmos.DrawLine(bl, br); Gizmos.DrawLine(br, tr); Gizmos.DrawLine(tr, tl); Gizmos.DrawLine(tl, bl);
        }

        private void DrawDashedRect(RectInt rect, float offset = 0f)
        {
            Vector3 bl = new Vector3(rect.xMin - offset, rect.yMin - offset, 0);
            Vector3 br = new Vector3(rect.xMax + offset, rect.yMin - offset, 0);
            Vector3 tl = new Vector3(rect.xMin - offset, rect.yMax + offset, 0);
            Vector3 tr = new Vector3(rect.xMax + offset, rect.yMax + offset, 0);
            DrawDashedLine(bl, br); DrawDashedLine(br, tr); DrawDashedLine(tr, tl); DrawDashedLine(tl, bl);
        }

        private void DrawDashedLine(Vector3 start, Vector3 end)
        {
            float dashSize = 0.2f; float distance = Vector3.Distance(start, end);
            int count = Mathf.FloorToInt(distance / dashSize);
            for (int i = 0; i < count; i += 2)
                Gizmos.DrawLine(Vector3.Lerp(start, end, (float)i / count), Vector3.Lerp(start, end, (float)(i + 1) / count));
        }

        private void DrawValidityStatus()
        {
            RoomValidator.ValidationResult result = RoomValidator.Validate(this);
            Gizmos.color = result.isValid ? Color.green : Color.red;
            BoundsInt bounds = floorMap.cellBounds;
            Vector3 centre = floorMap.CellToLocal(new Vector3Int((int)bounds.center.x, (int)bounds.center.y, 0)) + floorMap.tileAnchor;
            Gizmos.DrawWireSphere(centre, 0.5f);
            if (result.isValid) Gizmos.DrawSphere(centre, 0.2f);
        }
    }
}
