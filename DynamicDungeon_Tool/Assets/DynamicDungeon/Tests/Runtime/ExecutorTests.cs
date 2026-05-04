using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Nodes;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.TestTools;

namespace DynamicDungeon.Tests.Runtime
{
    public sealed class ExecutorTests
    {
        private const string BlockingOutputChannelName = "BlockingOutput";
        private const int WaitTimeoutMilliseconds = 5000;

        [Test]
        public async Task SuccessfulRunProducesSnapshotMatchingNodeOutput()
        {
            FlatFillNode node = new FlatFillNode("flat-fill", 4.25f);
            string outputChannelName = GetSingleOutputChannelName(node);
            Executor executor = new Executor();
            ExecutionPlan plan = ExecutionPlan.Build(new IGenNode[] { node }, 4, 3, 1234L);

            ExecutionResult result = await executor.ExecuteAsync(plan, CancellationToken.None);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.WasCancelled, Is.False);
            Assert.That(result.ErrorMessage, Is.Null);
            Assert.That(result.Snapshot, Is.Not.Null);
            Assert.That(result.Snapshot.Width, Is.EqualTo(4));
            Assert.That(result.Snapshot.Height, Is.EqualTo(3));
            Assert.That(result.Snapshot.FloatChannels.Length, Is.EqualTo(1));
            Assert.That(result.Snapshot.FloatChannels[0].Name, Is.EqualTo(outputChannelName));
            AssertAllValuesEqual(result.Snapshot.FloatChannels[0].Data, node.FillValue);
        }

        [Test]
        public async Task CancelledRunReturnsNoSnapshotAndDisposesAllocatedWorld()
        {
            ManualResetEventSlim enteredGate = new ManualResetEventSlim(false);
            ManualResetEventSlim releaseGate = new ManualResetEventSlim(false);
            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            Executor executor = new Executor();
            ExecutionPlan plan = null;

            try
            {
                BlockingNode blockingNode = new BlockingNode("blocking-node", BlockingOutputChannelName, enteredGate, releaseGate);
                FlatFillNode flatFillNode = new FlatFillNode("flat-fill", 2.0f);
                plan = ExecutionPlan.Build(new IGenNode[] { blockingNode, flatFillNode }, 4, 4, 99L);

                LogAssert.NoUnexpectedReceived();
                Task<ExecutionResult> executionTask = executor.ExecuteAsync(plan, cancellationTokenSource.Token);

                Assert.That(enteredGate.Wait(WaitTimeoutMilliseconds), Is.True);
                cancellationTokenSource.Cancel();
                releaseGate.Set();

                ExecutionResult result = await executionTask;

                Assert.That(result.IsSuccess, Is.False);
                Assert.That(result.WasCancelled, Is.True);
                Assert.That(result.ErrorMessage, Is.Null);
                Assert.That(result.Snapshot, Is.Null);
                Assert.Throws<ObjectDisposedException>(() => plan.AllocatedWorld.GetFloatChannel(BlockingOutputChannelName));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                releaseGate.Set();
                cancellationTokenSource.Dispose();
                enteredGate.Dispose();
                releaseGate.Dispose();
            }
        }

        [Test]
        public async Task ScheduleExceptionReturnsFailureAndStopsFurtherExecution()
        {
            Executor executor = new Executor();
            ThrowingNode throwingNode = new ThrowingNode("throwing-node", "ThrowingOutput", "Schedule exploded.");
            CountingNode downstreamNode = new CountingNode("counting-node", "CountingOutput");
            ExecutionPlan plan = ExecutionPlan.Build(new IGenNode[] { throwingNode, downstreamNode }, 3, 3, 456L);

            LogAssert.NoUnexpectedReceived();
            ExecutionResult result = await executor.ExecuteAsync(plan, CancellationToken.None);

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.WasCancelled, Is.False);
            Assert.That(result.Snapshot, Is.Null);
            Assert.That(result.ErrorMessage, Does.Contain("Schedule exploded."));
            Assert.That(downstreamNode.ScheduleCallCount, Is.EqualTo(0));
            Assert.Throws<ObjectDisposedException>(() => plan.AllocatedWorld.GetFloatChannel("ThrowingOutput"));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public async Task ProgressReportsStayWithinRangeAndReachOneOnSuccess()
        {
            FlatFillNode node = new FlatFillNode("flat-fill", -1.5f);
            Executor executor = new Executor();
            ExecutionPlan plan = ExecutionPlan.Build(new IGenNode[] { node }, 5, 2, 678L);
            ProgressRecorder progressRecorder = new ProgressRecorder();

            ExecutionResult result = await executor.ExecuteAsync(plan, CancellationToken.None, progressRecorder);
            float[] reportedValues = progressRecorder.GetValues();

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(reportedValues.Length, Is.GreaterThan(0));

            int index;
            for (index = 0; index < reportedValues.Length; index++)
            {
                Assert.That(reportedValues[index], Is.InRange(0.0f, 1.0f));
            }

            Assert.That(reportedValues[reportedValues.Length - 1], Is.EqualTo(1.0f));
        }

        [Test]
        public async Task ExecuteAsyncWhileAlreadyRunningReturnsInvalidOperationException()
        {
            ManualResetEventSlim enteredGate = new ManualResetEventSlim(false);
            ManualResetEventSlim releaseGate = new ManualResetEventSlim(false);
            Executor executor = new Executor();
            ExecutionPlan firstPlan = null;
            ExecutionPlan secondPlan = null;

            try
            {
                BlockingNode blockingNode = new BlockingNode("blocking-node", BlockingOutputChannelName, enteredGate, releaseGate);
                firstPlan = ExecutionPlan.Build(new IGenNode[] { blockingNode }, 2, 2, 10L);
                secondPlan = ExecutionPlan.Build(new IGenNode[] { new FlatFillNode("flat-fill", 1.0f) }, 2, 2, 11L);

                Task<ExecutionResult> firstExecution = executor.ExecuteAsync(firstPlan, CancellationToken.None);
                Assert.That(enteredGate.Wait(WaitTimeoutMilliseconds), Is.True);

                InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await executor.ExecuteAsync(secondPlan, CancellationToken.None));
                Assert.That(exception, Is.Not.Null);

                releaseGate.Set();
                ExecutionResult firstResult = await firstExecution;
                Assert.That(firstResult.IsSuccess, Is.True);
            }
            finally
            {
                releaseGate.Set();

                if (secondPlan != null)
                {
                    secondPlan.Dispose();
                }

                enteredGate.Dispose();
                releaseGate.Dispose();
            }
        }

        private static void AssertAllValuesEqual(float[] values, float expectedValue)
        {
            int index;
            for (index = 0; index < values.Length; index++)
            {
                Assert.That(values[index], Is.EqualTo(expectedValue));
            }
        }

        private static string GetSingleOutputChannelName(IGenNode node)
        {
            Assert.That(node.ChannelDeclarations.Count, Is.EqualTo(1));
            Assert.That(node.ChannelDeclarations[0].IsWrite, Is.True);
            Assert.That(node.ChannelDeclarations[0].Type, Is.EqualTo(ChannelType.Float));
            return node.ChannelDeclarations[0].ChannelName;
        }

        private sealed class BlockingNode : IGenNode
        {
            private static readonly NodePortDefinition[] _ports = Array.Empty<NodePortDefinition>();
            private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

            private readonly string _nodeId;
            private readonly ChannelDeclaration[] _channelDeclarations;
            private readonly ManualResetEventSlim _enteredGate;
            private readonly ManualResetEventSlim _releaseGate;

            public IReadOnlyList<NodePortDefinition> Ports
            {
                get
                {
                    return _ports;
                }
            }

            public IReadOnlyList<ChannelDeclaration> ChannelDeclarations
            {
                get
                {
                    return _channelDeclarations;
                }
            }

            public IReadOnlyList<BlackboardKey> BlackboardDeclarations
            {
                get
                {
                    return _blackboardDeclarations;
                }
            }

            public string NodeId
            {
                get
                {
                    return _nodeId;
                }
            }

            public string NodeName
            {
                get
                {
                    return "Blocking Node";
                }
            }

            public BlockingNode(string nodeId, string outputChannelName, ManualResetEventSlim enteredGate, ManualResetEventSlim releaseGate)
            {
                _nodeId = nodeId;
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(outputChannelName, ChannelType.Float, true)
                };
                _enteredGate = enteredGate;
                _releaseGate = releaseGate;
            }

            public JobHandle Schedule(NodeExecutionContext context)
            {
                _enteredGate.Set();

                if (!_releaseGate.Wait(WaitTimeoutMilliseconds))
                {
                    throw new TimeoutException("Blocking node test gate timed out.");
                }

                return default;
            }
        }

        private sealed class CountingNode : IGenNode
        {
            private static readonly NodePortDefinition[] _ports = Array.Empty<NodePortDefinition>();
            private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

            private readonly string _nodeId;
            private readonly ChannelDeclaration[] _channelDeclarations;
            private int _scheduleCallCount;

            public IReadOnlyList<NodePortDefinition> Ports
            {
                get
                {
                    return _ports;
                }
            }

            public IReadOnlyList<ChannelDeclaration> ChannelDeclarations
            {
                get
                {
                    return _channelDeclarations;
                }
            }

            public IReadOnlyList<BlackboardKey> BlackboardDeclarations
            {
                get
                {
                    return _blackboardDeclarations;
                }
            }

            public string NodeId
            {
                get
                {
                    return _nodeId;
                }
            }

            public string NodeName
            {
                get
                {
                    return "Counting Node";
                }
            }

            public int ScheduleCallCount
            {
                get
                {
                    return _scheduleCallCount;
                }
            }

            public CountingNode(string nodeId, string outputChannelName)
            {
                _nodeId = nodeId;
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(outputChannelName, ChannelType.Float, true)
                };
                _scheduleCallCount = 0;
            }

            public JobHandle Schedule(NodeExecutionContext context)
            {
                Interlocked.Increment(ref _scheduleCallCount);
                return default;
            }
        }

        private sealed class ProgressRecorder : IProgress<float>
        {
            private readonly List<float> _values;
            private readonly object _syncRoot;

            public ProgressRecorder()
            {
                _values = new List<float>();
                _syncRoot = new object();
            }

            public void Report(float value)
            {
                lock (_syncRoot)
                {
                    _values.Add(value);
                }
            }

            public float[] GetValues()
            {
                lock (_syncRoot)
                {
                    return _values.ToArray();
                }
            }
        }

        private sealed class ThrowingNode : IGenNode
        {
            private static readonly NodePortDefinition[] _ports = Array.Empty<NodePortDefinition>();
            private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

            private readonly string _nodeId;
            private readonly string _errorMessage;
            private readonly ChannelDeclaration[] _channelDeclarations;

            public IReadOnlyList<NodePortDefinition> Ports
            {
                get
                {
                    return _ports;
                }
            }

            public IReadOnlyList<ChannelDeclaration> ChannelDeclarations
            {
                get
                {
                    return _channelDeclarations;
                }
            }

            public IReadOnlyList<BlackboardKey> BlackboardDeclarations
            {
                get
                {
                    return _blackboardDeclarations;
                }
            }

            public string NodeId
            {
                get
                {
                    return _nodeId;
                }
            }

            public string NodeName
            {
                get
                {
                    return "Throwing Node";
                }
            }

            public ThrowingNode(string nodeId, string outputChannelName, string errorMessage)
            {
                _nodeId = nodeId;
                _errorMessage = errorMessage;
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(outputChannelName, ChannelType.Float, true)
                };
            }

            public JobHandle Schedule(NodeExecutionContext context)
            {
                throw new InvalidOperationException(_errorMessage);
            }
        }
    }
}
