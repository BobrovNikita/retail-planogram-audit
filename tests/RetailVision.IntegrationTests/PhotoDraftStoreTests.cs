using RetailVision.Core;
using RetailVision.Infrastructure;

namespace RetailVision.IntegrationTests;

public sealed class PhotoDraftStoreTests
{
    [Fact]
    public async Task OwnerQuotaExpiryAndTraversalAreEnforced()
    {
        var directory=Path.Combine(Path.GetTempPath(),"rv-draft-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var store=new PhotoDraftStore(directory);
            for(var i=0;i<5;i++) await store.CreateAsync(new(){OwnerId="owner"},[1],default);
            await Assert.ThrowsAsync<InvalidDataException>(()=>store.CreateAsync(new(){OwnerId="owner"},[1],default));
            Assert.Empty(await store.ListAsync("other",default));Assert.Null(await store.ReadAsync("../outside","owner",default));
            var expired=new PhotoAuditDraft {OwnerId="other",ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(-1)};await store.CreateAsync(expired,[1],default);
            Assert.Null(await store.ImageAsync(expired.Id,"other",default));Assert.Empty(await store.ListAsync("other",default));Assert.False(File.Exists(Path.Combine(directory,"photo-drafts",expired.Id+".png")));
        }
        finally {if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
    [Fact]
    public async Task ConcurrentRevisionAndOwnershipCannotOverwriteDraft()
    {
        var directory=Path.Combine(Path.GetTempPath(),"rv-draft-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var store=new PhotoDraftStore(directory);var draft=new PhotoAuditDraft {OwnerId="owner"};await store.CreateAsync(draft,[1],default);
            await Assert.ThrowsAsync<InvalidDataException>(()=>store.SaveAsync(draft,"other",default));await store.DeleteAsync(draft.Id,"other",default);
            Assert.NotNull(await store.ReadAsync(draft.Id,"owner",default));await store.SaveAsync(draft,"owner",default);
            await Assert.ThrowsAsync<DraftConflictException>(()=>store.SaveAsync(draft,"owner",default));
        }
        finally {if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
}
