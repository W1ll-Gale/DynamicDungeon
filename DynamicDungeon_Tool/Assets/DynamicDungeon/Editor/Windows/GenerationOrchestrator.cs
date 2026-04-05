using System;
using System.Collections.Generic;
using System.Threading;
using DynamicDungeon.Editor.Nodes;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace DynamicDungeon.Editor.Windows
{
    public sealed class GenerationOrchestrator : IDisposable
    {
        private const double DebounceDelaySeconds = 0.4d;
        private const string IdleStatusText = "Idle";
        private const string GeneratingStatusText = "Generating...";
        private const string DoneStatusText = "Done";
        private const string FailedStatusText = "Failed";

        private readonly DynamicDungeonGraphView _graphView;
        private readonly Action<string> _statusChanged;
        private readonly Action<IReadOnlyList<GraphDiagnostic>> _diagnosticsUpdated;
        private readonly Executor _executor;
        private readonly HashSet<string> _pendingDirtyNodeIds;

        private GenGraph _graph;
        private ExecutionPlan _cachedPlan;
        private WorldSnapshot _lastSnapshot;
        private CancellationTokenSource _generationCancellationTokenSource;
        private bool _debounceScheduled;
        private bool _generateAllRequested;
        private bool _isGenerating;
        private bool _isDisposed;
        private double _nextGenerationTime;

        public GenerationOrchestrator(DynamicDungeonGraphView graphView, Action<string> statusChanged, Action<IReadOnlyList<GraphDiagnostic>> diagnosticsUpdated)
        {
            _graphView = graphView;
            _statusChanged = statusChanged;
            _diagnosticsUpdated = diagnosticsUpdated;
            _executor = new Executor();
            _pendingDirtyNodeIds = new HashSet<string>(StringComparer.Ordinal);
        }

        public void SetGraph(GenGraph graph)
        {
            CancelGeneration();
            ClearScheduledRefresh();
            ReplaceCachedPlan(null);
            _lastSnapshot = null;
            _graph = graph;

            if (_graphView != null)
            {
                _graphView.SetGenerationOverlayVisible(false);
                _graphView.ClearNodePreviews();
            }

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
                    RefreshStaleNodePreviews();
                }
                catch (ObjectDisposedException)
                {
                    _cachedPlan = null;
                    if (_graphView != null)
                    {
                        _graphView.MarkNodePreviewStale(nodeId);
                    }
                }
            }
            else if (_graphView != null)
            {
                _graphView.MarkNodePreviewStale(nodeId);
            }

            ScheduleDebouncedRefresh();

            if (_isGenerating)
            {
                CancelInFlightExecution();
            }
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
                RefreshStaleNodePreviews();
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
                RefreshStaleNodePreviews();
            }

            ScheduleDebouncedRefresh();

            if (_isGenerating)
            {
                CancelInFlightExecution();
            }
        }

        public void CancelGeneration()
        {
            CancelInFlightExecution();
            ClearScheduledRefresh();

            if (!_isGenerating)
            {
                if (_graphView != null)
                {
                    _graphView.SetGenerationOverlayVisible(false);
                }

                _statusChanged?.Invoke(IdleStatusText);
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

            RunGenerationAsync(forceFullGeneration);
        }

        private async void RunGenerationAsync(bool forceFullGeneration)
        {
            _isGenerating = true;
            _statusChanged?.Invoke(GeneratingStatusText);

            if (_graphView != null)
            {
                _graphView.SetGenerationOverlayVisible(true);
            }

            DisposeCancellationSource();
            _generationCancellationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = _generationCancellationTokenSource.Token;

            try
            {
                List<string> dirtyNodeIds = new List<string>(_pendingDirtyNodeIds);
                _pendingDirtyNodeIds.Clear();

                bool generateAll = forceFullGeneration || _generateAllRequested || _lastSnapshot == null;
                _generateAllRequested = false;

                GraphCompileResult compileResult = GraphCompiler.Compile(_graph);
                if (!compileResult.IsSuccess || compileResult.Plan == null)
                {
                    if (compileResult.Plan != null)
                    {
                        compileResult.Plan.Dispose();
                    }

                    ReplaceCachedPlan(null);
                    ReportDiagnostics(compileResult.Diagnostics);
                    _statusChanged?.Invoke(FailedStatusText);
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

                ExecutionResult executionResult = await _executor.ExecuteAsync(plan, cancellationToken, null, false);
                if (executionResult.WasCancelled || cancellationToken.IsCancellationRequested)
                {
                    _statusChanged?.Invoke(IdleStatusText);
                    return;
                }

                if (!executionResult.IsSuccess || executionResult.Snapshot == null)
                {
                    ReportDiagnostics(CreateExecutionFailureDiagnostics(compileResult.Diagnostics, executionResult.ErrorMessage));
                    _statusChanged?.Invoke(FailedStatusText);
                    return;
                }

                _lastSnapshot = executionResult.Snapshot;
                UpdateNodePreviewsFromPlan(plan);
                ReportDiagnostics(compileResult.Diagnostics);
                _statusChanged?.Invoke(DoneStatusText);
            }
            catch (Exception exception)
            {
                Debug.LogErrorFormat("Editor generation failed: {0}", exception.Message);
                ReportDiagnostics(CreateExecutionFailureDiagnostics(Array.Empty<GraphDiagnostic>(), exception.Message));
                _statusChanged?.Invoke(FailedStatusText);
            }
            finally
            {
                _isGenerating = false;

                if (_graphView != null)
                {
                    _graphView.SetGenerationOverlayVisible(false);
                }

                if (_generateAllRequested)
                {
                    BeginGeneration(true);
                }
                else if (_pendingDirtyNodeIds.Count > 0)
                {
                    PropagatePendingDirtyNodesToCachedPlan();
                    RefreshStaleNodePreviews();
                    ScheduleDebouncedRefresh();
                }
            }
        }

        private void ScheduleDebouncedRefresh()
        {
            if (_isDisposed || _graph == null)
            {
                return;
            }

            _nextGenerationTime = EditorApplication.timeSinceStartup + DebounceDelaySeconds;
            if (_debounceScheduled)
            {
                return;
            }

            _debounceScheduled = true;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (_isDisposed)
            {
                ClearScheduledRefresh();
                return;
            }

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
            EditorApplication.update -= OnEditorUpdate;
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

            outputDeclaration = default;
            return false;
        }
    }
}
