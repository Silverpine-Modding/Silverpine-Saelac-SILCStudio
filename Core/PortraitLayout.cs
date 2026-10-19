using System.Drawing;

namespace SilverpineSilcEditor.Core;

// Silverpine 1.7.3 level0: FakeDialogBox/Root and DialogBox/Root.
// See Assets/PortraitGuides/README.md for source objects and extraction details.
internal static class PortraitLayout
{
    internal const float CanvasWidth = 1436;
    internal static float CanvasHeight(float aspectRatio) => CanvasWidth / aspectRatio;

    internal static PortraitPlacement Portrait(Size image, float canvasHeight,
        float offsetX, float offsetY, float scale, bool npc)
    {
        // CustomNPCManager mirrors the stored X offset as well as the image.
        var origin = npc ? new PointF(225.5f + 177 - offsetX, 449.7373046875f + offsetY)
            : new PointF(1286.199951171875f + offsetX, 455.7373046875f + offsetY);
        var pivot = npc ? new PointF(.3330457806587219f, .8315349221229553f)
            : new PointF(.5809476375579834f, .835785448551178f);
        return Place(image, new SizeF(679, 1347), origin, pivot, canvasHeight, scale, npc);
    }

    internal static PortraitPlacement Silhouette(Size image, float canvasHeight) => Place(
        image, new SizeF(396.7850036621094f, 787.1423950195312f),
        new PointF(134.14994049072266f, 601.5373077392578f),
        new PointF(.3330457806587219f, .8315349221229553f), canvasHeight, 1, false);

    // This is a fixed-height bottom-anchored overlay, not a generic thirds grid.
    internal static RectangleF Guides(float canvasHeight) => new(0, canvasHeight - 806.9999694824219f, CanvasWidth, 807);

    internal static PointF DragOffset(PointF initial, PointF screenDelta, float previewScale, bool npc) =>
        new(initial.X + screenDelta.X / previewScale * (npc ? -1 : 1), initial.Y - screenDelta.Y / previewScale);

    private static PortraitPlacement Place(Size image, SizeF frame, PointF origin,
        PointF pivot, float canvasHeight, float scale, bool mirror)
    {
        // Unity UI Image.PreserveSpriteAspectRatio retains the RectTransform pivot.
        // Fit the ENTIRE PNG, including transparent margins; never crop to opaque pixels.
        float fit = Math.Min(frame.Width / image.Width, frame.Height / image.Height);
        float width = image.Width * fit, height = image.Height * fit;
        float sx = mirror ? -scale : scale;
        float x1 = origin.X - pivot.X * width * sx;
        float x2 = origin.X + (1 - pivot.X) * width * sx;
        float y1 = canvasHeight - (origin.Y + (1 - pivot.Y) * height * scale);
        float y2 = canvasHeight - (origin.Y - pivot.Y * height * scale);
        return new PortraitPlacement(RectangleF.FromLTRB(Math.Min(x1, x2), Math.Min(y1, y2),
            Math.Max(x1, x2), Math.Max(y1, y2)), sx < 0, scale < 0);
    }
}

internal readonly record struct PortraitPlacement(RectangleF Bounds, bool FlipX, bool FlipY);
