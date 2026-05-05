using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Output;
using DynamicDungeon.Runtime.Semantic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace DynamicDungeon.Runtime.Component
{
    [DisallowMultipleComponent]
    public sealed class TilemapWorldGenerator : MonoBehaviour
    {
        private const string IdleStatusLabel = "Idle";
        private const string GeneratingStatusLabel = "Generating...";
        private const string DoneStatusLabel = "Done";
        private const string FailedStatusLabel = "Failed";
        private const string BakeEditorOnlyMessage = "Bake is only available in the Unity Editor.";
        private const string BakeInProgressMessage = "Cannot bake while generation is already running.";
        private const string BakedSnapshotMissingDataMessage = "Assigned baked world snapshot is missing snapshot data.";
        private const string BakedSnapshotPathFormat = "Assets/{0}_BakedSnapshot.asset";
        private const string BakedSnapshotDimensionMismatchMessageFormat = "Baked world snapshot dimension mismatch: snapshot is {0}x{1}, current world is {2}x{3}.";
        private const string MissingGraphMessage = "World generation failed: no Tilemap World Graph asset is assigned.";
        private const string GraphCompilationFailedMessage = "Graph compilation failed.";
        private const double ProgressRevealDelaySeconds = 0.25d;
        private const float ExecutionProgressStart = 0.35f;
        private const float ExecutionProgressEnd = 0.70f;
        private const float SnapshotProgress = 0.78f;
        private const float OutputStartProgress = 0.82f;
        private const float OutputTilemapProgress = 0.88f;
        private const float OutputBackgroundProgress = 0.92f;
        private const float OutputPrefabProgress = 0.96f;

        [SerializeField]
        private bool _generateOnStart;

        [SerializeField]
        private SeedMode _seedMode = SeedMode.Stable;

        [SerializeField]
        private long _stableSeed = 12345L;

        [SerializeField]
        private int _worldWidth = 128;

        [SerializeField]
        private int _worldHeight = 128;

        [SerializeField]
        private GenGraph _graph;

        [SerializeField]
        private Grid _grid;

        [SerializeField]
        private List<TilemapLayerDefinition> _layerDefinitions = new List<TilemapLayerDefinition>();

        [SerializeField]
        private BiomeAsset _biome;

        [SerializeField]
        private Vector3Int _tilemapOffset;

        [SerializeField]
        private bool _renderBackgroundFromFloorTiles;

        [SerializeField]
        private TilemapLayerDefinition _backgroundLayerDefinition;

        [SerializeField]
        private int _backgroundLogicalId = LogicalTileId.Floor;

        [SerializeField]
        private string _backgroundBiomeChannelName;

        [SerializeField]
        private BakedWorldSnapshot _bakedWorldSnapshot;

        [SerializeField]
        private List<ExposedPropertyOverride> _propertyOverrides = new List<ExposedPropertyOverride>();

        private readonly Executor _executor = new Executor();
        private readonly TilemapOutputPass _tilemapOutputPass = new TilemapOutputPass();
        private readonly TilemapLayerWriter _tilemapLayerWriter = new TilemapLayerWriter();
        private readonly PrefabPlacementOutputPass _prefabPlacementOutputPass = new PrefabPlacementOutputPass();
        private readonly GeneratedPrefabWriter _generatedPrefabWriter = new GeneratedPrefabWriter();
        private readonly System.Random _seedRandom = new System.Random();
        private readonly SemaphoreSlim _generationRequestSemaphore = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _generationCancellationSource;
        private Task _currentGenerationTask = Task.CompletedTask;
        private string _statusLabel = IdleStatusLabel;
        private string _generationStatus = IdleStatusLabel;
        private float _generationProgress;
        private double _generationStartedAt;
        private long _lastUsedSeed;
        private int _mainThreadId;
        private bool _generationCancelRequested;
        private WorldSnapshot _lastSuccessfulSnapshot;
        private string _lastOutputChannelName = string.Empty;

        public event Action OnGenerationStarted;
        public event Action<GenerationCompletedArgs> OnGenerationCompleted;

        public bool IsGenerating
        {
            get
            {
                return string.Equals(_statusLabel, GeneratingStatusLabel, StringComparison.Ordinal);
            }
        }

        public string StatusLabel
        {
            get
            {
                return _statusLabel;
            }
        }

        public float GenerationProgress
        {
            get
            {
                return _generationProgress;
            }
        }

        public string GenerationStatus
        {
            get
            {
                return _generationStatus;
            }
        }

        public bool ShouldShowGenerationProgress
        {
            get
            {
                return IsGenerating && Time.realtimeSinceStartupAsDouble - _generationStartedAt >= ProgressRevealDelaySeconds;
            }
        }

        public bool IsBaked
        {
            get
            {
                return HasValidBakedSnapshot();
            }
        }

        public GenGraph Graph
        {
            get
            {
                return _graph;
            }
            set
            {
                _graph = value;
            }
        }

        public List<ExposedPropertyOverride> PropertyOverrides
        {
            get
            {
                return _propertyOverrides;
            }
        }

        public long LastUsedSeed
        {
            get
            {
                return _lastUsedSeed;
            }
        }

        public WorldSnapshot LastSuccessfulSnapshot
        {
            get
            {
                if (_lastSuccessfulSnapshot != null)
                {
                    return _lastSuccessfulSnapshot;
                }

                return HasValidBakedSnapshot() ? _bakedWorldSnapshot.Snapshot : null;
            }
        }

        public string LastOutputChannelName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_lastOutputChannelName))
                {
                    return _lastOutputChannelName;
                }

                return HasValidBakedSnapshot() ? _bakedWorldSnapshot.OutputChannelName : string.Empty;
            }
        }

        public Vector3Int TilemapOffset
        {
            get
            {
                return _tilemapOffset;
            }
        }

        public Grid ResolvedGrid
        {
            get
            {
                return ResolveGrid(false);
            }
        }

        public Vector3Int GetTilemapOffsetForSnapshot(WorldSnapshot snapshot)
        {
            return GetCenteredTilemapOffset(snapshot);
        }

        public void ReconcilePropertyOverrides()
        {
            if (_propertyOverrides == null)
            {
                _propertyOverrides = new List<ExposedPropertyOverride>();
            }

            if (_graph == null || _graph.ExposedProperties == null)
            {
                _propertyOverrides.Clear();
                return;
            }

            List<ExposedProperty> exposedProperties = _graph.ExposedProperties;

            // Build a reordered list that matches the graph's property order.
            List<ExposedPropertyOverride> reordered = new List<ExposedPropertyOverride>(exposedProperties.Count);

            int graphIndex;
            for (graphIndex = 0; graphIndex < exposedProperties.Count; graphIndex++)
            {
                ExposedProperty graphProperty = exposedProperties[graphIndex];
                if (graphProperty == null || string.IsNullOrWhiteSpace(graphProperty.PropertyName))
                {
                    continue;
                }

                ExposedPropertyOverride existingOverride = FindOverrideForProperty(graphProperty);
                if (existingOverride != null)
                {
                    existingOverride.PropertyId = graphProperty.PropertyId ?? string.Empty;
                    existingOverride.PropertyName = graphProperty.PropertyName ?? string.Empty;
                    reordered.Add(existingOverride);
                }
                else
                {
                    ExposedPropertyOverride newOverride = new ExposedPropertyOverride();
                    newOverride.PropertyId = graphProperty.PropertyId ?? string.Empty;
                    newOverride.PropertyName = graphProperty.PropertyName;
                    newOverride.OverrideValue = graphProperty.DefaultValue ?? "0";
                    reordered.Add(newOverride);
                }
            }

            _propertyOverrides = reordered;
        }

        public void CancelGeneration()
        {
            if (_generationCancellationSource != null && !_generationCancellationSource.IsCancellationRequested)
            {
                _generationCancelRequested = true;
                SetGenerationProgress(_generationProgress, "Cancellation requested...");
                _generationCancellationSource.Cancel();
            }
        }

        public void Clear()
        {
            if (IsGenerating)
            {
                Debug.LogWarning("Cannot clear generated world output while generation is running.", this);
                return;
            }

            ClearTilemapsIfPossible();
            _lastSuccessfulSnapshot = null;
            _lastOutputChannelName = string.Empty;
            _statusLabel = IdleStatusLabel;
            SetGenerationProgress(0.0f, "Cleared generated world output.");

#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        }

        public void Bake()
        {
#if UNITY_EDITOR
            if (IsGenerating)
            {
                Debug.LogError(BakeInProgressMessage, this);
                return;
            }

            try
            {
                ValidateConfiguration();

                long seed = GetSeedForRun();
                string outputChannelName;
                bool hasConnectedOutput;
                WorldSnapshot snapshot = ExecuteGenerationSynchronously(seed, out outputChannelName, out hasConnectedOutput);
                if (hasConnectedOutput)
                {
                    WriteSnapshotToTilemaps(snapshot, outputChannelName);
                }

                SaveBakedSnapshotAsset(snapshot, seed, outputChannelName);
                CacheSuccessfulSnapshot(snapshot, outputChannelName);

                _statusLabel = DoneStatusLabel;
            }
            catch (Exception exception)
            {
                Debug.LogError("Bake failed: " + exception.Message, this);
                _statusLabel = FailedStatusLabel;
            }
#else
            Debug.LogError(BakeEditorOnlyMessage, this);
#endif
        }

        public void ClearBake()
        {
            _bakedWorldSnapshot = null;
            _lastSuccessfulSnapshot = null;
            _lastOutputChannelName = string.Empty;

#if UNITY_EDITOR
            EditorUtility.SetDirty(this);
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        }

        public void Generate()
        {
            Task generationTask = GenerateAsync();
            _ = ObserveFireAndForget(generationTask);
        }

        public async Task GenerateAsync(CancellationToken cancellationToken = default)
        {
            Task generationTask;

            await _generationRequestSemaphore.WaitAsync(cancellationToken);

            try
            {
                if (_currentGenerationTask != null && !_currentGenerationTask.IsCompleted)
                {
                    CancelGeneration();
                    await _currentGenerationTask;
                }

                DisposeCancellationSource();
                _generationCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                generationTask = RunGenerationAsync(_generationCancellationSource.Token);
                _currentGenerationTask = generationTask;
            }
            finally
            {
                _generationRequestSemaphore.Release();
            }

            await generationTask;
        }

        private void Awake()
        {
            CaptureMainThreadIdIfNeeded();
        }

        private void OnDestroy()
        {
            CancelGeneration();
            DisposeCancellationSource();
        }

        private void Start()
        {
            if (_generateOnStart && !IsBaked)
            {
                Generate();
            }
        }

        private async Task ObserveFireAndForget(Task generationTask)
        {
            try
            {
                await generationTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                Debug.LogError("World generation failed unexpectedly: " + exception, this);
            }
        }

        private void ClearTilemapsIfPossible()
        {
            Transform generatedPrefabParent = transform;
            if (generatedPrefabParent != null)
            {
                _generatedPrefabWriter.EnsureRoot(generatedPrefabParent);
                _generatedPrefabWriter.ClearAll();
            }

            if (_layerDefinitions == null || _layerDefinitions.Count == 0)
            {
                return;
            }

            Grid resolvedGrid = ResolveGrid(false);
            if (resolvedGrid == null)
            {
                return;
            }

            _tilemapLayerWriter.EnsureTimelapsCreated(resolvedGrid, _layerDefinitions);
            _tilemapLayerWriter.ClearAll();
        }

        private GenerationCompletedArgs CreateFailureArgs(string errorMessage)
        {
            GenerationCompletedArgs args = new GenerationCompletedArgs();
            args.IsSuccess = false;
            args.WasBakedFallback = false;
            args.ErrorMessage = errorMessage;

            WorldSnapshot bakedSnapshot;
            if (TryUseBakedFallback(out bakedSnapshot))
            {
                args.Snapshot = bakedSnapshot;
                args.WasBakedFallback = true;
                return args;
            }

            try
            {
                ClearTilemapsIfPossible();
            }
            catch (Exception clearException)
            {
                Debug.LogError("Failed to clear tilemaps after generation failure: " + clearException, this);
            }

            args.Snapshot = null;
            return args;
        }

        private void DisposeCancellationSource()
        {
            if (_generationCancellationSource != null)
            {
                _generationCancellationSource.Dispose();
                _generationCancellationSource = null;
            }
        }

        private long GetSeedForRun()
        {
            SeedMode modeToUse = _graph != null ? _graph.DefaultSeedMode : _seedMode;

            if (modeToUse == SeedMode.Stable)
            {
                long stableSeed = _graph != null ? _graph.DefaultSeed : _stableSeed;
                _lastUsedSeed = stableSeed;
                return stableSeed;
            }

            _lastUsedSeed = GenerationSeedUtility.CreateRandomSeed(_seedRandom);
            return _lastUsedSeed;
        }

        private void RaiseGenerationCompleted(GenerationCompletedArgs args)
        {
            Action<GenerationCompletedArgs> generationCompleted = OnGenerationCompleted;
            if (generationCompleted != null)
            {
                generationCompleted(args);
            }
        }

        private void RaiseGenerationStarted()
        {
            Action generationStarted = OnGenerationStarted;
            if (generationStarted != null)
            {
                generationStarted();
            }
        }

        private Grid ResolveGrid(bool throwIfMissing)
        {
            if (_grid == null)
            {
                _grid = GetComponentInChildren<Grid>();
            }

            if (_grid == null && throwIfMissing)
            {
                throw new InvalidOperationException("TilemapWorldGenerator requires a Grid reference.");
            }

            return _grid;
        }

        private async Task RunGenerationAsync(CancellationToken cancellationToken)
        {
            CaptureMainThreadIdIfNeeded();
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            ExecutionPlan undisposedPlan = null;
            _statusLabel = GeneratingStatusLabel;
            _generationCancelRequested = false;
            _generationStartedAt = Time.realtimeSinceStartupAsDouble;
            SetGenerationProgress(0.0f, "Preparing world generation...");
            RaiseGenerationStarted();

            try
            {
                ValidateConfiguration();
                cancellationToken.ThrowIfCancellationRequested();

                SetGenerationProgress(0.1f, "Compiling generation graph...");
                long seed = GetSeedForRun();
                GraphCompileResult compileResult;
                string compileErrorMessage;
                if (!TryCompileGraph(seed, out compileResult, out compileErrorMessage))
                {
                    _statusLabel = FailedStatusLabel;
                    SetGenerationProgress(0.0f, compileErrorMessage ?? GraphCompilationFailedMessage);
                    RaiseGenerationCompleted(CreateFailureArgs(compileErrorMessage));
                    return;
                }

                undisposedPlan = compileResult.Plan;
                ApplyPropertyOverrides(compileResult.Plan);
                cancellationToken.ThrowIfCancellationRequested();
                SetGenerationProgress(ExecutionProgressStart, "Executing generation graph...");
                IProgress<ExecutionProgress> nodeProgress = new ExecutionProgressReporter(SetGenerationProgressFromExecution);
                ExecutionResult executionResult = await _executor.ExecuteAsync(
                    compileResult.Plan,
                    cancellationToken,
                    nodeProgress: nodeProgress);
                undisposedPlan = null;

                if (executionResult.WasCancelled)
                {
                    _statusLabel = IdleStatusLabel;
                    SetGenerationProgress(0.0f, "Generation cancelled.");

                    GenerationCompletedArgs cancelledArgs = new GenerationCompletedArgs();
                    cancelledArgs.Snapshot = null;
                    cancelledArgs.IsSuccess = false;
                    cancelledArgs.WasBakedFallback = false;
                    cancelledArgs.ErrorMessage = "Generation cancelled.";
                    RaiseGenerationCompleted(cancelledArgs);
                    return;
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (!executionResult.IsSuccess || executionResult.Snapshot == null)
                {
                    string executionError = string.IsNullOrWhiteSpace(executionResult.ErrorMessage) ? "Generation failed." : executionResult.ErrorMessage;
                    Debug.LogError("World generation failed: " + executionError, this);
                    _statusLabel = FailedStatusLabel;
                    SetGenerationProgress(0.0f, executionError);
                    RaiseGenerationCompleted(CreateFailureArgs(executionError));
                    return;
                }

                if (compileResult.HasConnectedOutput)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await SetGenerationProgressAndYield(OutputStartProgress, "Graph executed. Preparing generated tilemaps and prefabs...", cancellationToken);
                    await WriteSnapshotToTilemapsAsync(executionResult.Snapshot, compileResult.OutputChannelName, cancellationToken);
                }
                else
                {
                    await SetGenerationProgressAndYield(OutputStartProgress, "Graph executed. Clearing generated output because the graph has no connected output.", cancellationToken);
                    ClearTilemapsIfPossible();
                }

                GenerationCompletedArgs completedArgs = new GenerationCompletedArgs();
                completedArgs.Snapshot = executionResult.Snapshot;
                completedArgs.IsSuccess = true;
                completedArgs.WasBakedFallback = false;
                completedArgs.ErrorMessage = null;

                CacheSuccessfulSnapshot(executionResult.Snapshot, compileResult.OutputChannelName);
                _statusLabel = DoneStatusLabel;
                SetGenerationProgress(1.0f, BuildGenerationCompleteStatus(executionResult.Snapshot, seed, stopwatch.ElapsedMilliseconds, compileResult.HasConnectedOutput));
                RaiseGenerationCompleted(completedArgs);
            }
            catch (OperationCanceledException)
            {
                _statusLabel = IdleStatusLabel;
                SetGenerationProgress(0.0f, "Generation cancelled.");

                GenerationCompletedArgs cancelledArgs = new GenerationCompletedArgs();
                cancelledArgs.Snapshot = null;
                cancelledArgs.IsSuccess = false;
                cancelledArgs.WasBakedFallback = false;
                cancelledArgs.ErrorMessage = "Generation cancelled.";
                RaiseGenerationCompleted(cancelledArgs);
            }
            catch (Exception exception)
            {
                Debug.LogError("World generation failed: " + exception, this);
                _statusLabel = FailedStatusLabel;
                SetGenerationProgress(0.0f, exception.Message);
                RaiseGenerationCompleted(CreateFailureArgs(exception.Message));
            }
            finally
            {
                if (undisposedPlan != null)
                {
                    undisposedPlan.Dispose();
                }
            }
        }

        private void SetGenerationProgress(float progress, string status)
        {
            _generationProgress = Clamp01(progress);
            _generationStatus = string.IsNullOrWhiteSpace(status) ? _statusLabel : status;
#if UNITY_EDITOR
            if (IsOnMainThread())
            {
                EditorApplication.QueuePlayerLoopUpdate();
            }
#endif
        }

        private async Task SetGenerationProgressAndYield(float progress, string status, CancellationToken cancellationToken)
        {
            SetGenerationProgress(progress, status);
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
        }

        private void SetGenerationProgressFromExecution(ExecutionProgress progress)
        {
            if (!IsGenerating || _generationCancelRequested)
            {
                return;
            }

            if (progress.Stage == ExecutionProgressStage.CreatingSnapshot)
            {
                SetGenerationProgress(SnapshotProgress, BuildSnapshotProgressStatus(progress));
                return;
            }

            if (progress.TotalNodeCount <= 0)
            {
                SetGenerationProgress(ExecutionProgressEnd, "Executed 0/0 graph nodes.");
                return;
            }

            int nodeNumber = Clamp(progress.CurrentNodeNumber, 1, progress.TotalNodeCount);
            float nodeProgress = progress.IsNodeComplete
                ? progress.NormalizedProgress
                : (float)(nodeNumber - 1) / progress.TotalNodeCount;
            float overallProgress = Lerp(ExecutionProgressStart, ExecutionProgressEnd, nodeProgress);
            string nodeProgressLabel = nodeNumber.ToString(CultureInfo.InvariantCulture) + "/" + progress.TotalNodeCount.ToString(CultureInfo.InvariantCulture);
            string verb = progress.IsNodeComplete ? "Executed" : "Executing";

            SetGenerationProgress(
                overallProgress,
                verb + " node " + nodeProgressLabel + ": " + GetNodeDisplayName(progress));
        }

        private static string BuildSnapshotProgressStatus(ExecutionProgress progress)
        {
            if (progress.TotalNodeCount <= 0)
            {
                return "Executed 0/0 graph nodes. Building world snapshot...";
            }

            string nodeProgressLabel = progress.CompletedNodeCount.ToString(CultureInfo.InvariantCulture) +
                "/" +
                progress.TotalNodeCount.ToString(CultureInfo.InvariantCulture);
            return "Executed " + nodeProgressLabel + " graph nodes. Building world snapshot...";
        }

        private static float Clamp01(float value)
        {
            if (value < 0.0f)
            {
                return 0.0f;
            }

            if (value > 1.0f)
            {
                return 1.0f;
            }

            return value;
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            if (value < minimum)
            {
                return minimum;
            }

            if (value > maximum)
            {
                return maximum;
            }

            return value;
        }

        private static float Lerp(float from, float to, float t)
        {
            return from + ((to - from) * Clamp01(t));
        }

        private static string GetNodeDisplayName(ExecutionProgress progress)
        {
            if (!string.IsNullOrWhiteSpace(progress.NodeName))
            {
                return progress.NodeName;
            }

            if (!string.IsNullOrWhiteSpace(progress.NodeId))
            {
                return progress.NodeId;
            }

            return "Unknown node";
        }

        private sealed class ExecutionProgressReporter : IProgress<ExecutionProgress>
        {
            private readonly Action<ExecutionProgress> _report;

            public ExecutionProgressReporter(Action<ExecutionProgress> report)
            {
                _report = report;
            }

            public void Report(ExecutionProgress value)
            {
                if (_report != null)
                {
                    _report(value);
                }
            }
        }

        private void CaptureMainThreadIdIfNeeded()
        {
            if (_mainThreadId == 0)
            {
                _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            }
        }

        private bool IsOnMainThread()
        {
            return _mainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == _mainThreadId;
        }

        private static string BuildGenerationCompleteStatus(WorldSnapshot snapshot, long seed, long elapsedMilliseconds, bool renderedOutput)
        {
            if (snapshot == null)
            {
                return "Generation completed.";
            }

            string action = renderedOutput ? "Generated" : "Executed";
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1}x{2} world. Seed {3}, {4}ms.",
                action,
                snapshot.Width,
                snapshot.Height,
                seed,
                elapsedMilliseconds);
        }

        private void ValidateConfiguration()
        {
            int worldWidth = _graph != null ? _graph.WorldWidth : _worldWidth;
            int worldHeight = _graph != null ? _graph.WorldHeight : _worldHeight;

            if (worldWidth <= 0)
            {
                throw new InvalidOperationException("WorldWidth must be greater than zero.");
            }

            if (worldHeight <= 0)
            {
                throw new InvalidOperationException("WorldHeight must be greater than zero.");
            }

            if (_biome == null)
            {
                throw new InvalidOperationException("TilemapWorldGenerator requires a Biome assignment.");
            }

            if (_layerDefinitions == null || _layerDefinitions.Count == 0)
            {
                throw new InvalidOperationException("TilemapWorldGenerator requires at least one TilemapLayerDefinition.");
            }

            ResolveGrid(true);
        }

        private bool TryCompileGraph(long seed, out GraphCompileResult compileResult, out string errorMessage)
        {
            compileResult = null;
            errorMessage = null;

            if (_graph == null)
            {
                Debug.LogError(MissingGraphMessage, this);
                errorMessage = MissingGraphMessage;
                return false;
            }

            string schemaErrorMessage;
            if (!GraphOutputUtility.TryValidateCurrentSchema(_graph, out schemaErrorMessage))
            {
                Debug.LogError("Graph schema validation failed: " + schemaErrorMessage, this);
                errorMessage = schemaErrorMessage ?? GraphCompilationFailedMessage;
                return false;
            }

            long originalSeed = _graph.DefaultSeed;

            try
            {
                _graph.DefaultSeed = seed;

                compileResult = GraphCompiler.Compile(_graph);
                if (!compileResult.IsSuccess || compileResult.Plan == null)
                {
                    if (compileResult.Plan != null)
                    {
                        compileResult.Plan.Dispose();
                    }

                    LogCompileDiagnostics(compileResult.Diagnostics);
                    errorMessage = GraphCompilationFailedMessage;
                    return false;
                }

                return true;
            }
            finally
            {
                _graph.DefaultSeed = originalSeed;
            }
        }

        private static string BuildDiagnosticMessage(GraphDiagnostic diagnostic)
        {
            if (string.IsNullOrWhiteSpace(diagnostic.NodeId) && string.IsNullOrWhiteSpace(diagnostic.PortName))
            {
                return diagnostic.Message;
            }

            string nodePrefix = string.IsNullOrWhiteSpace(diagnostic.NodeId) ? string.Empty : "[" + diagnostic.NodeId + "] ";
            string portSuffix = string.IsNullOrWhiteSpace(diagnostic.PortName) ? string.Empty : " (Port: " + diagnostic.PortName + ")";
            return nodePrefix + diagnostic.Message + portSuffix;
        }

        private void LogCompileDiagnostics(IReadOnlyList<GraphDiagnostic> diagnostics)
        {
            if (diagnostics == null || diagnostics.Count == 0)
            {
                Debug.LogError(GraphCompilationFailedMessage, this);
                return;
            }

            int diagnosticIndex;
            for (diagnosticIndex = 0; diagnosticIndex < diagnostics.Count; diagnosticIndex++)
            {
                GraphDiagnostic diagnostic = diagnostics[diagnosticIndex];

                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    Debug.LogError(BuildDiagnosticMessage(diagnostic), this);
                }
                else if (diagnostic.Severity == DiagnosticSeverity.Warning)
                {
                    Debug.LogWarning(BuildDiagnosticMessage(diagnostic), this);
                }
            }
        }

        private void LogExecutionDiagnostics(IReadOnlyList<GraphDiagnostic> diagnostics)
        {
            if (diagnostics == null || diagnostics.Count == 0)
            {
                return;
            }

            int diagnosticIndex;
            for (diagnosticIndex = 0; diagnosticIndex < diagnostics.Count; diagnosticIndex++)
            {
                GraphDiagnostic diagnostic = diagnostics[diagnosticIndex];

                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    Debug.LogError(BuildDiagnosticMessage(diagnostic), this);
                }
                else if (diagnostic.Severity == DiagnosticSeverity.Warning)
                {
                    Debug.LogWarning(BuildDiagnosticMessage(diagnostic), this);
                }
            }
        }

        private WorldSnapshot ExecuteGenerationSynchronously(long seed, out string outputChannelName, out bool hasConnectedOutput)
        {
            outputChannelName = string.Empty;
            hasConnectedOutput = false;

            GraphCompileResult compileResult;
            string errorMessage;
            if (!TryCompileGraph(seed, out compileResult, out errorMessage))
            {
                throw new InvalidOperationException(errorMessage ?? GraphCompilationFailedMessage);
            }

            outputChannelName = compileResult.OutputChannelName;
            hasConnectedOutput = compileResult.HasConnectedOutput;
            ExecutionPlan undisposedPlan = compileResult.Plan;
            ExecutionResult executionResult;
            try
            {
                ApplyPropertyOverrides(compileResult.Plan);
                executionResult = _executor.Execute(compileResult.Plan, CancellationToken.None);
                undisposedPlan = null;
            }
            finally
            {
                if (undisposedPlan != null)
                {
                    undisposedPlan.Dispose();
                }
            }

            if (executionResult.WasCancelled)
            {
                throw new InvalidOperationException("Generation cancelled.");
            }

            LogExecutionDiagnostics(executionResult.Diagnostics);

            if (!executionResult.IsSuccess || executionResult.Snapshot == null)
            {
                string executionError = string.IsNullOrWhiteSpace(executionResult.ErrorMessage) ? "Generation failed." : executionResult.ErrorMessage;
                throw new InvalidOperationException(executionError);
            }

            return executionResult.Snapshot;
        }

        private bool HasValidBakedSnapshot()
        {
            return _bakedWorldSnapshot != null && _bakedWorldSnapshot.Snapshot != null;
        }

        private bool TryUseBakedFallback(out WorldSnapshot snapshot)
        {
            snapshot = null;

            BakedWorldSnapshot bakedSnapshot;
            string errorMessage;
            if (!TryGetValidBakedSnapshot(out bakedSnapshot, out errorMessage))
            {
                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    Debug.LogError(errorMessage, this);
                }

                return false;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(bakedSnapshot.OutputChannelName))
                {
                    snapshot = bakedSnapshot.Snapshot;
                    CacheSuccessfulSnapshot(snapshot, bakedSnapshot.OutputChannelName);
                    return true;
                }

                WriteSnapshotToTilemaps(bakedSnapshot.Snapshot, bakedSnapshot.OutputChannelName);
                snapshot = bakedSnapshot.Snapshot;
                CacheSuccessfulSnapshot(snapshot, bakedSnapshot.OutputChannelName);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("World generation fallback failed: " + exception, this);
                return false;
            }
        }

        private bool TryGetValidBakedSnapshot(out BakedWorldSnapshot bakedSnapshot, out string errorMessage)
        {
            bakedSnapshot = _bakedWorldSnapshot;
            errorMessage = null;

            if (bakedSnapshot == null)
            {
                return false;
            }

            if (bakedSnapshot.Snapshot == null)
            {
                errorMessage = BakedSnapshotMissingDataMessage;
                return false;
            }

            int bakedWidth = bakedSnapshot.Width > 0 ? bakedSnapshot.Width : bakedSnapshot.Snapshot.Width;
            int bakedHeight = bakedSnapshot.Height > 0 ? bakedSnapshot.Height : bakedSnapshot.Snapshot.Height;

            if (bakedWidth != _worldWidth || bakedHeight != _worldHeight)
            {
                errorMessage = string.Format(
                    CultureInfo.InvariantCulture,
                    BakedSnapshotDimensionMismatchMessageFormat,
                    bakedWidth,
                    bakedHeight,
                    _worldWidth,
                    _worldHeight);
                return false;
            }

            return true;
        }

        private void WriteSnapshotToTilemaps(
            WorldSnapshot snapshot,
            string outputChannelName,
            CancellationToken cancellationToken = default,
            bool reportProgress = false)
        {
            if (reportProgress)
            {
                SetGenerationProgress(OutputStartProgress, "Preparing generated tilemaps and prefabs...");
            }

            cancellationToken.ThrowIfCancellationRequested();
            Grid resolvedGrid = ResolveGrid(true);
            Vector3Int resolvedTilemapOffset = GetCenteredTilemapOffset(snapshot);
            _generatedPrefabWriter.EnsureRoot(transform);
            _generatedPrefabWriter.ClearAll();
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(outputChannelName))
            {
                TileSemanticRegistry registry = TileSemanticRegistry.GetOrLoad();

                if (reportProgress)
                {
                    SetGenerationProgress(OutputTilemapProgress, "Writing generated tilemaps...");
                }

                _tilemapLayerWriter.EnsureTimelapsCreated(resolvedGrid, _layerDefinitions);
                _tilemapLayerWriter.ClearAll();
                cancellationToken.ThrowIfCancellationRequested();
                _tilemapOutputPass.Execute(snapshot, outputChannelName, _biome, registry, _tilemapLayerWriter, _layerDefinitions, resolvedTilemapOffset);
                cancellationToken.ThrowIfCancellationRequested();
                if (_renderBackgroundFromFloorTiles)
                {
                    if (reportProgress)
                    {
                        SetGenerationProgress(OutputBackgroundProgress, "Writing generated background tilemap...");
                    }

                    ushort backgroundLogicalId = unchecked((ushort)Mathf.Max(0, _backgroundLogicalId));
                    _tilemapOutputPass.ExecuteBackgroundFill(snapshot, outputChannelName, _biome, _tilemapLayerWriter, _backgroundLayerDefinition, resolvedTilemapOffset, backgroundLogicalId, _backgroundBiomeChannelName);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            else
            {
                if (reportProgress)
                {
                    SetGenerationProgress(OutputStartProgress, "Clearing generated output because the graph has no connected output.");
                }

                ClearTilemapsIfPossible();
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (reportProgress)
            {
                SetGenerationProgress(OutputPrefabProgress, "Placing generated prefabs...");
            }

            _prefabPlacementOutputPass.Execute(snapshot, resolvedGrid, _generatedPrefabWriter, resolvedTilemapOffset);
            cancellationToken.ThrowIfCancellationRequested();
        }

        private void CacheSuccessfulSnapshot(WorldSnapshot snapshot, string outputChannelName)
        {
            _lastSuccessfulSnapshot = snapshot;
            _lastOutputChannelName = outputChannelName ?? string.Empty;
        }

        private async Task WriteSnapshotToTilemapsAsync(
            WorldSnapshot snapshot,
            string outputChannelName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Grid resolvedGrid = ResolveGrid(true);
            Vector3Int resolvedTilemapOffset = GetCenteredTilemapOffset(snapshot);

            await SetGenerationProgressAndYield(OutputStartProgress, "Clearing previous generated prefabs...", cancellationToken);
            _generatedPrefabWriter.EnsureRoot(transform);
            _generatedPrefabWriter.ClearAll();
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(outputChannelName))
            {
                TileSemanticRegistry registry = TileSemanticRegistry.GetOrLoad();

                await SetGenerationProgressAndYield(OutputTilemapProgress, "Writing generated tilemaps...", cancellationToken);
                _tilemapLayerWriter.EnsureTimelapsCreated(resolvedGrid, _layerDefinitions);
                _tilemapLayerWriter.ClearAll();
                cancellationToken.ThrowIfCancellationRequested();
                _tilemapOutputPass.Execute(snapshot, outputChannelName, _biome, registry, _tilemapLayerWriter, _layerDefinitions, resolvedTilemapOffset);
                cancellationToken.ThrowIfCancellationRequested();

                if (_renderBackgroundFromFloorTiles)
                {
                    await SetGenerationProgressAndYield(OutputBackgroundProgress, "Writing generated background tilemap...", cancellationToken);
                    ushort backgroundLogicalId = unchecked((ushort)Mathf.Max(0, _backgroundLogicalId));
                    _tilemapOutputPass.ExecuteBackgroundFill(snapshot, outputChannelName, _biome, _tilemapLayerWriter, _backgroundLayerDefinition, resolvedTilemapOffset, backgroundLogicalId, _backgroundBiomeChannelName);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            else
            {
                await SetGenerationProgressAndYield(OutputStartProgress, "Clearing generated output because the graph has no connected output.", cancellationToken);
                ClearTilemapsIfPossible();
                cancellationToken.ThrowIfCancellationRequested();
            }

            await SetGenerationProgressAndYield(OutputPrefabProgress, "Placing generated prefabs...", cancellationToken);
            _prefabPlacementOutputPass.Execute(snapshot, resolvedGrid, _generatedPrefabWriter, resolvedTilemapOffset);
            cancellationToken.ThrowIfCancellationRequested();
        }

        private Vector3Int GetCenteredTilemapOffset(WorldSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return _tilemapOffset;
            }

            return new Vector3Int(
                _tilemapOffset.x - (snapshot.Width / 2),
                _tilemapOffset.y - (snapshot.Height / 2),
                _tilemapOffset.z);
        }

        private void ApplyPropertyOverrides(ExecutionPlan plan)
        {
            if (plan == null || _propertyOverrides == null || _propertyOverrides.Count == 0 || _graph == null)
            {
                return;
            }

            int overrideIndex;
            for (overrideIndex = 0; overrideIndex < _propertyOverrides.Count; overrideIndex++)
            {
                ExposedPropertyOverride propertyOverride = _propertyOverrides[overrideIndex];
                if (propertyOverride == null)
                {
                    continue;
                }

                ExposedProperty graphProperty = ResolveGraphProperty(propertyOverride);
                if (graphProperty == null)
                {
                    continue;
                }

                propertyOverride.PropertyId = graphProperty.PropertyId ?? string.Empty;
                propertyOverride.PropertyName = graphProperty.PropertyName ?? string.Empty;

                float floatValue = 0.0f;
                bool parsed = false;

                if (graphProperty.Type == ChannelType.Float)
                {
                    parsed = float.TryParse(
                        propertyOverride.OverrideValue,
                        NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture,
                        out floatValue);
                }
                else if (graphProperty.Type == ChannelType.Int)
                {
                    int intValue;
                    parsed = int.TryParse(
                        propertyOverride.OverrideValue,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out intValue);
                    if (parsed)
                    {
                        floatValue = (float)intValue;
                    }
                }

                if (parsed)
                {
                    plan.SetInitialBlackboardValue(GetPropertyRuntimeKey(graphProperty), floatValue);
                }
            }
        }

        private ExposedPropertyOverride FindOverrideForProperty(ExposedProperty graphProperty)
        {
            if (_propertyOverrides == null || graphProperty == null)
            {
                return null;
            }

            string propertyId = graphProperty.PropertyId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(propertyId))
            {
                ExposedPropertyOverride overrideById = FindOverrideById(propertyId);
                if (overrideById != null)
                {
                    return overrideById;
                }
            }

            return FindOverrideByName(graphProperty.PropertyName);
        }

        private ExposedProperty ResolveGraphProperty(ExposedPropertyOverride propertyOverride)
        {
            if (_graph == null || propertyOverride == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(propertyOverride.PropertyId))
            {
                ExposedProperty propertyById = _graph.GetExposedProperty(propertyOverride.PropertyId);
                if (propertyById != null)
                {
                    return propertyById;
                }
            }

            if (!string.IsNullOrWhiteSpace(propertyOverride.PropertyName))
            {
                return _graph.GetExposedPropertyByName(propertyOverride.PropertyName);
            }

            return null;
        }

        private ExposedPropertyOverride FindOverrideById(string propertyId)
        {
            if (_propertyOverrides == null || string.IsNullOrWhiteSpace(propertyId))
            {
                return null;
            }

            int overrideIndex;
            for (overrideIndex = 0; overrideIndex < _propertyOverrides.Count; overrideIndex++)
            {
                ExposedPropertyOverride propertyOverride = _propertyOverrides[overrideIndex];
                if (propertyOverride != null &&
                    string.Equals(propertyOverride.PropertyId, propertyId, StringComparison.Ordinal))
                {
                    return propertyOverride;
                }
            }

            return null;
        }

        private ExposedPropertyOverride FindOverrideByName(string propertyName)
        {
            if (_propertyOverrides == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return null;
            }

            int overrideIndex;
            for (overrideIndex = 0; overrideIndex < _propertyOverrides.Count; overrideIndex++)
            {
                ExposedPropertyOverride propertyOverride = _propertyOverrides[overrideIndex];
                if (propertyOverride != null &&
                    string.Equals(propertyOverride.PropertyName, propertyName, StringComparison.Ordinal))
                {
                    return propertyOverride;
                }
            }

            return null;
        }

        private static string GetPropertyRuntimeKey(ExposedProperty property)
        {
            if (property == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(property.PropertyId)
                ? (property.PropertyName ?? string.Empty)
                : property.PropertyId;
        }

#if UNITY_EDITOR
        private void SaveBakedSnapshotAsset(WorldSnapshot snapshot, long seed, string outputChannelName)
        {
            UnityEngine.SceneManagement.Scene activeScene = gameObject.scene;
            string sceneName = string.IsNullOrWhiteSpace(activeScene.name) ? "Untitled" : activeScene.name;
            string bakedSnapshotPath = string.Format(CultureInfo.InvariantCulture, BakedSnapshotPathFormat, sceneName);

            UnityEngine.Object existingAsset = AssetDatabase.LoadMainAssetAtPath(bakedSnapshotPath);
            if (existingAsset != null && !(existingAsset is BakedWorldSnapshot))
            {
                throw new InvalidOperationException("Cannot save baked snapshot because '" + bakedSnapshotPath + "' is already used by a different asset.");
            }

            BakedWorldSnapshot bakedSnapshot = AssetDatabase.LoadAssetAtPath<BakedWorldSnapshot>(bakedSnapshotPath);
            if (bakedSnapshot == null)
            {
                bakedSnapshot = ScriptableObject.CreateInstance<BakedWorldSnapshot>();
                AssetDatabase.CreateAsset(bakedSnapshot, bakedSnapshotPath);
            }

            bakedSnapshot.Snapshot = snapshot;
            bakedSnapshot.OutputChannelName = outputChannelName ?? string.Empty;
            bakedSnapshot.Seed = seed;
            bakedSnapshot.Timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            bakedSnapshot.Width = snapshot.Width;
            bakedSnapshot.Height = snapshot.Height;

            _bakedWorldSnapshot = bakedSnapshot;

            EditorUtility.SetDirty(bakedSnapshot);
            EditorUtility.SetDirty(this);
            EditorSceneManager.MarkSceneDirty(activeScene);
            AssetDatabase.SaveAssets();
        }
#endif
    }
}
