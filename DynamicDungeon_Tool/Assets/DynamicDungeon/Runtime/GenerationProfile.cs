using UnityEngine;

/// <summary>
/// Defines global rules for the dungeon generation simulation, such as gravity and physics iterations.
/// </summary>
[CreateAssetMenu(fileName = "NewGenerationProfile", menuName = "DynamicDungeon/Generation Profile")]
public class GenerationProfile : ScriptableObject
{
    [Header("Physics Settings")]
    /// <summary>
    /// The direction gravity pulls affects tiles (e.g., (0, -1) for side-sc    roller, (0, 0) for top-down).
    /// </summary>
    public Vector2Int gravityDirection = new Vector2Int(0, -1);

    /// <summary>
    /// How many times the physics simulation (falling sand, flowing water) runs after the map is generated.
    /// </summary>
    [Range(0, 100)]
    public int physicsIterations = 0;
}