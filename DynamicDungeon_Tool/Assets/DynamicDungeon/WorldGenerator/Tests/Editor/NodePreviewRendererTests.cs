using DynamicDungeon.Editor.Nodes;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace DynamicDungeon.Tests.Editor
{
    public sealed class NodePreviewRendererTests
    {
        [Test]
        public void RenderPointListChannelDrawsPointsAtExpectedCells()
        {
            Texture2D texture = NodePreviewRenderer.RenderPointListChannel(
                new[]
                {
                    new int2(1, 0),
                    new int2(2, 2)
                },
                4,
                3);

            try
            {
                Assert.That(texture, Is.Not.Null);

                Color32[] pixels = texture.GetPixels32();
                Color32 background = new Color32(24, 24, 24, byte.MaxValue);
                Color32 point = new Color32(217, 51, 128, byte.MaxValue);

                Assert.That(pixels[(0 * 4) + 1], Is.EqualTo(point));
                Assert.That(pixels[(2 * 4) + 2], Is.EqualTo(point));
                Assert.That(pixels[(1 * 4) + 1], Is.EqualTo(background));
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }
}
