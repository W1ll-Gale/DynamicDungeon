using UnityEngine;

/// <summary>
/// Abstract base class for a single step in the dungeon generation pipeline.
/// </summary>
public abstract class GenerationPass : ScriptableObject
{
    [Header("Pass Settings")]
    public bool enabled = true;

    /// <summary>
    /// Executes this generation step on the provided context.
    /// </summary>
    /// <param name="context">The shared dungeon state.</param>
    public abstract void Execute(DungeonContext context);
}