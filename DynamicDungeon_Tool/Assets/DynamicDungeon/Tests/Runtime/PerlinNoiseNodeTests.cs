using System;
using System.Threading;
using System.Threading.Tasks;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Nodes;
using NUnit.Framework;
using UnityEngine;

namespace DynamicDungeon.Tests.Runtime
{
    public sealed class PerlinNoiseNodeTests
    {
        private const string OutputChannelName = "HeightMap";
        private const int Width = 8;
        private const int Height = 6;

        [Test]
        public async Task SameSeedAndParametersProduceByteIdenticalOutput()
        {
            Executor executor = new Executor();
            PerlinNoiseNode firstNode = new PerlinNoiseNode("perlin-node", OutputChannelName, 0.075f, 1.0f, new Vector2(2.5f, -1.25f), 3);
            PerlinNoiseNode secondNode = new PerlinNoiseNode("perlin-node", OutputChannelName, 0.075f, 1.0f, new Vector2(2.5f, -1.25f), 3);
            ExecutionPlan firstPlan = ExecutionPlan.Build(new IGenNode[] { firstNode }, Width, Height, 12345L);
            ExecutionPlan secondPlan = ExecutionPlan.Build(new IGenNode[] { secondNode }, Width, Height, 12345L);

            ExecutionResult firstResult = await executor.ExecuteAsync(firstPlan, CancellationToken.None);
            ExecutionResult secondResult = await executor.ExecuteAsync(secondPlan, CancellationToken.None);

            Assert.That(firstResult.IsSuccess, Is.True);
            Assert.That(secondResult.IsSuccess, Is.True);
            Assert.That(firstResult.Snapshot, Is.Not.Null);
            Assert.That(secondResult.Snapshot, Is.Not.Null);

            float[] firstData = firstResult.Snapshot.FloatChannels[0].Data;
            float[] secondData = secondResult.Snapshot.FloatChannels[0].Data;
            AssertByteIdentical(firstData, secondData);
        }

        [Test]
        public async Task DifferentGlobalSeedsProduceDifferentOutput()
        {
            Executor executor = new Executor();
            PerlinNoiseNode firstNode = new PerlinNoiseNode("perlin-node", OutputChannelName, 0.1f, 1.0f, new Vector2(0.0f, 0.0f), 2);
            PerlinNoiseNode secondNode = new PerlinNoiseNode("perlin-node", OutputChannelName, 0.1f, 1.0f, new Vector2(0.0f, 0.0f), 2);
            ExecutionPlan firstPlan = ExecutionPlan.Build(new IGenNode[] { firstNode }, Width, Height, 111L);
            ExecutionPlan secondPlan = ExecutionPlan.Build(new IGenNode[] { secondNode }, Width, Height, 222L);

            ExecutionResult firstResult = await executor.ExecuteAsync(firstPlan, CancellationToken.None);
            ExecutionResult secondResult = await executor.ExecuteAsync(secondPlan, CancellationToken.None);

            Assert.That(firstResult.IsSuccess, Is.True);
            Assert.That(secondResult.IsSuccess, Is.True);
            Assert.That(HasAnyByteDifference(firstResult.Snapshot.FloatChannels[0].Data, secondResult.Snapshot.FloatChannels[0].Data), Is.True);
        }

        [Test]
        public async Task OutputValuesStayWithinZeroToOneRange()
        {
            Executor executor = new Executor();
            PerlinNoiseNode node = new PerlinNoiseNode("perlin-node", OutputChannelName, 0.2f, 1.5f, new Vector2(-4.0f, 7.0f), 4);
            ExecutionPlan plan = ExecutionPlan.Build(new IGenNode[] { node }, Width, Height, 999L);

            ExecutionResult result = await executor.ExecuteAsync(plan, CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True);

            float[] data = result.Snapshot.FloatChannels[0].Data;
            int index;
            for (index = 0; index < data.Length; index++)
            {
                Assert.That(data[index], Is.InRange(0.0f, 1.0f));
            }
        }

        [Test]
        public async Task NodeIntegratesWithExecutionPlanAndExecutorEndToEnd()
        {
            Executor executor = new Executor();
            PerlinNoiseNode node = new PerlinNoiseNode("perlin-node", OutputChannelName, 0.05f, 0.8f, new Vector2(1.0f, 3.0f), 1);
            ExecutionPlan plan = ExecutionPlan.Build(new IGenNode[] { node }, Width, Height, 2026L);

            ExecutionResult result = await executor.ExecuteAsync(plan, CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.WasCancelled, Is.False);
            Assert.That(result.ErrorMessage, Is.Null);
            Assert.That(result.Snapshot, Is.Not.Null);
            Assert.That(result.Snapshot.Width, Is.EqualTo(Width));
            Assert.That(result.Snapshot.Height, Is.EqualTo(Height));
            Assert.That(result.Snapshot.FloatChannels.Length, Is.EqualTo(1));
            Assert.That(result.Snapshot.FloatChannels[0].Name, Is.EqualTo(OutputChannelName));
            Assert.That(result.Snapshot.FloatChannels[0].Data.Length, Is.EqualTo(Width * Height));
        }

        private static void AssertByteIdentical(float[] expected, float[] actual)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length));

            int index;
            for (index = 0; index < expected.Length; index++)
            {
                byte[] expectedBytes = BitConverter.GetBytes(expected[index]);
                byte[] actualBytes = BitConverter.GetBytes(actual[index]);
                CollectionAssert.AreEqual(expectedBytes, actualBytes);
            }
        }

        private static bool HasAnyByteDifference(float[] first, float[] second)
        {
            if (first.Length != second.Length)
            {
                return true;
            }

            int index;
            for (index = 0; index < first.Length; index++)
            {
                byte[] firstBytes = BitConverter.GetBytes(first[index]);
                byte[] secondBytes = BitConverter.GetBytes(second[index]);

                int byteIndex;
                for (byteIndex = 0; byteIndex < firstBytes.Length; byteIndex++)
                {
                    if (firstBytes[byteIndex] != secondBytes[byteIndex])
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
