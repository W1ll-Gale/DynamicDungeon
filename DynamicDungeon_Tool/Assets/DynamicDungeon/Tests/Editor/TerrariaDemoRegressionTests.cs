using System.Threading;
using DynamicDungeon.Runtime.Component;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Placement;
using NUnit.Framework;
using UnityEditor;

namespace DynamicDungeon.Tests.Editor
{
    public sealed class TerrariaDemoRegressionTests
    {
        private const string DemoGraphPath = "Assets/DynamicDungeon/Examples/TerrariaDemo/Graphs/TerrariaDemoGraph.asset";
        private const string BakedSnapshotPath = "Assets/DynamicDungeon/Examples/TerrariaDemo/TerrariaDemoScene_BakedSnapshot.asset";
        private const float FloatTolerance = 0.0001f;

        [Test]
        public void TerrariaDemoGraphMatchesBakedSnapshot()
        {
            GenGraph graph = AssetDatabase.LoadAssetAtPath<GenGraph>(DemoGraphPath);
            BakedWorldSnapshot bakedSnapshot = AssetDatabase.LoadAssetAtPath<BakedWorldSnapshot>(BakedSnapshotPath);

            Assert.That(graph, Is.Not.Null);
            Assert.That(bakedSnapshot, Is.Not.Null);
            Assert.That(bakedSnapshot.Snapshot, Is.Not.Null);

            GraphCompileResult compileResult = GraphCompiler.Compile(graph);
            Assert.That(compileResult.IsSuccess, Is.True, BuildDiagnosticMessage(compileResult));

            Executor executor = new Executor();
            ExecutionResult executionResult = executor.Execute(compileResult.Plan, CancellationToken.None);

            Assert.That(executionResult.IsSuccess, Is.True, executionResult.ErrorMessage);
            Assert.That(executionResult.Snapshot, Is.Not.Null);

            AssertSnapshotsEqual(bakedSnapshot.Snapshot, executionResult.Snapshot);
        }

        private static string BuildDiagnosticMessage(GraphCompileResult compileResult)
        {
            if (compileResult == null || compileResult.Diagnostics == null)
            {
                return string.Empty;
            }

            string message = string.Empty;
            for (int diagnosticIndex = 0; diagnosticIndex < compileResult.Diagnostics.Count; diagnosticIndex++)
            {
                GraphDiagnostic diagnostic = compileResult.Diagnostics[diagnosticIndex];
                message += diagnostic.Severity + ": " + diagnostic.Message + "\n";
            }

            return message;
        }

        private static void AssertSnapshotsEqual(WorldSnapshot expected, WorldSnapshot actual)
        {
            Assert.That(actual.Width, Is.EqualTo(expected.Width));
            Assert.That(actual.Height, Is.EqualTo(expected.Height));
            Assert.That(actual.Seed, Is.EqualTo(expected.Seed));

            AssertFloatChannelsEqual(expected.FloatChannels, actual.FloatChannels);
            AssertIntChannelsEqual(expected.IntChannels, actual.IntChannels);
            AssertBoolMaskChannelsEqual(expected.BoolMaskChannels, actual.BoolMaskChannels);
            AssertPointListChannelsEqual(expected.PointListChannels, actual.PointListChannels);
            AssertPrefabPlacementChannelsEqual(expected.PrefabPlacementChannels, actual.PrefabPlacementChannels);
        }

        private static void AssertFloatChannelsEqual(WorldSnapshot.FloatChannelSnapshot[] expected, WorldSnapshot.FloatChannelSnapshot[] actual)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length), "Float channel count changed.");
            for (int channelIndex = 0; channelIndex < expected.Length; channelIndex++)
            {
                Assert.That(actual[channelIndex].Name, Is.EqualTo(expected[channelIndex].Name));
                Assert.That(actual[channelIndex].Data.Length, Is.EqualTo(expected[channelIndex].Data.Length), expected[channelIndex].Name);

                for (int dataIndex = 0; dataIndex < expected[channelIndex].Data.Length; dataIndex++)
                {
                    Assert.That(actual[channelIndex].Data[dataIndex], Is.EqualTo(expected[channelIndex].Data[dataIndex]).Within(FloatTolerance), expected[channelIndex].Name + "[" + dataIndex + "]");
                }
            }
        }

        private static void AssertIntChannelsEqual(WorldSnapshot.IntChannelSnapshot[] expected, WorldSnapshot.IntChannelSnapshot[] actual)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length), "Int channel count changed.");
            for (int channelIndex = 0; channelIndex < expected.Length; channelIndex++)
            {
                Assert.That(actual[channelIndex].Name, Is.EqualTo(expected[channelIndex].Name));
                Assert.That(actual[channelIndex].Data, Is.EqualTo(expected[channelIndex].Data), expected[channelIndex].Name);
            }
        }

        private static void AssertBoolMaskChannelsEqual(WorldSnapshot.BoolMaskChannelSnapshot[] expected, WorldSnapshot.BoolMaskChannelSnapshot[] actual)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length), "Bool mask channel count changed.");
            for (int channelIndex = 0; channelIndex < expected.Length; channelIndex++)
            {
                Assert.That(actual[channelIndex].Name, Is.EqualTo(expected[channelIndex].Name));
                Assert.That(actual[channelIndex].Data, Is.EqualTo(expected[channelIndex].Data), expected[channelIndex].Name);
            }
        }

        private static void AssertPointListChannelsEqual(WorldSnapshot.PointListChannelSnapshot[] expected, WorldSnapshot.PointListChannelSnapshot[] actual)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length), "Point list channel count changed.");
            for (int channelIndex = 0; channelIndex < expected.Length; channelIndex++)
            {
                Assert.That(actual[channelIndex].Name, Is.EqualTo(expected[channelIndex].Name));
                Assert.That(actual[channelIndex].Data.Length, Is.EqualTo(expected[channelIndex].Data.Length), expected[channelIndex].Name);

                for (int dataIndex = 0; dataIndex < expected[channelIndex].Data.Length; dataIndex++)
                {
                    Assert.That(actual[channelIndex].Data[dataIndex], Is.EqualTo(expected[channelIndex].Data[dataIndex]), expected[channelIndex].Name + "[" + dataIndex + "]");
                }
            }
        }

        private static void AssertPrefabPlacementChannelsEqual(WorldSnapshot.PrefabPlacementListChannelSnapshot[] expected, WorldSnapshot.PrefabPlacementListChannelSnapshot[] actual)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length), "Prefab placement channel count changed.");
            for (int channelIndex = 0; channelIndex < expected.Length; channelIndex++)
            {
                Assert.That(actual[channelIndex].Name, Is.EqualTo(expected[channelIndex].Name));
                Assert.That(actual[channelIndex].Data.Length, Is.EqualTo(expected[channelIndex].Data.Length), expected[channelIndex].Name);

                for (int dataIndex = 0; dataIndex < expected[channelIndex].Data.Length; dataIndex++)
                {
                    AssertPrefabPlacementEqual(expected[channelIndex].Data[dataIndex], actual[channelIndex].Data[dataIndex], expected[channelIndex].Name + "[" + dataIndex + "]");
                }
            }
        }

        private static void AssertPrefabPlacementEqual(PrefabPlacementRecord expected, PrefabPlacementRecord actual, string context)
        {
            Assert.That(actual.TemplateIndex, Is.EqualTo(expected.TemplateIndex), context);
            Assert.That(actual.OriginX, Is.EqualTo(expected.OriginX), context);
            Assert.That(actual.OriginY, Is.EqualTo(expected.OriginY), context);
            Assert.That(actual.RotationQuarterTurns, Is.EqualTo(expected.RotationQuarterTurns), context);
            Assert.That(actual.Flags, Is.EqualTo(expected.Flags), context);
        }
    }
}
