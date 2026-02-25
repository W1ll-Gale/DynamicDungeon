using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class GenMap : ICloneable, ISerializationCallbackReceiver
{
    [SerializeField] private int _width;
    [SerializeField] private int _height;
    [SerializeField] private long _seed;
    [SerializeField] private string _latestFloatLayerId;
    [SerializeField] private string _latestIntLayerId;
    [SerializeField] private string _latestMaskLayerId;
    [SerializeField] private string _latestMarkerSetId;

    [SerializeField] private List<FloatLayer> _floatLayerList = new List<FloatLayer>();
    [SerializeField] private List<IntLayer> _intLayerList = new List<IntLayer>();
    [SerializeField] private List<BoolMaskLayer> _maskLayerList = new List<BoolMaskLayer>();
    [SerializeField] private List<MarkerSet> _markerSetList = new List<MarkerSet>();
    [SerializeField] private List<string> _metaKeys = new List<string>();
    [SerializeField] private List<string> _metaValues = new List<string>();

    private Dictionary<string, FloatLayer> _floatLayers;
    private Dictionary<string, IntLayer> _intLayers;
    private Dictionary<string, BoolMaskLayer> _maskLayers;
    private Dictionary<string, MarkerSet> _markerSets;
    private Dictionary<string, string> _metadata;

    public int Width => _width;
    public int Height => _height;
    public long Seed => _seed;

    public IReadOnlyDictionary<string, FloatLayer> FloatLayers => GetFloatDict();
    public IReadOnlyDictionary<string, IntLayer> IntLayers => GetIntDict();
    public IReadOnlyDictionary<string, BoolMaskLayer> MaskLayers => GetMaskDict();
    public IReadOnlyDictionary<string, MarkerSet> MarkerSets => GetMarkerDict();

    public GenMap(int width, int height, long seed = 0L)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        _width = width;
        _height = height;
        _seed = seed;

        _floatLayers = new Dictionary<string, FloatLayer>(StringComparer.Ordinal);
        _intLayers = new Dictionary<string, IntLayer>(StringComparer.Ordinal);
        _maskLayers = new Dictionary<string, BoolMaskLayer>(StringComparer.Ordinal);
        _markerSets = new Dictionary<string, MarkerSet>(StringComparer.Ordinal);
        _metadata = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public void SetFloatLayer(string name, float[,] data)
    {
        ValidateDimensions(data.GetLength(0), data.GetLength(1), name);
        GetFloatDict()[name] = new FloatLayer(name, data);
        _latestFloatLayerId = name;
    }

    public void SetFloatLayer(GraphLayerReference reference, float[,] data)
    {
        string layerId = GetRequiredLayerId(reference);
        ValidateDimensions(data.GetLength(0), data.GetLength(1), layerId);
        GetFloatDict()[layerId] = new FloatLayer(layerId, data);
        _latestFloatLayerId = layerId;
    }

    public void SetFloatLayer(FloatLayer layer)
    {
        if (layer == null) throw new ArgumentNullException(nameof(layer));
        ValidateDimensions(layer.Width, layer.Height, layer.LayerName);
        GetFloatDict()[layer.LayerName] = layer;
        _latestFloatLayerId = layer.LayerName;
    }

    public void SetFloatLayer(GraphLayerReference reference, FloatLayer layer)
    {
        if (layer == null) throw new ArgumentNullException(nameof(layer));

        string layerId = GetRequiredLayerId(reference);
        ValidateDimensions(layer.Width, layer.Height, layerId);
        GetFloatDict()[layerId] = new FloatLayer(layerId, layer.Data);
        _latestFloatLayerId = layerId;
    }

    public bool TryGetFloatLayer(string name, out FloatLayer layer)
        => GetFloatDict().TryGetValue(name, out layer);

    public bool TryGetFloatLayer(GraphLayerReference reference, out FloatLayer layer)
    {
        layer = null;
        return TryGetLayer(reference, GetFloatDict(), out layer);
    }

    public FloatLayer GetFloatLayer(string name)
    {
        if (TryGetFloatLayer(name, out FloatLayer layer)) return layer;
        throw new KeyNotFoundException(
            $"[GenMap] Float layer '{name}' not found. Available: [{string.Join(", ", GetFloatDict().Keys)}]");
    }

    public bool HasFloatLayer(string name) => GetFloatDict().ContainsKey(name);
    public bool HasFloatLayer(GraphLayerReference reference)
        => TryGetLayer(reference, GetFloatDict(), out FloatLayer _);
    public void RemoveFloatLayer(string name)
    {
        if (GetFloatDict().Remove(name) && _latestFloatLayerId == name)
            _latestFloatLayerId = ResolveFallbackLayerId(GetFloatDict());
    }

    public bool TryGetLatestFloatLayer(out FloatLayer layer, out string layerId)
        => TryGetLatestLayer(GetFloatDict(), ref _latestFloatLayerId, out layer, out layerId);

    public void SetIntLayer(string name, int[,] data)
    {
        ValidateDimensions(data.GetLength(0), data.GetLength(1), name);
        GetIntDict()[name] = new IntLayer(name, data);
        _latestIntLayerId = name;
    }

    public void SetIntLayer(GraphLayerReference reference, int[,] data)
    {
        string layerId = GetRequiredLayerId(reference);
        ValidateDimensions(data.GetLength(0), data.GetLength(1), layerId);
        GetIntDict()[layerId] = new IntLayer(layerId, data);
        _latestIntLayerId = layerId;
    }

    public void SetIntLayer(IntLayer layer)
    {
        if (layer == null) throw new ArgumentNullException(nameof(layer));
        ValidateDimensions(layer.Width, layer.Height, layer.LayerName);
        GetIntDict()[layer.LayerName] = layer;
        _latestIntLayerId = layer.LayerName;
    }

    public void SetIntLayer(GraphLayerReference reference, IntLayer layer)
    {
        if (layer == null) throw new ArgumentNullException(nameof(layer));

        string layerId = GetRequiredLayerId(reference);
        ValidateDimensions(layer.Width, layer.Height, layerId);
        GetIntDict()[layerId] = new IntLayer(layerId, layer.Data);
        _latestIntLayerId = layerId;
    }

    public bool TryGetIntLayer(string name, out IntLayer layer)
        => GetIntDict().TryGetValue(name, out layer);

    public bool TryGetIntLayer(GraphLayerReference reference, out IntLayer layer)
    {
        layer = null;
        return TryGetLayer(reference, GetIntDict(), out layer);
    }

    public IntLayer GetIntLayer(string name)
    {
        if (TryGetIntLayer(name, out IntLayer layer)) return layer;
        throw new KeyNotFoundException(
            $"[GenMap] Int layer '{name}' not found. Available: [{string.Join(", ", GetIntDict().Keys)}]");
    }

    public bool HasIntLayer(string name) => GetIntDict().ContainsKey(name);
    public bool HasIntLayer(GraphLayerReference reference)
        => TryGetLayer(reference, GetIntDict(), out IntLayer _);
    public void RemoveIntLayer(string name)
    {
        if (GetIntDict().Remove(name) && _latestIntLayerId == name)
            _latestIntLayerId = ResolveFallbackLayerId(GetIntDict());
    }

    public bool TryGetLatestIntLayer(out IntLayer layer, out string layerId)
        => TryGetLatestLayer(GetIntDict(), ref _latestIntLayerId, out layer, out layerId);

    public void SetMaskLayer(string name, bool[,] data)
    {
        ValidateDimensions(data.GetLength(0), data.GetLength(1), name);
        GetMaskDict()[name] = new BoolMaskLayer(name, data);
        _latestMaskLayerId = name;
    }

    public void SetMaskLayer(GraphLayerReference reference, bool[,] data)
    {
        string layerId = GetRequiredLayerId(reference);
        ValidateDimensions(data.GetLength(0), data.GetLength(1), layerId);
        GetMaskDict()[layerId] = new BoolMaskLayer(layerId, data);
        _latestMaskLayerId = layerId;
    }

    public void SetMaskLayer(BoolMaskLayer layer)
    {
        if (layer == null) throw new ArgumentNullException(nameof(layer));
        ValidateDimensions(layer.Width, layer.Height, layer.LayerName);
        GetMaskDict()[layer.LayerName] = layer;
        _latestMaskLayerId = layer.LayerName;
    }

    public void SetMaskLayer(GraphLayerReference reference, BoolMaskLayer layer)
    {
        if (layer == null) throw new ArgumentNullException(nameof(layer));

        string layerId = GetRequiredLayerId(reference);
        ValidateDimensions(layer.Width, layer.Height, layerId);
        GetMaskDict()[layerId] = new BoolMaskLayer(layerId, layer.Data);
        _latestMaskLayerId = layerId;
    }

    public bool TryGetMaskLayer(string name, out BoolMaskLayer layer)
        => GetMaskDict().TryGetValue(name, out layer);

    public bool TryGetMaskLayer(GraphLayerReference reference, out BoolMaskLayer layer)
    {
        layer = null;
        return TryGetLayer(reference, GetMaskDict(), out layer);
    }

    public BoolMaskLayer GetMaskLayer(string name)
    {
        if (TryGetMaskLayer(name, out BoolMaskLayer layer)) return layer;
        throw new KeyNotFoundException(
            $"[GenMap] Mask layer '{name}' not found. Available: [{string.Join(", ", GetMaskDict().Keys)}]");
    }

    public bool HasMaskLayer(string name) => GetMaskDict().ContainsKey(name);
    public bool HasMaskLayer(GraphLayerReference reference)
        => TryGetLayer(reference, GetMaskDict(), out BoolMaskLayer _);
    public void RemoveMaskLayer(string name)
    {
        if (GetMaskDict().Remove(name) && _latestMaskLayerId == name)
            _latestMaskLayerId = ResolveFallbackLayerId(GetMaskDict());
    }

    public bool TryGetLatestMaskLayer(out BoolMaskLayer layer, out string layerId)
        => TryGetLatestLayer(GetMaskDict(), ref _latestMaskLayerId, out layer, out layerId);

    public void SetMarkerSet(MarkerSet set)
    {
        if (set == null) throw new ArgumentNullException(nameof(set));
        if (string.IsNullOrWhiteSpace(set.SetName))
            throw new ArgumentException("Marker set must have a name.", nameof(set));

        GetMarkerDict()[set.SetName] = set;
        _latestMarkerSetId = set.SetName;
    }

    public bool TryGetMarkerSet(string name, out MarkerSet set)
        => GetMarkerDict().TryGetValue(name, out set);

    public MarkerSet GetMarkerSet(string name)
    {
        if (TryGetMarkerSet(name, out MarkerSet set)) return set;
        throw new KeyNotFoundException(
            $"[GenMap] Marker set '{name}' not found. Available: [{string.Join(", ", GetMarkerDict().Keys)}]");
    }

    public bool HasMarkerSet(string name) => GetMarkerDict().ContainsKey(name);
    public void RemoveMarkerSet(string name)
    {
        if (GetMarkerDict().Remove(name) && _latestMarkerSetId == name)
            _latestMarkerSetId = ResolveFallbackLayerId(GetMarkerDict());
    }

    public void SetMetadata(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException(nameof(key));
        GetMetaDict()[key] = value ?? string.Empty;
    }

    public bool TryGetMetadata(string key, out string value)
        => GetMetaDict().TryGetValue(key, out value);

    public string GetMetadata(string key, string defaultValue = "")
        => TryGetMetadata(key, out string value) ? value : defaultValue;

    public GenMap Clone()
    {
        GenMap clone = new GenMap(_width, _height, _seed);
        clone._latestFloatLayerId = _latestFloatLayerId;
        clone._latestIntLayerId = _latestIntLayerId;
        clone._latestMaskLayerId = _latestMaskLayerId;
        clone._latestMarkerSetId = _latestMarkerSetId;

        foreach (KeyValuePair<string, FloatLayer> pair in GetFloatDict())
            clone.GetFloatDict()[pair.Key] = (FloatLayer)pair.Value.Clone();

        foreach (KeyValuePair<string, IntLayer> pair in GetIntDict())
            clone.GetIntDict()[pair.Key] = (IntLayer)pair.Value.Clone();

        foreach (KeyValuePair<string, BoolMaskLayer> pair in GetMaskDict())
            clone.GetMaskDict()[pair.Key] = (BoolMaskLayer)pair.Value.Clone();

        foreach (KeyValuePair<string, MarkerSet> pair in GetMarkerDict())
            clone.GetMarkerDict()[pair.Key] = pair.Value.Clone();

        foreach (KeyValuePair<string, string> pair in GetMetaDict())
            clone.GetMetaDict()[pair.Key] = pair.Value;

        return clone;
    }

    object ICloneable.Clone() => Clone();

    public override string ToString()
    {
        return $"GenMap [{_width}x{_height}] Seed={_seed} " +
               $"FloatLayers=[{string.Join(", ", GetFloatDict().Keys)}] " +
               $"IntLayers=[{string.Join(", ", GetIntDict().Keys)}] " +
               $"Masks=[{string.Join(", ", GetMaskDict().Keys)}] " +
               $"Markers=[{string.Join(", ", GetMarkerDict().Keys)}]";
    }

    public void OnBeforeSerialize()
    {
        SyncList(_floatLayerList, GetFloatDict().Values);
        SyncList(_intLayerList, GetIntDict().Values);
        SyncList(_maskLayerList, GetMaskDict().Values);
        SyncList(_markerSetList, GetMarkerDict().Values);

        _metaKeys.Clear();
        _metaValues.Clear();
        foreach (KeyValuePair<string, string> pair in GetMetaDict())
        {
            _metaKeys.Add(pair.Key);
            _metaValues.Add(pair.Value);
        }
    }

    public void OnAfterDeserialize()
    {
        _floatLayers = null;
        _intLayers = null;
        _maskLayers = null;
        _markerSets = null;
        _metadata = null;
    }

    private Dictionary<string, FloatLayer> GetFloatDict()
    {
        if (_floatLayers != null) return _floatLayers;

        _floatLayers = new Dictionary<string, FloatLayer>(StringComparer.Ordinal);
        foreach (FloatLayer layer in _floatLayerList)
            if (layer != null) _floatLayers[layer.LayerName] = layer;
        return _floatLayers;
    }

    private Dictionary<string, IntLayer> GetIntDict()
    {
        if (_intLayers != null) return _intLayers;

        _intLayers = new Dictionary<string, IntLayer>(StringComparer.Ordinal);
        foreach (IntLayer layer in _intLayerList)
            if (layer != null) _intLayers[layer.LayerName] = layer;
        return _intLayers;
    }

    private Dictionary<string, BoolMaskLayer> GetMaskDict()
    {
        if (_maskLayers != null) return _maskLayers;

        _maskLayers = new Dictionary<string, BoolMaskLayer>(StringComparer.Ordinal);
        foreach (BoolMaskLayer layer in _maskLayerList)
            if (layer != null) _maskLayers[layer.LayerName] = layer;
        return _maskLayers;
    }

    private Dictionary<string, MarkerSet> GetMarkerDict()
    {
        if (_markerSets != null) return _markerSets;

        _markerSets = new Dictionary<string, MarkerSet>(StringComparer.Ordinal);
        foreach (MarkerSet set in _markerSetList)
        {
            if (set != null && !string.IsNullOrWhiteSpace(set.SetName))
                _markerSets[set.SetName] = set;
        }
        return _markerSets;
    }

    private Dictionary<string, string> GetMetaDict()
    {
        if (_metadata != null) return _metadata;

        _metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        int count = Mathf.Min(_metaKeys.Count, _metaValues.Count);
        for (int index = 0; index < count; index++)
            _metadata[_metaKeys[index]] = _metaValues[index];
        return _metadata;
    }

    private void ValidateDimensions(int width, int height, string layerName)
    {
        if (width != _width || height != _height)
        {
            throw new ArgumentException(
                $"[GenMap] Layer '{layerName}' dimensions ({width}x{height}) do not match map dimensions ({_width}x{_height}).");
        }
    }

    private static string GetRequiredLayerId(GraphLayerReference reference)
    {
        if (!reference.IsAssigned) throw new ArgumentException("Layer reference is not assigned.", nameof(reference));
        return reference.LayerId;
    }

    private static bool TryGetLayer<TLayer>(
        GraphLayerReference reference,
        IReadOnlyDictionary<string, TLayer> layers,
        out TLayer layer)
    {
        layer = default;

        if (!reference.IsAssigned)
            return false;

        return layers.TryGetValue(reference.LayerId, out layer);
    }

    private static bool TryGetLatestLayer<TLayer>(
        IReadOnlyDictionary<string, TLayer> layers,
        ref string latestLayerId,
        out TLayer layer,
        out string layerId)
    {
        layer = default;
        layerId = latestLayerId;

        if (!string.IsNullOrWhiteSpace(latestLayerId) && layers.TryGetValue(latestLayerId, out layer))
            return true;

        foreach (KeyValuePair<string, TLayer> pair in layers)
        {
            latestLayerId = pair.Key;
            layerId = pair.Key;
            layer = pair.Value;
            return true;
        }

        latestLayerId = string.Empty;
        layerId = string.Empty;
        return false;
    }

    private static string ResolveFallbackLayerId<TLayer>(IReadOnlyDictionary<string, TLayer> layers)
    {
        foreach (KeyValuePair<string, TLayer> pair in layers)
            return pair.Key;

        return string.Empty;
    }

    private static void SyncList<T>(List<T> list, IEnumerable<T> values)
    {
        list.Clear();
        foreach (T value in values)
            list.Add(value);
    }
}
