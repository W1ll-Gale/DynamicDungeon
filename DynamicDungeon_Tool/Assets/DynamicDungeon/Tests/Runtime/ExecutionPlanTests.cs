using System;
using System.Collections.Generic;
using System.Reflection;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Nodes;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine.TestTools;

namespace DynamicDungeon.Tests.Runtime
{
    public sealed class ExecutionPlanTests
    {
        private const string FlatOutputChannelName = "FlatOutput";

        [Test]
        public void BuildWithSingleFlatFillNodeAllocatesExpectedWorldChannel()
        {
            FlatFillNode node = new FlatFillNode("node-flat", 2.5f);

            using (ExecutionPlan plan = ExecutionPlan.Build(new IGenNode[] { node }, 5, 4, 1234L))
            {
                Assert.That(plan.Jobs.Count, Is.EqualTo(1));
                Assert.That(plan.AllocatedWorld.Width, Is.EqualTo(5));
                Assert.That(plan.AllocatedWorld.Height, Is.EqualTo(4));
                Assert.That(plan.AllocatedWorld.HasFloatChannel(FlatOutputChannelName), Is.True);
                Assert.That(plan.AllocatedWorld.HasIntChannel(FlatOutputChannelName), Is.False);
                Assert.That(plan.AllocatedWorld.HasBoolMaskChannel(FlatOutputChannelName), Is.False);

                NativeArray<float> outputChannel = plan.AllocatedWorld.GetFloatChannel(FlatOutputChannelName);
                Assert.That(outputChannel.IsCreated, Is.True);
                Assert.That(outputChannel.Length, Is.EqualTo(20));
            }
        }

        [Test]
        public void SchedulingFlatFillNodeWritesFillValueToEveryTile()
        {
            FlatFillNode node = new FlatFillNode("node-flat", -3.75f);
            ExecutionPlan plan = null;
            NodeChannelBindings channelBindings = default;
            NativeHashMap<FixedString64Bytes, float> numericBlackboard = default;

            try
            {
                plan = ExecutionPlan.Build(new IGenNode[] { node }, 6, 3, 5678L);
                channelBindings = new NodeChannelBindings(1, Allocator.TempJob);
                numericBlackboard = new NativeHashMap<FixedString64Bytes, float>(1, Allocator.TempJob);

                NativeArray<float> outputChannel = plan.AllocatedWorld.GetFloatChannel(FlatOutputChannelName);
                channelBindings.BindFloatChannel(FlatOutputChannelName, outputChannel);

                NodeExecutionContext context = new NodeExecutionContext(
                    channelBindings,
                    numericBlackboard,
                    plan.GetLocalSeed(node.NodeId),
                    plan.AllocatedWorld.Width,
                    plan.AllocatedWorld.Height,
                    default);

                JobHandle handle = node.Schedule(context);
                handle.Complete();

                int index;
                for (index = 0; index < outputChannel.Length; index++)
                {
                    Assert.That(outputChannel[index], Is.EqualTo(node.FillValue));
                }
            }
            finally
            {
                if (numericBlackboard.IsCreated)
                {
                    numericBlackboard.Dispose();
                }

                if (channelBindings.IsCreated)
                {
                    channelBindings.Dispose();
                }

                if (plan != null)
                {
                    plan.Dispose();
                }
            }
        }

        [Test]
        public void DisposingPlanDisposesAllocatedWorldWithoutLeakWarnings()
        {
            FlatFillNode node = new FlatFillNode("node-flat", 1.0f);
            ExecutionPlan plan = ExecutionPlan.Build(new IGenNode[] { node }, 2, 2, 99L);

            LogAssert.NoUnexpectedReceived();
            Assert.DoesNotThrow(() => plan.Dispose());
            Assert.DoesNotThrow(() => plan.Dispose());
            Assert.Throws<ObjectDisposedException>(() => plan.AllocatedWorld.GetFloatChannel(FlatOutputChannelName));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MarkDirtyFlagsTargetNodeAndDownstreamReaders()
        {
            TestNode nodeA = new TestNode(
                "node-a",
                "Node A",
                new[]
                {
                    new ChannelDeclaration("ChannelA", ChannelType.Float, true)
                });

            TestNode nodeB = new TestNode(
                "node-b",
                "Node B",
                new[]
                {
                    new ChannelDeclaration("ChannelA", ChannelType.Float, false),
                    new ChannelDeclaration("ChannelB", ChannelType.Float, true)
                });

            TestNode nodeC = new TestNode(
                "node-c",
                "Node C",
                new[]
                {
                    new ChannelDeclaration("ChannelB", ChannelType.Float, false),
                    new ChannelDeclaration("ChannelC", ChannelType.Float, true)
                });

            TestNode nodeD = new TestNode(
                "node-d",
                "Node D",
                new[]
                {
                    new ChannelDeclaration("ChannelX", ChannelType.Float, true)
                });

            using (ExecutionPlan plan = ExecutionPlan.Build(new IGenNode[] { nodeA, nodeB, nodeC, nodeD }, 4, 4, 42L))
            {
                SetAllJobsDirtyState(plan, false);

                plan.MarkDirty(nodeB.NodeId);

                Assert.That(plan.Jobs[0].IsDirty, Is.False);
                Assert.That(plan.Jobs[1].IsDirty, Is.True);
                Assert.That(plan.Jobs[2].IsDirty, Is.True);
                Assert.That(plan.Jobs[3].IsDirty, Is.False);
            }
        }

        [Test]
        public void MarkAllDirtyFlagsEveryNode()
        {
            TestNode nodeA = new TestNode(
                "node-a",
                "Node A",
                new[]
                {
                    new ChannelDeclaration("ChannelA", ChannelType.Float, true)
                });

            TestNode nodeB = new TestNode(
                "node-b",
                "Node B",
                new[]
                {
                    new ChannelDeclaration("ChannelA", ChannelType.Float, false),
                    new ChannelDeclaration("ChannelB", ChannelType.Float, true)
                });

            TestNode nodeC = new TestNode(
                "node-c",
                "Node C",
                new[]
                {
                    new ChannelDeclaration("ChannelB", ChannelType.Float, false),
                    new ChannelDeclaration("ChannelC", ChannelType.Float, true)
                });

            using (ExecutionPlan plan = ExecutionPlan.Build(new IGenNode[] { nodeA, nodeB, nodeC }, 3, 3, 17L))
            {
                SetAllJobsDirtyState(plan, false);

                plan.MarkAllDirty();

                int index;
                for (index = 0; index < plan.Jobs.Count; index++)
                {
                    Assert.That(plan.Jobs[index].IsDirty, Is.True);
                }
            }
        }

        private static void SetAllJobsDirtyState(ExecutionPlan plan, bool isDirty)
        {
            FieldInfo jobsField = typeof(ExecutionPlan).GetField("_jobs", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(jobsField, Is.Not.Null);

            List<NodeJobDescriptor> jobs = (List<NodeJobDescriptor>)jobsField.GetValue(plan);
            Assert.That(jobs, Is.Not.Null);

            int index;
            for (index = 0; index < jobs.Count; index++)
            {
                NodeJobDescriptor job = jobs[index];
                job.IsDirty = isDirty;
                jobs[index] = job;
            }
        }

        private sealed class TestNode : IGenNode
        {
            private static readonly NodePortDefinition[] _ports = Array.Empty<NodePortDefinition>();
            private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

            private readonly string _nodeId;
            private readonly string _nodeName;
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
                    return _nodeName;
                }
            }

            public TestNode(string nodeId, string nodeName, ChannelDeclaration[] channelDeclarations)
            {
                if (string.IsNullOrWhiteSpace(nodeId))
                {
                    throw new ArgumentException("Node ID must be non-empty.", nameof(nodeId));
                }

                if (string.IsNullOrWhiteSpace(nodeName))
                {
                    throw new ArgumentException("Node name must be non-empty.", nameof(nodeName));
                }

                _nodeId = nodeId;
                _nodeName = nodeName;
                _channelDeclarations = channelDeclarations ?? Array.Empty<ChannelDeclaration>();
            }

            public JobHandle Schedule(NodeExecutionContext context)
            {
                return context.InputDependency;
            }
        }
    }
}
