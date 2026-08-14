using System.Text.Json;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;

namespace UrabaConecta.Application;

public sealed class BusinessImageUseCases(
    IBusinessImageStore store,
    IPlatformAdministrationStore businesses,
    IObjectStorage storage,
    IImageProcessor processor,
    IPublicDirectoryCache publicCache,
    TimeProvider timeProvider) : IBusinessImageUseCases
{
    public async Task<IReadOnlyList<BusinessImageDto>> ListAsync(PlatformActor actor, Guid businessId,
        CancellationToken cancellationToken = default)
    {
        await EnsureScopeAsync(actor, businessId, cancellationToken);
        // Sólo las imágenes del establecimiento. Las del catálogo pertenecen a su servicio o a su
        // producto y se administran junto a ellos: mezcladas aquí aparecían como fotografías sueltas
        // de la galería, sin decir de qué eran, y borrar una desde esta pantalla habría dejado un
        // servicio sin foto sin que nadie entendiera por qué.
        return Map((await store.ListAsync(businessId, cancellationToken))
            .Where(x => x.Kind is BusinessImageKind.Logo or BusinessImageKind.Cover
                or BusinessImageKind.Gallery));
    }

    public async Task<IReadOnlyList<BusinessImageDto>> ListCatalogAsync(PlatformActor actor, Guid businessId,
        CancellationToken cancellationToken = default)
    {
        await EnsureScopeAsync(actor, businessId, cancellationToken);
        return Map((await store.ListAsync(businessId, cancellationToken))
            .Where(x => x.Kind is BusinessImageKind.Service or BusinessImageKind.Product));
    }

    public async Task<BusinessImageDto> UploadAsync(PlatformActor actor, Guid businessId, string kind,
        UploadedImage file, string? altText, Guid? targetId = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureScopeAsync(actor, businessId, cancellationToken);
        var imageKind = ParseKind(kind);
        var isCatalog = imageKind is BusinessImageKind.Service or BusinessImageKind.Product;
        if (isCatalog && targetId.GetValueOrDefault() == Guid.Empty)
            throw new ApiException("TARGET_REQUIRED",
                "Indique el servicio o el producto al que pertenece la fotografía.");
        if (!isCatalog && targetId is not null)
            throw new ApiException("TARGET_NOT_ALLOWED",
                "Sólo las fotografías de servicio o producto se asocian a una fila del catálogo.");
        if (file.Content.Length == 0)
            throw new ApiException("EMPTY_FILE", "El archivo está vacío.");
        if (file.Content.Length > ImagePolicy.MaximumOriginalBytes)
            throw new ApiException("FILE_TOO_LARGE",
                $"El archivo supera el máximo de {ImagePolicy.MaximumOriginalBytes / (1024 * 1024)} MB.", 413);

        // La firma binaria manda: no se confía ni en la extensión ni en el content type declarado.
        var normalized = TryDomain(() => processor.Normalize(file.Content, imageKind));

        var now = timeProvider.GetUtcNow();
        await using var tx = await store.BeginTransactionAsync(cancellationToken);
        var business = await store.GetBusinessAsync(businessId, cancellationToken)
            ?? throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos el negocio.", 404);
        if (business.Status == BusinessStatus.Archived)
            throw new ApiException("BUSINESS_ARCHIVED", "Un negocio archivado no admite cambios.", 409);

        if (isCatalog && !await store.CatalogTargetExistsAsync(businessId, imageKind, targetId!.Value,
                cancellationToken))
            throw new ApiException("TARGET_NOT_FOUND",
                imageKind == BusinessImageKind.Service
                    ? "No encontramos ese servicio en el negocio."
                    : "No encontramos ese producto en el negocio.", 404);

        var all = await store.ListAsync(businessId, cancellationToken);
        // Para el catálogo lo vigente es lo de ESA fila, no lo de todos los servicios del negocio:
        // filtrar sólo por tipo habría borrado la foto de cada servicio al subir la del siguiente.
        var current = (isCatalog
            ? all.Where(x => x.Kind == imageKind &&
                (imageKind == BusinessImageKind.Service ? x.ServiceId : x.ProductId) == targetId)
            : all.Where(x => x.Kind == imageKind)).ToList();
        if (imageKind == BusinessImageKind.Gallery)
        {
            if (current.Count >= BusinessImage.MaximumGalleryImages)
                throw new ApiException("GALLERY_FULL",
                    $"La galería admite máximo {BusinessImage.MaximumGalleryImages} fotografías.", 409);
        }
        else if (current.Count > 0)
        {
            // Logo, portada y cada fila del catálogo tienen una sola foto vigente: el reemplazo
            // elimina lógicamente la anterior.
            foreach (var previous in current)
            {
                previous.SoftDelete(now, previous.Version);
                Audit(businessId, actor, PlatformAuditAction.ImageRemoved, previous, "reemplazo", now);
            }
        }

        var key = BuildKey(businessId, imageKind, normalized.Extension);
        await storage.PutAsync(key, normalized.Content, normalized.ContentType, cancellationToken);

        var order = imageKind == BusinessImageKind.Gallery
            ? (current.Count == 0 ? 0 : current.Max(x => x.DisplayOrder) + 1)
            : 0;
        var image = TryDomain(() => new BusinessImage(Guid.NewGuid(), businessId, imageKind, key,
            normalized.ContentType, normalized.Width, normalized.Height, normalized.Content.LongLength,
            altText, order, now,
            imageKind == BusinessImageKind.Service ? targetId : null,
            imageKind == BusinessImageKind.Product ? targetId : null));
        store.Add(image);
        Audit(businessId, actor, PlatformAuditAction.ImageUploaded, image, "carga", now);
        await store.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        publicCache.Invalidate();
        return Map(image);
    }

    public async Task<BusinessImageDto> DescribeAsync(PlatformActor actor, Guid businessId, Guid imageId,
        UpdateBusinessImageRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureScopeAsync(actor, businessId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        await using var tx = await store.BeginTransactionAsync(cancellationToken);
        var image = await store.GetAsync(businessId, imageId, cancellationToken)
            ?? throw new ApiException("IMAGE_NOT_FOUND", "No encontramos la imagen.", 404);
        TryDomain(() => { image.Describe(request.AltText, request.DisplayOrder, now, request.Version); return true; });
        await store.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        publicCache.Invalidate();
        return Map(image);
    }

    public async Task RemoveAsync(PlatformActor actor, Guid businessId, Guid imageId, long version,
        CancellationToken cancellationToken = default)
    {
        await EnsureScopeAsync(actor, businessId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        await using var tx = await store.BeginTransactionAsync(cancellationToken);
        var image = await store.GetAsync(businessId, imageId, cancellationToken)
            ?? throw new ApiException("IMAGE_NOT_FOUND", "No encontramos la imagen.", 404);
        TryDomain(() => { image.SoftDelete(now, version); return true; });
        Audit(businessId, actor, PlatformAuditAction.ImageRemoved, image, "eliminación", now);
        await store.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        publicCache.Invalidate();
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// Tres vías legítimas y ninguna más: la administración de plataforma, la socia que dio de alta el
    /// negocio, y el propietario activo de ESE negocio. La última existe para que el propietario
    /// gestione su logo, su portada y su galería sin pasar por "Administración", con el mismo
    /// almacenamiento, la misma validación y la misma auditoría que ya usan las otras dos.
    /// </summary>
    private async Task EnsureScopeAsync(PlatformActor actor, Guid businessId, CancellationToken cancellationToken)
    {
        var isOwner = actor.IsBusinessOwner
            && await businesses.GetMembershipByUserAsync(businessId, actor.UserId, cancellationToken)
                is { IsActive: true, Role: MembershipRole.Owner };
        if (!actor.CanOperate && !isOwner)
            throw new ApiException("FORBIDDEN", "No tiene permiso para administrar imágenes.", 403);
        if (actor.IsPlatformAdmin || isOwner) return;
        var record = await businesses.GetAsync(businessId, cancellationToken)
            ?? throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos el negocio.", 404);
        if (record.Business.CreatedByUserId != actor.UserId)
            throw new ApiException("FORBIDDEN", "El negocio no está a su cargo.", 403);
    }

    /// <summary>El nombre lo genera el servidor; el del archivo original nunca se usa.</summary>
    private static string BuildKey(Guid businessId, BusinessImageKind kind, string extension)
        => $"businesses/{businessId:N}/{kind.ToString().ToLowerInvariant()}/{Guid.NewGuid():N}{extension}";

    private void Audit(Guid businessId, PlatformActor actor, PlatformAuditAction action, BusinessImage image,
        string reason, DateTimeOffset now)
        => store.AddAudit(new PlatformAuditEntry(Guid.NewGuid(), businessId, actor.UserId, action, "{}",
            JsonSerializer.Serialize(new { image.Id, Kind = image.Kind.ToString(), Reason = reason }),
            now, actor.CorrelationId));

    private IReadOnlyList<BusinessImageDto> Map(IEnumerable<BusinessImage> images)
        => images.Where(x => !x.IsDeleted)
            .OrderBy(x => x.Kind).ThenBy(x => x.DisplayOrder)
            .Select(Map).ToList();

    private BusinessImageDto Map(BusinessImage image)
        => new(image.Id, image.Kind.ToString(), storage.PublicUrl(image.StorageKey), image.AltText,
            image.Width, image.Height, image.DisplayOrder, image.Version, image.ServiceId, image.ProductId);

    private static BusinessImageKind ParseKind(string value)
        => Enum.TryParse<BusinessImageKind>(value, ignoreCase: true, out var kind)
            ? kind
            : throw new ApiException("INVALID_IMAGE_KIND", "El tipo de imagen no es válido.");

    private static T TryDomain<T>(Func<T> action)
    {
        try { return action(); } catch (DomainException ex) { throw new ApiException(ex.Code, ex.Message, 400); }
    }
}
