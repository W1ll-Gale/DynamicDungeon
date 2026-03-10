using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DynamicDungeon.Runtime.Biome;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Nodes;
using DynamicDungeon.Runtime.Output;
using DynamicDungeon.Runtime.Semantic;
using UnityEngine;

namespace DynamicDungeon.Runtime.Component
{
    [DisallowMultipleComponent]
    public sealed class DungeonGeneratorComponent : MonoBehaviour
    {
        private const string IdleStatusLabel = "Idle";
        private const string GeneratingStatusLabel = "Generating…";
        private const string DoneStatusLabel = "Done";
        private const string FailedStatusLabel = "Failed";
        private const string DefaultIntChannelName = "LogicalIds";
        private const string PhaseOneNoiseChannelName = "Phase1Noise";
        private const string PhaseOnePerlinNodeId = "phase1-perlin-node";
        private const string PhaseOneFlatFillNodeId = "phase1-flat-fill-node";
        private const float PhaseOneFlatFillValue = 1.0f;
        private const float PhaseOneNoiseFrequency = 0.045f;
        private const float PhaseOneNoiseAmplitude = 1.0f;
        private const int PhaseOneNoiseOctaves = 4;
        private const float PhaseOneWallThreshold = 0.58f;

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
        private Grid _grid;

        [SerializeField]
        private List<TilemapLayerDefinition> _layerDefinitions = new List<TilemapLayerDefinition>();

        [SerializeField]
        private BiomeAsset _biome;

        [SerializeField]
        private string _intChannelName = DefaultIntChannelName;

        [SerializeField]
        private Vector3Int _tilemapOffset;

        [SerializeField]
        [HideInInspector]
        private WorldSnapshot _bakedWorldSnapshot;

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

        public void CancelGeneration()
        {
            if (_generationCancellationSource != null)
            {
                _generationCancellationSource.Cancel();
            }
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
            if (_generateOnStart)
            {
                Generate();
            }
        }

        private static WorldSnapshot.FloatChannelSnapshot FindFloatChannel(WorldSnapshot snapshot, string channelName)
        {
            int index;
            for (index = 0; index < snapshot.FloatChannels.Length; index++)
            {
                WorldSnapshot.FloatChannelSnapshot channel = snapshot.FloatChannels[index];
                if (channel != null && string.Equals(channel.Name, channelName, StringComparison.Ordinal))
                {
                    return channel;
                }
            }

            throw new InvalidOperationException("WorldSnapshot does not contain float channel '" + channelName + "'.");
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

            if (_bakedWorldSnapshot != null)
            {
                try
                {
                    WriteSnapshotToTilemaps(_bakedWorldSnapshot);
                    args.Snapshot = _bakedWorldSnapshot;
                    args.WasBakedFallback = true;
                    return args;
                }
                catch (Exception fallbackException)
                {
                    Debug.LogError("Dungeon generation fallback failed: " + fallbackException, this);
                }
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
                ExecutionPlan plan = BuildPhaseOneExecutionPlan(seed);
                ExecutionResult executionResult = await _executor.ExecuteAsync(plan, cancellationToken);

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

                WorldSnapshot outputSnapshot = CreatePhaseOneOutputSnapshot(executionResult.Snapshot);
                WriteSnapshotToTilemaps(outputSnapshot);

                GenerationCompletedArgs completedArgs = new GenerationCompletedArgs();
                completedArgs.Snapshot = outputSnapshot;
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

            if (string.IsNullOrWhiteSpace(_intChannelName))
            {
                throw new InvalidOperationException("IntChannelName must be non-empty.");
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

        private ExecutionPlan BuildPhaseOneExecutionPlan(long seed)
        {
            PerlinNoiseNode perlinNoiseNode = new PerlinNoiseNode(
                PhaseOnePerlinNodeId,
                PhaseOneNoiseChannelName,
                PhaseOneNoiseFrequency,
                PhaseOneNoiseAmplitude,
                Vector2.zero,
                PhaseOneNoiseOctaves);

            FlatFillNode flatFillNode = new FlatFillNode(PhaseOneFlatFillNodeId, PhaseOneFlatFillValue);

            IGenNode[] orderedNodes =
            {
                perlinNoiseNode,
                flatFillNode
            };

            return ExecutionPlan.Build(orderedNodes, _worldWidth, _worldHeight, seed);
        }

        private WorldSnapshot CreatePhaseOneOutputSnapshot(WorldSnapshot sourceSnapshot)
        {
            WorldSnapshot.FloatChannelSnapshot flatFillChannel = FindFloatChannel(sourceSnapshot, "FlatOutput");
            WorldSnapshot.FloatChannelSnapshot noiseChannel = FindFloatChannel(sourceSnapshot, PhaseOneNoiseChannelName);

            if (flatFillChannel.Data.Length != sourceSnapshot.Width * sourceSnapshot.Height)
            {
                throw new InvalidOperationException("Flat fill channel length does not match the world dimensions.");
            }

            if (noiseChannel.Data.Length != sourceSnapshot.Width * sourceSnapshot.Height)
            {
                throw new InvalidOperationException("Perlin noise channel length does not match the world dimensions.");
            }

            int[] logicalIds = new int[sourceSnapshot.Width * sourceSnapshot.Height];

            int index;
            for (index = 0; index < logicalIds.Length; index++)
            {
                if (flatFillChannel.Data[index] <= 0.0f)
                {
                    logicalIds[index] = LogicalTileId.Void;
                    continue;
                }

                logicalIds[index] = noiseChannel.Data[index] >= PhaseOneWallThreshold
                    ? LogicalTileId.Wall
                    : LogicalTileId.Floor;
            }

            WorldSnapshot.IntChannelSnapshot logicalChannel = new WorldSnapshot.IntChannelSnapshot();
            logicalChannel.Name = _intChannelName;
            logicalChannel.Data = logicalIds;

            WorldSnapshot outputSnapshot = new WorldSnapshot();
            outputSnapshot.Width = sourceSnapshot.Width;
            outputSnapshot.Height = sourceSnapshot.Height;
            outputSnapshot.Seed = sourceSnapshot.Seed;
            outputSnapshot.FloatChannels = sourceSnapshot.FloatChannels;
            outputSnapshot.BoolMaskChannels = sourceSnapshot.BoolMaskChannels;
            outputSnapshot.IntChannels = new[] { logicalChannel };
            return outputSnapshot;
        }

        private void WriteSnapshotToTilemaps(WorldSnapshot snapshot)
        {
            Grid resolvedGrid = ResolveGrid(true);
            TileSemanticRegistry registry = TileSemanticRegistry.GetOrLoad();

            _tilemapLayerWriter.EnsureTimelapsCreated(resolvedGrid, _layerDefinitions);
            _tilemapLayerWriter.ClearAll();
            _tilemapOutputPass.Execute(snapshot, _intChannelName, _biome, registry, _tilemapLayerWriter, _layerDefinitions, _tilemapOffset);
        }
    }
}
