using Microsoft.EntityFrameworkCore;
using RetailVision.Core;

namespace RetailVision.Infrastructure;

public sealed class PlanogramWriteException(IReadOnlyList<string> errors, bool conflict = false) : Exception(string.Join(" ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
    public bool Conflict { get; } = conflict;
}

public sealed class PlanogramStore(RetailVisionDbContext db)
{
    public IQueryable<Planogram> Query() => db.Planograms.Include(p => p.Shelves).ThenInclude(s => s.Slots).ThenInclude(s => s.Product);

    public static PlanogramDocument Export(Planogram p) => new()
    {
        Name = p.Name, Description = p.Description, Store = p.Store,
        Shelves = p.Shelves.OrderBy(s => s.Number).Select(s => new ShelfDocument
        {
            Number = s.Number, Slots = s.Slots.OrderBy(x => x.Position).Select(x => new SlotDocument
            { Position = x.Position, Sku = x.Product.Sku, Facings = x.Facings }).ToList()
        }).ToList()
    };

    public async Task<Planogram> SaveAsync(int id, int version, PlanogramDocument? document, CancellationToken ct)
    {
        if (id < 0 || (id > 0 && version < 1)) throw new PlanogramWriteException(["Некорректный идентификатор или версия."]);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var plan = id == 0 ? new Planogram() : await Query().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (plan is null || (id > 0 && plan.Version != version))
            throw new PlanogramWriteException(["Планограмма удалена или изменена. Откройте актуальную версию; ваши изменения ещё не сохранены."], true);
        var existingSkus = plan.Shelves.SelectMany(s => s.Slots).Select(s => s.Product.Sku).ToHashSet();
        var products = await db.Products.ToDictionaryAsync(p => p.Sku, ct);
        var allowed = products.Values.Where(p => p.IsActive || existingSkus.Contains(p.Sku)).Select(p => p.Sku).ToHashSet();
        var errors = PlanogramValidator.Validate(document, allowed);
        if (errors.Count != 0) throw new PlanogramWriteException(errors);
        plan.Name = document!.Name.Trim();
        plan.Description = document.Description.Trim();
        plan.Store = document.Store.Trim();
        try
        {
            if (id == 0) db.Planograms.Add(plan);
            else
            {
                plan.Version++;
                // Check version before replacing children, within the same transaction.
                await db.SaveChangesAsync(ct);
                db.RemoveRange(plan.Shelves);
                await db.SaveChangesAsync(ct);
                plan.Shelves.Clear();
            }
            plan.Shelves = document.Shelves.OrderBy(s => s.Number).Select(s => new PlanogramShelf
            {
                Number = s.Number, Slots = s.Slots.OrderBy(x => x.Position).Select(x => new PlanogramSlot
                { Position = x.Position, Facings = x.Facings, ProductId = products[x.Sku].Id }).ToList()
            }).ToList();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return plan;
        }
        catch (DbUpdateException)
        {
            throw new PlanogramWriteException(["Связанные данные изменились во время сохранения. Обновите планограмму."], true);
        }
    }
}
