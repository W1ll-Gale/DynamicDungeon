using System;
using System.Collections.Generic;
using System.Reflection;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using NUnit.Framework;
using UnityEngine;

namespace DynamicDungeon.Tests.Runtime
{
    public sealed class GraphMigrationTests
    {
        private const string CurrentSchemaFieldName = "_currentSchemaVersion";
        private int _originalCurrentSchemaVersion;

        [SetUp]
        public void SetUp()
        {
            _originalCurrentSchemaVersion = GetCurrentSchemaVersionOverride();
            SetCurrentSchemaVersionOverride(GraphSchemaVersion.Current);
        }

        [TearDown]
        public void TearDown()
        {
            SetCurrentSchemaVersionOverride(_originalCurrentSchemaVersion);
        }

        [Test]
        public void GraphAtVersionZeroMigratesToVersionOneCorrectly()
        {
            SetCurrentSchemaVersionOverride(1);

            GenGraph graph = CreateGraphWithVersion(0);
            try
            {
                List<IGraphMigration> migrations = new List<IGraphMigration>
                {
                    new RenameGraphMigration(0, "Migrated Name")
                };

                MigrationResult result = GraphMigrationRunner.RunMigrations(graph, migrations);

                Assert.That(result.Success, Is.True);
                Assert.That(result.FromVersion, Is.EqualTo(0));
                Assert.That(result.ToVersion, Is.EqualTo(1));
                Assert.That(result.ErrorMessage, Is.Null);
                Assert.That(graph.SchemaVersion, Is.EqualTo(1));
                Assert.That(graph.name, Is.EqualTo("Migrated Name"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void TwoMigrationChainAppliesInSequenceAndProducesVersionTwo()
        {
            SetCurrentSchemaVersionOverride(2);

            GenGraph graph = CreateGraphWithVersion(0);
            try
            {
                List<IGraphMigration> migrations = new List<IGraphMigration>
                {
                    new AppendNodeMigration(1, "second-node"),
                    new RenameGraphMigration(0, "After First Migration")
                };

                MigrationResult result = GraphMigrationRunner.RunMigrations(graph, migrations);

                Assert.That(result.Success, Is.True);
                Assert.That(result.FromVersion, Is.EqualTo(0));
                Assert.That(result.ToVersion, Is.EqualTo(2));
                Assert.That(result.ErrorMessage, Is.Null);
                Assert.That(graph.SchemaVersion, Is.EqualTo(2));
                Assert.That(graph.name, Is.EqualTo("After First Migration"));
                Assert.That(graph.Nodes.Count, Is.EqualTo(1));
                Assert.That(graph.Nodes[0].NodeId, Is.EqualTo("second-node"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void RequiresMigrationReturnsFalseForGraphAtCurrentVersion()
        {
            GenGraph graph = CreateGraphWithVersion(GraphSchemaVersion.Current);
            try
            {
                bool requiresMigration = GraphMigrationRunner.RequiresMigration(graph);

                Assert.That(requiresMigration, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void IsNewerThanToolReturnsTrueWhenGraphVersionExceedsCurrent()
        {
            GenGraph graph = CreateGraphWithVersion(GraphSchemaVersion.Current + 1);
            try
            {
                bool isNewerThanTool = GraphMigrationRunner.IsNewerThanTool(graph);

                Assert.That(isNewerThanTool, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void RunMigrationsIsNoOpAndSucceedsWhenGraphIsAlreadyCurrent()
        {
            GenGraph graph = CreateGraphWithVersion(GraphSchemaVersion.Current);
            try
            {
                List<IGraphMigration> migrations = new List<IGraphMigration>
                {
                    new RenameGraphMigration(0, "Should Not Run")
                };

                MigrationResult result = GraphMigrationRunner.RunMigrations(graph, migrations);

                Assert.That(result.Success, Is.True);
                Assert.That(result.FromVersion, Is.EqualTo(GraphSchemaVersion.Current));
                Assert.That(result.ToVersion, Is.EqualTo(GraphSchemaVersion.Current));
                Assert.That(result.ErrorMessage, Is.Null);
                Assert.That(graph.SchemaVersion, Is.EqualTo(GraphSchemaVersion.Current));
                Assert.That(graph.name, Is.EqualTo("MigrationTestGraph"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void VersionTwoGraphMigrationRenamesDuplicateOutputPortsAndRewritesConnections()
        {
            GenGraph graph = CreateGraphWithVersion(2);
            try
            {
                GenNodeData firstNode = new GenNodeData("first-node", "TestType", "First", Vector2.zero);
                firstNode.Ports.Add(new GenPortData("Output", PortDirection.Output, ChannelType.Float));
                graph.Nodes.Add(firstNode);

                GenNodeData secondNode = new GenNodeData("second-node", "TestType", "Second", Vector2.zero);
                secondNode.Ports.Add(new GenPortData("Output", PortDirection.Output, ChannelType.Float));
                graph.Nodes.Add(secondNode);

                GenNodeData consumerNode = new GenNodeData("consumer-node", "TestType", "Consumer", Vector2.zero);
                consumerNode.Ports.Add(new GenPortData("Input", PortDirection.Input, ChannelType.Float));
                graph.Nodes.Add(consumerNode);

                graph.Connections.Add(new GenConnectionData("second-node", "Output", "consumer-node", "Input"));

                MigrationResult result = GraphMigrationRunner.RunMigrations(graph, DefaultGraphMigrations.All);

                Assert.That(result.Success, Is.True);
                Assert.That(result.ToVersion, Is.EqualTo(GraphSchemaVersion.Current));
                Assert.That(firstNode.Ports[0].PortName, Is.EqualTo("Output"));
                Assert.That(firstNode.Ports[0].DisplayName, Is.EqualTo("Output"));

                string expectedRenamedPort = GraphPortNameUtility.CreateGeneratedOutputPortName("second-node", "Output");
                Assert.That(secondNode.Ports[0].PortName, Is.EqualTo(expectedRenamedPort));
                Assert.That(secondNode.Ports[0].DisplayName, Is.EqualTo("Output"));
                Assert.That(graph.Connections[0].FromPortName, Is.EqualTo(expectedRenamedPort));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void VersionTwoGraphMigrationLeavesUniqueOutputPortsUnchangedApartFromDisplayNameBackfill()
        {
            GenGraph graph = CreateGraphWithVersion(2);
            try
            {
                GenNodeData firstNode = new GenNodeData("first-node", "TestType", "First", Vector2.zero);
                firstNode.Ports.Add(new GenPortData("HeightMap", PortDirection.Output, ChannelType.Float));
                graph.Nodes.Add(firstNode);

                MigrationResult result = GraphMigrationRunner.RunMigrations(graph, DefaultGraphMigrations.All);

                Assert.That(result.Success, Is.True);
                Assert.That(result.ToVersion, Is.EqualTo(GraphSchemaVersion.Current));
                Assert.That(firstNode.Ports[0].PortName, Is.EqualTo("HeightMap"));
                Assert.That(firstNode.Ports[0].DisplayName, Is.EqualTo("HeightMap"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void VersionThreeGraphMigrationRewritesLegacyBlackboardNodesIntoExposedProperties()
        {
            GenGraph graph = CreateGraphWithVersion(3);
            try
            {
                GraphOutputUtility.EnsureSingleOutputNode(graph, false);

                GenNodeData writerNode = new GenNodeData(
                    "writer-node",
                    "DynamicDungeon.Runtime.Core.BlackboardWriterNode",
                    "Blackboard Writer",
                    Vector2.zero);
                writerNode.Parameters.Add(new SerializedParameter("key", "SurfaceHeight"));
                writerNode.Parameters.Add(new SerializedParameter("value", "2.5"));
                graph.Nodes.Add(writerNode);

                GenNodeData readerNode = new GenNodeData(
                    "reader-node",
                    "DynamicDungeon.Runtime.Core.BlackboardReaderNode",
                    "Blackboard Reader",
                    Vector2.zero);
                readerNode.Parameters.Add(new SerializedParameter("key", "SurfaceHeight"));
                readerNode.Ports.Add(new GenPortData("LegacyOutput", PortDirection.Output, ChannelType.Float));
                graph.Nodes.Add(readerNode);

                MigrationResult result = GraphMigrationRunner.RunMigrations(graph, DefaultGraphMigrations.All);

                Assert.That(result.Success, Is.True);
                Assert.That(result.ToVersion, Is.EqualTo(GraphSchemaVersion.Current));
                Assert.That(graph.GetNode("writer-node"), Is.Null);
                Assert.That(graph.GetNode("reader-node"), Is.Not.Null);
                Assert.That(graph.GetNode("reader-node").NodeTypeName, Is.EqualTo(ExposedPropertyNodeUtility.NodeTypeName));
                Assert.That(graph.GetNode("reader-node").Ports[0].PortName, Is.EqualTo(ExposedPropertyNodeUtility.OutputPortName));

                ExposedProperty property = graph.GetExposedPropertyByName("SurfaceHeight");
                Assert.That(property, Is.Not.Null);
                Assert.That(property.PropertyId, Is.Not.Empty);
                Assert.That(property.DefaultValue, Is.EqualTo("2.5"));

                GraphCompileResult compileResult = GraphCompiler.Compile(graph);
                Assert.That(compileResult.IsSuccess, Is.True);
                Assert.That(compileResult.Plan, Is.Not.Null);
                compileResult.Plan.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        private static GenGraph CreateGraphWithVersion(int schemaVersion)
        {
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();
            graph.name = "MigrationTestGraph";
            graph.SchemaVersion = schemaVersion;
            return graph;
        }

        private static int GetCurrentSchemaVersionOverride()
        {
            FieldInfo currentSchemaField = GetCurrentSchemaField();
            return (int)currentSchemaField.GetValue(null);
        }

        private static FieldInfo GetCurrentSchemaField()
        {
            FieldInfo currentSchemaField = typeof(GraphMigrationRunner).GetField(CurrentSchemaFieldName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(currentSchemaField, Is.Not.Null);
            return currentSchemaField;
        }

        private static void SetCurrentSchemaVersionOverride(int schemaVersion)
        {
            FieldInfo currentSchemaField = GetCurrentSchemaField();
            currentSchemaField.SetValue(null, schemaVersion);
        }

        private sealed class AppendNodeMigration : IGraphMigration
        {
            private readonly int _fromVersion;
            private readonly string _nodeId;

            public int FromVersion
            {
                get
                {
                    return _fromVersion;
                }
            }

            public AppendNodeMigration(int fromVersion, string nodeId)
            {
                _fromVersion = fromVersion;
                _nodeId = nodeId;
            }

            public void Migrate(GenGraph graph)
            {
                graph.Nodes.Add(new GenNodeData(_nodeId, "TestType", "Test Node", Vector2.zero));
            }
        }

        private sealed class RenameGraphMigration : IGraphMigration
        {
            private readonly int _fromVersion;
            private readonly string _graphName;

            public int FromVersion
            {
                get
                {
                    return _fromVersion;
                }
            }

            public RenameGraphMigration(int fromVersion, string graphName)
            {
                _fromVersion = fromVersion;
                _graphName = graphName;
            }

            public void Migrate(GenGraph graph)
            {
                graph.name = _graphName;
            }
        }
    }
}
