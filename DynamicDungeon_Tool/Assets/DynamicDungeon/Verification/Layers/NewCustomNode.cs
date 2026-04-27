using System;
using UnityEngine;
using DynamicDungeon.Runtime.Nodes;
using DynamicDungeon.Runtime.Graph;

public class TestCompiler
{
    public static void RunTest()
    {
        var node1 = new HeightBandNode("123", "Height Band");
        var node2 = new BiomeMaskNode("456", "Biome Mask");
        Debug.Log("Height Band Channel: " + node1.ChannelDeclarations[0].ChannelName);
    }
}
