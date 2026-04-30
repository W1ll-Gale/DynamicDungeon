using System.Reflection;
using System.Collections.Generic;
using DynamicDungeon.Editor.Nodes;
using DynamicDungeon.Editor.Windows;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Editor.Utilities;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Nodes;
using NUnit.Framework;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace DynamicDungeon.Tests.Editor
{
    public sealed class ExtendedNoiseEditorTests
    {
        [Test]
        public void ConstantNodeOutputTypeIsExposedAsAnEditableSerialisedParameter()
        {
            bool isEditable = GenNodeInstantiationUtility.IsEditableSerialisedParameter(typeof(ConstantNode), "outputType");

            Assert.That(isEditable, Is.True);
        }

        [Test]
        public void ConstantNodeDefaultParametersIncludeOutputType()
        {
            GenNodeData nodeData = new GenNodeData("constant-node", typeof(ConstantNode).FullName, "Constant", Vector2.zero);

            List<SerializedParameter> parameters = GenNodeInstantiationUtility.CreateDefaultParameters(nodeData, typeof(ConstantNode));

            Assert.That(ContainsParameter(parameters, "outputType"), Is.True);
        }

        [Test]
        public void ConstantOutputTypeSwitchPreservesCastCompatibleConnection()
        {
            DynamicDungeonGraphView graphView = new DynamicDungeonGraphView();
            GenGraph graph = ScriptableObject.CreateInstance<GenGraph>();

            try
            {
                string constantNodeId = "constant-node";
                string constantOutputPortName = GraphPortNameUtility.CreateGeneratedOutputPortName(constantNodeId, GraphPortNameUtility.LegacyGenericOutputDisplayName);
                GenNodeData constantNode = new GenNodeData(constantNodeId, typeof(ConstantNode).FullName, "Constant", Vector2.zero);
                constantNode.Ports.Add(new GenPortData(constantOutputPortName, PortDirection.Output, ChannelType.Float, GraphPortNameUtility.LegacyGenericOutputDisplayName));
                constantNode.Parameters.Add(new SerializedParameter("outputType", "Float"));
                constantNode.Parameters.Add(new SerializedParameter("floatValue", "1"));
                graph.Nodes.Add(constantNode);

                string invertNodeId = "invert-node";
                string invertOutputPortName = GraphPortNameUtility.CreateGeneratedOutputPortName(invertNodeId, "Output");
                GenNodeData invertNode = new GenNodeData(invertNodeId, typeof(InvertNode).FullName, "Invert", new Vector2(300.0f, 0.0f));
                invertNode.Ports.Add(new GenPortData("Input", PortDirection.Input, ChannelType.BoolMask));
                invertNode.Ports.Add(new GenPortData(invertOutputPortName, PortDirection.Output, ChannelType.BoolMask, "Output"));
                graph.Nodes.Add(invertNode);

                GenConnectionData connection = new GenConnectionData(constantNodeId, constantOutputPortName, invertNodeId, "Input");
                connection.CastMode = CastMode.FloatToBoolMask;
                graph.Connections.Add(connection);

                graphView.LoadGraph(graph);

                GenNodeView constantNodeView = FindNodeView(graphView, constantNodeId);
                Assert.That(constantNodeView, Is.Not.Null);

                MethodInfo parameterChangeMethod = typeof(GenNodeView).GetMethod("OnParameterValueChanged", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(parameterChangeMethod, Is.Not.Null);
                parameterChangeMethod.Invoke(constantNodeView, new object[] { "outputType", "Int" });

                Assert.That(graph.Connections.Count, Is.EqualTo(1));
                Assert.That(graph.Connections[0].FromNodeId, Is.EqualTo(constantNodeId));
                Assert.That(graph.Connections[0].ToNodeId, Is.EqualTo(invertNodeId));
                Assert.That(graph.Connections[0].CastMode, Is.EqualTo(CastMode.IntToBoolMask));
                Assert.That(CountEdges(graphView), Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(graph);
            }
        }

        [Test]
        public void GradientNoiseNodeShowsCentreOnlyForRadialAndAngleOnlyForDiagonal()
        {
            GradientNoiseNode node = new GradientNoiseNode("gradient-node", "Gradient", "GradientOut");

            Assert.That(node.IsParameterVisible("centre"), Is.False);
            Assert.That(node.IsParameterVisible("angle"), Is.False);
            Assert.That(node.IsParameterVisible("scale"), Is.True);
            Assert.That(node.IsParameterVisible("radius"), Is.False);
            Assert.That(node.IsParameterVisible("amplitude"), Is.True);

            node.ReceiveParameter("direction", "Radial");
            Assert.That(node.IsParameterVisible("centre"), Is.True);
            Assert.That(node.IsParameterVisible("angle"), Is.False);
            Assert.That(node.IsParameterVisible("scale"), Is.False);
            Assert.That(node.IsParameterVisible("radius"), Is.True);

            node.ReceiveParameter("direction", "Diagonal");
            Assert.That(node.IsParameterVisible("centre"), Is.False);
            Assert.That(node.IsParameterVisible("angle"), Is.True);
            Assert.That(node.IsParameterVisible("scale"), Is.True);
            Assert.That(node.IsParameterVisible("radius"), Is.False);
        }

        [Test]
        public void UnifiedNoiseNodeShowsCentreOnlyForRadialAndAngleOnlyForDiagonal()
        {
            NoiseNode node = new NoiseNode("noise-node", "Noise");
            node.ReceiveParameter("algorithm", "Gradient");

            Assert.That(node.IsParameterVisible("centre"), Is.False);
            Assert.That(node.IsParameterVisible("angle"), Is.False);
            Assert.That(node.IsParameterVisible("scale"), Is.True);
            Assert.That(node.IsParameterVisible("radius"), Is.False);
            Assert.That(node.IsParameterVisible("amplitude"), Is.True);

            node.ReceiveParameter("direction", "Radial");
            Assert.That(node.IsParameterVisible("centre"), Is.True);
            Assert.That(node.IsParameterVisible("angle"), Is.False);
            Assert.That(node.IsParameterVisible("scale"), Is.False);
            Assert.That(node.IsParameterVisible("radius"), Is.True);

            node.ReceiveParameter("direction", "Diagonal");
            Assert.That(node.IsParameterVisible("centre"), Is.False);
            Assert.That(node.IsParameterVisible("angle"), Is.True);
            Assert.That(node.IsParameterVisible("scale"), Is.True);
            Assert.That(node.IsParameterVisible("radius"), Is.False);
        }

        [Test]
        public void PrefabStamperNodeShowsOnlyRelevantFootprintParameters()
        {
            PrefabStamperNode node = new PrefabStamperNode("prefab-stamper", "Prefab Stamper");

            Assert.That(node.IsParameterVisible("interiorLogicalId"), Is.False);
            Assert.That(node.IsParameterVisible("outlineLogicalId"), Is.False);
            Assert.That(node.IsParameterVisible("blendMode"), Is.False);
            Assert.That(node.IsParameterVisible("maxOverlapTiles"), Is.True);

            node.ReceiveParameter("footprintMode", "FillInterior");
            Assert.That(node.IsParameterVisible("interiorLogicalId"), Is.True);
            Assert.That(node.IsParameterVisible("outlineLogicalId"), Is.False);
            Assert.That(node.IsParameterVisible("blendMode"), Is.True);
            Assert.That(node.IsParameterVisible("maxOverlapTiles"), Is.True);

            node.ReceiveParameter("footprintMode", "OutlineAndCarve");
            Assert.That(node.IsParameterVisible("interiorLogicalId"), Is.False);
            Assert.That(node.IsParameterVisible("outlineLogicalId"), Is.True);
            Assert.That(node.IsParameterVisible("blendMode"), Is.True);
            Assert.That(node.IsParameterVisible("maxOverlapTiles"), Is.False);

            node.ReceiveParameter("footprintMode", "CarveInterior");
            Assert.That(node.IsParameterVisible("interiorLogicalId"), Is.False);
            Assert.That(node.IsParameterVisible("outlineLogicalId"), Is.False);
            Assert.That(node.IsParameterVisible("blendMode"), Is.False);
            Assert.That(node.IsParameterVisible("maxOverlapTiles"), Is.False);
        }

        [Test]
        public void GradientDefaultsUseMapCentreRatherThanBottomLeft()
        {
            GenNodeData gradientNodeData = new GenNodeData("gradient-node", typeof(GradientNoiseNode).FullName, "Gradient", Vector2.zero);
            List<SerializedParameter> gradientParameters = GenNodeInstantiationUtility.CreateDefaultParameters(gradientNodeData, typeof(GradientNoiseNode));

            GenNodeData noiseNodeData = new GenNodeData("noise-node", typeof(NoiseNode).FullName, "Noise", Vector2.zero);
            List<SerializedParameter> noiseParameters = GenNodeInstantiationUtility.CreateDefaultParameters(noiseNodeData, typeof(NoiseNode));

            Assert.That(GetParameterValue(gradientParameters, "centre"), Is.EqualTo("0.5,0.5"));
            Assert.That(GetParameterValue(noiseParameters, "centre"), Is.EqualTo("0.5,0.5"));
            Assert.That(GetParameterValue(gradientParameters, "scale"), Is.EqualTo("1"));
            Assert.That(GetParameterValue(noiseParameters, "scale"), Is.EqualTo("1"));
            Assert.That(GetParameterValue(gradientParameters, "radius"), Is.EqualTo("1"));
            Assert.That(GetParameterValue(noiseParameters, "radius"), Is.EqualTo("1"));
            Assert.That(GetParameterValue(gradientParameters, "amplitude"), Is.EqualTo("1"));
            Assert.That(GetParameterValue(noiseParameters, "amplitude"), Is.EqualTo("1"));
        }

        private static bool ContainsParameter(IReadOnlyList<SerializedParameter> parameters, string name)
        {
            int index;
            for (index = 0; index < parameters.Count; index++)
            {
                SerializedParameter parameter = parameters[index];
                if (parameter != null && parameter.Name == name)
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetParameterValue(IReadOnlyList<SerializedParameter> parameters, string name)
        {
            int index;
            for (index = 0; index < parameters.Count; index++)
            {
                SerializedParameter parameter = parameters[index];
                if (parameter != null && parameter.Name == name)
                {
                    return parameter.Value;
                }
            }

            return null;
        }

        private static GenNodeView FindNodeView(DynamicDungeonGraphView graphView, string nodeId)
        {
            foreach (GraphElement graphElement in graphView.graphElements)
            {
                GenNodeView nodeView = graphElement as GenNodeView;
                if (nodeView != null && nodeView.NodeData != null && nodeView.NodeData.NodeId == nodeId)
                {
                    return nodeView;
                }
            }

            return null;
        }

        private static int CountEdges(DynamicDungeonGraphView graphView)
        {
            int count = 0;

            foreach (GraphElement graphElement in graphView.graphElements)
            {
                if (graphElement is Edge)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
