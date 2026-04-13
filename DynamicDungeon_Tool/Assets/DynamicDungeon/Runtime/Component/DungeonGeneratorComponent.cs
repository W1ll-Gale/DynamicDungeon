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
    public sealed class DungeonGeneratorComponent : MonoBehaviour
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
        private const string MissingGraphMessage = "Dungeon generation failed: no GenGraph is assigned.";
        private const string GraphCompilationFailedMessage = "Graph compilation failed.";

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
        private BakedWorldSnapshot _bakedWorldSnapshot;

        private readonly Executor _executor = new Executor();
        private readonly TilemapOutputPass _tilemapOutputPass = new TilemapOutputPass();
        private readonly TilemapLayerWriter _tilemapLayerWriter = new TilemapLayerWriter();
        private readonly System.Random _seedRandom = new System.Random();
        private readonly SemaphoreSlim _generationRequestSemaphore = new SemaphoreSlim(1, 1);

        private CancellationTokenSource _generationCancellationSource;
        private Task _currentGenerationTask = Task.CompletedTask;
        private string _statusLabel = IdleStatusLabel;

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

        public void CancelGeneration()
        {
            if (_generationCancellationSource != null)
            {
                _generationCancellationSource.Cancel();
            }
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
                Debug.LogError("Dungeon generation failed unexpectedly: " + exception, this);
            }
        }

        private void ClearTilemapsIfPossible()
        {
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
            if (_seedMode == SeedMode.Stable)
            {
                return _stableSeed;
            }

            unchecked
            {
                long upperBits = (long)_seedRandom.Next() << 32;
                long lowerBits = (uint)_seedRandom.Next();
                return upperBits | lowerBits;
            }
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
                throw new InvalidOperationException("DungeonGeneratorComponent requires a Grid reference.");
            }

            return _grid;
        }

        private async Task RunGenerationAsync(CancellationToken cancellationToken)
        {
            _statusLabel = GeneratingStatusLabel;
            RaiseGenerationStarted();

            try
            {
                ValidateConfiguration();

                long seed = GetSeedForRun();
                GraphCompileResult compileResult;
                string compileErrorMessage;
                if (!TryCompileGraph(seed, out compileResult, out compileErrorMessage))
                {
                    _statusLabel = FailedStatusLabel;
                    RaiseGenerationCompleted(CreateFailureArgs(compileErrorMessage));
                    return;
                }

                ExecutionResult executionResult = await _executor.ExecuteAsync(compileResult.Plan, cancellationToken);

                if (executionResult.WasCancelled)
                {
                    _statusLabel = IdleStatusLabel;

                    GenerationCompletedArgs cancelledArgs = new GenerationCompletedArgs();
                    cancelledArgs.Snapshot = null;
                    cancelledArgs.IsSuccess = false;
                    cancelledArgs.WasBakedFallback = false;
                    cancelledArgs.ErrorMessage = "Generation cancelled.";
                    RaiseGenerationCompleted(cancelledArgs);
                    return;
                }

                if (!executionResult.IsSuccess || executionResult.Snapshot == null)
                {
                    string executionError = string.IsNullOrWhiteSpace(executionResult.ErrorMessage) ? "Generation failed." : executionResult.ErrorMessage;
                    Debug.LogError("Dungeon generation failed: " + executionError, this);
                    _statusLabel = FailedStatusLabel;
                    RaiseGenerationCompleted(CreateFailureArgs(executionError));
                    return;
                }

                if (compileResult.HasConnectedOutput)
                {
                    WriteSnapshotToTilemaps(executionResult.Snapshot, compileResult.OutputChannelName);
                }

                GenerationCompletedArgs completedArgs = new GenerationCompletedArgs();
                completedArgs.Snapshot = executionResult.Snapshot;
                completedArgs.IsSuccess = true;
                completedArgs.WasBakedFallback = false;
                completedArgs.ErrorMessage = null;

                _statusLabel = DoneStatusLabel;
                RaiseGenerationCompleted(completedArgs);
            }
            catch (Exception exception)
            {
                Debug.LogError("Dungeon generation failed: " + exception, this);
                _statusLabel = FailedStatusLabel;
                RaiseGenerationCompleted(CreateFailureArgs(exception.Message));
            }
        }

        private void ValidateConfiguration()
        {
            if (_worldWidth <= 0)
            {
                throw new InvalidOperationException("WorldWidth must be greater than zero.");
            }

            if (_worldHeight <= 0)
            {
                throw new InvalidOperationException("WorldHeight must be greater than zero.");
            }

            if (_biome == null)
            {
                throw new InvalidOperationException("DungeonGeneratorComponent requires a Biome assignment.");
            }

            if (_layerDefinitions == null || _layerDefinitions.Count == 0)
            {
                throw new InvalidOperationException("DungeonGeneratorComponent requires at least one TilemapLayerDefinition.");
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

            bool graphChanged;
            string migrationErrorMessage;
            if (!GraphOutputUtility.TryUpgradeToCurrentSchema(_graph, out graphChanged, out migrationErrorMessage))
            {
                Debug.LogError("Graph upgrade failed: " + migrationErrorMessage, this);
                errorMessage = migrationErrorMessage ?? GraphCompilationFailedMessage;
                return false;
            }

#if UNITY_EDITOR
            if (graphChanged)
            {
                EditorUtility.SetDirty(_graph);
            }
#endif

            int originalWorldWidth = _graph.WorldWidth;
            int originalWorldHeight = _graph.WorldHeight;
            long originalSeed = _graph.DefaultSeed;

            try
            {
                _graph.WorldWidth = _worldWidth;
                _graph.WorldHeight = _worldHeight;
                _graph.DefaultSeed = seed;

                compileResult = GraphCompiler.Compile(_graph);
                if (!compileResult.IsSuccess || compileResult.Plan == null)
                {
                    LogCompileDiagnostics(compileResult.Diagnostics);
                    errorMessage = GraphCompilationFailedMessage;
                    return false;
                }

                return true;
            }
            finally
            {
                _graph.WorldWidth = originalWorldWidth;
                _graph.WorldHeight = originalWorldHeight;
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
            ExecutionResult executionResult = _executor.ExecuteAsync(compileResult.Plan, CancellationToken.None).GetAwaiter().GetResult();

            if (executionResult.WasCancelled)
            {
                throw new InvalidOperationException("Generation cancelled.");
            }

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
                    return true;
                }

                WriteSnapshotToTilemaps(bakedSnapshot.Snapshot, bakedSnapshot.OutputChannelName);
                snapshot = bakedSnapshot.Snapshot;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("Dungeon generation fallback failed: " + exception, this);
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

        private void WriteSnapshotToTilemaps(WorldSnapshot snapshot, string outputChannelName)
        {
            if (string.IsNullOrWhiteSpace(outputChannelName))
            {
                return;
            }

            Grid resolvedGrid = ResolveGrid(true);
            TileSemanticRegistry registry = TileSemanticRegistry.GetOrLoad();

            _tilemapLayerWriter.EnsureTimelapsCreated(resolvedGrid, _layerDefinitions);
            _tilemapLayerWriter.ClearAll();
            _tilemapOutputPass.Execute(snapshot, outputChannelName, _biome, registry, _tilemapLayerWriter, _layerDefinitions, _tilemapOffset);
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
