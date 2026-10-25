using SkiaSharp;

namespace RetailVision.Vision;

public record PreparedPhoto(byte[] Png, int Width, int Height);
public record Letterbox(float[] Tensor, int Width, int Height, int ResizedWidth, int ResizedHeight, int Left, int Top);

public static class PhotoPreprocessor
{
    public const int MaxBytes = 8 * 1024 * 1024;
    public static async Task<PreparedPhoto> PrepareAsync(Stream source, CancellationToken ct)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await source.ReadAsync(buffer, ct)) != 0)
        {
            if (bytes.Length + count > MaxBytes) throw new InvalidDataException("Фото должно быть не больше 8 МБ.");
            await bytes.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        using var data = SKData.CreateCopy(bytes.ToArray());
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Не удалось прочитать изображение.");
        var info = codec.Info;
        if (info.Width < 1 || info.Height < 1 || info.Width > 6000 || info.Height > 6000 || (long)info.Width * info.Height > 12_000_000)
            throw new InvalidDataException("Фото: до 6000 пикселей по стороне и до 12 мегапикселей.");
        if (codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp) || codec.FrameCount > 1)
            throw new InvalidDataException("Выберите неподвижное изображение JPEG, PNG или WebP.");
        using var original = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(original.Info, original.GetPixels()) != SKCodecResult.Success)
            throw new InvalidDataException("Изображение повреждено или загружено не полностью.");
        ct.ThrowIfCancellationRequested();
        var swap = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        using var upright = new SKBitmap(swap ? info.Height : info.Width, swap ? info.Width : info.Height);
        using (var canvas = new SKCanvas(upright))
        {
            canvas.Clear(SKColors.White);
            switch (codec.EncodedOrigin)
            {
                case SKEncodedOrigin.TopRight: canvas.Translate(info.Width, 0); canvas.Scale(-1, 1); break;
                case SKEncodedOrigin.BottomRight: canvas.Translate(info.Width, info.Height); canvas.RotateDegrees(180); break;
                case SKEncodedOrigin.BottomLeft: canvas.Translate(0, info.Height); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.RightTop: canvas.Translate(info.Height, 0); canvas.RotateDegrees(90); break;
                case SKEncodedOrigin.RightBottom: canvas.Translate(info.Height, info.Width); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
                case SKEncodedOrigin.LeftBottom: canvas.Translate(0, info.Width); canvas.RotateDegrees(270); break;
            }
            using var originalImage = SKImage.FromBitmap(original);
            canvas.DrawImage(originalImage, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        }
        var scale = Math.Min(1, 2400.0 / Math.Max(upright.Width, upright.Height));
        using var resized = upright.Resize(new SKImageInfo(Math.Max(1, (int)Math.Round(upright.Width * scale)), Math.Max(1, (int)Math.Round(upright.Height * scale))), new SKSamplingOptions(SKFilterMode.Linear));
        if (resized is null) throw new InvalidDataException("Не удалось уменьшить фото.");
        using var image = SKImage.FromBitmap(resized);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return new(png.ToArray(), resized.Width, resized.Height);
    }

    // Matches the supported export: RGB / 255, NCHW, centered letterbox with 114 padding.
    public static Letterbox ToTensor(byte[] png, int size = 640)
    {
        if (size is < 32 or > 1280 || png.Length > 32 * 1024 * 1024) throw new InvalidDataException("Invalid image or tensor size.");
        using var data = SKData.CreateCopy(png);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Invalid prepared photo.");
        if (codec.Info.Width is < 1 or > 2400 || codec.Info.Height is < 1 or > 2400 || codec.EncodedFormat != SKEncodedImageFormat.Png)
            throw new InvalidDataException("Detector expects a prepared PNG up to 2400 pixels.");
        using var bitmap = SKBitmap.Decode(png) ?? throw new InvalidDataException("Invalid prepared photo.");
        var scale = Math.Min((double)size / bitmap.Width, (double)size / bitmap.Height);
        var w = Math.Max(1, (int)Math.Round(bitmap.Width * scale)); var h = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
        var left = (size - w) / 2; var top = (size - h) / 2;
        using var resized = bitmap.Resize(new SKImageInfo(w, h), new SKSamplingOptions(SKFilterMode.Linear)) ?? throw new InvalidDataException("Resize failed.");
        var tensor = new float[3 * size * size]; Array.Fill(tensor, 114f / 255);
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++)
        {
            var pixel = resized.GetPixel(x, y); var index = (y + top) * size + x + left;
            tensor[index] = pixel.Red / 255f; tensor[index + size * size] = pixel.Green / 255f; tensor[index + 2 * size * size] = pixel.Blue / 255f;
        }
        return new(tensor, bitmap.Width, bitmap.Height, w, h, left, top);
    }
}
