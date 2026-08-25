using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UrabaConecta.Application;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;

namespace UrabaConecta.Infrastructure.Persistence;

/// <summary>
/// Reconciliación administrativa de filas heredadas. Es deliberadamente una operación HTTP
/// explícita y no un startup task: un despliegue nunca cambia por sorpresa el catálogo público.
/// El fingerprint liga APPLY con el estado exacto que PlatformAdmin vio en DRY-RUN.
/// </summary>
public sealed class BusinessReadinessReconciliation(AppDbContext db, IPublicDirectoryCache publicCache,
    TimeProvider clock) : IBusinessReadinessReconciliation
{
    private const string Keep = "KeepPublished";
    private const string Unpublish = "MoveToPendingConfiguration";

    public async Task<ReadinessReconciliationPlanDto> DryRunAsync(PlatformActor actor,
        CancellationToken cancellationToken = default)
    {
        EnsurePlatformAdmin(actor);
        return await BuildPlanAsync(cancellationToken);
    }

    public async Task<ReadinessReconciliationApplyResultDto> ApplyAsync(PlatformActor actor,
        ApplyReadinessReconciliationRequest request, CancellationToken cancellationToken = default)
    {
        EnsurePlatformAdmin(actor);
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        var publishedIds = await db.Businesses.AsNoTracking().Where(x => x.IsPublished)
            .OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(cancellationToken);
        var locked = new Dictionary<Guid, Business>();
        foreach (var businessId in publishedIds)
        {
            var business = await db.Businesses
                .FromSqlInterpolated($"SELECT * FROM businesses WHERE \"Id\"={businessId} FOR UPDATE")
                .SingleAsync(cancellationToken);
            locked[business.Id] = business;
        }

        var current = await BuildPlanAsync(cancellationToken);
        var proposed = current.Businesses.Where(x => x.ProposedAction == Unpublish).ToList();
        // Repetir APPLY después de una reconciliación exitosa es un no-op, incluso si conserva el
        // fingerprint anterior. No hay ya ninguna fila publicada inválida que pueda tocar.
        if (proposed.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return new(current.PlanFingerprint, clock.GetUtcNow(), 0, []);
        }
        var suppliedFingerprint = request.PlanFingerprint?.ToLowerInvariant() ?? "";
        if (suppliedFingerprint.Length != current.PlanFingerprint.Length ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(current.PlanFingerprint),
                Encoding.ASCII.GetBytes(suppliedFingerprint)))
            throw new ApiException("RECONCILIATION_PLAN_STALE",
                "El catálogo cambió desde el dry-run. Genere y revise un plan nuevo.", 409);

        var now = clock.GetUtcNow();
        foreach (var item in proposed)
        {
            var business = locked[item.BusinessId];
            var previousStatus = business.Status;
            var before = JsonSerializer.Serialize(new { business.Status, business.IsPublished, business.Version });
            business.MarkPending(now, business.Version);
            db.PlatformAuditEntries.Add(new PlatformAuditEntry(Guid.NewGuid(), business.Id, actor.UserId,
                PlatformAuditAction.BusinessReadinessReconciled, before,
                JsonSerializer.Serialize(new { business.Status, business.IsPublished, business.Version,
                    item.ReadinessPercent, item.MissingRequirements }), now, actor.CorrelationId));
            db.BusinessStatusChanges.Add(new BusinessStatusChange(Guid.NewGuid(), business.Id,
                previousStatus, business.Status, actor.UserId,
                "Reconciliación explícita contra el readiness operativo vigente.", now));
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        publicCache.Invalidate();
        return new(current.PlanFingerprint, now, proposed.Count,
            proposed.Select(x => x.BusinessId).ToList());
    }

    private async Task<ReadinessReconciliationPlanDto> BuildPlanAsync(CancellationToken cancellationToken)
    {
        var businesses = await db.Businesses.AsNoTracking().Where(x => x.IsPublished)
            .OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Name, x.Status, x.IsPublished, x.Version })
            .ToListAsync(cancellationToken);
        var facts = await BusinessOperationalReadinessQuery.LoadAsync(db,
            businesses.Select(x => x.Id), cancellationToken);
        var items = businesses.Select(b =>
        {
            var readiness = BusinessOperationalReadiness.Evaluate(facts[b.Id]);
            return new ReadinessReconciliationItemDto(b.Id, b.Name, b.Status.ToString(), b.IsPublished,
                readiness.CompletionPercentage, readiness.MissingLabels,
                readiness.IsReady ? Keep : Unpublish, b.Version);
        }).ToList();
        return new(Fingerprint(items), clock.GetUtcNow(), items,
            items.Count(x => x.ProposedAction == Unpublish));
    }

    private static string Fingerprint(IReadOnlyList<ReadinessReconciliationItemDto> items)
    {
        var canonical = string.Join('\n', items.OrderBy(x => x.BusinessId).Select(x =>
            $"{x.BusinessId:N}|{x.BusinessVersion}|{x.CurrentStatus}|{x.IsPublished}|" +
            $"{x.ReadinessPercent}|{x.ProposedAction}|{string.Join('~', x.MissingRequirements)}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static void EnsurePlatformAdmin(PlatformActor actor)
    {
        if (!actor.IsPlatformAdmin)
            throw new ApiException("FORBIDDEN", "Solo la administración de plataforma reconcilia publicaciones.", 403);
    }
}
