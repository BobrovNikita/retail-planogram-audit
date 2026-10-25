using RetailVision.Core;
using RetailVision.Vision;
using SkiaSharp;

namespace RetailVision.Core.Tests;

public sealed class VisionTests
{
    [Fact]
    public async Task ExifOrientationIsAppliedBeforeCalibration()
    {
        await using var source=File.OpenRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","rotated.jpg"));
        var photo=await PhotoPreprocessor.PrepareAsync(source,default);Assert.Equal((20,40),(photo.Width,photo.Height));
        using var data=SKData.CreateCopy(photo.Png);using var codec=SKCodec.Create(data);Assert.Equal(SKEncodedImageFormat.Png,codec.EncodedFormat);Assert.Equal(SKEncodedOrigin.TopLeft,codec.EncodedOrigin);
    }
    [Theory]
    [InlineData("checksum")]
    [InlineData("mapping")]
    [InlineData("input")]
    [InlineData("contract")]
    [InlineData("filename")]
    [InlineData("size")]
    public void ModelManifestMustMatchBytesGraphAndMetadata(string change)
    {
        var directory=Path.Combine(Path.GetTempPath(),"rv-manifest-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var options=new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
            var model=System.Text.Json.JsonSerializer.Deserialize<ModelManifest>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","manifest.json")),options)!;
            model=change switch {"checksum"=>model with{Sha256=new string('0',64)},"mapping"=>model with{Classes=["OTHER"]},"input"=>model with{InputName="wrong"},"contract"=>model with{Contract="raw-yolo"},"filename"=>model with{ModelFile="../synthetic.onnx"},_=>model with{InputSize=320}};
            File.Copy(Path.Combine(AppContext.BaseDirectory,"Fixtures","synthetic.onnx"),Path.Combine(directory,"synthetic.onnx"));
            var path=Path.Combine(directory,"manifest.json");File.WriteAllText(path,System.Text.Json.JsonSerializer.Serialize(model,options));
            Assert.Throws<InvalidDataException>(()=>new OnnxProductDetector(path,true));
        }
        finally {Directory.Delete(directory,true);}
    }
    private static byte[] Png(int w=800,int h=500)
    {
        using var bitmap=new SKBitmap(w,h);bitmap.Erase(SKColors.Red);
        using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Png,100);return data.ToArray();
    }
    [Fact]
    public void RgbNchwAndLetterboxPaddingAreCorrect()
    {
        var box=PhotoPreprocessor.ToTensor(Png());
        Assert.Equal((640,400,0,120),(box.ResizedWidth,box.ResizedHeight,box.Left,box.Top));
        Assert.Equal(114f/255,box.Tensor[0]);Assert.Equal(1,box.Tensor[200*640+100]);Assert.Equal(0,box.Tensor[640*640+200*640+100]);
        var vertical=PhotoPreprocessor.ToTensor(Png(500,800));Assert.Equal((400,640,120,0),(vertical.ResizedWidth,vertical.ResizedHeight,vertical.Left,vertical.Top));
    }
    [Fact]
    public async Task NativeOnnxInferenceAndInverseLetterboxUseActualInput()
    {
        using var detector=new OnnxProductDetector(Path.Combine(AppContext.BaseDirectory,"Fixtures","manifest.json"),true);
        var result=await detector.DetectAsync(new DetectionRequest(Image:Png()));
        Assert.Equal("TEST",result.Mode);var d=Assert.Single(result.Products);
        Assert.Equal("RV-COLA",d.Sku);Assert.Equal(.1,d.X,5);Assert.Equal(.2,d.Y,5);Assert.Equal(.1,d.Width,5);Assert.Equal(.4,d.Height,5);
        Assert.InRange(d.Confidence,.9001,.91);Assert.Equal(0,d.Shelf);
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>detector.DetectAsync(new DetectionRequest(Image:Png()),cancel.Token));
        await Assert.ThrowsAsync<ArgumentException>(()=>detector.DetectAsync(new("correct")));
        await Assert.ThrowsAsync<ArgumentException>(()=>new FixtureProductDetector().DetectAsync(new("correct",Png())));
    }
    [Fact]
    public void ProductionRejectsSyntheticModels() => Assert.Throws<InvalidDataException>(()=>new OnnxProductDetector(Path.Combine(AppContext.BaseDirectory,"Fixtures","manifest.json")));
    [Theory]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'></svg>")]
    [InlineData("not an image")]
    public async Task UploadRejectsUnsupportedContent(string value)
    { using var input=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(value));await Assert.ThrowsAsync<InvalidDataException>(()=>PhotoPreprocessor.PrepareAsync(input,default)); }
    [Fact]
    public async Task PhotoDimensionsAreBoundedBeforeDecode()
    {
        using var input=new MemoryStream(Png(6001,2));await Assert.ThrowsAsync<InvalidDataException>(()=>PhotoPreprocessor.PrepareAsync(input,default));
        using var valid=new MemoryStream(Png(3000,1000));var prepared=await PhotoPreprocessor.PrepareAsync(valid,default);Assert.Equal((2400,800),(prepared.Width,prepared.Height));
    }
    [Theory]
    [InlineData("nan")]
    [InlineData("class")]
    [InlineData("score")]
    [InlineData("shape")]
    public void InvalidOutputFailsClosed(string issue)
    {
        var output=new float[1800];output[4]=.9f;
        if(issue=="nan")output[0]=float.NaN;if(issue=="class")output[5]=1.5f;if(issue=="score")output[4]=1.1f;if(issue=="shape")output=new float[6];
        Assert.Throws<InvalidDataException>(()=>OnnxProductDetector.Parse(output,PhotoPreprocessor.ToTensor(Png()),new[]{"A"}));
    }
    [Fact]
    public void PaddingBoxesAreDroppedAndLowConfidenceCandidatesRetained()
    {
        var output=new float[1800];new float[]{64,200,128,360,.2f,0}.CopyTo(output,0);new float[]{10,5,30,20,.9f,0}.CopyTo(output,6);
        var d=Assert.Single(OnnxProductDetector.Parse(output,PhotoPreprocessor.ToTensor(Png()),new[]{"A"}));Assert.Equal(.2,d.Confidence,5);
    }
    [Fact]
    public void ClippedBorderCoordinatesRemainValidForCompliance()
    {
        var output=new float[1800];new float[]{6.4f,120,700,600,.9f,0}.CopyTo(output,0);
        var d=Assert.Single(OnnxProductDetector.Parse(output,PhotoPreprocessor.ToTensor(Png()),new[]{"A"}));
        var result=new ComplianceAnalyzer().Analyze([new(1,1,"A")],[d with{Shelf=1}]);Assert.Equal(100,result.Percent);
    }
    [Fact]
    public void CalibrationRejectsOverlapsAndMissingShelves()
    {
        var p=new PlanogramDocument { Shelves=[new(){Number=1},new(){Number=2}] };
        Assert.NotEmpty(ShelfCalibration.Validate(p,[new(1,0,1,0,.6),new(2,0,1,.5,1)]));
        Assert.NotEmpty(ShelfCalibration.Validate(p,[new(1,0,1,0,.5)]));
        Assert.Empty(ShelfCalibration.Validate(p,[new(1,0,1,0,.5),new(2,0,1,.5,1)]));
    }
}
