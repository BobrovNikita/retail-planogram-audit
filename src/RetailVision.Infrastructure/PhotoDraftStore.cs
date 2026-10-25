using System.Text.Json;
using System.Text.RegularExpressions;
using RetailVision.Core;

namespace RetailVision.Infrastructure;

public sealed class DraftConflictException() : Exception("Черновик изменён в другой вкладке. Обновите страницу.");

public sealed class PhotoDraftStore(string dataDirectory) : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetFullPath(dataDirectory), "photo-drafts");
    private readonly SemaphoreSlim gate = new(1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static bool Valid(string? id) => id is not null && Regex.IsMatch(id, "^[a-f0-9]{32}$");
    private string PathFor(string id, string extension) => Path.Combine(directory, id + extension);
    private async Task<PhotoAuditDraft?> Load(string id, CancellationToken ct)
    {
        if (!Valid(id) || !File.Exists(PathFor(id, ".json"))) return null;
        await using var stream = File.OpenRead(PathFor(id, ".json"));
        return await JsonSerializer.DeserializeAsync<PhotoAuditDraft>(stream, Json, ct);
    }
    private async Task Write(PhotoAuditDraft draft, CancellationToken ct)
    {
        var temp = PathFor(draft.Id, ".tmp");
        try
        {
            await File.WriteAllBytesAsync(temp, JsonSerializer.SerializeToUtf8Bytes(draft, Json), ct);
            File.Move(temp, PathFor(draft.Id, ".json"), true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public async Task CreateAsync(PhotoAuditDraft draft, byte[] png, CancellationToken ct)
    {
        if (!Valid(draft.Id) || string.IsNullOrWhiteSpace(draft.OwnerId) || png.Length > 32 * 1024 * 1024) throw new InvalidDataException("Некорректный черновик.");
        await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(directory);
            var total = 0; var owned = 0;
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                var old = await Load(Path.GetFileNameWithoutExtension(path), ct);
                if (old is null) continue;
                if (old.ExpiresAt <= DateTimeOffset.UtcNow) { DeleteFiles(old.Id); continue; }
                total++; if (old.OwnerId == draft.OwnerId) owned++;
            }
            if (total >= 20 || owned >= 5) throw new InvalidDataException("Лимит черновиков достигнут. Удалите ненужные фото или дождитесь истечения 24 часов.");
            if (File.Exists(PathFor(draft.Id, ".json"))) throw new DraftConflictException();
            try { await File.WriteAllBytesAsync(PathFor(draft.Id, ".png"), png, ct); await Write(draft, ct); }
            catch { DeleteFiles(draft.Id); throw; }
        }
        finally { gate.Release(); }
    }
    public async Task<PhotoAuditDraft?> ReadAsync(string id, string owner, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var draft = await Load(id, ct);
            return draft?.OwnerId == owner && draft.ExpiresAt > DateTimeOffset.UtcNow ? draft : null;
        }
        finally { gate.Release(); }
    }
    public async Task<List<PhotoAuditDraft>> ListAsync(string owner, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var result = new List<PhotoAuditDraft>();
            if (!Directory.Exists(directory)) return result;
            foreach(var path in Directory.EnumerateFiles(directory,"*.json"))
            {
                var draft = await Load(Path.GetFileNameWithoutExtension(path),ct);
                if(draft is null) continue;
                if(draft.ExpiresAt <= DateTimeOffset.UtcNow) {DeleteFiles(draft.Id);continue;}
                if(draft.OwnerId == owner) result.Add(draft);
            }
            return result.OrderByDescending(d=>d.ExpiresAt).ToList();
        }
        finally {gate.Release();}
    }
    public async Task<byte[]?> ImageAsync(string id, string owner, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var draft = await Load(id, ct);
            return draft?.OwnerId == owner && draft.ExpiresAt > DateTimeOffset.UtcNow && File.Exists(PathFor(id, ".png"))
                ? await File.ReadAllBytesAsync(PathFor(id, ".png"), ct) : null;
        }
        finally { gate.Release(); }
    }
    public async Task SaveAsync(PhotoAuditDraft draft, string owner, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var current = await Load(draft.Id, ct);
            if (current is null || current.OwnerId != owner || current.ExpiresAt <= DateTimeOffset.UtcNow) throw new InvalidDataException("Черновик недоступен или срок хранения истёк.");
            if (current.Revision != draft.Revision) throw new DraftConflictException();
            await Write(draft with { Revision = current.Revision + 1 }, ct);
        }
        finally { gate.Release(); }
    }
    public async Task DeleteAsync(string id, string owner, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { if ((await Load(id, ct))?.OwnerId == owner) DeleteFiles(id); }
        finally { gate.Release(); }
    }
    private void DeleteFiles(string id)
    {
        if (!Valid(id)) return;
        File.Delete(PathFor(id, ".png")); File.Delete(PathFor(id, ".json"));
    }
    public void Dispose() => gate.Dispose();
}
