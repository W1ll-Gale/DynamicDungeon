using System;
using UnityEngine;

[Serializable]
public sealed class FloatLayer : GenLayer
{
    [SerializeField] private float[] _rawData;

    public float[,] Data
    {
        get
        {
            float[,] result = new float[Width, Height];
            Buffer.BlockCopy(_rawData, 0, result, 0, _rawData.Length * sizeof(float));
            return result;
        }
    }

    public FloatLayer(string layerName, int width, int height) : base(layerName, width, height)
    {
        _rawData = new float[width * height];
    }

    public FloatLayer(string layerName, float[,] data) : base(layerName, data.GetLength(0), data.GetLength(1))
    {
        _rawData = new float[Width * Height];
        Buffer.BlockCopy(data, 0, _rawData, 0, _rawData.Length * sizeof(float));
    }

    public float GetValue(int x, int y) => _rawData[y * Width + x];

    public void SetValue(int x, int y, float value) => _rawData[y * Width + x] = value;

    public override GenLayer Clone()
    {
        float[] clonedRaw = new float[_rawData.Length];
        Array.Copy(_rawData, clonedRaw, _rawData.Length);

        FloatLayer clone = new FloatLayer(LayerName, Width, Height);
        clone._rawData = clonedRaw;
        return clone;
    }

    public override float[,] ToNormalizedFloats()
    {
        float min = float.MaxValue;
        float max = float.MinValue;

        for (int i = 0; i < _rawData.Length; i++)
        {
            if (_rawData[i] < min) min = _rawData[i];
            if (_rawData[i] > max) max = _rawData[i];
        }

        float range = max - min;
        float[,] out2D = new float[Width, Height];

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                float raw = _rawData[y * Width + x];
                out2D[x, y] = (range > Mathf.Epsilon) ? (raw - min) / range : 0f;
            }
        }
        return out2D;
    }
}
