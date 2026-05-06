using DynamicDungeon.Editor.Windows;
using NUnit.Framework;
using UnityEngine;

namespace DynamicDungeon.Tests.Editor
{
    public sealed class DynamicDungeonGraphViewExpandedPreviewTests
    {
        [Test]
        public void OpenExpandedPreviewShowsOverlayAndRequestsFit()
        {
            DynamicDungeonGraphView graphView = new DynamicDungeonGraphView();
            Texture2D texture = CreateTexture(400, 200);

            try
            {
                graphView.OpenExpandedPreviewForTesting("node-a", texture, "Preview A");

                Assert.That(graphView.IsExpandedPreviewVisibleForTesting, Is.True);
                Assert.That(graphView.ExpandedPreviewNodeIdForTesting, Is.EqualTo("node-a"));
                Assert.That(graphView.ExpandedPreviewNeedsFitForTesting, Is.True);
                Assert.That(
                    DynamicDungeonGraphView.CalculateExpandedPreviewFitZoom(800.0f, 600.0f, texture.width, texture.height),
                    Is.EqualTo(2.0f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void RefreshingCurrentExpandedPreviewPreservesZoomAndPan()
        {
            DynamicDungeonGraphView graphView = new DynamicDungeonGraphView();
            Texture2D initialTexture = CreateTexture(128, 128);
            Texture2D refreshedTexture = CreateTexture(256, 64);

            try
            {
                graphView.OpenExpandedPreviewForTesting("node-a", initialTexture, "Preview A");
                graphView.SetExpandedPreviewTransformStateForTesting(2.25f, new Vector2(32.0f, -14.0f), false);

                graphView.UpdateExpandedPreviewForCurrentNodeForTesting("node-a", refreshedTexture, "Preview A");

                Assert.That(graphView.IsExpandedPreviewVisibleForTesting, Is.True);
                Assert.That(graphView.ExpandedPreviewZoomForTesting, Is.EqualTo(2.25f).Within(0.0001f));
                Assert.That(graphView.ExpandedPreviewPanOffsetForTesting, Is.EqualTo(new Vector2(32.0f, -14.0f)));
                Assert.That(graphView.ExpandedPreviewNeedsFitForTesting, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(initialTexture);
                Object.DestroyImmediate(refreshedTexture);
            }
        }

        [Test]
        public void ClearingCurrentExpandedPreviewHidesOverlay()
        {
            DynamicDungeonGraphView graphView = new DynamicDungeonGraphView();
            Texture2D texture = CreateTexture(128, 128);

            try
            {
                graphView.OpenExpandedPreviewForTesting("node-a", texture, "Preview A");

                graphView.UpdateExpandedPreviewForCurrentNodeForTesting("node-a", null, "Preview A");

                Assert.That(graphView.IsExpandedPreviewVisibleForTesting, Is.False);
                Assert.That(graphView.ExpandedPreviewNodeIdForTesting, Is.Null);
                Assert.That(graphView.ExpandedPreviewNeedsFitForTesting, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        private static Texture2D CreateTexture(int width, int height)
        {
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            return texture;
        }
    }
}
