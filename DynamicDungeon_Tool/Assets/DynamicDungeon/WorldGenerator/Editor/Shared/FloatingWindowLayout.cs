using System;
using UnityEngine;

namespace DynamicDungeon.Editor.Shared
{
    [Serializable]
    public sealed class FloatingWindowLayout
    {
        public bool DockToLeft = true;
        public bool DockToTop = true;
        public float HorizontalOffset = 8.0f;
        public float VerticalOffset = 8.0f;
        public Vector2 Size = new Vector2(280.0f, 420.0f);

        public Rect GetRect(Rect parentRect)
        {
            float safeWidth = Mathf.Max(120.0f, Size.x);
            float safeHeight = Mathf.Max(120.0f, Size.y);

            float maxX = Mathf.Max(0.0f, parentRect.width - safeWidth);
            float maxY = Mathf.Max(0.0f, parentRect.height - safeHeight);

            float x = DockToLeft
                ? Mathf.Clamp(HorizontalOffset, 0.0f, maxX)
                : Mathf.Clamp(parentRect.width - safeWidth - HorizontalOffset, 0.0f, maxX);
            float y = DockToTop
                ? Mathf.Clamp(VerticalOffset, 0.0f, maxY)
                : Mathf.Clamp(parentRect.height - safeHeight - VerticalOffset, 0.0f, maxY);

            return new Rect(x, y, safeWidth, safeHeight);
        }

        public void Capture(Rect windowRect, Rect parentRect)
        {
            float leftDistance = Mathf.Abs(windowRect.xMin);
            float rightDistance = Mathf.Abs(parentRect.width - windowRect.xMax);
            float topDistance = Mathf.Abs(windowRect.yMin);
            float bottomDistance = Mathf.Abs(parentRect.height - windowRect.yMax);

            DockToLeft = leftDistance <= rightDistance;
            DockToTop = topDistance <= bottomDistance;
            HorizontalOffset = DockToLeft ? windowRect.xMin : parentRect.width - windowRect.xMax;
            VerticalOffset = DockToTop ? windowRect.yMin : parentRect.height - windowRect.yMax;
            Size = new Vector2(windowRect.width, windowRect.height);

            ClampToParent(parentRect);
        }

        public void ClampToParent(Rect parentRect)
        {
            Size = new Vector2(
                Mathf.Clamp(Size.x, 120.0f, Mathf.Max(120.0f, parentRect.width)),
                Mathf.Clamp(Size.y, 120.0f, Mathf.Max(120.0f, parentRect.height)));

            float maxHorizontalOffset = Mathf.Max(0.0f, parentRect.width - Size.x);
            float maxVerticalOffset = Mathf.Max(0.0f, parentRect.height - Size.y);
            HorizontalOffset = Mathf.Clamp(HorizontalOffset, 0.0f, maxHorizontalOffset);
            VerticalOffset = Mathf.Clamp(VerticalOffset, 0.0f, maxVerticalOffset);
        }
    }
}
