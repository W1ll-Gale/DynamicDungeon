using System;
using System.Collections.Generic;
using System.Threading;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
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
        private readonly Action<WorldSnapshot> _generationCompleted;
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

        public GenerationOrchestrator(DynamicDungeonGraphView graphView, Action<string> statusChanged, Action<WorldSnapshot> generationCompleted)
        {
            _graphView = graphView;
            _statusChanged = statusChanged;
            _generationCompleted = generationCompleted;
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
                _graphView.UpdateNodePreviews(null);
            }

            _statusChanged?.Invoke(IdleStatusText);
        }

        public void MarkNodeDirty(string nodeId)
        {
            if (_isDisposed || _graph == null || string.IsNullOrWhiteSpace(nodeId))
            {
                return;
            }

            _pendingDirtyNodeIds.Add(nodeId);

            if (_cachedPlan != null)
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

            if (_isGenerating)
            {
                CancelInFlightExecution();
                return;
            }

            BeginGeneration(true);
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
                    _statusChanged?.Invoke(FailedStatusText);
                    return;
                }

                _lastSnapshot = executionResult.Snapshot;
                _statusChanged?.Invoke(DoneStatusText);
                _generationCompleted?.Invoke(executionResult.Snapshot);
            }
            catch (Exception exception)
            {
                Debug.LogErrorFormat("Editor generation failed: {0}", exception.Message);
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
    }
}
