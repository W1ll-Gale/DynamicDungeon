using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DynamicDungeon.Runtime.Component;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using NUnit.Framework;
using UnityEngine;

namespace DynamicDungeon.Tests.Runtime
{
    public sealed class BlackboardTests
    {
        [Test]
        public async Task ExposedPropertyNodeReadsFloatDefaultIntoFloatChannel()
        {
            const string propertyId = "surface-height-id";
            const float expectedValue = 0.625f;

            Executor executor = new Executor();
            ExposedPropertyNode propertyNode = new ExposedPropertyNode(
                "reader-node",
                "Surface Height",
                propertyId,
                "Surface Height",
                ChannelType.Float);
            Dictionary<string, float> initialValues = new Dictionary<string, float>
            {
                { propertyId, expectedValue }
            };

            ExecutionResult result = await executor.ExecuteAsync(
                ExecutionPlan.Build(new IGenNode[] { propertyNode }, 4, 3, 123L, initialValues),
                CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Snapshot, Is.Not.Null);
            Assert.That(result.Snapshot.FloatChannels.Length, Is.EqualTo(1));
            Assert.That(
                result.Snapshot.FloatChannels[0].Name,
                Is.EqualTo(ExposedPropertyNodeUtility.CreateOutputChannelName(propertyNode.NodeId)));

            float[] output = result.Snapshot.FloatChannels[0].Data;
            int index;
            for (index = 0; index < output.Length; index++)
            {
                Assert.That(output[index], Is.EqualTo(expectedValue));
            }
        }

        [Test]
        public async Task ExposedPropertyNodeReadsIntDefaultIntoIntChannel()
        {
            const string propertyId = "iteration-count-id";
            const int expectedValue = 7;

            Executor executor = new Executor();
            ExposedPropertyNode propertyNode = new ExposedPropertyNode(
                "int-node",
                "Iteration Count",
                propertyId,
                "Iteration Count",
                ChannelType.Int);
            Dictionary<string, float> initialValues = new Dictionary<string, float>
            {
                { propertyId, expectedValue }
            };

            ExecutionResult result = await executor.ExecuteAsync(
                ExecutionPlan.Build(new IGenNode[] { propertyNode }, 3, 2, 456L, initialValues),
                CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Snapshot, Is.Not.Null);
            Assert.That(result.Snapshot.IntChannels.Length, Is.EqualTo(1));
            Assert.That(
                result.Snapshot.IntChannels[0].Name,
                Is.EqualTo(ExposedPropertyNodeUtility.CreateOutputChannelName(propertyNode.NodeId)));

            int[] output = result.Snapshot.IntChannels[0].Data;
            int index;
            for (index = 0; index < output.Length; index++)
            {
                Assert.That(output[index], Is.EqualTo(expectedValue));
            }
        }

        [Test]
        public void ReconcilePropertyOverridesKeepsOverrideBoundByPropertyIdAfterRename()
        {
            GameObject gameObject = new GameObject("TilemapWorldGeneratorTest");
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();
            TilemapWorldGenerator component = gameObject.AddComponent<TilemapWorldGenerator>();

            try
            {
                ExposedProperty property = graph.AddExposedProperty("Old Name", ChannelType.Float, "1");
                component.Graph = graph;
                component.PropertyOverrides.Add(
                    new ExposedPropertyOverride
                    {
                        PropertyId = property.PropertyId,
                        PropertyName = property.PropertyName,
                        OverrideValue = "7.5"
                    });

                property.PropertyName = "New Name";
                component.ReconcilePropertyOverrides();

                Assert.That(component.PropertyOverrides.Count, Is.EqualTo(1));
                Assert.That(component.PropertyOverrides[0].PropertyId, Is.EqualTo(property.PropertyId));
                Assert.That(component.PropertyOverrides[0].PropertyName, Is.EqualTo("New Name"));
                Assert.That(component.PropertyOverrides[0].OverrideValue, Is.EqualTo("7.5"));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(graph);
            }
        }
    }
}
