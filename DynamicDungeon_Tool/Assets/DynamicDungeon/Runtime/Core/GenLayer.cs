using System;
using UnityEngine;

[Serializable]
public abstract class GenLayer
{
    [SerializeField] private string _layerName;
    [SerializeField] private int _width;
    [SerializeField] private int _height;

    public string LayerName => _layerName;
    public int Width => _width;
    public int Height => _height;

    protected GenLayer(string layerName, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(layerName)) throw new ArgumentException("Layer name cannot be null or whitespace.", nameof(layerName));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Width must be > 0.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "Height must be > 0.");

        _layerName = layerName;
        _width = width;
        _height = height;
    }

    public abstract GenLayer Clone();

    public abstract float[,] ToNormalizedFloats();
}