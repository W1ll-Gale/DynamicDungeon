using UnityEngine;

namespace DynamicDungeon.ConstraintDungeon
{
    [CreateAssetMenu(fileName = "New Socket Type", menuName = ConstraintDungeonMenuPaths.SocketTypeAssetMenu)]
    public class SocketTypeAsset : ScriptableObject
    {

        [Tooltip("Colour used to display this door in the editor.")]
        public Color gizmoColor = Color.cyan;

        public string SocketName => name;
    }
}
