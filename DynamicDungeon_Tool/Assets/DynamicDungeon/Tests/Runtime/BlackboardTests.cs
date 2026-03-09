using System;
using System.Threading;
using System.Threading.Tasks;
using DynamicDungeon.Runtime.Core;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace DynamicDungeon.Tests.Runtime
{
    public sealed class BlackboardTests
    {
        private const string BlackboardKeyName = "SurfaceHeight";
        private const string OutputChannelName = "BlackboardOutput";
        private const float WrittenValue = 0.625f;

        [Test]
        public async Task WriterFollowedByReaderProducesExpectedChannelValues()
        {
            Executor executor = new Executor();
            BlackboardWriterNode writerNode = new BlackboardWriterNode("writer-node", BlackboardKeyName, WrittenValue);
            BlackboardReaderNode readerNode = new BlackboardReaderNode("reader-node", BlackboardKeyName, OutputChannelName);
            ExecutionPlan plan = ExecutionPlan.Build(new IGenNode[] { writerNode, readerNode }, 4, 3, 123L);

            ExecutionResult result = await executor.ExecuteAsync(plan, CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.WasCancelled, Is.False);
            Assert.That(result.ErrorMessage, Is.Null);
            Assert.That(result.Snapshot, Is.Not.Null);
            Assert.That(result.Snapshot.FloatChannels.Length, Is.EqualTo(1));
            Assert.That(result.Snapshot.FloatChannels[0].Name, Is.EqualTo(OutputChannelName));

            float[] output = result.Snapshot.FloatChannels[0].Data;
            int index;
            for (index = 0; index < output.Length; index++)
            {
                Assert.That(output[index], Is.EqualTo(WrittenValue));
            }
        }

        [Test]
        public void MissingUpstreamBlackboardWriteThrowsClearBuildError()
        {
            BlackboardReaderNode readerNode = new BlackboardReaderNode("reader-node", BlackboardKeyName, OutputChannelName);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => ExecutionPlan.Build(new IGenNode[] { readerNode }, 2, 2, 77L));

            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.Message, Does.Contain("Blackboard Reader"));
            Assert.That(exception.Message, Does.Contain(BlackboardKeyName));
            Assert.That(exception.Message, Does.Contain("declared a write"));
        }

        [Test]
        public async Task NumericBlackboardDisposesCleanlyAtEndOfRun()
        {
            Executor executor = new Executor();
            BlackboardWriterNode writerNode = new BlackboardWriterNode("writer-node", BlackboardKeyName, WrittenValue);
            BlackboardReaderNode readerNode = new BlackboardReaderNode("reader-node", BlackboardKeyName, OutputChannelName);
            ExecutionPlan plan = ExecutionPlan.Build(new IGenNode[] { writerNode, readerNode }, 3, 2, 456L);

            LogAssert.NoUnexpectedReceived();
            ExecutionResult result = await executor.ExecuteAsync(plan, CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True);
            Assert.Throws<ObjectDisposedException>(() => plan.AllocatedWorld.GetFloatChannel(OutputChannelName));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
