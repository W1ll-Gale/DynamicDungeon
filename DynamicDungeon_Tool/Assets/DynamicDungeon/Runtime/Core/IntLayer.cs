using System;
using UnityEngine;

public sealed class IntLayer : GenLayer
{
    [SerializeField] private int[] _rawData;

    public int[,] Data
    {
        get
        {
            int[,] result = new int[Width, Height];
            Buffer.BlockCopy(_rawData, 0, result, 0, _rawData.Length * sizeof(int));
            return result;
        }
    }

    public IntLayer(string layerName, int width, int height) : base(layerName, width, height)
    {
        _rawData = new int[width * height];
    }

    public IntLayer(string layerName, int[,] data) : base(layerName, data.GetLength(0), data.GetLength(1))
    {
        _rawData = new int[Width * Height];
        Buffer.BlockCopy(data, 0, _rawData, 0, _rawData.Length * sizeof(int));
    }

    public int GetValue(int x, int y) => _rawData[y * Width + x];

    public void SetValue(int x, int y, int value) => _rawData[y * Width + x] = value;

    public override GenLayer Clone()
    {
        int[] clonedRaw = new int[_rawData.Length];
        Array.Copy(_rawData, clonedRaw, _rawData.Length);

        IntLayer clone = new IntLayer(LayerName, Width, Height);
        clone._rawData = clonedRaw;
        return clone;
    }

    public override float[,] ToNormalizedFloats()
    {
        int max = int.MinValue;
        int min = int.MaxValue;
        float[,] out2D = new float[Width, Height];

        for (int i = 0; i < _rawData.Length; i++)
        {
            if (_rawData[i] > max) max = _rawData[i];
            if (_rawData[i] < min) min = _rawData[i];
        }

        int range = max - min;

        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                out2D[x, y] = (range > 0) ? (float)(_rawData[y * Width + x] - min) / range : 0f;

        return out2D;
    }
}