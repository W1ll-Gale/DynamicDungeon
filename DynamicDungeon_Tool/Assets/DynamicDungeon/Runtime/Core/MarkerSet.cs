using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class Marker
{
    [SerializeField] private string _label;
    [SerializeField] private Vector2Int _position;
    [SerializeField] private string _value;

    public string Label => _label;
    public Vector2Int Position => _position;
    public string Value => _value;

    public Marker(string label, Vector2Int position, string value = "")
    {
        _label = label ?? string.Empty;
        _position = position;
        _value = value ?? string.Empty;
    }

    public Marker Clone() => new Marker(_label, _position, _value);
}

[Serializable]
public sealed class MarkerSet
{
    [SerializeField] private string _setName;
    [SerializeField] private List<Marker> _markers = new List<Marker>();

    public string SetName => _setName;
    public IReadOnlyList<Marker> Markers => _markers;

    public MarkerSet(string setName)
    {
        _setName = setName ?? string.Empty;
    }

    public void Add(Marker marker)
    {
        if (marker == null) throw new ArgumentNullException(nameof(marker));
        _markers.Add(marker);
    }

    public void Add(string label, Vector2Int position, string value = "")
        => _markers.Add(new Marker(label, position, value));

    public void Clear() => _markers.Clear();

    public MarkerSet Clone()
    {
        MarkerSet clone = new MarkerSet(_setName);
        for (int index = 0; index < _markers.Count; index++)
            clone._markers.Add(_markers[index]?.Clone());
        return clone;
    }
}
