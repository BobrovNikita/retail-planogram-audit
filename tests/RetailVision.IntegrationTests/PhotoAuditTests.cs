using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RetailVision.Core;
using RetailVision.Infrastructure;
using RetailVision.Vision;
using RetailVision.Web;
using SkiaSharp;

namespace RetailVision.IntegrationTests;

public sealed class PhotoAuditTests
{
    private static async Task<string> Token(HttpClient client,string page)
    {
        var html=await client.GetStringAsync(page);
        return WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    }
    private static async Task Login(HttpClient client,AppFactory factory)
    {
        var result=await client.PostAsync("/account/login",new FormUrlEncodedContent(new Dictionary<string,string>
        { ["Email"]=factory.AdminEmail,["Password"]=factory.AdminPassword,["__RequestVerificationToken"]=await Token(client,"/account/login") }));
        Assert.Equal(HttpStatusCode.Redirect,result.StatusCode);
    }
    private static byte[] Photo()
    {
        using var bitmap=new SKBitmap(800,500);bitmap.Erase(SKColors.White);using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);return png.ToArray();
    }
    private static async Task<HttpResponseMessage> Upload(HttpClient client,byte[] photo)
    {
        using var content=new MultipartFormDataContent();content.Add(new StringContent("1"),"PlanogramId");content.Add(new StringContent(await Token(client,"/photoaudit")),"__RequestVerificationToken");
        content.Add(new ByteArrayContent(photo),"Photo","untrusted-file-name.jpg");return await client.PostAsync("/photoaudit/upload",content);
    }
    private static async Task<HttpResponseMessage> Calibrate(HttpClient client,string id,int revision=1,bool overlapping=false,bool csrf=true)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,"/photoaudit/calibrate/"+id)
        {Content=JsonContent.Create(new {revision,shelves=new[]{new ShelfGeometry(1,0,1,0,overlapping?.7:.5),new ShelfGeometry(2,0,1,.5,1)},confidenceThreshold=.5})};
        if(csrf)request.Headers.Add("RequestVerificationToken",await Token(client,"/photoaudit/edit/"+id));return await client.SendAsync(request);
    }
    private static async Task<HttpResponseMessage> Analyze(HttpClient client,string id,int revision) => await client.PostAsync("/photoaudit/analyze/"+id,
        new FormUrlEncodedContent(new Dictionary<string,string>{["revision"]=revision.ToString(),["__RequestVerificationToken"]=await Token(client,"/photoaudit/edit/"+id)}));
    [Theory]
    [InlineData("/photoaudit")]
    [InlineData("/photoaudit/edit/not-a-draft")]
    [InlineData("/photoaudit/image/not-a-draft")]
    public async Task PhotoEndpointsRequireAuthentication(string path)
    {
        using var factory=new AppFactory();using var client=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
        Assert.Equal(HttpStatusCode.Redirect,(await client.GetAsync(path)).StatusCode);
    }
    [Fact]
    public async Task UploadCalibrationAndUnavailableModelRemainPrivateAndDoNotCreateFakeAudit()
    {
        using var factory=new AppFactory();using var client=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});await Login(client,factory);
        var uploaded=await Upload(client,Photo());Assert.Equal(HttpStatusCode.Redirect,uploaded.StatusCode);var id=uploaded.Headers.Location!.ToString().Split('/').Last();
        var image=await client.GetAsync("/photoaudit/image/"+id);Assert.Equal("image/png",image.Content.Headers.ContentType!.MediaType);Assert.True(image.Headers.CacheControl!.NoStore);
        Assert.Equal(HttpStatusCode.OK,(await Calibrate(client,id)).StatusCode);
        var html=await client.GetStringAsync("/photoaudit/edit/"+id);Assert.Contains("value=\"2\"",html);
        var analyzed=await Analyze(client,id,2);Assert.Equal(HttpStatusCode.ServiceUnavailable,analyzed.StatusCode);Assert.Contains("модель SKU не подключена",WebUtility.HtmlDecode(await analyzed.Content.ReadAsStringAsync()));
        var store=factory.Services.GetRequiredService<PhotoDraftStore>();Assert.Null(await store.ReadAsync(id,"another-owner",default));Assert.Null(await store.ImageAsync(id,"another-owner",default));
        using var scope=factory.Services.CreateScope();Assert.Empty(scope.ServiceProvider.GetRequiredService<RetailVisionDbContext>().Audits);
        var token=await Token(client,"/photoaudit/edit/"+id);Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/photoaudit/delete/"+id,new FormUrlEncodedContent(new Dictionary<string,string>{["__RequestVerificationToken"]=token}))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/photoaudit/image/"+id)).StatusCode);
    }
    [Fact]
    public async Task InvalidCalibrationCsrfAndStaleRevisionAreRejected()
    {
        using var factory=new AppFactory();using var client=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});await Login(client,factory);
        var upload=await Upload(client,Photo());var id=upload.Headers.Location!.ToString().Split('/').Last();
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Analyze(client,id,1)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Calibrate(client,id,csrf:false)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Calibrate(client,id,overlapping:true)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Calibrate(client,id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Calibrate(client,id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/photoaudit/edit/not-a-guid")).StatusCode);
    }
    [Fact]
    public async Task InvalidFileNeverBecomesAnImage()
    {
        using var factory=new AppFactory();using var client=factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});await Login(client,factory);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Upload(client,"<svg></svg>"u8.ToArray())).StatusCode);
    }
    [Fact]
    public async Task RealRuntimeFlowsThroughCalibrationAndReportUsingExplicitSyntheticGraph()
    {
        using var factory=new AppFactory();
        using var detector=new OnnxProductDetector(Path.Combine(AppContext.BaseDirectory,"Fixtures","manifest.json"),true);
        using var app=factory.WithWebHostBuilder(builder=>builder.ConfigureTestServices(services=>
        {services.RemoveAll<PhotoVisionService>();services.AddSingleton(new PhotoVisionService(detector));}));
        using var client=app.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});await Login(client,factory);
        var upload=await Upload(client,Photo());Assert.Equal(HttpStatusCode.Redirect,upload.StatusCode);var id=upload.Headers.Location!.ToString().Split('/').Last();
        Assert.Equal(HttpStatusCode.OK,(await Calibrate(client,id)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await Analyze(client,id,2)).StatusCode);
        var html=WebUtility.HtmlDecode(await client.GetStringAsync("/photoaudit/edit/"+id));
        Assert.Contains("TEST / synthetic-contract-test",html);Assert.Contains("id=\"onnx-0\"",html);Assert.Contains("Другая полка",html);Assert.Contains("id=\"photo-report\"",html);
        // Saving a new calibration invalidates the previous result, including after reloading.
        Assert.Equal(HttpStatusCode.OK,(await Calibrate(client,id,3)).StatusCode);
        Assert.DoesNotContain("id=\"photo-report\"",await client.GetStringAsync("/photoaudit/edit/"+id));
    }
}
