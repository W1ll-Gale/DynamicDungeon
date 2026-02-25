using UnityEngine;

public abstract class GenModule : ScriptableObject
{
    [Header("Pipeline Routing")]
    public bool enabled = true;

    [Tooltip("The name of the layer to read data FROM (if applicable).")]
    public string inputLayer = "Main";

    [Tooltip("The name of the layer to write results TO.")]
    public string outputLayer = "Main";

    public abstract void Execute(GenMap map);
}