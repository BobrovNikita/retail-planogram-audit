using System.Text.RegularExpressions;
using SkiaSharp;

namespace RetailVision.Infrastructure;

public sealed class ProductImageStore(string dataDirectory) : IDisposable
{
    public const int MaxBytes = 4 * 1024 * 1024;
    private readonly string directory = Path.Combine(dataDirectory, "product-images");
    private readonly SemaphoreSlim gate = new(2);

    public async Task<string> SaveAsync(Stream input, CancellationToken ct)
    {
        // Reject excess work instead of retaining an unbounded queue of upload buffers.
        if (!await gate.WaitAsync(0, ct)) throw new InvalidDataException("Обработка изображений занята. Повторите загрузку позже.");
        string? path = null;
        try
        {
            using var bytes = new MemoryStream();
            var buffer = new byte[81920];
            int count;
            while ((count = await input.ReadAsync(buffer, ct)) != 0)
            {
                if (bytes.Length + count > MaxBytes) throw new InvalidDataException("Изображение не должно превышать 4 МБ.");
                await bytes.WriteAsync(buffer.AsMemory(0, count), ct);
            }
            using var data = SKData.CreateCopy(bytes.ToArray());
            using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Файл не является поддерживаемым изображением.");
            var info = codec.Info;
            if (info.Width < 1 || info.Height < 1 || info.Width > 4096 || info.Height > 4096 || (long)info.Width * info.Height > 4_000_000)
                throw new InvalidDataException("Изображение: до 4096 пикселей по стороне и до 4 мегапикселей.");
            if (codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Webp))
                throw new InvalidDataException("Разрешены только PNG, JPEG и WebP.");
            if (codec.FrameCount > 1) throw new InvalidDataException("Анимированные изображения не поддерживаются.");
            using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
                throw new InvalidDataException("Изображение повреждено или загружено не полностью.");
            using var oriented = Orient(bitmap, codec.EncodedOrigin);
            var scale = Math.Min(1.0, 1200.0 / Math.Max(oriented.Width, oriented.Height));
            using var thumbnail = oriented.Resize(new SKImageInfo(Math.Max(1, (int)(oriented.Width * scale)), Math.Max(1, (int)(oriented.Height * scale))), new SKSamplingOptions(SKFilterMode.Linear))
                ?? throw new InvalidDataException("Не удалось обработать изображение.");
            using var image = SKImage.FromBitmap(thumbnail);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            Directory.CreateDirectory(directory);
            var key = Guid.NewGuid().ToString("N") + ".png";
            path = Path.Combine(directory, key);
            await File.WriteAllBytesAsync(path, png.ToArray(), ct);
            return key;
        }
        catch
        {
            if (path is not null) File.Delete(path);
            throw;
        }
        finally { gate.Release(); }
    }

    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swap ? source.Height : source.Width, swap ? source.Width : source.Height);
        using var canvas = new SKCanvas(result);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: canvas.Translate(source.Width, 0); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.BottomRight: canvas.Translate(source.Width, source.Height); canvas.RotateDegrees(180); break;
            case SKEncodedOrigin.BottomLeft: canvas.Translate(0, source.Height); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(source.Height, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(source.Height, source.Width); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, source.Width); canvas.RotateDegrees(270); break;
        }
        using var image = SKImage.FromBitmap(source);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        return result;
    }

    public string? Resolve(string? key) => key is not null && Regex.IsMatch(key, "^[a-f0-9]{32}\\.png$")
        ? Path.Combine(directory, key) : null;
    public void Delete(string? key)
    {
        var path = Resolve(key);
        if (path is not null) File.Delete(path);
    }
    public void Dispose() => gate.Dispose();
}
