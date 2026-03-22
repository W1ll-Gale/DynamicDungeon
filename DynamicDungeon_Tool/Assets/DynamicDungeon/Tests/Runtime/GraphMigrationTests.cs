using System;
using System.Collections.Generic;
using System.Reflection;
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
