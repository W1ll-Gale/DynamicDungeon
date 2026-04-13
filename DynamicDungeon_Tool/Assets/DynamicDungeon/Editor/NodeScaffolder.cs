using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.ProjectWindowCallback;
using UnityEngine;

namespace DynamicDungeon.Editor
{
    public static class NodeScaffolder
    {
        private const string MenuItemPath = "Assets/Create/DynamicDungeon/Custom Node Script";

        [MenuItem(MenuItemPath)]
        public static void CreateCustomNodeScript()
        {
            string selectedFolderPath = DynamicDungeonEditorAssetUtility.GetSelectedFolderPath();
            string defaultScriptPath = AssetDatabase.GenerateUniqueAssetPath((selectedFolderPath + "/NewCustomNode.cs").Replace("\\", "/"));
            Texture2D scriptIcon = EditorGUIUtility.IconContent("cs Script Icon").image as Texture2D;
            CustomNodeScriptNameEditAction endNameEditAction = ScriptableObject.CreateInstance<CustomNodeScriptNameEditAction>();
            ProjectWindowUtil.StartNameEditingIfProjectWindowExists(0, endNameEditAction, defaultScriptPath, scriptIcon, null);
        }

        private static string BuildDisplayName(string className)
        {
            if (string.IsNullOrWhiteSpace(className))
            {
                return "Custom Node";
            }

            StringBuilder displayNameBuilder = new StringBuilder(className.Length + 8);

            int index;
            for (index = 0; index < className.Length; index++)
            {
                char currentCharacter = className[index];
                if (index > 0 && char.IsUpper(currentCharacter) && (char.IsLower(className[index - 1]) || (index + 1 < className.Length && char.IsLower(className[index + 1]))))
                {
                    displayNameBuilder.Append(' ');
                }

                displayNameBuilder.Append(currentCharacter);
            }

            return displayNameBuilder.ToString();
        }

        private static string BuildScriptContents(string className)
        {
            string displayName = BuildDisplayName(className);

            return
$@"using System;
using System.Collections.Generic;
using DynamicDungeon.Runtime.Core;
using DynamicDungeon.Runtime.Graph;
using Unity.Jobs;

namespace DynamicDungeon.Runtime.Nodes
{{
    [NodeCategory(""Custom"")]
    [NodeDisplayName(""{displayName}"")]
    public sealed class {className} : IGenNode, IParameterReceiver
    {{
        private static readonly NodePortDefinition[] _ports = Array.Empty<NodePortDefinition>();
        private static readonly ChannelDeclaration[] _channelDeclarations = Array.Empty<ChannelDeclaration>();
        private static readonly BlackboardKey[] _blackboardDeclarations = Array.Empty<BlackboardKey>();

        private readonly string _nodeId;
        private readonly string _nodeName;

        public IReadOnlyList<NodePortDefinition> Ports
        {{
            get
            {{
                return _ports;
            }}
        }}

        public IReadOnlyList<ChannelDeclaration> ChannelDeclarations
        {{
            get
            {{
                return _channelDeclarations;
            }}
        }}

        public IReadOnlyList<BlackboardKey> BlackboardDeclarations
        {{
            get
            {{
                return _blackboardDeclarations;
            }}
        }}

        public string NodeId
        {{
            get
            {{
                return _nodeId;
            }}
        }}

        public string NodeName
        {{
            get
            {{
                return _nodeName;
            }}
        }}

        public {className}(string nodeId, string nodeName)
        {{
            if (string.IsNullOrWhiteSpace(nodeId))
            {{
                throw new ArgumentException(""Node ID must be non-empty."", nameof(nodeId));
            }}

            _nodeId = nodeId;
            _nodeName = string.IsNullOrWhiteSpace(nodeName) ? ""{displayName}"" : nodeName;
        }}

        public void ReceiveParameter(string name, string value)
        {{
            // Implement: Parse and store any editable serialised parameters that this node exposes.
        }}

        public JobHandle Schedule(NodeExecutionContext context)
        {{
            // Implement: Read any required channels from the context, schedule your jobs, and return the final dependency handle.
            return default(JobHandle);
        }}
    }}
}}
";
        }

        private static bool IsValidClassName(string className)
        {
            if (string.IsNullOrWhiteSpace(className))
            {
                return false;
            }

            char firstCharacter = className[0];
            if (!char.IsLetter(firstCharacter) && firstCharacter != '_')
            {
                return false;
            }

            int index;
            for (index = 1; index < className.Length; index++)
            {
                char currentCharacter = className[index];
                if (!char.IsLetterOrDigit(currentCharacter) && currentCharacter != '_')
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class CustomNodeScriptNameEditAction : EndNameEditAction
        {
            public override void Action(int instanceId, string pathName, string resourceFile)
            {
                string normalisedPath = (pathName ?? string.Empty).Replace("\\", "/");
                string className = Path.GetFileNameWithoutExtension(normalisedPath);
                if (!IsValidClassName(className))
                {
                    EditorUtility.DisplayDialog("Custom Node Script", "The script name must be a valid C# class name.", "OK");
                    return;
                }

                string absolutePath = Path.Combine(Directory.GetCurrentDirectory(), normalisedPath);
                string scriptContents = BuildScriptContents(className);
                File.WriteAllText(absolutePath, scriptContents, new UTF8Encoding(false));
                AssetDatabase.ImportAsset(normalisedPath);

                MonoScript createdScript = AssetDatabase.LoadAssetAtPath<MonoScript>(normalisedPath);
                if (createdScript != null)
                {
                    ProjectWindowUtil.ShowCreatedAsset(createdScript);
                }
            }
        }
    }
}
