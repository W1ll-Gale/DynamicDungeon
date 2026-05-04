using System.Threading.Tasks;
using DynamicDungeon.ConstraintDungeon.Solver;
using UnityEngine;

namespace DynamicDungeon.ConstraintDungeon
{
    public class DungeonGenerator : MonoBehaviour
    {
        private const double ProgressRevealDelaySeconds = 0.25d;

        public DungeonGenerationMode generationMode = DungeonGenerationMode.FlowGraph;
        public DungeonFlow dungeonFlow;
        public OrganicGenerationSettings organicSettings = new OrganicGenerationSettings();
        [Min(1)] public int layoutAttempts = 1000;
        [Min(1)] public int maxSearchSteps = 50000;
        [Min(0)] public int flowSeed = 0;
        public bool useRandomFlowSeed = true;
        public bool generateOnStart = true;
        public bool enableDiagnostics;

        [Header("State")]
        [SerializeField, HideInInspector] private System.Collections.Generic.List<GameObject> generatedRooms = new System.Collections.Generic.List<GameObject>();

        private DungeonGenerationService generationService;
        private float generationProgress;
        private string generationStatus = "Idle";
        private double generationStartedAt;

        public bool IsGenerating => generationService != null && generationService.IsGenerating;
        public float GenerationProgress => generationProgress;
        public string GenerationStatus => generationStatus;
        public bool ShouldShowGenerationProgress =>
            IsGenerating && Time.realtimeSinceStartupAsDouble - generationStartedAt >= ProgressRevealDelaySeconds;

        private void Awake()
        {
            EnsureService();
        }

        private void Start()
        {
            if (generateOnStart)
            {
                Generate();
            }
        }

        public async void Generate()
        {
            try
            {
                await GenerateAndRenderAsync();
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public async Task GenerateAndRenderAsync()
        {
            DungeonGenerationResult result = await GenerateLayoutAsync();
            if (result == null || !result.Success || result.Layout == null)
            {
                generationProgress = 0f;
                generationStatus = result?.FailureReason ?? "Generation failed.";
                return;
            }

            generationStatus = "Rendering rooms...";
            RenderLayout(result.Layout);
            ReportSuccess(result);
        }

        public Task<DungeonGenerationResult> GenerateLayoutAsync()
        {
            EnsureService();
            generationStartedAt = Time.realtimeSinceStartupAsDouble;
            return generationService.GenerateLayoutAsync(CreateRequest());
        }

        public void CancelGeneration()
        {
            generationService?.Cancel();
        }

        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                DungeonRoomInstantiator.DestroyObject(transform.GetChild(i).gameObject);
            }

            generatedRooms.Clear();
        }

        private void EnsureService()
        {
            if (generationService != null)
            {
                return;
            }

            generationService = new DungeonGenerationService((progress, status) =>
            {
                generationProgress = progress;
                generationStatus = status;
            });
        }

        private DungeonGenerationRequest CreateRequest()
        {
            return new DungeonGenerationRequest
            {
                Mode = generationMode,
                Flow = dungeonFlow,
                OrganicSettings = organicSettings,
                LayoutAttempts = layoutAttempts,
                MaxSearchSteps = maxSearchSteps,
                FlowSeed = flowSeed,
                UseRandomFlowSeed = useRandomFlowSeed,
                EnableDiagnostics = enableDiagnostics
            };
        }

        private void RenderLayout(DungeonLayout layout)
        {
            Clear();
            foreach (PlacedRoom roomData in layout.Rooms)
            {
                GameObject instance = DungeonRoomInstantiator.InstantiateRoom(roomData, transform);
                if (instance != null)
                {
                    generatedRooms.Add(instance);
                }
            }
        }

        private void ReportSuccess(DungeonGenerationResult result)
        {
            DungeonLayout layout = result.Layout;
            if (generationMode == DungeonGenerationMode.OrganicGrowth)
            {
                int countedRooms = 0;
                foreach (PlacedRoom room in layout.Rooms)
                {
                    if (room.node.type != RoomType.Corridor)
                    {
                        countedRooms++;
                    }
                }

                int corridorCount = layout.Rooms.Count - countedRooms;
                string targetText = organicSettings.useRoomCountRange
                    ? $"{organicSettings.minRoomCount}-{organicSettings.maxRoomCount}"
                    : organicSettings.targetRoomCount.ToString();
                generationProgress = 1f;
                generationStatus = $"Generated {countedRooms}/{targetText} rooms plus {corridorCount} corridors. Seed {result.Seed}, {result.ElapsedMilliseconds}ms.";
                return;
            }

            generationProgress = 1f;
            generationStatus = $"Generated {layout.Rooms.Count} rooms. Seed {result.Seed}, {result.ElapsedMilliseconds}ms.";
        }
    }
}
