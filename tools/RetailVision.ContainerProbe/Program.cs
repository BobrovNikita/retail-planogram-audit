using RetailVision.Core;
using RetailVision.Vision;
using SkiaSharp;

// This executable and synthetic fixture are included ONLY in Docker's verification target.
var manifest = args.Single();
using var bitmap = new SKBitmap(800, 500);
bitmap.Erase(SKColors.Red);
using var image = SKImage.FromBitmap(bitmap);
using var data = image.Encode(SKEncodedImageFormat.Png, 100);
await using var source = new MemoryStream(data.ToArray());
var prepared = await PhotoPreprocessor.PrepareAsync(source, default);
if (prepared.Width != 800 || prepared.Height != 500) throw new InvalidOperationException("SkiaSharp decode/encode failed.");
using var detector = new OnnxProductDetector(manifest, allowSyntheticTestModel: true);
var result = await detector.DetectAsync(new DetectionRequest(Image: prepared.Png));
var detection = result.Products.Single();
if (result.Mode != "TEST" || detection.Sku != "RV-COLA" || detection.Confidence <= .9001 || detection.Confidence >= .91)
    throw new InvalidOperationException("Synthetic ONNX inference failed.");
Console.WriteLine("Container native probe passed: SkiaSharp PNG and input-dependent synthetic ONNX inference. No trained-model accuracy measured.");
