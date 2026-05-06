using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DynamicDungeon.ConstraintDungeon;
using DynamicDungeon.Runtime;
using DynamicDungeon.Runtime.Component;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace DynamicDungeon.ConstraintDungeon.Editor.Benchmarking
{
    public sealed class DynamicDungeonBenchmarkWindow : EditorWindow
    {
        private const string OutputFolderPreferenceKey = "DynamicDungeon.Benchmarking.OutputFolder";
        private const string MapSizesPreferenceKey = "DynamicDungeon.Benchmarking.MapSizes";
        private const string RoomCountsPreferenceKey = "DynamicDungeon.Benchmarking.RoomCounts";
        private const string RepeatCountPreferenceKey = "DynamicDungeon.Benchmarking.RepeatCount";
        private const string WarmupCountPreferenceKey = "DynamicDungeon.Benchmarking.WarmupCount";
        private const string LeakThresholdPreferenceKey = "DynamicDungeon.Benchmarking.LeakThresholdMb";
        private const string SeedPreferenceKey = "DynamicDungeon.Benchmarking.Seed";
        private const string UseSeedOverridePreferenceKey = "DynamicDungeon.Benchmarking.UseSeedOverride";
        private const string DefaultMapSizes = "128x128, 256x256, 512x512";
        private const string DefaultRoomCounts = "10, 25, 50";
        private const string CsvExtension = ".benchmark.csv";

        private enum BenchmarkMode
        {
            WorldGraph,
            Dungeon,
            Combined
        }

        private enum ChartMetric
        {
            AverageElapsedMs,
            AverageGenerationMemoryMb,
            AverageRetainedMemoryMb
        }

        private readonly List<TilemapWorldGenerator> worldGenerators = new List<TilemapWorldGenerator>();
        private readonly List<DungeonGenerator> dungeonGenerators = new List<DungeonGenerator>();
        private readonly List<BenchmarkResultRow> results = new List<BenchmarkResultRow>();

        private BenchmarkMode benchmarkMode;
        private int selectedWorldGeneratorIndex;
        private int selectedDungeonGeneratorIndex;
        private string mapSizesText;
        private string roomCountsText;
        private int repeatCount;
        private int warmupCount;
        private long seed;
        private bool useSeedOverride;
        private float leakThresholdMb;
        private string outputFolder;
        private ChartMetric chartMetric;
        private Vector2 scrollPosition;
        private bool isRunning;
        private bool cancelRequested;
        private string status;
        private float progress;
        private CancellationTokenSource activeCancellationSource;
        private DungeonGenerator activeDungeonGenerator;

        [MenuItem(DynamicDungeonMenuPaths.ToolsBaseRoot + "Automated Benchmarks")]
        public static void OpenWindow()
        {
            DynamicDungeonBenchmarkWindow window = GetWindow<DynamicDungeonBenchmarkWindow>();
            window.titleContent = new GUIContent("Benchmarks");
            window.minSize = new Vector2(430.0f, 520.0f);
            window.Show();
            window.RefreshSceneTargets();
        }

        private void OnEnable()
        {
            mapSizesText = EditorPrefs.GetString(MapSizesPreferenceKey, DefaultMapSizes);
            roomCountsText = EditorPrefs.GetString(RoomCountsPreferenceKey, DefaultRoomCounts);
            repeatCount = Mathf.Max(1, EditorPrefs.GetInt(RepeatCountPreferenceKey, 3));
            warmupCount = Mathf.Max(0, EditorPrefs.GetInt(WarmupCountPreferenceKey, 1));
            leakThresholdMb = Mathf.Max(0.0f, EditorPrefs.GetFloat(LeakThresholdPreferenceKey, 16.0f));
            if (!long.TryParse(EditorPrefs.GetString(SeedPreferenceKey, "12345"), NumberStyles.Integer, CultureInfo.InvariantCulture, out seed))
            {
                seed = 12345L;
            }
            useSeedOverride = EditorPrefs.GetBool(UseSeedOverridePreferenceKey, true);
            outputFolder = EditorPrefs.GetString(OutputFolderPreferenceKey, string.Empty);
            EditorApplication.hierarchyChanged += HandleSceneTargetsChanged;
            EditorSceneManager.activeSceneChangedInEditMode += HandleActiveSceneChanged;
            EditorSceneManager.sceneOpened += HandleSceneOpened;
            EditorSceneManager.sceneClosed += HandleSceneClosed;
            RefreshSceneTargets();
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= HandleSceneTargetsChanged;
            EditorSceneManager.activeSceneChangedInEditMode -= HandleActiveSceneChanged;
            EditorSceneManager.sceneOpened -= HandleSceneOpened;
            EditorSceneManager.sceneClosed -= HandleSceneClosed;
            SavePreferences();
            activeCancellationSource?.Cancel();
            activeCancellationSource?.Dispose();
            activeCancellationSource = null;
        }

        private void OnGUI()
        {
            using (new EditorGUI.DisabledScope(isRunning))
            {
                DrawModeSection();
                DrawTargetSection();
                DrawParameterSection();
                DrawOutputSection();
            }

            DrawRunSection();
            DrawResultsSection();
        }

        private void DrawModeSection()
        {
            EditorGUILayout.LabelField("Benchmark Mode", EditorStyles.boldLabel);
            benchmarkMode = (BenchmarkMode)EditorGUILayout.EnumPopup("Mode", benchmarkMode);
            EditorGUILayout.HelpBox(GetModeHelpText(), MessageType.Info);
            GUILayout.Space(6.0f);
        }

        private void DrawTargetSection()
        {
            PruneSceneTargets();
            EditorGUILayout.LabelField("Scene Target", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Scene Targets", GUILayout.Width(170.0f)))
            {
                RefreshSceneTargets();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (benchmarkMode == BenchmarkMode.Dungeon)
            {
                selectedDungeonGeneratorIndex = DrawObjectPopup("Dungeon Generator", dungeonGenerators, selectedDungeonGeneratorIndex);
                DungeonGenerator dungeonGenerator = GetSelectedDungeonGenerator();
                if (dungeonGenerator == null)
                {
                    EditorGUILayout.HelpBox("No scene DungeonGenerator was found.", MessageType.Warning);
                }
                else if (dungeonGenerator.generationMode != DungeonGenerationMode.OrganicGrowth || dungeonGenerator.organicSettings == null)
                {
                    EditorGUILayout.HelpBox("Dungeon benchmark mode requires a DungeonGenerator using Organic Growth with an assigned Organic Growth Profile.", MessageType.Error);
                }
            }
            else
            {
                selectedWorldGeneratorIndex = DrawObjectPopup("World Generator", worldGenerators, selectedWorldGeneratorIndex);
                TilemapWorldGenerator worldGenerator = GetSelectedWorldGenerator();
                if (worldGenerator == null)
                {
                    EditorGUILayout.HelpBox("No scene TilemapWorldGenerator was found.", MessageType.Warning);
                }
                else if (worldGenerator.Graph == null)
                {
                    EditorGUILayout.HelpBox("The selected TilemapWorldGenerator has no graph assigned.", MessageType.Error);
                }
            }

            GUILayout.Space(6.0f);
        }

        private void DrawParameterSection()
        {
            EditorGUILayout.LabelField("Run Parameters", EditorStyles.boldLabel);
            if (benchmarkMode == BenchmarkMode.Dungeon)
            {
                roomCountsText = EditorGUILayout.TextField(new GUIContent("Room Counts", "Comma-separated requested organic room counts."), roomCountsText);
            }
            else
            {
                mapSizesText = EditorGUILayout.TextField(new GUIContent("Map Sizes", "Comma-separated sizes like 128x128, 256x256."), mapSizesText);
            }

            repeatCount = Mathf.Max(1, EditorGUILayout.IntField("Repeats", repeatCount));
            warmupCount = Mathf.Max(0, EditorGUILayout.IntField("Warmups", warmupCount));
            useSeedOverride = EditorGUILayout.Toggle("Use Stable Seed", useSeedOverride);
            using (new EditorGUI.DisabledScope(!useSeedOverride))
            {
                seed = EditorGUILayout.LongField("Seed", seed);
            }

            leakThresholdMb = Mathf.Max(0.0f, EditorGUILayout.FloatField("Leak Warning MB", leakThresholdMb));
            GUILayout.Space(6.0f);
        }

        private void DrawOutputSection()
        {
            EditorGUILayout.LabelField("CSV Output", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            outputFolder = EditorGUILayout.TextField("Folder", outputFolder);
            if (GUILayout.Button("Browse", GUILayout.Width(80.0f)))
            {
                string startFolder = string.IsNullOrWhiteSpace(outputFolder) ? GetDefaultOutputFolder() : outputFolder;
                string selectedFolder = EditorUtility.OpenFolderPanel("Benchmark CSV Output Folder", startFolder, string.Empty);
                if (!string.IsNullOrWhiteSpace(selectedFolder))
                {
                    outputFolder = selectedFolder;
                }
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Use Default Output Folder"))
            {
                outputFolder = string.Empty;
            }

            EditorGUILayout.LabelField("Resolved Folder", ResolveOutputFolder(), EditorStyles.miniLabel);
            GUILayout.Space(6.0f);
        }

        private void DrawRunSection()
        {
            EditorGUILayout.LabelField("Execution", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(isRunning || !CanRun(out _)))
            {
                if (GUILayout.Button("RUN BENCHMARK", GUILayout.Height(36.0f)))
                {
                    _ = RunBenchmarkAsync();
                }
            }

            if (isRunning)
            {
                Rect progressRect = GUILayoutUtility.GetRect(18.0f, 18.0f, "TextField");
                EditorGUI.ProgressBar(progressRect, Mathf.Clamp01(progress), status ?? string.Empty);
                using (new EditorGUI.DisabledScope(cancelRequested))
                {
                    if (GUILayout.Button(cancelRequested ? "CANCELLING..." : "CANCEL BENCHMARK"))
                    {
                        RequestCancel();
                    }
                }
            }
            else if (!CanRun(out string validationError))
            {
                EditorGUILayout.HelpBox(validationError, MessageType.Warning);
            }
            else if (!string.IsNullOrWhiteSpace(status))
            {
                EditorGUILayout.HelpBox(status, MessageType.Info);
            }

            GUILayout.Space(6.0f);
        }

        private void DrawResultsSection()
        {
            EditorGUILayout.LabelField("Last Results", EditorStyles.boldLabel);
            if (results.Count == 0)
            {
                EditorGUILayout.LabelField("No benchmark results yet.", EditorStyles.miniLabel);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(results.Count.ToString(CultureInfo.InvariantCulture) + " recorded rows", EditorStyles.miniLabel);
            if (GUILayout.Button("Reveal Output Folder", GUILayout.Width(150.0f)))
            {
                EditorUtility.RevealInFinder(ResolveOutputFolder());
            }
            EditorGUILayout.EndHorizontal();

            DrawResultsChart();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.MinHeight(120.0f));
            int firstIndex = Mathf.Max(0, results.Count - 20);
            for (int index = firstIndex; index < results.Count; index++)
            {
                BenchmarkResultRow row = results[index];
                EditorGUILayout.LabelField(row.ToSummary(), EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawResultsChart()
        {
            List<ChartPoint> chartPoints = BuildChartPoints();
            if (chartPoints.Count == 0)
            {
                EditorGUILayout.LabelField("No successful measured rows to graph.", EditorStyles.miniLabel);
                return;
            }

            chartMetric = (ChartMetric)EditorGUILayout.EnumPopup("Chart Metric", chartMetric);
            Rect chartRect = GUILayoutUtility.GetRect(10.0f, 190.0f, GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            DrawChartBackground(chartRect);

            float labelHeight = 34.0f;
            Rect plotRect = new Rect(chartRect.x + 64.0f, chartRect.y + 24.0f, chartRect.width - 76.0f, chartRect.height - labelHeight - 32.0f);
            if (plotRect.width <= 0.0f || plotRect.height <= 0.0f)
            {
                return;
            }

            double maxValue = 0.0d;
            for (int index = 0; index < chartPoints.Count; index++)
            {
                maxValue = Math.Max(maxValue, chartPoints[index].Value);
            }

            if (maxValue <= 0.0d)
            {
                maxValue = 1.0d;
            }

            DrawChartGrid(plotRect, maxValue);
            DrawChartBars(plotRect, chartPoints, maxValue);
            DrawChartLabels(plotRect, chartPoints, maxValue);
        }

        private static void DrawChartBackground(Rect chartRect)
        {
            EditorGUI.DrawRect(chartRect, new Color(0.16f, 0.16f, 0.16f, 1.0f));
            EditorGUI.DrawRect(new Rect(chartRect.x, chartRect.y, chartRect.width, 1.0f), new Color(0.28f, 0.28f, 0.28f, 1.0f));
            EditorGUI.DrawRect(new Rect(chartRect.x, chartRect.yMax - 1.0f, chartRect.width, 1.0f), new Color(0.08f, 0.08f, 0.08f, 1.0f));
        }

        private void DrawChartGrid(Rect plotRect, double maxValue)
        {
            GUIStyle labelStyle = EditorStyles.miniLabel;
            Color gridColor = new Color(1.0f, 1.0f, 1.0f, 0.12f);

            for (int index = 0; index <= 4; index++)
            {
                float y = Mathf.Lerp(plotRect.yMax, plotRect.y, index / 4.0f);
                EditorGUI.DrawRect(new Rect(plotRect.x, y, plotRect.width, 1.0f), gridColor);

                double value = maxValue * index / 4.0d;
                Rect labelRect = new Rect(plotRect.x - 62.0f, y - 8.0f, 58.0f, 16.0f);
                GUI.Label(labelRect, FormatChartValue(value), labelStyle);
            }
        }

        private void DrawChartBars(Rect plotRect, IReadOnlyList<ChartPoint> chartPoints, double maxValue)
        {
            float groupWidth = plotRect.width / chartPoints.Count;
            float barWidth = Mathf.Clamp(groupWidth * 0.58f, 8.0f, 44.0f);
            Color barColor = chartMetric == ChartMetric.AverageElapsedMs ? new Color(0.24f, 0.58f, 1.0f, 1.0f) : new Color(0.35f, 0.78f, 0.48f, 1.0f);
            Color failedColor = new Color(1.0f, 0.45f, 0.32f, 1.0f);

            for (int index = 0; index < chartPoints.Count; index++)
            {
                ChartPoint point = chartPoints[index];
                float normalized = Mathf.Clamp01((float)(point.Value / maxValue));
                float barHeight = Mathf.Max(1.0f, plotRect.height * normalized);
                float x = plotRect.x + groupWidth * index + (groupWidth - barWidth) * 0.5f;
                Rect barRect = new Rect(x, plotRect.yMax - barHeight, barWidth, barHeight);
                EditorGUI.DrawRect(barRect, point.FailedRuns > 0 ? failedColor : barColor);
            }
        }

        private void DrawChartLabels(Rect plotRect, IReadOnlyList<ChartPoint> chartPoints, double maxValue)
        {
            float groupWidth = plotRect.width / chartPoints.Count;
            GUIStyle centeredLabel = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.UpperCenter,
                clipping = TextClipping.Clip
            };

            for (int index = 0; index < chartPoints.Count; index++)
            {
                ChartPoint point = chartPoints[index];
                float x = plotRect.x + groupWidth * index;
                GUI.Label(new Rect(x, plotRect.yMax + 2.0f, groupWidth, 30.0f), point.Label, centeredLabel);

                float normalized = Mathf.Clamp01((float)(point.Value / maxValue));
                float valueY = plotRect.yMax - Mathf.Max(1.0f, plotRect.height * normalized) - 16.0f;
                GUI.Label(new Rect(x, valueY, groupWidth, 16.0f), FormatChartValue(point.Value), centeredLabel);
            }

            string metricLabel = GetChartMetricLabel();
            GUI.Label(new Rect(plotRect.x, plotRect.y - 20.0f, plotRect.width, 16.0f), metricLabel, EditorStyles.miniBoldLabel);
        }

        private List<ChartPoint> BuildChartPoints()
        {
            List<ChartAccumulator> accumulators = new List<ChartAccumulator>();

            for (int index = 0; index < results.Count; index++)
            {
                BenchmarkResultRow row = results[index];
                string label = BuildChartPointLabel(row);
                ChartAccumulator accumulator = FindOrCreateAccumulator(accumulators, label);
                if (!row.Success)
                {
                    accumulator.FailedRuns++;
                    continue;
                }

                accumulator.Total += GetChartMetricValue(row);
                accumulator.Count++;
            }

            List<ChartPoint> points = new List<ChartPoint>();
            for (int index = 0; index < accumulators.Count; index++)
            {
                ChartAccumulator accumulator = accumulators[index];
                if (accumulator.Count == 0)
                {
                    continue;
                }

                points.Add(new ChartPoint(accumulator.Label, accumulator.Total / accumulator.Count, accumulator.FailedRuns));
            }

            return points;
        }

        private static ChartAccumulator FindOrCreateAccumulator(List<ChartAccumulator> accumulators, string label)
        {
            for (int index = 0; index < accumulators.Count; index++)
            {
                if (accumulators[index].Label == label)
                {
                    return accumulators[index];
                }
            }

            ChartAccumulator accumulator = new ChartAccumulator(label);
            accumulators.Add(accumulator);
            return accumulator;
        }

        private static string BuildChartPointLabel(BenchmarkResultRow row)
        {
            if (row.RequestedRoomCount > 0 && row.TileCount == 0)
            {
                return row.RequestedRoomCount.ToString(CultureInfo.InvariantCulture) + " rooms";
            }

            string size = row.Width.ToString(CultureInfo.InvariantCulture) + "x" + row.Height.ToString(CultureInfo.InvariantCulture);
            if (row.RequestedRoomCount > 0)
            {
                return size + "\n" + row.RequestedRoomCount.ToString(CultureInfo.InvariantCulture) + " rooms";
            }

            return size;
        }

        private double GetChartMetricValue(BenchmarkResultRow row)
        {
            switch (chartMetric)
            {
                case ChartMetric.AverageGenerationMemoryMb:
                    return Math.Max(0.0d, BytesToMegabytes(row.MemoryAfterGenerationBytes - row.MemoryBeforeBytes));
                case ChartMetric.AverageRetainedMemoryMb:
                    return Math.Max(0.0d, BytesToMegabytes(row.RetainedAfterCleanupBytes));
                default:
                    return row.ElapsedMilliseconds;
            }
        }

        private string GetChartMetricLabel()
        {
            switch (chartMetric)
            {
                case ChartMetric.AverageGenerationMemoryMb:
                    return "Average generation memory delta (MB)";
                case ChartMetric.AverageRetainedMemoryMb:
                    return "Average retained memory after cleanup (MB)";
                default:
                    return "Average elapsed time (ms)";
            }
        }

        private string FormatChartValue(double value)
        {
            if (chartMetric == ChartMetric.AverageElapsedMs)
            {
                return value.ToString("0", CultureInfo.InvariantCulture) + "ms";
            }

            return FormatMemoryChartValue(value);
        }

        private static string FormatMemoryChartValue(double valueMb)
        {
            if (valueMb <= 0.0d)
            {
                return "0";
            }

            if (valueMb >= 10.0d)
            {
                return valueMb.ToString("0", CultureInfo.InvariantCulture) + "MB";
            }

            if (valueMb >= 1.0d)
            {
                return valueMb.ToString("0.0", CultureInfo.InvariantCulture) + "MB";
            }

            double valueKb = valueMb * 1024.0d;
            if (valueKb >= 10.0d)
            {
                return valueKb.ToString("0", CultureInfo.InvariantCulture) + "KB";
            }

            if (valueKb >= 1.0d)
            {
                return valueKb.ToString("0.0", CultureInfo.InvariantCulture) + "KB";
            }

            double valueBytes = valueKb * 1024.0d;
            return valueBytes.ToString("0", CultureInfo.InvariantCulture) + "B";
        }

        private static double BytesToMegabytes(long bytes)
        {
            return bytes / (1024.0d * 1024.0d);
        }

        private async Task RunBenchmarkAsync()
        {
            if (!CanRun(out string validationError))
            {
                status = validationError;
                Repaint();
                return;
            }

            SavePreferences();
            results.Clear();
            isRunning = true;
            cancelRequested = false;
            progress = 0.0f;
            status = "Starting benchmark...";
            activeCancellationSource = new CancellationTokenSource();
            Repaint();

            try
            {
                List<MapSize> mapSizes = benchmarkMode == BenchmarkMode.Dungeon ? new List<MapSize> { MapSize.None } : ParseMapSizes(mapSizesText);
                List<int> roomCounts = benchmarkMode == BenchmarkMode.Dungeon ? ParseRoomCounts(roomCountsText) : new List<int> { 0 };
                int totalRunsIncludingWarmups = mapSizes.Count * roomCounts.Count * (repeatCount + warmupCount);
                int completedRunsIncludingWarmups = 0;

                foreach (MapSize mapSize in mapSizes)
                {
                    foreach (int roomCount in roomCounts)
                    {
                        for (int warmupIndex = 0; warmupIndex < warmupCount; warmupIndex++)
                        {
                            ThrowIfCancelled();
                            status = BuildRunStatus("Warmup", mapSize, roomCount, warmupIndex + 1, warmupCount);
                            progress = totalRunsIncludingWarmups > 0 ? (float)completedRunsIncludingWarmups / totalRunsIncludingWarmups : 0.0f;
                            Repaint();
                            await RunSingleAsync(mapSize, roomCount, warmupIndex + 1, true, activeCancellationSource.Token);
                            completedRunsIncludingWarmups++;
                        }

                        for (int repeatIndex = 0; repeatIndex < repeatCount; repeatIndex++)
                        {
                            ThrowIfCancelled();
                            status = BuildRunStatus("Run", mapSize, roomCount, repeatIndex + 1, repeatCount);
                            progress = totalRunsIncludingWarmups > 0 ? (float)completedRunsIncludingWarmups / totalRunsIncludingWarmups : 0.0f;
                            Repaint();
                            BenchmarkResultRow row = await RunSingleAsync(mapSize, roomCount, repeatIndex + 1, false, activeCancellationSource.Token);
                            results.Add(row);
                            completedRunsIncludingWarmups++;
                        }
                    }
                }

                string csvPath = WriteCsv(results);
                status = "Benchmark complete. CSV exported to " + csvPath;
                progress = 1.0f;
            }
            catch (OperationCanceledException)
            {
                status = "Benchmark cancelled.";
            }
            catch (Exception exception)
            {
                status = "Benchmark failed: " + exception.Message;
                Debug.LogException(exception);
            }
            finally
            {
                activeCancellationSource?.Dispose();
                activeCancellationSource = null;
                activeDungeonGenerator = null;
                isRunning = false;
                cancelRequested = false;
                Repaint();
            }
        }

        private async Task<BenchmarkResultRow> RunSingleAsync(MapSize mapSize, int roomCount, int runIndex, bool warmup, CancellationToken cancellationToken)
        {
            if (benchmarkMode == BenchmarkMode.Dungeon)
            {
                return await RunDungeonBenchmarkAsync(GetSelectedDungeonGenerator(), mapSize, roomCount, runIndex, warmup, cancellationToken);
            }

            return await RunWorldBenchmarkAsync(GetSelectedWorldGenerator(), mapSize, runIndex, warmup, cancellationToken);
        }

        private async Task<BenchmarkResultRow> RunWorldBenchmarkAsync(TilemapWorldGenerator generator, MapSize mapSize, int runIndex, bool warmup, CancellationToken cancellationToken)
        {
            if (!IsValidSceneTarget(generator))
            {
                throw new InvalidOperationException("Selected TilemapWorldGenerator is no longer available in the active scene.");
            }

            GenGraph graph = generator.Graph;
            int oldGraphWidth = graph.WorldWidth;
            int oldGraphHeight = graph.WorldHeight;
            long oldGraphSeed = graph.DefaultSeed;
            SeedMode oldGraphSeedMode = graph.DefaultSeedMode;

            long rowSeed = useSeedOverride ? seed + runIndex - 1 : generator.LastUsedSeed;
            BenchmarkResultRow row = BenchmarkResultRow.Create(benchmarkMode.ToString(), mapSize, 0, runIndex, warmup, rowSeed);
            row.TargetName = generator.name;
            row.TargetPath = GetScenePath(generator.gameObject);
            row.AssetName = graph.name;
            row.AssetPath = AssetDatabase.GetAssetPath(graph);

            try
            {
                generator.Clear();
                graph.WorldWidth = mapSize.Width;
                graph.WorldHeight = mapSize.Height;
                if (useSeedOverride)
                {
                    graph.DefaultSeedMode = SeedMode.Stable;
                    graph.DefaultSeed = rowSeed;
                }

                CollectMemory(out long memoryBefore);
                Stopwatch stopwatch = Stopwatch.StartNew();
                GenerationCompletedArgs completedArgs = await GenerateWorldAndCaptureAsync(generator, cancellationToken);
                stopwatch.Stop();
                long memoryAfterGeneration = GC.GetTotalMemory(false);
                WorldSnapshot snapshot = completedArgs != null ? completedArgs.Snapshot : generator.LastSuccessfulSnapshot;
                bool success = completedArgs != null ? completedArgs.IsSuccess : snapshot != null;
                string error = completedArgs != null ? completedArgs.ErrorMessage : string.Empty;
                generator.Clear();
                CollectMemory(out long memoryAfterCleanup);

                row.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                row.Success = success;
                row.Status = success ? generator.GenerationStatus : BuildFailureStatus(generator.GenerationStatus, error);
                row.OutputWidth = snapshot != null ? snapshot.Width : 0;
                row.OutputHeight = snapshot != null ? snapshot.Height : 0;
                row.MemoryBeforeBytes = memoryBefore;
                row.MemoryAfterGenerationBytes = memoryAfterGeneration;
                row.MemoryAfterCleanupBytes = memoryAfterCleanup;
                row.LeakWarning = row.RetainedAfterCleanupBytes > GetLeakThresholdBytes();
            }
            finally
            {
                graph.WorldWidth = oldGraphWidth;
                graph.WorldHeight = oldGraphHeight;
                graph.DefaultSeed = oldGraphSeed;
                graph.DefaultSeedMode = oldGraphSeedMode;
                if (IsValidSceneTarget(generator))
                {
                    generator.Clear();
                }
            }

            return row;
        }

        private async Task<BenchmarkResultRow> RunDungeonBenchmarkAsync(DungeonGenerator generator, MapSize mapSize, int roomCount, int runIndex, bool warmup, CancellationToken cancellationToken)
        {
            if (!IsValidSceneTarget(generator))
            {
                throw new InvalidOperationException("Selected DungeonGenerator is no longer available in the active scene.");
            }

            OrganicGenerationSettings organicSettings = generator.organicSettings;
            DungeonGenerationMode oldGenerationMode = generator.generationMode;
            SeedMode oldSeedMode = generator.seedMode;
            long oldSeed = generator.stableSeed;
            bool oldUseRoomCountRange = organicSettings.useRoomCountRange;
            int oldTargetRoomCount = organicSettings.targetRoomCount;
            int oldMinRoomCount = organicSettings.minRoomCount;
            int oldMaxRoomCount = organicSettings.maxRoomCount;

            long rowSeed = useSeedOverride ? seed + runIndex - 1 : generator.LastUsedSeed;
            BenchmarkResultRow row = BenchmarkResultRow.Create(benchmarkMode.ToString(), mapSize, roomCount, runIndex, warmup, rowSeed);
            row.TargetName = generator.name;
            row.TargetPath = GetScenePath(generator.gameObject);
            row.AssetName = organicSettings.name;
            row.AssetPath = AssetDatabase.GetAssetPath(organicSettings);

            activeDungeonGenerator = generator;

            try
            {
                generator.Clear();
                generator.generationMode = DungeonGenerationMode.OrganicGrowth;
                organicSettings.useRoomCountRange = false;
                organicSettings.targetRoomCount = roomCount;
                organicSettings.minRoomCount = roomCount;
                organicSettings.maxRoomCount = roomCount;
                if (useSeedOverride)
                {
                    generator.seedMode = SeedMode.Stable;
                    generator.stableSeed = rowSeed;
                }

                CollectMemory(out long memoryBefore);
                Stopwatch stopwatch = Stopwatch.StartNew();
                await generator.GenerateAndRenderAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                stopwatch.Stop();
                long memoryAfterGeneration = GC.GetTotalMemory(false);
                int generatedInstanceCount = generator.transform.childCount;
                string generationStatus = generator.GenerationStatus ?? string.Empty;
                bool success = generationStatus.StartsWith("Generated", StringComparison.OrdinalIgnoreCase);
                generator.Clear();
                CollectMemory(out long memoryAfterCleanup);

                row.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                row.Success = success;
                row.Status = generationStatus;
                row.GeneratedInstanceCount = generatedInstanceCount;
                row.MemoryBeforeBytes = memoryBefore;
                row.MemoryAfterGenerationBytes = memoryAfterGeneration;
                row.MemoryAfterCleanupBytes = memoryAfterCleanup;
                row.LeakWarning = row.RetainedAfterCleanupBytes > GetLeakThresholdBytes();
            }
            finally
            {
                generator.generationMode = oldGenerationMode;
                generator.seedMode = oldSeedMode;
                generator.stableSeed = oldSeed;
                organicSettings.useRoomCountRange = oldUseRoomCountRange;
                organicSettings.targetRoomCount = oldTargetRoomCount;
                organicSettings.minRoomCount = oldMinRoomCount;
                organicSettings.maxRoomCount = oldMaxRoomCount;
                if (IsValidSceneTarget(generator))
                {
                    generator.Clear();
                }
                activeDungeonGenerator = null;
            }

            return row;
        }

        private static async Task<GenerationCompletedArgs> GenerateWorldAndCaptureAsync(TilemapWorldGenerator generator, CancellationToken cancellationToken)
        {
            GenerationCompletedArgs completedArgs = null;
            Action<GenerationCompletedArgs> handler = args => completedArgs = args;
            generator.OnGenerationCompleted += handler;
            try
            {
                await generator.GenerateAsync(cancellationToken);
            }
            finally
            {
                generator.OnGenerationCompleted -= handler;
            }

            return completedArgs;
        }

        private void RefreshSceneTargets()
        {
            worldGenerators.Clear();
            dungeonGenerators.Clear();
            FillSceneObjects(worldGenerators);
            FillSceneObjects(dungeonGenerators);
            selectedWorldGeneratorIndex = Mathf.Clamp(selectedWorldGeneratorIndex, 0, Mathf.Max(0, worldGenerators.Count - 1));
            selectedDungeonGeneratorIndex = Mathf.Clamp(selectedDungeonGeneratorIndex, 0, Mathf.Max(0, dungeonGenerators.Count - 1));
            Repaint();
        }

        private void HandleSceneTargetsChanged()
        {
            if (isRunning)
            {
                return;
            }

            RefreshSceneTargets();
        }

        private void HandleActiveSceneChanged(Scene oldScene, Scene newScene)
        {
            HandleSceneStateChanged();
        }

        private void HandleSceneOpened(Scene scene, OpenSceneMode mode)
        {
            HandleSceneStateChanged();
        }

        private void HandleSceneClosed(Scene scene)
        {
            HandleSceneStateChanged();
        }

        private void HandleSceneStateChanged()
        {
            if (isRunning)
            {
                RequestCancel();
                return;
            }

            RefreshSceneTargets();
        }

        private void PruneSceneTargets()
        {
            PruneSceneObjects(worldGenerators);
            PruneSceneObjects(dungeonGenerators);
            selectedWorldGeneratorIndex = Mathf.Clamp(selectedWorldGeneratorIndex, 0, Mathf.Max(0, worldGenerators.Count - 1));
            selectedDungeonGeneratorIndex = Mathf.Clamp(selectedDungeonGeneratorIndex, 0, Mathf.Max(0, dungeonGenerators.Count - 1));
        }

        private static void FillSceneObjects<T>(List<T> targets) where T : Component
        {
            T[] objects = Resources.FindObjectsOfTypeAll<T>();
            Array.Sort(objects, CompareSceneObjects);
            foreach (T item in objects)
            {
                if (!IsValidSceneTarget(item))
                {
                    continue;
                }

                targets.Add(item);
            }
        }

        private static void PruneSceneObjects<T>(List<T> targets) where T : Component
        {
            for (int index = targets.Count - 1; index >= 0; index--)
            {
                if (!IsValidSceneTarget(targets[index]))
                {
                    targets.RemoveAt(index);
                }
            }
        }

        private static int CompareSceneObjects<T>(T left, T right) where T : Component
        {
            return string.CompareOrdinal(GetSafeScenePath(left), GetSafeScenePath(right));
        }

        private static bool IsValidSceneTarget(Component target)
        {
            if (target == null)
            {
                return false;
            }

            GameObject gameObject = null;
            try
            {
                gameObject = target.gameObject;
            }
            catch (MissingReferenceException)
            {
                return false;
            }

            return gameObject != null && gameObject.scene.IsValid() && gameObject.scene.isLoaded;
        }

        private int DrawObjectPopup<T>(string label, List<T> targets, int selectedIndex) where T : Component
        {
            PruneSceneObjects(targets);
            if (targets.Count == 0)
            {
                EditorGUILayout.Popup(label, 0, new[] { "None" });
                return 0;
            }

            string[] names = new string[targets.Count];
            for (int index = 0; index < targets.Count; index++)
            {
                names[index] = GetSafeScenePath(targets[index]);
            }

            return EditorGUILayout.Popup(label, Mathf.Clamp(selectedIndex, 0, targets.Count - 1), names);
        }

        private TilemapWorldGenerator GetSelectedWorldGenerator()
        {
            PruneSceneObjects(worldGenerators);
            if (worldGenerators.Count == 0)
            {
                return null;
            }

            return worldGenerators[Mathf.Clamp(selectedWorldGeneratorIndex, 0, worldGenerators.Count - 1)];
        }

        private DungeonGenerator GetSelectedDungeonGenerator()
        {
            PruneSceneObjects(dungeonGenerators);
            if (dungeonGenerators.Count == 0)
            {
                return null;
            }

            return dungeonGenerators[Mathf.Clamp(selectedDungeonGeneratorIndex, 0, dungeonGenerators.Count - 1)];
        }

        private bool CanRun(out string validationError)
        {
            validationError = null;

            if (benchmarkMode == BenchmarkMode.Dungeon)
            {
                DungeonGenerator dungeonGenerator = GetSelectedDungeonGenerator();
                if (dungeonGenerator == null)
                {
                    validationError = "Select a scene DungeonGenerator.";
                    return false;
                }

                if (dungeonGenerator.generationMode != DungeonGenerationMode.OrganicGrowth || dungeonGenerator.organicSettings == null)
                {
                    validationError = "Dungeon mode requires Organic Growth with an assigned profile.";
                    return false;
                }

                if (ParseRoomCounts(roomCountsText).Count == 0)
                {
                    validationError = "Enter at least one valid room count.";
                    return false;
                }

                return true;
            }

            if (ParseMapSizes(mapSizesText).Count == 0)
            {
                validationError = "Enter at least one valid map size, e.g. 128x128.";
                return false;
            }

            TilemapWorldGenerator worldGenerator = GetSelectedWorldGenerator();
            if (worldGenerator == null)
            {
                validationError = "Select a scene TilemapWorldGenerator.";
                return false;
            }

            if (worldGenerator.Graph == null)
            {
                validationError = "Selected TilemapWorldGenerator has no graph assigned.";
                return false;
            }

            return true;
        }

        private static List<MapSize> ParseMapSizes(string value)
        {
            List<MapSize> sizes = new List<MapSize>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return sizes;
            }

            string[] parts = value.Split(',');
            foreach (string rawPart in parts)
            {
                string part = rawPart.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(part))
                {
                    continue;
                }

                string[] dimensions = part.Split('x');
                if (dimensions.Length == 1 && int.TryParse(dimensions[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int squareSize))
                {
                    if (squareSize > 0)
                    {
                        sizes.Add(new MapSize(squareSize, squareSize));
                    }

                    continue;
                }

                if (dimensions.Length != 2)
                {
                    continue;
                }

                if (int.TryParse(dimensions[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int width) &&
                    int.TryParse(dimensions[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int height) &&
                    width > 0 && height > 0)
                {
                    sizes.Add(new MapSize(width, height));
                }
            }

            return sizes;
        }

        private static List<int> ParseRoomCounts(string value)
        {
            List<int> roomCounts = new List<int>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return roomCounts;
            }

            string[] parts = value.Split(',');
            foreach (string rawPart in parts)
            {
                if (int.TryParse(rawPart.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int roomCount) && roomCount > 0)
                {
                    roomCounts.Add(roomCount);
                }
            }

            return roomCounts;
        }

        private string WriteCsv(IReadOnlyList<BenchmarkResultRow> rows)
        {
            string folder = ResolveOutputFolder();
            Directory.CreateDirectory(folder);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string fileName = "dynamic_dungeon_" + benchmarkMode.ToString().ToLowerInvariant() + "_benchmark_" + timestamp + CsvExtension;
            string path = Path.Combine(folder, fileName);
            File.WriteAllText(path, BenchmarkResultRow.ToCsv(rows), Encoding.UTF8);
            AssetDatabase.Refresh();
            return path;
        }

        private string ResolveOutputFolder()
        {
            return string.IsNullOrWhiteSpace(outputFolder) ? GetDefaultOutputFolder() : outputFolder;
        }

        private static string GetDefaultOutputFolder()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "BenchmarkResults"));
        }

        private void SavePreferences()
        {
            EditorPrefs.SetString(MapSizesPreferenceKey, mapSizesText ?? string.Empty);
            EditorPrefs.SetString(RoomCountsPreferenceKey, roomCountsText ?? string.Empty);
            EditorPrefs.SetInt(RepeatCountPreferenceKey, repeatCount);
            EditorPrefs.SetInt(WarmupCountPreferenceKey, warmupCount);
            EditorPrefs.SetFloat(LeakThresholdPreferenceKey, leakThresholdMb);
            EditorPrefs.SetString(SeedPreferenceKey, seed.ToString(CultureInfo.InvariantCulture));
            EditorPrefs.SetBool(UseSeedOverridePreferenceKey, useSeedOverride);
            EditorPrefs.SetString(OutputFolderPreferenceKey, outputFolder ?? string.Empty);
        }

        private void RequestCancel()
        {
            if (cancelRequested)
            {
                return;
            }

            cancelRequested = true;
            activeCancellationSource?.Cancel();
            activeDungeonGenerator?.CancelGeneration();
            status = "Cancelling benchmark...";
            Repaint();
        }

        private void ThrowIfCancelled()
        {
            if (cancelRequested || (activeCancellationSource != null && activeCancellationSource.IsCancellationRequested))
            {
                throw new OperationCanceledException();
            }
        }

        private static void CollectMemory(out long memory)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            EditorUtility.UnloadUnusedAssetsImmediate();
            GC.Collect();
            memory = GC.GetTotalMemory(true);
        }

        private long GetLeakThresholdBytes()
        {
            return (long)(Mathf.Max(0.0f, leakThresholdMb) * 1024.0f * 1024.0f);
        }

        private static string BuildFailureStatus(string generationStatus, string error)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                return error;
            }

            return string.IsNullOrWhiteSpace(generationStatus) ? "Generation failed." : generationStatus;
        }

        private static string BuildRunStatus(string prefix, MapSize mapSize, int roomCount, int current, int total)
        {
            if (roomCount > 0 && mapSize.TileCount == 0)
            {
                return prefix + " " + current.ToString(CultureInfo.InvariantCulture) + "/" + total.ToString(CultureInfo.InvariantCulture) + ", rooms " + roomCount.ToString(CultureInfo.InvariantCulture);
            }

            string roomText = roomCount > 0 ? ", rooms " + roomCount.ToString(CultureInfo.InvariantCulture) : string.Empty;
            return prefix + " " + current.ToString(CultureInfo.InvariantCulture) + "/" + total.ToString(CultureInfo.InvariantCulture) + " @ " + mapSize.ToLabel() + roomText;
        }

        private string GetModeHelpText()
        {
            switch (benchmarkMode)
            {
                case BenchmarkMode.Dungeon:
                    return "Runs the selected scene DungeonGenerator through full organic generation and room rendering at each requested organic room count.";
                case BenchmarkMode.Combined:
                    return "Runs the selected scene TilemapWorldGenerator end-to-end. Use this for a combined graph such as terrain plus dungeon or prefab placement.";
                default:
                    return "Runs the selected scene TilemapWorldGenerator end-to-end at each map size.";
            }
        }

        private static string GetScenePath(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(gameObject.name);
            Transform current = gameObject.transform.parent;
            while (current != null)
            {
                builder.Insert(0, current.name + "/");
                current = current.parent;
            }

            return gameObject.scene.name + ":" + builder;
        }

        private static string GetSafeScenePath(Component component)
        {
            if (!IsValidSceneTarget(component))
            {
                return "Missing";
            }

            try
            {
                return GetScenePath(component.gameObject);
            }
            catch (MissingReferenceException)
            {
                return "Missing";
            }
        }

        private readonly struct MapSize
        {
            public static readonly MapSize None = new MapSize(0, 0);

            public readonly int Width;
            public readonly int Height;

            public MapSize(int width, int height)
            {
                Width = width;
                Height = height;
            }

            public long TileCount => (long)Width * Height;

            public string ToLabel()
            {
                return Width.ToString(CultureInfo.InvariantCulture) + "x" + Height.ToString(CultureInfo.InvariantCulture);
            }
        }

        private sealed class ChartAccumulator
        {
            public readonly string Label;
            public double Total;
            public int Count;
            public int FailedRuns;

            public ChartAccumulator(string label)
            {
                Label = label;
            }
        }

        private readonly struct ChartPoint
        {
            public readonly string Label;
            public readonly double Value;
            public readonly int FailedRuns;

            public ChartPoint(string label, double value, int failedRuns)
            {
                Label = label;
                Value = value;
                FailedRuns = failedRuns;
            }
        }

        private sealed class BenchmarkResultRow
        {
            public string Timestamp;
            public string Mode;
            public string TargetName;
            public string TargetPath;
            public string AssetName;
            public string AssetPath;
            public int Width;
            public int Height;
            public long TileCount;
            public int RequestedRoomCount;
            public int RunIndex;
            public bool Warmup;
            public long Seed;
            public long ElapsedMilliseconds;
            public bool Success;
            public string Status;
            public int OutputWidth;
            public int OutputHeight;
            public int GeneratedInstanceCount;
            public long MemoryBeforeBytes;
            public long MemoryAfterGenerationBytes;
            public long MemoryAfterCleanupBytes;
            public bool LeakWarning;

            public long RetainedAfterCleanupBytes => MemoryAfterCleanupBytes - MemoryBeforeBytes;

            public static BenchmarkResultRow Create(string mode, MapSize mapSize, int requestedRoomCount, int runIndex, bool warmup, long seed)
            {
                return new BenchmarkResultRow
                {
                    Timestamp = DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
                    Mode = mode,
                    Width = mapSize.Width,
                    Height = mapSize.Height,
                    TileCount = mapSize.TileCount,
                    RequestedRoomCount = requestedRoomCount,
                    RunIndex = runIndex,
                    Warmup = warmup,
                    Seed = seed
                };
            }

            public string ToSummary()
            {
                string successText = Success ? "OK" : "FAIL";
                string leakText = LeakWarning ? " leak-warning" : string.Empty;
                string sizeText = RequestedRoomCount > 0 && TileCount == 0
                    ? RequestedRoomCount.ToString(CultureInfo.InvariantCulture) + " rooms"
                    : Width.ToString(CultureInfo.InvariantCulture) + "x" + Height.ToString(CultureInfo.InvariantCulture);
                return Mode + " " + sizeText + " run " + RunIndex.ToString(CultureInfo.InvariantCulture) + ": " + ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + "ms " + successText + leakText;
            }

            public static string ToCsv(IReadOnlyList<BenchmarkResultRow> rows)
            {
                StringBuilder builder = new StringBuilder();
                builder.AppendLine("timestamp,mode,target_name,target_path,asset_name,asset_path,width,height,tile_count,requested_room_count,run_index,warmup,seed,elapsed_ms,success,status,output_width,output_height,generated_instance_count,memory_before_bytes,memory_after_generation_bytes,memory_after_cleanup_bytes,retained_after_cleanup_bytes,leak_warning");
                foreach (BenchmarkResultRow row in rows)
                {
                    AppendCsvRow(builder, row);
                }

                return builder.ToString();
            }

            private static void AppendCsvRow(StringBuilder builder, BenchmarkResultRow row)
            {
                Append(builder, row.Timestamp);
                Append(builder, row.Mode);
                Append(builder, row.TargetName);
                Append(builder, row.TargetPath);
                Append(builder, row.AssetName);
                Append(builder, row.AssetPath);
                Append(builder, row.Width);
                Append(builder, row.Height);
                Append(builder, row.TileCount);
                Append(builder, row.RequestedRoomCount);
                Append(builder, row.RunIndex);
                Append(builder, row.Warmup);
                Append(builder, row.Seed);
                Append(builder, row.ElapsedMilliseconds);
                Append(builder, row.Success);
                Append(builder, row.Status);
                Append(builder, row.OutputWidth);
                Append(builder, row.OutputHeight);
                Append(builder, row.GeneratedInstanceCount);
                Append(builder, row.MemoryBeforeBytes);
                Append(builder, row.MemoryAfterGenerationBytes);
                Append(builder, row.MemoryAfterCleanupBytes);
                Append(builder, row.RetainedAfterCleanupBytes);
                Append(builder, row.LeakWarning, true);
                builder.AppendLine();
            }

            private static void Append(StringBuilder builder, object value, bool last = false)
            {
                string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                bool quote = text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
                if (quote)
                {
                    builder.Append('"');
                    builder.Append(text.Replace("\"", "\"\""));
                    builder.Append('"');
                }
                else
                {
                    builder.Append(text);
                }

                if (!last)
                {
                    builder.Append(',');
                }
            }
        }
    }
}
