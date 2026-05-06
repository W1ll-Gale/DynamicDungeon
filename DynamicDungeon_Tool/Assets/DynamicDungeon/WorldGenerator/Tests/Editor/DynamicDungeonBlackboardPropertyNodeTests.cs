using System;
using System.Collections.Generic;
using DynamicDungeon.Editor.Windows;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using NUnit.Framework;
using UnityEngine;

namespace DynamicDungeon.Tests.Editor
{
    public sealed class DynamicDungeonBlackboardPropertyNodeTests
    {
        [Test]
        public void BlackboardDropCreatesGetterNodeBoundByPropertyId()
        {
            DynamicDungeonGraphView graphView = new DynamicDungeonGraphView();
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();

            try
            {
                ExposedProperty property = graph.AddExposedProperty("Seed Strength", ChannelType.Float, "0");
                graphView.LoadGraph(graph);

                graphView.CreateExposedPropertyNodeFromBlackboardForTesting(property.PropertyId, new Vector2(140.0f, 80.0f));

                Assert.That(graph.Nodes.Count, Is.EqualTo(1));
                Assert.That(ExposedPropertyNodeUtility.IsExposedPropertyNode(graph.Nodes[0]), Is.True);
                Assert.That(ExposedPropertyNodeUtility.GetPropertyId(graph.Nodes[0]), Is.EqualTo(property.PropertyId));
                Assert.That(graph.Nodes[0].NodeName, Is.EqualTo("Seed Strength"));
                Assert.That(graph.Nodes[0].Ports.Count, Is.EqualTo(1));
                Assert.That(graph.Nodes[0].Ports[0].PortName, Is.EqualTo(ExposedPropertyNodeUtility.OutputPortName));
                Assert.That(graph.Nodes[0].Ports[0].Type, Is.EqualTo(ChannelType.Float));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void ChangingPropertyTypeRemovesIncompatibleOutgoingConnections()
        {
            int mutationCount = 0;
            DynamicDungeonGraphView graphView = new DynamicDungeonGraphView();
            BlackboardPanel panel = new BlackboardPanel(() => mutationCount++);
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();

            try
            {
                graphView.Add(panel);
                panel.SetGraph(graph);

                ExposedProperty property = graph.AddExposedProperty("Density", ChannelType.Float, "0");
                graphView.LoadGraph(graph);
                graphView.CreateExposedPropertyNodeFromBlackboardForTesting(property.PropertyId, new Vector2(100.0f, 100.0f));

                GenNodeData consumerNode = new GenNodeData(
                    "float-consumer",
                    typeof(FloatInputTestNode).FullName,
                    "Float Consumer",
                    Vector2.zero);
                consumerNode.Ports.Add(new GenPortData("Input", PortDirection.Input, ChannelType.Float));
                graph.Nodes.Add(consumerNode);
                graph.Connections.Add(
                    new GenConnectionData(
                        graph.Nodes[0].NodeId,
                        ExposedPropertyNodeUtility.OutputPortName,
                        consumerNode.NodeId,
                        "Input"));

                panel.ChangePropertyTypeForTesting(property, ChannelType.Int);

                Assert.That(graph.Connections.Count, Is.EqualTo(0));
                Assert.That(graph.Nodes[0].Ports[0].Type, Is.EqualTo(ChannelType.Int));
                Assert.That(mutationCount, Is.GreaterThanOrEqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(graph);
            }
        }

        internal sealed class FloatInputTestNode : IGenNode
        {
            private readonly NodePortDefinition[] _ports;
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
                    return Array.Empty<BlackboardKey>();
                }
            }

            public string NodeId
            {
                get
                {
                    return "float-consumer";
                }
            }

            public string NodeName
            {
                get
                {
                    return "Float Consumer";
                }
            }

            public FloatInputTestNode(string nodeId, string nodeName, string inputChannelName)
            {
                _ports = new[]
                {
                    new NodePortDefinition("Input", PortDirection.Input, ChannelType.Float, PortCapacity.Single, true)
                };
                _channelDeclarations = new[]
                {
                    new ChannelDeclaration(inputChannelName, ChannelType.Float, false)
                };
            }

            public Unity.Jobs.JobHandle Schedule(NodeExecutionContext context)
            {
                return context.InputDependency;
            }
        }
    }
}
