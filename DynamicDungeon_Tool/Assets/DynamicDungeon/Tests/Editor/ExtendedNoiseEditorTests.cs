using System.Collections.Generic;
using DynamicDungeon.Editor.Utilities;
using DynamicDungeon.Runtime.Graph;
using DynamicDungeon.Runtime.Nodes;
using NUnit.Framework;
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
    }
}
