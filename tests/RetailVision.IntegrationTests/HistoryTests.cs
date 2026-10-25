using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using RetailVision.Core;
using RetailVision.Infrastructure;
using RetailVision.Vision;

namespace RetailVision.IntegrationTests;

public sealed class HistoryTests
{
    private static HttpClient Client(AppFactory factory)=>factory.CreateClient(new WebApplicationFactoryClientOptions{AllowAutoRedirect=false});
    private static async Task<string> Token(HttpClient client,string page="/audit")=>WebUtility.HtmlDecode(Regex.Match(await client.GetStringAsync(page),"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static async Task Login(HttpClient client,AppFactory factory,string? email=null,string? password=null)
    {
        var response=await client.PostAsync("/account/login",new FormUrlEncodedContent(new Dictionary<string,string>{["Email"]=email??factory.AdminEmail,["Password"]=password??factory.AdminPassword,["__RequestVerificationToken"]=await Token(client,"/account/login")}));
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
    }
    private static async Task<int> Save(HttpClient client,string scenario="uncertain",string? key=null)
    {
        var response=await client.PostAsync("/history/savedemo",new FormUrlEncodedContent(new Dictionary<string,string>{["scenario"]=scenario,["key"]=key??Guid.NewGuid().ToString("N"),["__RequestVerificationToken"]=await Token(client)}));
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);return int.Parse(response.Headers.Location!.ToString().Split('/').Last());
    }
    private static async Task<JsonElement> Export(HttpClient client,int id)=>JsonDocument.Parse(await client.GetStringAsync("/history/export/"+id)).RootElement.Clone();
    private static async Task<HttpResponseMessage> Correct(HttpClient client,int id,int revision,IReadOnlyList<DetectionCorrection>? detections=null,bool restore=false,bool csrf=true,string reason="Уточнён результат")
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,"/history/correct/"+id){Content=JsonContent.Create(new {revision,reason,detections,restore})};
        if(csrf)request.Headers.Add("RequestVerificationToken",await Token(client,"/history/edit/"+id));return await client.SendAsync(request);
    }
    private static List<DetectionCorrection> Edits(JsonElement report)=>report.GetProperty("current").EnumerateArray().Select(r=>
    {var d=r.GetProperty("detection").Deserialize<Detection>(AuditStore.Json)!;return new DetectionCorrection(d.Id,d.Sku,d.X,d.Y,d.Width,d.Height,r.GetProperty("confirmedByHuman").GetBoolean());}).ToList();
    [Theory]
    [InlineData("/history")][InlineData("/history/analytics")][InlineData("/history/details/1")][InlineData("/history/image/1")]
    [InlineData("/history/export/1")][InlineData("/history/print/1")][InlineData("/history/edit/1")][InlineData("/history/reviewdata/1")][InlineData("/home/history")][InlineData("/home/analytics")]
    public async Task HistoryIsPrivate(string path)
    {
        using var factory=new AppFactory();using var client=Client(factory);Assert.Equal(HttpStatusCode.Redirect,(await client.GetAsync(path)).StatusCode);
    }
    [Fact]
    public async Task SaveIsIdempotentAndSnapshotSurvivesCatalogChanges()
    {
        using var factory=new AppFactory();using var client=Client(factory);await Login(client,factory);
        var key=Guid.NewGuid().ToString("N");var id=await Save(client,"missing",key);Assert.Equal(id,await Save(client,"missing",key));
        using(var scope=factory.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<RetailVisionDbContext>();Assert.Single(await db.Audits.ToListAsync());var product=await db.Products.FirstAsync();product.Name="Changed catalog";await db.SaveChangesAsync();}
        var export=await Export(client,id);Assert.Equal("DEMO",export.GetProperty("mode").GetString());Assert.Equal("missing",export.GetProperty("snapshot").GetProperty("scenario").GetString());
        Assert.DoesNotContain("Changed catalog",export.ToString());Assert.Equal(87.5,export.GetProperty("result").GetProperty("percent").GetDouble());
        var page=await client.GetAsync("/history/details/"+id);Assert.Equal(HttpStatusCode.OK,page.StatusCode);Assert.True(page.Headers.CacheControl!.NoStore);
        var html=WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());Assert.Contains("нейросеть не использовалась",html);Assert.Contains("/history/print/",html);Assert.Contains("saved-expected-1-3",html);Assert.Contains("эталон v1",html);
        var print=WebUtility.HtmlDecode(await client.GetStringAsync("/history/print/"+id));Assert.Contains("data-print",print);Assert.Contains("Журнал исправлений",print);Assert.DoesNotContain("class=\"sidebar\"",print);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/history/image/"+id)).StatusCode);
    }
    [Fact]
    public async Task CorrectionPreservesOriginalAndRejectsStaleOrInvalidWrites()
    {
        using var factory=new AppFactory();using var client=Client(factory);await Login(client,factory);var id=await Save(client);var original=await Export(client,id);var edits=Edits(original);
        edits[2]=edits[2] with{Confirmed=true};
        Assert.Equal(HttpStatusCode.BadRequest,(await Correct(client,id,1,edits,csrf:false)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Correct(client,id,1,edits,reason:" ")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Correct(client,id,1,edits)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Correct(client,id,1,edits)).StatusCode);
        var revised=await Export(client,id);Assert.Equal(2,revised.GetProperty("revision").GetInt32());Assert.Equal(100,revised.GetProperty("result").GetProperty("percent").GetDouble());
        Assert.Equal(original.GetProperty("snapshot").ToString(),revised.GetProperty("snapshot").ToString());Assert.Single(revised.GetProperty("corrections").EnumerateArray());
        Assert.Equal(.2,revised.GetProperty("current")[2].GetProperty("detection").GetProperty("confidence").GetDouble());
        Assert.Equal(HttpStatusCode.OK,(await Correct(client,id,2,restore:true,reason:"Возврат к исходным детекциям")).StatusCode);
        var restored=await Export(client,id);Assert.Equal(87.5,restored.GetProperty("result").GetProperty("percent").GetDouble());Assert.Equal(2,restored.GetProperty("corrections").GetArrayLength());
        using var scope=factory.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<RetailVisionDbContext>();
        Assert.Equal(8,await db.Set<DetectedProduct>().CountAsync());Assert.Equal(restored.GetProperty("result").GetProperty("violations").GetArrayLength(),await db.Set<AuditViolation>().CountAsync());
    }
    [Fact]
    public async Task AddRemoveRelabelAndMoveRecalculateTheReport()
    {
        using var factory=new AppFactory();using var client=Client(factory);await Login(client,factory);var id=await Save(client,"missing");var edits=Edits(await Export(client,id));
        var added=new DetectionCorrection("manual-"+Guid.NewGuid().ToString("N"),"RV-LIME",.52,.12,.12,.30,true);edits.Add(added);
        Assert.Equal(HttpStatusCode.OK,(await Correct(client,id,1,edits)).StatusCode);Assert.Equal(100,(await Export(client,id)).GetProperty("result").GetProperty("percent").GetDouble());
        edits[^1]=added with {Sku="RV-TEA",Y=.58};Assert.Equal(HttpStatusCode.OK,(await Correct(client,id,2,edits)).StatusCode);
        Assert.True((await Export(client,id)).GetProperty("result").GetProperty("percent").GetDouble()<100);
        Assert.Equal(HttpStatusCode.OK,(await Correct(client,id,3,[])).StatusCode);var empty=await Export(client,id);Assert.Equal(0,empty.GetProperty("result").GetProperty("percent").GetDouble());Assert.Equal(8,empty.GetProperty("result").GetProperty("violations").GetArrayLength());
    }
    [Theory]
    [InlineData("sku")][InlineData("box")][InlineData("duplicate")][InlineData("identity")][InlineData("unconfirmed")]
    public async Task InvalidCorrectionCannotMutateTheAudit(string kind)
    {
        using var factory=new AppFactory();using var client=Client(factory);await Login(client,factory);var id=await Save(client);var edits=Edits(await Export(client,id));
        edits[0]=kind switch {"sku"=>edits[0] with{Sku="FORGED",Confirmed=true},"box"=>edits[0] with{Width=2,Confirmed=true},"duplicate"=>edits[1],"identity"=>edits[0] with{Id="<script>",Confirmed=true},_=>edits[0] with{Sku="RV-TEA"}};
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Correct(client,id,1,edits)).StatusCode);Assert.Equal(1,(await Export(client,id)).GetProperty("revision").GetInt32());
    }
    [Fact]
    public async Task OtherAdministratorsCannotReadExportOrChangeTheReport()
    {
        using var factory=new AppFactory();using var client=Client(factory);await Login(client,factory);var id=await Save(client);
        var password=Guid.NewGuid().ToString("N")+"!aA7";
        using(var scope=factory.Services.CreateScope()){var manager=scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();var user=new IdentityUser("second@example.invalid"){Email="second@example.invalid"};Assert.True((await manager.CreateAsync(user,password)).Succeeded);Assert.True((await manager.AddToRoleAsync(user,"Administrator")).Succeeded);}
        using var other=Client(factory);await Login(other,factory,"second@example.invalid",password);
        foreach(var action in new[]{"details","edit","export","image","reviewdata","print"})Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/history/{action}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/history/compare?ids={id}&ids=999")).StatusCode);
        using var request=new HttpRequestMessage(HttpMethod.Post,"/history/correct/"+id){Content=JsonContent.Create(new {revision=1,reason="forged",restore=true})};request.Headers.Add("RequestVerificationToken",await Token(other));
        Assert.Equal(HttpStatusCode.NotFound,(await other.SendAsync(request)).StatusCode);
        Assert.Contains("Сохранено: 0",WebUtility.HtmlDecode(await other.GetStringAsync("/history")));
    }
    [Fact]
    public async Task DateModeStateFiltersAndComparisonUseSavedData()
    {
        using var factory=new AppFactory();using var client=Client(factory);await Login(client,factory);var a=await Save(client,"correct");var b=await Save(client,"missing");
        using(var scope=factory.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<RetailVisionDbContext>();(await db.Audits.FindAsync(a))!.CreatedAtUtc=new DateTime(2026,1,15,23,59,59,DateTimeKind.Utc);(await db.Audits.FindAsync(b))!.CreatedAtUtc=new DateTime(2026,1,16,0,0,0,DateTimeKind.Utc);await db.SaveChangesAsync();}
        var html=WebUtility.HtmlDecode(await client.GetStringAsync("/history?From=2026-01-15&To=2026-01-15&Mode=DEMO"));Assert.Contains("Сохранено: 1",html);Assert.Contains($"/history/details/{a}",html);Assert.DoesNotContain($"/history/details/{b}",html);
        Assert.Contains("Сохранено: 0",WebUtility.HtmlDecode(await client.GetStringAsync("/history?Mode=REAL")));
        Assert.Contains("Сохранено: 2",WebUtility.HtmlDecode(await client.GetStringAsync("/history?From=&To=&Mode=DEMO&State=&Search=")));
        Assert.Contains("Сохранено: 2",WebUtility.HtmlDecode(await client.GetStringAsync("/history?From=&To=&Mode=&State=&Search=")));
        Assert.Contains("Нет данных",WebUtility.HtmlDecode(await client.GetStringAsync("/history/analytics")));
        Assert.Contains("93,8%",WebUtility.HtmlDecode(await client.GetStringAsync("/history/analytics?Mode=DEMO")));
        var comparison=WebUtility.HtmlDecode(await client.GetStringAsync($"/history/compare?ids={a}&ids={b}"));Assert.Contains("-12,5 п.п.",comparison);Assert.DoesNotContain("РАЗНЫЕ УСЛОВИЯ",comparison);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await client.GetAsync("/history?From=2026-02-01&To=2026-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await client.GetAsync("/history?Mode=FORGED")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Correct(client,a,1,restore:true)).StatusCode);Assert.Contains("Сохранено: 1",WebUtility.HtmlDecode(await client.GetStringAsync("/history?State=corrected")));
    }
    [Fact]
    public async Task PreparedRealDraftPersistenceCopiesPhotoAndKeepsPlanSnapshot()
    {
        // Persistence test only: constructed input is not a claim of trained-model recognition.
        using var factory=new AppFactory();using var client=Client(factory);await Login(client,factory);
        PhotoAuditDraft draft;byte[] png;
        using(var bitmap=new SkiaSharp.SKBitmap(8,8)){bitmap.Erase(SkiaSharp.SKColors.White);using var image=SkiaSharp.SKImage.FromBitmap(bitmap);using var encoded=image.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);png=encoded.ToArray();}
        using(var scope=factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<RetailVisionDbContext>();var owner=await db.Users.Where(u=>u.Email==factory.AdminEmail).Select(u=>u.Id).SingleAsync();
            var plan=await scope.ServiceProvider.GetRequiredService<PlanogramStore>().Query().FirstAsync();var document=PlanogramStore.Export(plan);
            var detection=(await new FixtureProductDetector().DetectAsync(new DetectionRequest("correct"))) with {Mode="REAL",ModelVersion="persistence-test-input",ModelSha256=new string('a',64)};
            var bands=new List<ShelfGeometry>{new(1,0,1,0,.5),new(2,0,1,.5,1)};
            draft=new PhotoAuditDraft {OwnerId=owner,PlanogramId=plan.Id,PlanogramVersion=plan.Version,Planogram=document,ProductNames=FixtureProductDetector.CreateProducts().ToDictionary(p=>p.Key,p=>p.Value.Name),Width=8,Height=8,Shelves=bands,Detection=detection,Compliance=new ComplianceAnalyzer().Analyze(document,detection.Products,new ComplianceOptions {Shelves=bands})};
        }
        var drafts=factory.Services.GetRequiredService<PhotoDraftStore>();await drafts.CreateAsync(draft,png,default);
        async Task<HttpResponseMessage> SavePhoto()=>await client.PostAsync("/history/savephoto/"+draft.Id,new FormUrlEncodedContent(new Dictionary<string,string>{["revision"]="1",["__RequestVerificationToken"]=await Token(client)}));
        var saved=await SavePhoto();Assert.Equal(HttpStatusCode.Redirect,saved.StatusCode);var id=int.Parse(saved.Headers.Location!.ToString().Split('/').Last());Assert.Equal(saved.Headers.Location,(await SavePhoto()).Headers.Location);
        await drafts.DeleteAsync(draft.Id,draft.OwnerId,default);var imageResponse=await client.GetAsync("/history/image/"+id);Assert.True(imageResponse.Headers.CacheControl!.NoStore);Assert.Equal(png,await imageResponse.Content.ReadAsByteArrayAsync());
        using(var scope=factory.Services.CreateScope()){var db=scope.ServiceProvider.GetRequiredService<RetailVisionDbContext>();(await db.Planograms.FindAsync(draft.PlanogramId))!.Name="Renamed later";await db.SaveChangesAsync();}
        Assert.DoesNotContain("Renamed later",(await Export(client,id)).ToString());Assert.DoesNotContain("Нет данных",WebUtility.HtmlDecode(await client.GetStringAsync("/history/analytics")));
        using var anonymous=Client(factory);Assert.DoesNotContain($"/history/details/{id}",await anonymous.GetStringAsync("/"));Assert.Contains($"/history/details/{id}",await client.GetStringAsync("/"));
    }
    [Fact]
    public async Task MigrationPreservesMultipleLegacyAuditRows()
    {
        await using var connection=new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        await using var db=new RetailVisionDbContext(new DbContextOptionsBuilder<RetailVisionDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261024112910_CatalogEditing");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Planograms (Id,Name,Description,Store,Version) VALUES (1,'old','','',1);");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO ModelVersions (Id,Name,Mode) VALUES (1,'old','DEMO');");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Audits (Id,CreatedAtUtc,PlanogramId,ModelVersionId,PlanogramSnapshotJson,Mode,CompliancePercent) VALUES (1,'2026-01-01',1,1,'{{}}','DEMO',50),(2,'2026-01-02',1,1,'{{}}','DEMO',75);");
        await db.Database.MigrateAsync();Assert.Equal(2,await db.Audits.CountAsync());Assert.Equal(125,await db.Audits.SumAsync(a=>a.CompliancePercent));Assert.All(await db.Audits.ToListAsync(),a=>Assert.Equal("",a.OwnerId));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}
