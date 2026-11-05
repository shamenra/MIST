using System;
using System.IO;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace NexusMods.App.UI.Controls.GameWidget;

/// <summary>
/// Renders a square game-sidebar tile from Steam's local wide logo and header/hero.
/// This is an in-memory transformation: no writes to Steam, games, or disk caches.
/// </summary>
internal static class MISTSteamSidebarIconRenderer
{
    internal static Bitmap Render(string logoPath, string? backdropPath, int size)
    {
        if (size < 16 || size > 512)
            throw new ArgumentOutOfRangeException(nameof(size));

        using var logo = SKBitmap.Decode(logoPath)
            ?? throw new InvalidDataException($"Steam logo could not be decoded: {logoPath}");

        // Very large/malformed images should not become unbounded UI allocations.
        if (logo.Width < 1 || logo.Height < 1 || logo.Width > 8192 || logo.Height > 8192)
            throw new InvalidDataException("Steam logo dimensions are invalid");

        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Failed to allocate Steam sidebar icon surface");
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(17, 19, 27)); // M.I.S.T. dark UI backdrop.

        if (!string.IsNullOrEmpty(backdropPath))
        {
            using var backdrop = SKBitmap.Decode(backdropPath);
            if (backdrop is not null && backdrop.Width > 0 && backdrop.Height > 0 &&
                backdrop.Width <= 8192 && backdrop.Height <= 8192)
            {
                // Center-crop Steam's wide header into our square button.
                float cropSide = Math.Min(backdrop.Width, backdrop.Height);
                var src = new SKRect(
                    (backdrop.Width - cropSide) / 2f,
                    (backdrop.Height - cropSide) / 2f,
                    (backdrop.Width + cropSide) / 2f,
                    (backdrop.Height + cropSide) / 2f);
                using var artPaint = new SKPaint { IsAntialias = true };
                canvas.DrawBitmap(backdrop, src, new SKRect(0, 0, size, size), artPaint);
            }
        }

        // Keep light Steam logos legible over busy cover/hero artwork.
        using (var dimPaint = new SKPaint { Color = new SKColor(0, 0, 0, 135) })
            canvas.DrawRect(new SKRect(0, 0, size, size), dimPaint);

        // Fit the full logo into the icon, preserving its original aspect ratio.
        float available = size * 0.90f;
        float scale = Math.Min(available / logo.Width, available / logo.Height);
        float width = logo.Width * scale;
        float height = logo.Height * scale;
        var target = new SKRect(
            (size - width) / 2f, (size - height) / 2f,
            (size + width) / 2f, (size + height) / 2f);

        using (var logoPaint = new SKPaint { IsAntialias = true })
            canvas.DrawBitmap(logo, target, logoPaint);

        canvas.Flush();
        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("Failed to encode Steam sidebar icon");
        using var stream = new MemoryStream();
        encoded.SaveTo(stream);
        stream.Position = 0;
        return new Bitmap(stream);
    }
}
