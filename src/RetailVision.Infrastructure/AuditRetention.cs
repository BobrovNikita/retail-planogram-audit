using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RetailVision.Core;

namespace RetailVision.Infrastructure;

public sealed record RetentionPolicy
{
    public int TrashDays { get; }
    public bool AutoPurge { get; }
    public RetentionPolicy(int trashDays = 30, bool autoPurge = false)
    {
        if (trashDays is < 1 or > 3650) throw new ArgumentOutOfRangeException(nameof(trashDays), "TrashDays must be 1–3650.");
        TrashDays = trashDays; AutoPurge = autoPurge;
    }
}

public sealed class AuditRetention(RetailVisionDbContext db, RetentionPolicy policy, string dataDirectory, ILogger<AuditRetention> logger)
{
    public async Task SetArchivedAsync(int id, string owner, int revision, bool archived, CancellationToken ct)
    {
        var audit = await db.Audits.SingleOrDefaultAsync(a => a.Id == id && a.OwnerId == owner && a.SnapshotJson != "{}", ct)
            ?? throw new KeyNotFoundException();
        if (audit.Revision != revision) throw new AuditConflictException();
        if (archived == audit.ArchivedAtUtc.HasValue) return;
        audit.ArchivedAtUtc = archived ? DateTime.UtcNow : null;
        audit.Revision++;
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw new AuditConflictException(); }
    }

    public async Task PurgeAsync(int id, string owner, int revision, CancellationToken ct)
    {
        var audit = await db.Audits.SingleOrDefaultAsync(a => a.Id == id && a.OwnerId == owner && a.SnapshotJson != "{}", ct)
            ?? throw new KeyNotFoundException();
        if (audit.Revision != revision) throw new AuditConflictException();
        if (audit.ArchivedAtUtc is null || audit.ArchivedAtUtc > DateTime.UtcNow.AddDays(-policy.TrashDays))
            throw new ArgumentException("Срок восстановления в корзине ещё не истёк.");
        if (audit.PhotoPath is not null)
        {
            if (!Regex.IsMatch(audit.PhotoPath, "^[a-f0-9]{32}\\.png$")) throw new InvalidDataException("Invalid stored photo key.");
            if (!await db.Set<PendingPhotoDeletion>().AnyAsync(p => p.Path == audit.PhotoPath, ct))
                db.Add(new PendingPhotoDeletion { Path = audit.PhotoPath });
        }
        db.Remove(audit);
        // Audit + child rows + deletion queue are committed in one EF transaction.
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw new AuditConflictException(); }
        await DeleteQueuedPhotosAsync(ct);
    }

    public async Task MaintainAsync(CancellationToken ct)
    {
        if (policy.AutoPurge)
        {
            var cutoff = DateTime.UtcNow.AddDays(-policy.TrashDays);
            var candidates = await db.Audits.AsNoTracking().Where(a => a.ArchivedAtUtc <= cutoff && a.OwnerId != "" && a.SnapshotJson != "{}")
                .OrderBy(a => a.ArchivedAtUtc).Take(100).Select(a => new { a.Id, a.OwnerId, a.Revision }).ToListAsync(ct);
            foreach (var a in candidates)
            {
                try { await PurgeAsync(a.Id, a.OwnerId, a.Revision, ct); }
                catch (Exception ex) when (ex is AuditConflictException or KeyNotFoundException or ArgumentException)
                { db.ChangeTracker.Clear(); logger.LogInformation("Archived audit {AuditId} changed during cleanup; skipped", a.Id); }
            }
        }
        await DeleteQueuedPhotosAsync(ct);
    }

    private async Task DeleteQueuedPhotosAsync(CancellationToken ct)
    {
        var queued = await db.Set<PendingPhotoDeletion>().OrderBy(p => p.QueuedAtUtc).Take(100).ToListAsync(ct);
        foreach (var entry in queued)
        {
            if (!Regex.IsMatch(entry.Path, "^[a-f0-9]{32}\\.png$")) { logger.LogError("Invalid photo deletion key; skipped"); continue; }
            if (await db.Audits.AnyAsync(a => a.PhotoPath == entry.Path, ct)) continue;
            try
            {
                File.Delete(Path.Combine(Path.GetFullPath(dataDirectory), "audit-photos", entry.Path));
                db.Remove(entry); await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { logger.LogWarning(ex, "Photo cleanup delayed; queued for retry"); }
        }
    }
}
