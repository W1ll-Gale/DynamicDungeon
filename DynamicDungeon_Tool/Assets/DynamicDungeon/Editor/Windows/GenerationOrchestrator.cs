using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DynamicDungeon.Editor.Nodes;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace DynamicDungeon.Editor.Windows
{
    public sealed class GenerationOrchestrator : IDisposable
    {
        private const double DebounceDelaySeconds = 0.05d;
        private const double LoadingIndicatorDelaySeconds = 0.2d;
        private const string IdleStatusText = "Idle";
        private const string GeneratingStatusText = "Generating...";
        private const string DoneStatusText = "Done";
        private const string FailedStatusText = "Failed";

        private readonly DynamicDungeonGraphView _graphView;
        private readonly Action<string> _statusChanged;
        private readonly Action<IReadOnlyList<GraphDiagnostic>> _diagnosticsUpdated;
        private readonly Executor _executor;
        private readonly HashSet<string> _pendingDirtyNodeIds;
        private readonly object _pendingPreviewUpdatesLock;
        private readonly Queue<PreviewUpdateData> _pendingPreviewUpdates;

        private GenGraph _graph;
        private ExecutionPlan _cachedPlan;
        private WorldSnapshot _lastSnapshot;
        private Task<ExecutionResult> _activeExecutionTask;
        private IReadOnlyList<GraphDiagnostic> _activeCompileDiagnostics;
        private CancellationTokenSource _generationCancellationTokenSource;
        private bool _debounceScheduled;
        private bool _editorUpdateSubscribed;
        private bool _generateAllRequested;
        private bool _isGenerating;
        private bool _isDisposed;
        private bool _loadingIndicatorsVisible;
        private bool _suppressFollowUpGeneration;
        private int _activeGenerationVersion;
        private double _loadingIndicatorTime;
        private double _nextGenerationTime;

        private enum PreviewChannelType
        {
            None,
            Float,
            Int,
            BoolMask,
            PointList
        }

        private sealed class PreviewUpdateData
        {
            public int GenerationVersion;
            public string NodeId;
            public PreviewChannelType ChannelType;
            public int Width;
            public int Height;
            public float[] FloatChannel;
            public int[] IntChannel;
            public byte[] BoolMaskChannel;
            public int2[] PointListChannel;
        }

        public bool IsGenerating
        {
            get
            {
                return _isGenerating;
            }
        }

        public GenerationOrchestrator(DynamicDungeonGraphView graphView, Action<string> statusChanged, Action<IReadOnlyList<GraphDiagnostic>> diagnosticsUpdated)
        {
            _graphView = graphView;
            _statusChanged = statusChanged;
            _diagnosticsUpdated = diagnosticsUpdated;
            _executor = new Executor();
            _pendingDirtyNodeIds = new HashSet<string>(StringComparer.Ordinal);
            _pendingPreviewUpdatesLock = new object();
            _pendingPreviewUpdates = new Queue<PreviewUpdateData>();
            _activeCompileDiagnostics = Array.Empty<GraphDiagnostic>();

            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        public void SetGraph(GenGraph graph)
        {
            CancelGeneration();
            ClearScheduledRefresh();
            ClearPendingPreviewUpdates();
            ReplaceCachedPlan(null);
            _lastSnapshot = null;
            _graph = graph;

            if (_graphView != null)
            {
                _graphView.SetGenerationOverlayVisible(false);
                _graphView.ClearNodePreviews();
            }

            _loadingIndicatorsVisible = false;
            ReportDiagnostics(Array.Empty<GraphDiagnostic>());
            _statusChanged?.Invoke(IdleStatusText);
        }

        public void MarkNodeDirty(string nodeId)
        {
            if (_isDisposed || _graph == null || string.IsNullOrWhiteSpace(nodeId))
            {
                return;
            }

            _pendingDirtyNodeIds.Add(nodeId);

            if (!_isGenerating && _cachedPlan != null)
            {
                try
                {
                    _cachedPlan.MarkDirty(nodeId);
                }
                catch (ObjectDisposedException)
                {
                    _cachedPlan = null;
                }
            }

            if (_isGenerating)
            {
                ScheduleDebouncedRefresh();
                CancelInFlightExecution();
                return;
            }

            ClearScheduledRefresh();
            BeginGeneration(false);
        }

        public void GenerateAll()
        {
            if (_isDisposed || _graph == null)
            {
                _statusChanged?.Invoke(FailedStatusText);
                return;
            }

            _generateAllRequested = true;
            ClearScheduledRefresh();

            if (!_isGenerating && _cachedPlan != null)
            {
                _cachedPlan.MarkAllDirty();
            }

            if (_isGenerating)
            {
                CancelInFlightExecution();
                return;
            }

            BeginGeneration(true);
        }

        public void RequestPreviewRefresh()
        {
            if (_isDisposed || _graph == null)
            {
                return;
            }

            _generateAllRequested = true;

            if (!_isGenerating && _cachedPlan != null)
            {
                _cachedPlan.MarkAllDirty();
            }

            if (_isGenerating)
            {
                ScheduleDebouncedRefresh();
                CancelInFlightExecution();
                return;
            }

            ClearScheduledRefresh();
            BeginGeneration(true);
        }

        public void CancelGeneration()
        {
            _generateAllRequested = false;
            _suppressFollowUpGeneration = true;
            CancelInFlightExecution();
            ClearScheduledRefresh();
            WaitForActiveExecutionCompletion();
            FinishActiveGeneration();
            ClearPendingPreviewUpdates();

            if (!_isGenerating)
            {
                HideLoadingIndicators(IdleStatusText, true);
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            CancelGeneration();
            ReplaceCachedPlan(null);

            if (_generationCancellationTokenSource != null)
            {
                _generationCancellationTokenSource.Dispose();
                _generationCancellationTokenSource = null;
            }

            if (_editorUpdateSubscribed)
            {
                EditorApplication.update -= OnEditorUpdate;
                _editorUpdateSubscribed = false;
            }

            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
        }

        private void BeginGeneration(bool forceFullGeneration)
        {
            if (_isDisposed || _graph == null)
            {
                return;
            }

            if (_isGenerating)
            {
                if (forceFullGeneration)
                {
                    _generateAllRequested = true;
                }

                return;
            }

            StartGeneration(forceFullGeneration);
        }

        private void StartGeneration(bool forceFullGeneration)
        {
            _isGenerating = true;
            _activeGenerationVersion++;
            int generationVersion = _activeGenerationVersion;
            ClearPendingPreviewUpdates();
            _loadingIndicatorsVisible = false;
            _loadingIndicatorTime = EditorApplication.timeSinceStartup + LoadingIndicatorDelaySeconds;
            EnsureEditorUpdateSubscription();

            DisposeCancellationSource();
            _generationCancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = _generationCancellationTokenSource.Token;

            try
            {
                List<string> dirtyNodeIds = new List<string>(_pendingDirtyNodeIds);
                _pendingDirtyNodeIds.Clear();

                bool generateAll = forceFullGeneration || _generateAllRequested || _lastSnapshot == null;
                _generateAllRequested = false;

                GraphCompileResult compileResult = GraphCompiler.CompileForPreview(_graph);
                if (!compileResult.IsSuccess || compileResult.Plan == null)
                {
                    if (compileResult.Plan != null)
                    {
                        compileResult.Plan.Dispose();
                    }

                    ReplaceCachedPlan(null);
                    ReportDiagnostics(compileResult.Diagnostics);
                    _statusChanged?.Invoke(FailedStatusText);
                    DisposeCancellationSource();
                    _activeCompileDiagnostics = Array.Empty<GraphDiagnostic>();
                    _isGenerating = false;
                    ReleaseEditorUpdateSubscriptionIfIdle();
                    return;
                }

                ExecutionPlan plan = compileResult.Plan;
                if (!generateAll && _lastSnapshot != null)
                {
                    try
                    {
                        plan.RestoreWorldSnapshot(_lastSnapshot);
                        plan.MarkAllClean();

                        int dirtyNodeIndex;
                        for (dirtyNodeIndex = 0; dirtyNodeIndex < dirtyNodeIds.Count; dirtyNodeIndex++)
                        {
                            plan.MarkDirty(dirtyNodeIds[dirtyNodeIndex]);
                        }
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarningFormat("Reactive generation fell back to a full run: {0}", exception.Message);
                        generateAll = true;
                    }
                }

                if (generateAll)
                {
                    plan.MarkAllDirty();
                }

                ReplaceCachedPlan(plan);
                _activeCompileDiagnostics = compileResult.Diagnostics ?? Array.Empty<GraphDiagnostic>();
                _activeExecutionTask = _executor.ExecuteAsync(
                    plan,
                    cancellationToken,
                    null,
                    false,
                    completedJobIndex => QueueCompletedJobPreview(plan, completedJobIndex, generationVersion));
            }
            catch (Exception exception)
            {
                HideLoadingIndicators(FailedStatusText, true);
                Debug.LogErrorFormat("Editor generation failed: {0}", exception.Message);
                ReportDiagnostics(CreateExecutionFailureDiagnostics(Array.Empty<GraphDiagnostic>(), exception.Message));
                DisposeCancellationSource();
                _activeCompileDiagnostics = Array.Empty<GraphDiagnostic>();
                _isGenerating = false;
                ReleaseEditorUpdateSubscriptionIfIdle();
            }
        }

        private void ScheduleDebouncedRefresh()
        {
            if (_isDisposed || _graph == null)
            {
                return;
            }

            _nextGenerationTime = EditorApplication.timeSinceStartup + DebounceDelaySeconds;
            _debounceScheduled = true;
            EnsureEditorUpdateSubscription();
        }

        private void OnEditorUpdate()
        {
            ProcessPendingPreviewUpdates();

            if (_isDisposed)
            {
                ClearScheduledRefresh();
                ClearPendingPreviewUpdates();
                ReleaseEditorUpdateSubscriptionIfIdle();
                return;
            }

            if (_activeExecutionTask != null && _activeExecutionTask.IsCompleted)
            {
                FinishActiveGeneration();
            }

            TryShowLoadingIndicators();

            if (_isGenerating || EditorApplication.timeSinceStartup < _nextGenerationTime)
            {
                return;
            }

            ClearScheduledRefresh();
            BeginGeneration(false);
        }

        private void ClearScheduledRefresh()
        {
            if (!_debounceScheduled)
            {
                return;
            }

            _debounceScheduled = false;
            ReleaseEditorUpdateSubscriptionIfIdle();
        }

        private void TryShowLoadingIndicators()
        {
            if (!_isGenerating || _loadingIndicatorsVisible || EditorApplication.timeSinceStartup < _loadingIndicatorTime)
            {
                return;
            }

            _loadingIndicatorsVisible = true;

            if (_graphView != null)
            {
                _graphView.SetGenerationOverlayVisible(true);
            }

            RefreshStaleNodePreviews();
            _statusChanged?.Invoke(GeneratingStatusText);
        }

        private void HideLoadingIndicators(string statusText, bool updateStatusWhenHidden)
        {
            bool wasVisible = _loadingIndicatorsVisible;
            _loadingIndicatorsVisible = false;

            if (_graphView != null)
            {
                _graphView.SetGenerationOverlayVisible(false);
            }

            if (wasVisible || updateStatusWhenHidden)
            {
                _statusChanged?.Invoke(statusText);
            }
        }

        private void FinishActiveGeneration()
        {
            Task<ExecutionResult> completedTask = _activeExecutionTask;
            if (completedTask == null)
            {
                if (_suppressFollowUpGeneration)
                {
                    _pendingDirtyNodeIds.Clear();
                    _suppressFollowUpGeneration = false;
                }

                return;
            }

            _activeExecutionTask = null;

            try
            {
                ExecutionResult executionResult = completedTask.GetAwaiter().GetResult();
                if (executionResult.WasCancelled)
                {
                    ProcessPendingPreviewUpdates();
                    HideLoadingIndicators(IdleStatusText, true);
                }
                else if (!executionResult.IsSuccess || executionResult.Snapshot == null)
                {
                    HideLoadingIndicators(FailedStatusText, true);
                    ReportDiagnostics(
                        CreateExecutionFailureDiagnostics(
                            CombineDiagnostics(_activeCompileDiagnostics, executionResult.Diagnostics),
                            executionResult.ErrorMessage));
                }
                else
                {
                    _lastSnapshot = executionResult.Snapshot;
                    ProcessPendingPreviewUpdates();
                    UpdateNodePreviewsFromPlan(_cachedPlan);
                    HideLoadingIndicators(DoneStatusText, false);
                    ReportDiagnostics(CombineDiagnostics(_activeCompileDiagnostics, executionResult.Diagnostics));
                }
            }
            catch (Exception exception)
            {
                HideLoadingIndicators(FailedStatusText, true);
                Debug.LogErrorFormat("Editor generation failed: {0}", exception.Message);
                ReportDiagnostics(CreateExecutionFailureDiagnostics(Array.Empty<GraphDiagnostic>(), exception.Message));
            }
            finally
            {
                DisposeCancellationSource();
                _activeCompileDiagnostics = Array.Empty<GraphDiagnostic>();
                _isGenerating = false;

                if (_suppressFollowUpGeneration)
                {
                    _pendingDirtyNodeIds.Clear();
                    _generateAllRequested = false;
                    _suppressFollowUpGeneration = false;
                }
                else if (_generateAllRequested)
                {
                    BeginGeneration(true);
                }
                else if (_pendingDirtyNodeIds.Count > 0)
                {
                    PropagatePendingDirtyNodesToCachedPlan();
                    ScheduleDebouncedRefresh();
                }

                ReleaseEditorUpdateSubscriptionIfIdle();
            }
        }

        private void ReplaceCachedPlan(ExecutionPlan plan)
        {
            if (_cachedPlan != null && !ReferenceEquals(_cachedPlan, plan))
            {
                _cachedPlan.Dispose();
            }

            _cachedPlan = plan;
        }

        private void CancelInFlightExecution()
        {
            if (_generationCancellationTokenSource != null)
            {
                _generationCancellationTokenSource.Cancel();
            }
        }

        private void WaitForActiveExecutionCompletion()
        {
            Task<ExecutionResult> activeExecutionTask = _activeExecutionTask;
            if (activeExecutionTask == null || activeExecutionTask.IsCompleted)
            {
                return;
            }

            try
            {
                activeExecutionTask.Wait();
            }
            catch (AggregateException)
            {
            }
        }

        private void DisposeCancellationSource()
        {
            if (_generationCancellationTokenSource == null)
            {
                return;
            }

            _generationCancellationTokenSource.Dispose();
            _generationCancellationTokenSource = null;
        }

        private void PropagatePendingDirtyNodesToCachedPlan()
        {
            if (_cachedPlan == null)
            {
                return;
            }

            foreach (string nodeId in _pendingDirtyNodeIds)
            {
                _cachedPlan.MarkDirty(nodeId);
            }
        }

        private void QueueCompletedJobPreview(ExecutionPlan plan, int jobIndex, int generationVersion)
        {
            if (_isDisposed || plan == null || jobIndex < 0 || jobIndex >= plan.Jobs.Count)
            {
                return;
            }

            PreviewUpdateData previewUpdate = CapturePreviewUpdate(plan, jobIndex, generationVersion);
            if (previewUpdate == null || string.IsNullOrWhiteSpace(previewUpdate.NodeId))
            {
                return;
            }

            lock (_pendingPreviewUpdatesLock)
            {
                _pendingPreviewUpdates.Enqueue(previewUpdate);
            }
        }

        private void ProcessPendingPreviewUpdates()
        {
            if (_graphView == null)
            {
                ClearPendingPreviewUpdates();
                ReleaseEditorUpdateSubscriptionIfIdle();
                return;
            }

            while (true)
            {
                PreviewUpdateData previewUpdate;
                lock (_pendingPreviewUpdatesLock)
                {
                    if (_pendingPreviewUpdates.Count == 0)
                    {
                        break;
                    }

                    previewUpdate = _pendingPreviewUpdates.Dequeue();
                }

                if (previewUpdate == null || string.IsNullOrWhiteSpace(previewUpdate.NodeId))
                {
                    continue;
                }

                if (previewUpdate.GenerationVersion != _activeGenerationVersion)
                {
                    continue;
                }

                Texture2D texture = null;
                try
                {
                    texture = CreatePreviewTexture(previewUpdate);
                }
                catch (Exception exception)
                {
                    Debug.LogWarningFormat("Preview rendering failed for node '{0}': {1}", previewUpdate.NodeId, exception.Message);
                }

                _graphView.SetNodePreview(previewUpdate.NodeId, texture);
            }

            ReleaseEditorUpdateSubscriptionIfIdle();
        }

        private void RefreshStaleNodePreviews()
        {
            if (_cachedPlan == null || _graphView == null)
            {
                return;
            }

            int jobIndex;
            for (jobIndex = 0; jobIndex < _cachedPlan.Jobs.Count; jobIndex++)
            {
                NodeJobDescriptor job = _cachedPlan.Jobs[jobIndex];
                if (job.IsDirty)
                {
                    _graphView.MarkNodePreviewStale(job.Node.NodeId);
                }
            }
        }

        private void EnsureEditorUpdateSubscription()
        {
            if (_editorUpdateSubscribed)
            {
                return;
            }

            EditorApplication.update += OnEditorUpdate;
            _editorUpdateSubscribed = true;
        }

        private void ReleaseEditorUpdateSubscriptionIfIdle()
        {
            if (!_editorUpdateSubscribed || _debounceScheduled || _isGenerating || HasPendingPreviewUpdates())
            {
                return;
            }

            EditorApplication.update -= OnEditorUpdate;
            _editorUpdateSubscribed = false;
        }

        private bool HasPendingPreviewUpdates()
        {
            lock (_pendingPreviewUpdatesLock)
            {
                return _pendingPreviewUpdates.Count > 0;
            }
        }

        private void ClearPendingPreviewUpdates()
        {
            lock (_pendingPreviewUpdatesLock)
            {
                _pendingPreviewUpdates.Clear();
            }
        }

        private void OnBeforeAssemblyReload()
        {
            if (!_isGenerating && _activeExecutionTask == null)
            {
                ClearScheduledRefresh();
                return;
            }

            Debug.Log("Generation cancelled for domain reload.");
            CancelGeneration();
        }

        private void UpdateNodePreviewsFromPlan(ExecutionPlan plan)
        {
            if (plan == null || _graphView == null)
            {
                return;
            }

            WorldData worldData = plan.AllocatedWorld;

            int jobIndex;
            for (jobIndex = 0; jobIndex < plan.Jobs.Count; jobIndex++)
            {
                NodeJobDescriptor job = plan.Jobs[jobIndex];

                ChannelDeclaration primaryOutput;
                if (!TryGetPrimaryOutputDeclaration(job, out primaryOutput))
                {
                    _graphView.SetNodePreview(job.Node.NodeId, null);
                    continue;
                }

                Texture2D texture = null;
                try
                {
                    texture = CreatePreviewTexture(primaryOutput, worldData);
                }
                catch (Exception exception)
                {
                    Debug.LogWarningFormat("Preview rendering failed for node '{0}': {1}", job.Node.NodeName, exception.Message);
                }

                _graphView.SetNodePreview(job.Node.NodeId, texture);
            }
        }

        private void ReportDiagnostics(IReadOnlyList<GraphDiagnostic> diagnostics)
        {
            IReadOnlyList<GraphDiagnostic> safeDiagnostics = diagnostics ?? Array.Empty<GraphDiagnostic>();
            _diagnosticsUpdated?.Invoke(safeDiagnostics);
        }

        private static PreviewUpdateData CapturePreviewUpdate(ExecutionPlan plan, int jobIndex, int generationVersion)
        {
            if (plan == null || jobIndex < 0 || jobIndex >= plan.Jobs.Count)
            {
                return null;
            }

            NodeJobDescriptor job = plan.Jobs[jobIndex];
            PreviewUpdateData previewUpdate = new PreviewUpdateData();
            previewUpdate.GenerationVersion = generationVersion;
            previewUpdate.NodeId = job.Node.NodeId;
            previewUpdate.Width = plan.AllocatedWorld.Width;
            previewUpdate.Height = plan.AllocatedWorld.Height;

            ChannelDeclaration primaryOutput;
            if (!TryGetPrimaryOutputDeclaration(job, out primaryOutput))
            {
                previewUpdate.ChannelType = PreviewChannelType.None;
                return previewUpdate;
            }

            WorldData worldData = plan.AllocatedWorld;

            switch (primaryOutput.Type)
            {
                case ChannelType.Float:
                    NativeArray<float> floatChannel = worldData.GetFloatChannel(primaryOutput.ChannelName);
                    if (!floatChannel.IsCreated)
                    {
                        previewUpdate.ChannelType = PreviewChannelType.None;
                        return previewUpdate;
                    }

                    previewUpdate.ChannelType = PreviewChannelType.Float;
                    previewUpdate.FloatChannel = new float[floatChannel.Length];
                    CopyNativeArray(floatChannel, previewUpdate.FloatChannel);
                    return previewUpdate;
                case ChannelType.Int:
                    NativeArray<int> intChannel = worldData.GetIntChannel(primaryOutput.ChannelName);
                    if (!intChannel.IsCreated)
                    {
                        previewUpdate.ChannelType = PreviewChannelType.None;
                        return previewUpdate;
                    }

                    previewUpdate.ChannelType = PreviewChannelType.Int;
                    previewUpdate.IntChannel = new int[intChannel.Length];
                    CopyNativeArray(intChannel, previewUpdate.IntChannel);
                    return previewUpdate;
                case ChannelType.BoolMask:
                    NativeArray<byte> boolMaskChannel = worldData.GetBoolMaskChannel(primaryOutput.ChannelName);
                    if (!boolMaskChannel.IsCreated)
                    {
                        previewUpdate.ChannelType = PreviewChannelType.None;
                        return previewUpdate;
                    }

                    previewUpdate.ChannelType = PreviewChannelType.BoolMask;
                    previewUpdate.BoolMaskChannel = new byte[boolMaskChannel.Length];
                    CopyNativeArray(boolMaskChannel, previewUpdate.BoolMaskChannel);
                    return previewUpdate;
                case ChannelType.PointList:
                    NativeList<int2> pointListChannel = worldData.GetPointListChannel(primaryOutput.ChannelName);
                    if (!pointListChannel.IsCreated)
                    {
                        previewUpdate.ChannelType = PreviewChannelType.None;
                        return previewUpdate;
                    }

                    previewUpdate.ChannelType = PreviewChannelType.PointList;
                    previewUpdate.PointListChannel = new int2[pointListChannel.Length];
                    CopyNativeList(pointListChannel, previewUpdate.PointListChannel);
                    return previewUpdate;
                default:
                    previewUpdate.ChannelType = PreviewChannelType.None;
                    return previewUpdate;
            }
        }

        private static IReadOnlyList<GraphDiagnostic> CreateExecutionFailureDiagnostics(IReadOnlyList<GraphDiagnostic> compileDiagnostics, string errorMessage)
        {
            List<GraphDiagnostic> diagnostics = new List<GraphDiagnostic>();
            IReadOnlyList<GraphDiagnostic> safeCompileDiagnostics = compileDiagnostics ?? Array.Empty<GraphDiagnostic>();

            int diagnosticIndex;
            for (diagnosticIndex = 0; diagnosticIndex < safeCompileDiagnostics.Count; diagnosticIndex++)
            {
                diagnostics.Add(safeCompileDiagnostics[diagnosticIndex]);
            }

            string safeMessage = string.IsNullOrWhiteSpace(errorMessage) ? "Generation failed." : errorMessage;
            diagnostics.Add(new GraphDiagnostic(DiagnosticSeverity.Error, safeMessage, null, null));
            return diagnostics;
        }

        private static IReadOnlyList<GraphDiagnostic> CombineDiagnostics(IReadOnlyList<GraphDiagnostic> first, IReadOnlyList<GraphDiagnostic> second)
        {
            IReadOnlyList<GraphDiagnostic> safeFirst = first ?? Array.Empty<GraphDiagnostic>();
            IReadOnlyList<GraphDiagnostic> safeSecond = second ?? Array.Empty<GraphDiagnostic>();

            if (safeFirst.Count == 0)
            {
                return safeSecond;
            }

            if (safeSecond.Count == 0)
            {
                return safeFirst;
            }

            List<GraphDiagnostic> combined = new List<GraphDiagnostic>(safeFirst.Count + safeSecond.Count);
            int index;
            for (index = 0; index < safeFirst.Count; index++)
            {
                combined.Add(safeFirst[index]);
            }

            for (index = 0; index < safeSecond.Count; index++)
            {
                combined.Add(safeSecond[index]);
            }

            return combined;
        }

        private static Texture2D CreatePreviewTexture(PreviewUpdateData previewUpdate)
        {
            if (previewUpdate == null)
            {
                return null;
            }

            switch (previewUpdate.ChannelType)
            {
                case PreviewChannelType.Float:
                    return NodePreviewRenderer.RenderFloatChannel(previewUpdate.FloatChannel, previewUpdate.Width, previewUpdate.Height);
                case PreviewChannelType.Int:
                    return NodePreviewRenderer.RenderIntChannel(previewUpdate.IntChannel, previewUpdate.Width, previewUpdate.Height);
                case PreviewChannelType.BoolMask:
                    return NodePreviewRenderer.RenderBoolMaskChannel(previewUpdate.BoolMaskChannel, previewUpdate.Width, previewUpdate.Height);
                case PreviewChannelType.PointList:
                    return NodePreviewRenderer.RenderPointListChannel(previewUpdate.PointListChannel, previewUpdate.Width, previewUpdate.Height);
                default:
                    return null;
            }
        }

        private static Texture2D CreatePreviewTexture(ChannelDeclaration outputDeclaration, WorldData worldData)
        {
            switch (outputDeclaration.Type)
            {
                case ChannelType.Float:
                    NativeArray<float> floatChannel = worldData.GetFloatChannel(outputDeclaration.ChannelName);
                    return floatChannel.IsCreated
                        ? NodePreviewRenderer.RenderFloatChannel(floatChannel, worldData.Width, worldData.Height)
                        : null;
                case ChannelType.Int:
                    NativeArray<int> intChannel = worldData.GetIntChannel(outputDeclaration.ChannelName);
                    return intChannel.IsCreated
                        ? NodePreviewRenderer.RenderIntChannel(intChannel, worldData.Width, worldData.Height)
                        : null;
                case ChannelType.BoolMask:
                    NativeArray<byte> boolMaskChannel = worldData.GetBoolMaskChannel(outputDeclaration.ChannelName);
                    return boolMaskChannel.IsCreated
                        ? NodePreviewRenderer.RenderBoolMaskChannel(boolMaskChannel, worldData.Width, worldData.Height)
                        : null;
                case ChannelType.PointList:
                    NativeList<int2> pointListChannel = worldData.GetPointListChannel(outputDeclaration.ChannelName);
                    return pointListChannel.IsCreated
                        ? NodePreviewRenderer.RenderPointListChannel(pointListChannel, worldData.Width, worldData.Height)
                        : null;
                default:
                    return null;
            }
        }

        private static bool TryGetPrimaryOutputDeclaration(NodeJobDescriptor job, out ChannelDeclaration outputDeclaration)
        {
            int channelIndex;
            for (channelIndex = 0; channelIndex < job.Channels.Count; channelIndex++)
            {
                ChannelDeclaration channelDeclaration = job.Channels[channelIndex];
                if (channelDeclaration.IsWrite)
                {
                    outputDeclaration = channelDeclaration;
                    return true;
                }
            }

            if (job.Node is DynamicDungeon.Runtime.Nodes.TilemapOutputNode)
            {
                for (channelIndex = 0; channelIndex < job.Channels.Count; channelIndex++)
                {
                    ChannelDeclaration channelDeclaration = job.Channels[channelIndex];
                    if (IsPreviewableChannelType(channelDeclaration.Type))
                    {
                        outputDeclaration = channelDeclaration;
                        return true;
                    }
                }
            }

            outputDeclaration = default;
            return false;
        }

        private static bool IsPreviewableChannelType(ChannelType channelType)
        {
            return channelType == ChannelType.Float ||
                   channelType == ChannelType.Int ||
                   channelType == ChannelType.BoolMask ||
                   channelType == ChannelType.PointList;
        }

        private static void CopyNativeArray(NativeArray<float> source, float[] destination)
        {
            int index;
            for (index = 0; index < source.Length; index++)
            {
                destination[index] = source[index];
            }
        }

        private static void CopyNativeArray(NativeArray<int> source, int[] destination)
        {
            int index;
            for (index = 0; index < source.Length; index++)
            {
                destination[index] = source[index];
            }
        }

        private static void CopyNativeArray(NativeArray<byte> source, byte[] destination)
        {
            int index;
            for (index = 0; index < source.Length; index++)
            {
                destination[index] = source[index];
            }
        }

        private static void CopyNativeList(NativeList<int2> source, int2[] destination)
        {
            int index;
            for (index = 0; index < source.Length; index++)
            {
                destination[index] = source[index];
            }
        }
    }
}
