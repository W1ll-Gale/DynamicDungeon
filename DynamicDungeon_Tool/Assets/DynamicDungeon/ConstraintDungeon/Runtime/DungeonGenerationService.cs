using System;
using System.Threading;
using System.Threading.Tasks;
using DynamicDungeon.ConstraintDungeon.Solver;
using UnityEngine;

namespace DynamicDungeon.ConstraintDungeon
{
    public sealed class DungeonGenerationRequest
    {
        public DungeonGenerationMode Mode;
        public DungeonFlow Flow;
        public OrganicGenerationSettings OrganicSettings;
        public int LayoutAttempts = 1000;
        public int MaxSearchSteps = 50000;
        public int FlowSeed = 0;
        public bool UseRandomFlowSeed = true;
        public bool EnableDiagnostics;
    }

    public sealed class DungeonGenerationService
    {
        private const int ExpandedFlowAttemptsBeforeFallback = 2;

        private readonly Action<float, string> progressChanged;
        private CancellationTokenSource currentGenerationCts;
        private bool isGenerating;

        public bool IsGenerating => isGenerating;

        public DungeonGenerationService(Action<float, string> progressChanged = null)
        {
            this.progressChanged = progressChanged;
        }

        public async Task<DungeonGenerationResult> GenerateLayoutAsync(DungeonGenerationRequest request)
        {
            if (isGenerating)
            {
                Debug.LogWarning("[DungeonGenerationService] Generate requested while generation is already running.");
                return DungeonGenerationResult.Failed("Generation is already running.");
            }

            if (!ValidateRequest(request, out string failureStatus))
            {
                Report(0f, failureStatus);
                return DungeonGenerationResult.Failed(failureStatus);
            }

            isGenerating = true;
            Report(0f, "Preparing templates...");

            CancellationTokenSource cts = new CancellationTokenSource();
            currentGenerationCts = cts;
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                TemplateCatalog templates = PrepareTemplates(request);
                LogWarnings("[DungeonGenerationService]", templates.Warnings);

                if (templates.HasErrors)
                {
                    LogErrors("[DungeonGenerationService]", templates.Errors);
                    Report(0f, $"Template validation failed ({templates.Report.ErrorCount} error(s)).");
                    return DungeonGenerationResult.Failed($"Template validation failed ({templates.Report.ErrorCount} error(s)).");
                }

                DungeonGenerationResult result = await RunSolverAttemptsAsync(request, templates, cts);
                stopwatch.Stop();
                result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                return result;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                stopwatch.Stop();
                DungeonGenerationResult failedResult = DungeonGenerationResult.Failed(ex.Message);
                failedResult.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                return failedResult;
            }
            finally
            {
                if (currentGenerationCts == cts)
                {
                    currentGenerationCts = null;
                }

                cts.Dispose();
                isGenerating = false;
            }
        }

        public void Cancel()
        {
            if (!isGenerating || currentGenerationCts == null)
            {
                return;
            }

            Report(0f, "Cancelling...");
            currentGenerationCts.Cancel();
        }

        private static bool ValidateRequest(DungeonGenerationRequest request, out string failureStatus)
        {
            failureStatus = "Generation request is valid.";

            if (request == null)
            {
                Debug.LogError("[DungeonGenerationService] Missing generation request.");
                failureStatus = "Missing generation request.";
                return false;
            }

            if (request.Mode == DungeonGenerationMode.FlowGraph)
            {
                if (request.Flow == null)
                {
                    Debug.LogError("[DungeonGenerationService] No Dungeon Flow assigned for Flow Graph mode.");
                    failureStatus = "No Dungeon Flow assigned.";
                    return false;
                }

                DungeonFlowValidator.Result validation = DungeonFlowValidator.Validate(request.Flow);
                LogWarnings("[DungeonGenerationService]", validation.Warnings);

                if (!validation.IsValid)
                {
                    LogErrors("[DungeonGenerationService]", validation.Errors);
                    failureStatus = $"Flow validation failed ({validation.Errors.Count} error(s)).";
                    return false;
                }

                return true;
            }

            OrganicGenerationSettings organicSettings = request.OrganicSettings;
            organicSettings?.EnsureValidState();

            if (organicSettings == null || !organicSettings.HasAnyTemplate())
            {
                Debug.LogError("[DungeonGenerationService] Organic settings or template pool is missing.");
                failureStatus = "Organic settings or template pool is missing.";
                return false;
            }

            ValidationReport report = organicSettings.Validate();
            LogWarnings("[DungeonGenerationService]", report.Warnings);

            if (!report.IsValid)
            {
                LogErrors("[DungeonGenerationService]", report.Errors);
                failureStatus = $"Organic settings validation failed ({report.ErrorCount} error(s)).";
                return false;
            }

            return true;
        }

        private static TemplateCatalog PrepareTemplates(DungeonGenerationRequest request)
        {
            return request.Mode == DungeonGenerationMode.FlowGraph
                ? TemplatePreparer.PrepareForFlow(request.Flow)
                : TemplatePreparer.PrepareForOrganic(request.OrganicSettings);
        }

        private async Task<DungeonGenerationResult> RunSolverAttemptsAsync(DungeonGenerationRequest request, TemplateCatalog templates, CancellationTokenSource cts)
        {
            float solverProgress = 0f;
            DungeonLayout layout = null;
            bool wasCancelled = false;
            DungeonGenerationResult result = new DungeonGenerationResult();

            bool useCollapsedFlowGraph = false;
            bool canFallBackToCollapsedFlow = request.Mode == DungeonGenerationMode.FlowGraph &&
                                             request.Flow.HasExpandedCorridorLinks();
            DungeonFlow expandedSolverFlow = request.Mode == DungeonGenerationMode.FlowGraph
                ? request.Flow.CreateSolverFlow(true)
                : null;
            DungeonFlow collapsedSolverFlow = null;

            int attempts = Mathf.Max(1, request.LayoutAttempts);
            int baseSeed = GetBaseSeed(request);

            try
            {
                for (int index = 0; index < attempts; index++)
                {
                    if (cts.IsCancellationRequested)
                    {
                        break;
                    }

                    int attemptNumber = index + 1;
                    int attemptSeed = unchecked(baseSeed + index);
                    solverProgress = 0f;
                    DungeonGenerationDiagnostics diagnostics = new DungeonGenerationDiagnostics();
                    diagnostics.Begin(attemptNumber, attemptSeed);
                    Report(0f, $"Fitting rooms... Attempt {attemptNumber}/{attempts}");

                    DungeonFlow solverFlow = GetSolverFlowForAttempt(
                        request,
                        useCollapsedFlowGraph,
                        expandedSolverFlow,
                        ref collapsedSolverFlow);

                    Task<(DungeonLayout layout, string failureReason)> solverTask = Task.Run(() =>
                    {
                        IDungeonGenerationStrategy strategy = CreateStrategy(request);
                        GenerationContext context = new GenerationContext(
                            request,
                            templates,
                            solverFlow,
                            CreateSolverSettings(request, attemptSeed),
                            p => solverProgress = p,
                            cts.Token,
                            diagnostics);

                        DungeonLayout generatedLayout = strategy.Generate(context);
                        diagnostics.End(strategy.LastFailureReason);

                        if (generatedLayout == null && !string.IsNullOrEmpty(strategy.LastFailureReason))
                        {
                            LogDiagnostics(request, $"[DungeonGenerationService] Attempt {attemptNumber} failed: {strategy.LastFailureReason}");
                        }

                        return (generatedLayout, strategy.LastFailureReason);
                    });

                    while (!solverTask.IsCompleted)
                    {
                        Report(solverProgress, $"Fitting rooms... Attempt {attemptNumber}/{attempts} ({Mathf.RoundToInt(solverProgress * 100f)}%)");

                        if (cts.IsCancellationRequested)
                        {
                            wasCancelled = true;
                            await solverTask;
                            break;
                        }

                        await Task.Yield();
                    }

                    if (cts.IsCancellationRequested)
                    {
                        wasCancelled = true;
                        break;
                    }

                    if (wasCancelled)
                    {
                        break;
                    }

                    (DungeonLayout generatedLayout, string failureReason) solverOutput = await solverTask;
                    layout = solverOutput.generatedLayout;
                    string failureReason = solverOutput.failureReason;
                    result.Attempts.Add(DungeonGenerationAttemptSummary.FromDiagnostics(diagnostics, layout != null));
                    result.AttemptCount = attemptNumber;
                    result.Seed = attemptSeed;
                    result.Diagnostics = diagnostics;

                    if (layout != null)
                    {
                        result.Success = true;
                        result.Layout = layout;
                        result.FailureReason = null;
                        Report(1f, $"Generated {layout.Rooms.Count} rooms.");
                        break;
                    }

                    result.FailureReason = failureReason;
                    LogDiagnostics(request, $"[DungeonGenerationService] Diagnostics: {diagnostics.ToSummary()}");

                    if (canFallBackToCollapsedFlow && !useCollapsedFlowGraph && index + 1 >= ExpandedFlowAttemptsBeforeFallback)
                    {
                        Debug.LogWarning("[DungeonGenerationService] Expanded corridor counts could not be solved quickly. Falling back to one generated corridor per designer link.");
                        useCollapsedFlowGraph = true;
                    }

                    LogDiagnostics(request, $"[DungeonGenerationService] Attempt {attemptNumber} failed. Retrying...");
                }
            }
            finally
            {
                DungeonFlow.DestroyTemporarySolverFlow(expandedSolverFlow);
                DungeonFlow.DestroyTemporarySolverFlow(collapsedSolverFlow);
            }

            if (wasCancelled)
            {
                Report(0f, "Cancelled");
                result.Success = false;
                result.FailureReason = "Generation was cancelled.";
                return result;
            }

            if (layout == null)
            {
                Report(0f, "Failed to find a valid layout.");
                Debug.LogError("[DungeonGenerationService] Solver failed to find a valid layout.");
            }

            return result;
        }

        private static DungeonFlow GetSolverFlowForAttempt(
            DungeonGenerationRequest request,
            bool useCollapsedFlowGraph,
            DungeonFlow expandedSolverFlow,
            ref DungeonFlow collapsedSolverFlow)
        {
            if (request.Mode != DungeonGenerationMode.FlowGraph)
            {
                return null;
            }

            if (!useCollapsedFlowGraph)
            {
                return expandedSolverFlow;
            }

            if (collapsedSolverFlow == null)
            {
                collapsedSolverFlow = request.Flow.CreateSolverFlow(false);
            }

            return collapsedSolverFlow;
        }

        private static IDungeonGenerationStrategy CreateStrategy(DungeonGenerationRequest request)
        {
            if (request.Mode == DungeonGenerationMode.FlowGraph)
            {
                return new FlowGraphGenerationStrategy();
            }

            return new OrganicGrowthGenerationStrategy();
        }

        private static DungeonSolver.SolverSettings CreateSolverSettings(DungeonGenerationRequest request, int seed)
        {
            return new DungeonSolver.SolverSettings
            {
                maxSearchSteps = Mathf.Max(1, request.MaxSearchSteps),
                useRandomisation = true,
                seed = seed,
                enableDiagnostics = request.EnableDiagnostics
            };
        }

        private static int GetBaseSeed(DungeonGenerationRequest request)
        {
            if (request.Mode == DungeonGenerationMode.OrganicGrowth)
            {
                return request.OrganicSettings != null && !request.OrganicSettings.useRandomSeed
                    ? request.OrganicSettings.seed
                    : Environment.TickCount;
            }

            return request.UseRandomFlowSeed ? Environment.TickCount : request.FlowSeed;
        }

        private void Report(float progress, string status)
        {
            progressChanged?.Invoke(Mathf.Clamp01(progress), status);
        }

        private static void LogDiagnostics(DungeonGenerationRequest request, string message)
        {
            if (request.EnableDiagnostics)
            {
                Debug.LogWarning(message);
            }
        }

        private static void LogWarnings(string prefix, System.Collections.Generic.IEnumerable<string> warnings)
        {
            foreach (string warning in warnings)
            {
                Debug.LogWarning($"{prefix} {warning}");
            }
        }

        private static void LogErrors(string prefix, System.Collections.Generic.IEnumerable<string> errors)
        {
            foreach (string error in errors)
            {
                Debug.LogError($"{prefix} {error}");
            }
        }
    }
}
