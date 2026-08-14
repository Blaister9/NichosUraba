using System.IO.Compression;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using UrabaConecta.Application;
using UrabaConecta.Contracts;
using UrabaConecta.Infrastructure;
using UrabaConecta.Infrastructure.Caching;
using UrabaConecta.Infrastructure.Identity;
using UrabaConecta.Infrastructure.Persistence;
using UrabaConecta.Infrastructure.Security;
using UrabaConecta.Infrastructure.Storage;
using UrabaConecta.Web.Components;
using UrabaConecta.Web.Components.Account;
using UrabaConecta.Web.Services;

var builder = WebApplication.CreateBuilder(args);
if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var railwayPort))
    builder.WebHost.UseUrls($"http://0.0.0.0:{railwayPort}");
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.SignalR", LogLevel.Warning);
// Fuera de Development el registro sale en JSON con sus ámbitos: es lo que hace consultable la
// correlación desde el visor de Railway. La consola legible se conserva en local.
if (!builder.Environment.IsDevelopment())
    builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
var deployment = new DeploymentIdentity(builder.Environment.EnvironmentName,
    typeof(Program).Assembly.GetName().Version?.ToString() ?? "desconocida",
    builder.Configuration["Deployment:Commit"] ?? "desconocido");
builder.Services.AddSingleton(deployment);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Falta ConnectionStrings:DefaultConnection.");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
}).AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options =>
{
    if (!builder.Environment.IsDevelopment()) options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.HttpOnly = true;
    // Explícito y no heredado del marco: la subida de imágenes desactiva el antiforgery por ser
    // multipart, así que lo único que impide un envío desde otro sitio con la cookie de la
    // víctima es que SameSite no la acompañe en una petición cruzada.
    options.Cookie.SameSite = SameSiteMode.Lax;
    // Las rutas de API responden con códigos de estado; sólo el sitio redirige al inicio de sesión.
    var redirectToLogin = options.Events.OnRedirectToLogin;
    var redirectToAccessDenied = options.Events.OnRedirectToAccessDenied;
    options.Events.OnRedirectToLogin = context =>
    {
        if (!context.Request.Path.StartsWithSegments("/api")) return redirectToLogin(context);
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (!context.Request.Path.StartsWithSegments("/api")) return redirectToAccessDenied(context);
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("BusinessMember", policy => policy.RequireAuthenticatedUser())
    .AddPolicy("BusinessOwner", policy => policy.RequireRole("BusinessOwner"))
    .AddPolicy("Appointments.Manage", policy => policy.RequireRole("BusinessOwner", "BusinessWorker"))
    // Quién manda sobre un negocio concreto lo decide el caso de uso contra la membresía; esta
    // política sólo descarta a quien no puede ser dueño de nada. La socia que da de alta negocios
    // es PartnerOperator y a la vez propietaria de los suyos: dejarla fuera aquí le mostraba las
    // tarjetas de Perfil e Imágenes y le cerraba la puerta al entrar.
    .AddPolicy("BusinessProfile.Manage",
        policy => policy.RequireRole("BusinessOwner", "PartnerOperator", "PlatformAdmin"))
    .AddPolicy("BusinessConfiguration.Manage", policy => policy.RequireRole("BusinessOwner", "BusinessWorker"))
    .AddPolicy("Workers.Manage", policy => policy.RequireRole("BusinessOwner", "BusinessWorker"))
    .AddPolicy("PlatformAdmin", policy => policy.RequireRole("PlatformAdmin"))
    // Las socias comparten la consola administrativa; el alcance real lo impone cada caso de uso.
    .AddPolicy("PlatformOperator", policy => policy.RequireRole("PlatformAdmin", "PartnerOperator"));
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 10;
    // Bloqueo temporal explícito ante intentos fallidos, en lugar del comportamiento implícito.
    options.Lockout.AllowedForNewUsers = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddRoles<IdentityRole<Guid>>()
  .AddEntityFrameworkStores<AppDbContext>()
  .AddSignInManager()
  .AddDefaultTokenProviders();
builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName(builder.Configuration["DataProtection:ApplicationName"] ?? "UrabaConecta");
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
else if (builder.Environment.IsEnvironment("Demo"))
    throw new InvalidOperationException("Falta DataProtection:KeysPath para el ambiente Demo.");
// Cifrado de las llaves en reposo. Sin certificado, el anillo queda persistido pero sin
// protección aplicativa adicional: quien acceda al volumen puede descifrar los datos personales.
if (builder.Configuration["DataProtection:CertificateBase64"] is { Length: > 0 } certificateBase64)
{
    var certificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader
        .LoadPkcs12(Convert.FromBase64String(certificateBase64),
            builder.Configuration["DataProtection:CertificatePassword"],
            System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.EphemeralKeySet);
    dataProtection.ProtectKeysWithCertificate(certificate);
}
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<LegalOptions>(builder.Configuration.GetSection(LegalOptions.SectionName));
builder.Services.Configure<ObjectStorageOptions>(
    builder.Configuration.GetSection(ObjectStorageOptions.SectionName));
var storageOptions = builder.Configuration.GetSection(ObjectStorageOptions.SectionName)
    .Get<ObjectStorageOptions>() ?? new ObjectStorageOptions();
var legalOptions = builder.Configuration.GetSection(LegalOptions.SectionName).Get<LegalOptions>() ?? new();
StartupGuard.ThrowIfInvalid(builder.Configuration, builder.Environment, legalOptions, storageOptions);
if (storageOptions.UsesS3) builder.Services.AddSingleton<IObjectStorage, S3CompatibleObjectStorage>();
else builder.Services.AddSingleton<IObjectStorage, LocalObjectStorage>();
builder.Services.AddSingleton<IImageProcessor, ImageSharpImageProcessor>();
builder.Services.AddScoped<IInvitationTokenService, InvitationTokenService>();
builder.Services.AddScoped<IInvitationIdentityGateway, InvitationIdentityGateway>();
builder.Services.AddScoped<IAccessInvitationStore, AccessInvitationStore>();
builder.Services.AddScoped<IBusinessImageStore, BusinessImageStore>();
builder.Services.AddScoped<IAccessInvitationUseCases, AccessInvitationUseCases>();
builder.Services.AddScoped<IBusinessImageUseCases, BusinessImageUseCases>();
builder.Services.AddScoped<IPlatformHealthProvider, PlatformHealthProvider>();
builder.Services.AddSingleton<IConsentPolicyProvider, ConsentPolicyProvider>();
builder.Services.AddScoped<IUrabaStore, UrabaStore>();
builder.Services.AddScoped<IMembershipAdministrationStore, MembershipAdministrationStore>();
builder.Services.AddScoped<IQueueStore, QueueStore>();
builder.Services.AddScoped<IOrderingStore, OrderingStore>();
builder.Services.AddScoped<IPlatformAdministrationStore, PlatformAdministrationStore>();
builder.Services.AddScoped<IOwnerDashboardStore, OwnerDashboardStore>();
builder.Services.AddScoped<IBusinessTimeZoneResolver, BusinessTimeZoneResolver>();
builder.Services.AddSingleton<IOwnerDashboardDiagnostics, OwnerDashboardDiagnostics>();
builder.Services.AddScoped<IIdentityAccountManager, IdentityAccountManager>();
builder.Services.AddScoped<IPublicCodeService, PublicCodeService>();
builder.Services.AddScoped<UrabaConecta.Application.IPersonalDataProtector, PersonalDataProtector>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IUrabaUseCases, UrabaUseCases>();
builder.Services.AddScoped<IQueueUseCases, QueueUseCases>();
builder.Services.AddScoped<IOrderingUseCases, OrderingUseCases>();
builder.Services.AddScoped<IPlatformAdministrationUseCases, PlatformAdministrationUseCases>();
builder.Services.AddScoped<IOwnerDashboardUseCases, OwnerDashboardUseCases>();
builder.Services.AddScoped<IQueueChangeNotifier, SignalRQueueChangeNotifier>();
builder.Services.AddScoped<IUrabaConectaApi, ServerUrabaConectaApi>();
builder.Services.AddSignalR();
builder.Services.AddMemoryCache();
builder.Services.Configure<PublicCacheOptions>(builder.Configuration.GetSection(PublicCacheOptions.SectionName));
builder.Services.AddSingleton<IPublicDirectoryCache, PublicDirectoryCache>();
// El documento HTML y las respuestas JSON se servían sin comprimir. Los activos estáticos ya
// salen precomprimidos por MapStaticAssets, así que esta compresión sólo alcanza lo dinámico.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ["text/html", "text/css", "text/plain", "text/javascript", "application/javascript",
        "application/json", "application/problem+json", "image/svg+xml"];
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<DatabaseMigrationState>();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("postgresql", tags: ["ready"])
    // Una instancia con el esquema atrasado responde errores de columna inexistente. Que no pase
    // la readiness deja el despliegue anterior sirviendo en lugar de publicar una versión rota.
    .AddCheck<DatabaseMigrationHealthCheck>("migrations", tags: ["ready"]);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var publicPermitLimit = builder.Configuration.GetValue("RateLimits:PublicWritesPerMinute", 12);
    options.AddPolicy("public-write", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = publicPermitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));
    var publicReadPermitLimit = builder.Configuration.GetValue("RateLimits:SensitiveReadsPerMinute", 1200);
    options.AddPolicy("public-sensitive-read", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = publicReadPermitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));
});

var app = builder.Build();
var contentSecurityPolicy = ContentSecurityPolicyFactory.Create(storageOptions.PublicBaseUrl);
app.UseForwardedHeaders();
// Antes de todo lo demás: si algo falla más abajo, su registro ya sale correlacionado.
app.UseMiddleware<RequestCorrelationMiddleware>();
app.UseResponseCompression();
if (app.Environment.IsDevelopment()) app.UseWebAssemblyDebugging();
else { app.UseExceptionHandler(); app.UseHsts(); }
app.UseExceptionHandler();
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/api"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found"));
app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers.ContentSecurityPolicy = contentSecurityPolicy;
    await next();
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true &&
        !context.Request.Path.StartsWithSegments("/Account/ChangeTemporaryPassword") &&
        !context.Request.Path.StartsWithSegments("/Account/Logout") &&
        !context.Request.Path.StartsWithSegments("/_blazor") &&
        !context.Request.Path.StartsWithSegments("/_framework") &&
        !context.Request.Path.StartsWithSegments("/health"))
    {
        var userManager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.GetUserAsync(context.User);
        if (user?.MustChangePassword == true)
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    title = "Debe cambiar la contraseña temporal antes de continuar.",
                    code = "PASSWORD_CHANGE_REQUIRED",
                    status = StatusCodes.Status403Forbidden
                });
                return;
            }
            context.Response.Redirect("/Account/ChangeTemporaryPassword");
            return;
        }
    }
    await next();
});
app.UseAntiforgery();
app.MapStaticAssets();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });

var publicApi = app.MapGroup("/api/v1/public");
publicApi.MapGet("/businesses", (string? q, string? municipality, string? category, IUrabaUseCases useCases, CancellationToken ct)
    => useCases.GetBusinessesAsync(q, municipality, category, ct));
publicApi.MapGet("/categories", (string? municipality, IUrabaUseCases useCases, CancellationToken ct)
    => useCases.GetCategoriesAsync(municipality, ct));
publicApi.MapGet("/businesses/{slug}", async (string slug, IUrabaUseCases useCases, CancellationToken ct) =>
    await useCases.GetBusinessAsync(slug, ct) is { } business ? Results.Ok(business) : Results.NotFound());
publicApi.MapGet("/businesses/{slug}/appointment-slots",
    (string slug, Guid serviceId, DateOnly date, IUrabaUseCases useCases, CancellationToken ct)
        => useCases.GetSlotsAsync(slug, serviceId, date, ct));
publicApi.MapPost("/businesses/{slug}/appointments",
    async (string slug, CreateAppointmentRequest request, IUrabaUseCases useCases, CancellationToken ct) =>
        Results.Created("", await useCases.CreateAppointmentAsync(slug, request, ct))).RequireRateLimiting("public-write");
publicApi.MapGet("/appointments/{code}",
    async (string code, IUrabaUseCases useCases, CancellationToken ct) =>
        await useCases.GetTrackingAsync(code, ct) is { } result ? Results.Ok(result) : Results.NotFound())
    .RequireRateLimiting("public-sensitive-read");
publicApi.MapPost("/appointments/{code}/cancel",
    async (string code, IUrabaUseCases useCases, CancellationToken ct) =>
    { await useCases.CancelAsync(code, ct); return Results.NoContent(); }).RequireRateLimiting("public-write");
// El código de seguimiento es la única credencial del cliente, y sólo le alcanza para avisar que
// envió el comprobante. Verificar no tiene ruta pública.
publicApi.MapPost("/appointments/{code}/deposit-reported",
    (string code, IUrabaUseCases useCases, CancellationToken ct) => useCases.ReportDepositAsync(code, ct))
    .RequireRateLimiting("public-write");
publicApi.MapGet("/businesses/{slug}/queue",
    async (string slug, IQueueUseCases queues, CancellationToken ct) =>
        await queues.GetPublicAsync(slug, ct) is { } result ? Results.Ok(result) : Results.NotFound());
publicApi.MapPost("/businesses/{slug}/queue/tickets",
    async (string slug, CreateQueueTicketRequest request, IQueueUseCases queues, CancellationToken ct) =>
        Results.Created("", await queues.JoinAsync(slug, request, ct))).RequireRateLimiting("public-write");
publicApi.MapGet("/queue/tickets/{code}",
    async (string code, IQueueUseCases queues, CancellationToken ct) =>
        await queues.TrackAsync(code, ct) is { } result ? Results.Ok(result) : Results.NotFound())
    .RequireRateLimiting("public-sensitive-read");
publicApi.MapPost("/queue/tickets/{code}/cancel",
    async (string code, QueueSessionCommandRequest request, IQueueUseCases queues, CancellationToken ct) =>
    { await queues.CancelPublicAsync(code, request.Version, ct); return Results.NoContent(); })
    .RequireRateLimiting("public-write");
publicApi.MapGet("/businesses/{slug}/menu", async (string slug, IOrderingUseCases orders, CancellationToken ct) =>
    await orders.GetMenuAsync(slug, ct) is { } result ? Results.Ok(result) : Results.NotFound());
publicApi.MapGet("/businesses/{slug}/pickup-slots",
    (string slug, DateOnly? date, IOrderingUseCases orders, CancellationToken ct) => orders.GetSlotsAsync(slug, date, ct));
publicApi.MapPost("/businesses/{slug}/orders",
    async (string slug, CreatePickupOrderRequest request, IOrderingUseCases orders, CancellationToken ct) =>
        Results.Created("", await orders.CreateAsync(slug, request, ct))).RequireRateLimiting("public-write");
publicApi.MapGet("/orders/{code}", async (string code, IOrderingUseCases orders, CancellationToken ct) =>
    await orders.TrackAsync(code, ct) is { } result ? Results.Ok(result) : Results.NotFound())
    .RequireRateLimiting("public-sensitive-read");
publicApi.MapPost("/orders/{code}/cancel",
    async (string code, PickupOrderCommandRequest request, IOrderingUseCases orders, CancellationToken ct) =>
    { await orders.CancelPublicAsync(code, request.Version, ct); return Results.NoContent(); })
    .RequireRateLimiting("public-write");
// PolicyVersion es la versión *efectiva*: la que el servidor exigirá en los formularios públicos.
publicApi.MapGet("/legal", (IOptions<LegalOptions> legal, IConsentPolicyProvider consent) =>
{
    var value = legal.Value;
    return new LegalInfoDto(value.ResponsibleName, value.Identification, value.Address, value.PrivacyEmail,
        value.SupportEmail, consent.CurrentVersion, value.PolicyEffectiveDate);
});

// Sirve las imágenes del proveedor local. En Production el proveedor es S3/R2 y las imágenes
// se sirven desde su dominio público, por lo que esta ruta no se usa.
app.MapGet("/media/{**key}", async (string key, IObjectStorage storage, CancellationToken ct) =>
{
    if (storage.Provider != ObjectStorageOptions.LocalProvider) return Results.NotFound();
    var stream = await storage.OpenReadAsync(key, ct);
    if (stream is null) return Results.NotFound();
    var contentType = Path.GetExtension(key).ToLowerInvariant() switch
    {
        ".png" => "image/png", ".webp" => "image/webp", _ => "image/jpeg"
    };
    return Results.Stream(stream, contentType, enableRangeProcessing: true);
});

// La consola administrativa la comparten el administrador técnico y las socias; el alcance
// (qué negocios ve y qué acciones puede ejecutar cada una) lo decide el caso de uso, no la ruta.
var platformApi = app.MapGroup("/api/v1/admin").RequireAuthorization("PlatformOperator");
platformApi.MapGet("/businesses",
    (string? q, string? municipality, string? status, string? module, HttpContext http,
        IPlatformAdministrationUseCases useCases, CancellationToken ct) =>
        useCases.ListAsync(Actor(http), q, municipality, status, module, ct));
platformApi.MapGet("/businesses/{businessId:guid}",
    (Guid businessId, HttpContext http, IPlatformAdministrationUseCases useCases, CancellationToken ct) =>
        useCases.GetAsync(Actor(http), businessId, ct));
platformApi.MapPost("/businesses",
    async (CreatePlatformBusinessRequest request, HttpContext http,
        IPlatformAdministrationUseCases useCases, CancellationToken ct) =>
        Results.Created("", await useCases.CreateAsync(Actor(http), request, ct)));
platformApi.MapPut("/businesses/{businessId:guid}",
    (Guid businessId, UpdatePlatformBusinessRequest request, HttpContext http,
        IPlatformAdministrationUseCases useCases, CancellationToken ct) =>
        useCases.UpdateAsync(Actor(http), businessId, request, ct));
platformApi.MapPut("/businesses/{businessId:guid}/profile",
    (Guid businessId, SaveBusinessProfileRequest request, HttpContext http,
        IPlatformAdministrationUseCases useCases, CancellationToken ct) =>
        useCases.SaveProfileAsync(Actor(http), businessId, request, ct));
platformApi.MapPost("/businesses/{businessId:guid}/submit-review",
    (Guid businessId, SubmitForReviewRequest request, HttpContext http,
        IPlatformAdministrationUseCases useCases, CancellationToken ct) =>
        useCases.SubmitForReviewAsync(Actor(http), businessId, request, ct));
platformApi.MapPost("/businesses/{businessId:guid}/reject-review",
    (Guid businessId, RejectReviewRequest request, HttpContext http,
        IPlatformAdministrationUseCases useCases, CancellationToken ct) =>
        useCases.RejectReviewAsync(Actor(http), businessId, request, ct));
platformApi.MapGet("/businesses/{businessId:guid}/preview",
    (Guid businessId, HttpContext http, IPlatformAdministrationUseCases useCases, CancellationToken ct) =>
        useCases.PreviewAsync(Actor(http), businessId, ct));
platformApi.MapGet("/businesses/{businessId:guid}/status-history",
    (Guid businessId, HttpContext http, IPlatformAdministrationUseCases useCases, CancellationToken ct) =>
        useCases.ListStatusHistoryAsync(Actor(http), businessId, ct));
platformApi.MapGet("/businesses/{businessId:guid}/audit",
    (Guid businessId, HttpContext http, IPlatformAdministrationUseCases useCases, CancellationToken ct) =>
        useCases.ListAuditAsync(Actor(http), businessId, ct));
platformApi.MapPut("/businesses/{businessId:guid}/modules",
    (Guid businessId, UpdatePlatformModulesRequest request, HttpContext http,
        IPlatformAdministrationUseCases useCases, CancellationToken ct) =>
        useCases.UpdateModulesAsync(Actor(http), businessId, request, ct));
platformApi.MapGet("/businesses/{businessId:guid}/images",
    (Guid businessId, HttpContext http, IBusinessImageUseCases images, CancellationToken ct) =>
        images.ListAsync(Actor(http), businessId, ct));
platformApi.MapGet("/businesses/{businessId:guid}/hours",
    (Guid businessId, HttpContext http, IPlatformAdministrationUseCases useCases, CancellationToken ct) =>
        useCases.ListHoursAsync(Actor(http), businessId, ct));
platformApi.MapPut("/businesses/{businessId:guid}/hours/{day}",
    (Guid businessId, DayOfWeek day, SaveBusinessHourRequest request, HttpContext http,
        IPlatformAdministrationUseCases useCases, CancellationToken ct) =>
        useCases.SetHourAsync(Actor(http), businessId, day, request, ct));
platformApi.MapGet("/businesses/{businessId:guid}/scheduling-staff",
    (Guid businessId, HttpContext http, IPlatformAdministrationUseCases useCases, CancellationToken ct) =>
        useCases.ListSchedulingStaffAsync(Actor(http), businessId, ct));
platformApi.MapGet("/businesses/{businessId:guid}/scheduling-exceptions",
    (Guid businessId, DateOnly? from, HttpContext http, IPlatformAdministrationUseCases useCases,
        CancellationToken ct) => useCases.ListSchedulingExceptionsAsync(Actor(http), businessId, from, ct));
platformApi.MapPost("/businesses/{businessId:guid}/scheduling-exceptions",
    async (Guid businessId, SaveAvailabilityExceptionRequest request, HttpContext http,
        IPlatformAdministrationUseCases useCases, CancellationToken ct) =>
        Results.Created("", await useCases.SaveSchedulingExceptionAsync(Actor(http), businessId, request, ct)));
platformApi.MapDelete("/businesses/{businessId:guid}/scheduling-exceptions/{exceptionId:guid}",
    async (Guid businessId, Guid exceptionId, long version, HttpContext http,
        IPlatformAdministrationUseCases useCases, CancellationToken ct) =>
    {
        await useCases.DeleteSchedulingExceptionAsync(Actor(http), businessId, exceptionId, version, ct);
        return Results.NoContent();
    });
platformApi.MapPost("/businesses/{businessId:guid}/images",
    async (Guid businessId, HttpRequest request, HttpContext http, IBusinessImageUseCases images,
        CancellationToken ct) =>
    {
        if (!request.HasFormContentType) return Results.BadRequest(new { code = "INVALID_UPLOAD" });
        var form = await request.ReadFormAsync(ct);
        var file = form.Files["file"];
        if (file is null) return Results.BadRequest(new { code = "FILE_REQUIRED" });
        if (file.Length > ImagePolicy.MaximumOriginalBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        // El destino del catálogo llega como campo del formulario; ausente o vacío significa que la
        // imagen es del establecimiento y no de una fila concreta.
        Guid? target = Guid.TryParse(form["targetId"].ToString(), out var parsed) ? parsed : null;
        return Results.Created("", await images.UploadAsync(Actor(http), businessId,
            form["kind"].ToString(), new UploadedImage(file.FileName, file.ContentType, buffer.ToArray()),
            form["altText"].ToString(), target, ct));
    }).DisableAntiforgery();
platformApi.MapPut("/businesses/{businessId:guid}/images/{imageId:guid}",
    (Guid businessId, Guid imageId, UpdateBusinessImageRequest request, HttpContext http,
        IBusinessImageUseCases images, CancellationToken ct) =>
        images.DescribeAsync(Actor(http), businessId, imageId, request, ct));
platformApi.MapDelete("/businesses/{businessId:guid}/images/{imageId:guid}",
    async (Guid businessId, Guid imageId, long version, HttpContext http, IBusinessImageUseCases images,
        CancellationToken ct) =>
    { await images.RemoveAsync(Actor(http), businessId, imageId, version, ct); return Results.NoContent(); });
platformApi.MapGet("/invitations",
    (Guid? businessId, HttpContext http, IAccessInvitationUseCases invitations, CancellationToken ct) =>
        invitations.ListAsync(Actor(http), businessId, ct));
platformApi.MapPost("/invitations",
    async (CreateInvitationRequest request, HttpContext http, IAccessInvitationUseCases invitations,
        CancellationToken ct) => Results.Created("", await invitations.InviteAsync(Actor(http), request, ct)));
platformApi.MapPost("/invitations/{invitationId:guid}/resend",
    (Guid invitationId, HttpContext http, IAccessInvitationUseCases invitations, CancellationToken ct) =>
        invitations.ResendAsync(Actor(http), invitationId, ct));
platformApi.MapDelete("/invitations/{invitationId:guid}",
    async (Guid invitationId, HttpContext http, IAccessInvitationUseCases invitations, CancellationToken ct) =>
    { await invitations.RevokeAsync(Actor(http), invitationId, ct); return Results.NoContent(); });
platformApi.MapPost("/access-resets",
    async (ResetAccessRequest request, HttpContext http, IAccessInvitationUseCases invitations,
        CancellationToken ct) => Results.Created("", await invitations.ResetAccessAsync(Actor(http), request, ct)));
platformApi.MapGet("/partner-operators",
    (HttpContext http, IAccessInvitationUseCases invitations, CancellationToken ct) =>
        invitations.ListPartnerOperatorsAsync(Actor(http), ct));
platformApi.MapDelete("/partner-operators/{userId:guid}",
    async (Guid userId, HttpContext http, IAccessInvitationUseCases invitations, CancellationToken ct) =>
    { await invitations.RevokePartnerOperatorAsync(Actor(http), userId, ct); return Results.NoContent(); });
platformApi.MapGet("/access-audit",
    (Guid? businessId, HttpContext http, IAccessInvitationUseCases invitations, CancellationToken ct) =>
        invitations.ListAuditAsync(Actor(http), businessId, ct));
// La auditoría del adelanto la consulta la administración técnica, no las socias: deja ver quién
// verificó qué cita y cuándo.
platformApi.MapGet("/appointments/{appointmentId:guid}/deposit-audit",
    async (Guid appointmentId, HttpContext http, IUrabaUseCases useCases, CancellationToken ct) =>
        http.User.IsInRole("PlatformAdmin")
            ? Results.Ok(await useCases.GetDepositAuditAsync(appointmentId, ct))
            : Results.StatusCode(StatusCodes.Status403Forbidden));
platformApi.MapGet("/health",
    async (HttpContext http, IPlatformHealthProvider health, CancellationToken ct) =>
        http.User.IsInRole("PlatformAdmin")
            ? Results.Ok(await health.GetAsync(ct))
            : Results.StatusCode(StatusCodes.Status403Forbidden));
// Comodín de acciones de estado. Los segmentos literales de arriba tienen mayor precedencia
// de enrutamiento, así que "submit-review" y "reject-review" nunca llegan aquí.
platformApi.MapPost("/businesses/{businessId:guid}/{action}",
    (Guid businessId, string action, PlatformBusinessStateRequest request, HttpContext http,
        IPlatformAdministrationUseCases useCases, CancellationToken ct) =>
        useCases.ChangeStateAsync(Actor(http), businessId, action, request, ct));

var privateApi = app.MapGroup("/api/v1/businesses").RequireAuthorization("BusinessMember");
privateApi.MapGet("/mine", (ClaimsPrincipal user, IUrabaUseCases useCases, CancellationToken ct)
    => useCases.GetMyBusinessesAsync(UserId(user), ct));

// El resumen operativo no recibe identificadores del cliente: el alcance sale de las membresías de
// quien pregunta. Aceptar "?businessIds=" dejaría al navegador elegir de qué negocios pide métricas,
// y bastaría con que la respuesta cambiara ante un identificador ajeno para delatar que ese negocio
// existe. Por eso la lista se resuelve aquí, con la misma consulta que alimenta "Mis establecimientos".
privateApi.MapGet("/dashboard", async (ClaimsPrincipal user, IUrabaUseCases businesses,
        IOwnerDashboardUseCases dashboard, CancellationToken ct)
    => await dashboard.SummarizeAsync(await businesses.GetMyBusinessesAsync(UserId(user), ct), ct));

// --- Perfil e imágenes del propietario -------------------------------------------------------
// Mismos casos de uso que usa la administración: la autorización distingue quién entra, no qué
// código corre. Un propietario ya no necesita "Administración" para editar su ficha ni sus fotos,
// que era el segundo P0 de la auditoría V6.
privateApi.MapGet("/{businessId:guid}/profile",
    (Guid businessId, HttpContext http, IPlatformAdministrationUseCases useCases, CancellationToken ct)
        => useCases.GetAsync(Actor(http), businessId, ct))
    .RequireAuthorization("BusinessProfile.Manage");
privateApi.MapPut("/{businessId:guid}/profile",
    (Guid businessId, SaveOwnerProfileRequest request, HttpContext http,
        IPlatformAdministrationUseCases useCases, CancellationToken ct)
        => useCases.SaveOwnerProfileAsync(Actor(http), businessId, request, ct))
    .RequireAuthorization("BusinessProfile.Manage");
privateApi.MapGet("/{businessId:guid}/images",
    (Guid businessId, HttpContext http, IBusinessImageUseCases images, CancellationToken ct)
        => images.ListAsync(Actor(http), businessId, ct))
    .RequireAuthorization("BusinessProfile.Manage");
privateApi.MapPost("/{businessId:guid}/images",
    async (Guid businessId, HttpRequest request, HttpContext http, IBusinessImageUseCases images,
        CancellationToken ct) =>
    {
        if (!request.HasFormContentType) return Results.BadRequest(new { code = "INVALID_UPLOAD" });
        var form = await request.ReadFormAsync(ct);
        var file = form.Files["file"];
        if (file is null) return Results.BadRequest(new { code = "FILE_REQUIRED" });
        if (file.Length > ImagePolicy.MaximumOriginalBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        // El destino del catálogo llega como campo del formulario; ausente o vacío significa que la
        // imagen es del establecimiento y no de una fila concreta.
        Guid? target = Guid.TryParse(form["targetId"].ToString(), out var parsed) ? parsed : null;
        return Results.Created("", await images.UploadAsync(Actor(http), businessId,
            form["kind"].ToString(), new UploadedImage(file.FileName, file.ContentType, buffer.ToArray()),
            form["altText"].ToString(), target, ct));
    }).RequireAuthorization("BusinessProfile.Manage").DisableAntiforgery();
privateApi.MapPut("/{businessId:guid}/images/{imageId:guid}",
    (Guid businessId, Guid imageId, UpdateBusinessImageRequest request, HttpContext http,
        IBusinessImageUseCases images, CancellationToken ct)
        => images.DescribeAsync(Actor(http), businessId, imageId, request, ct))
    .RequireAuthorization("BusinessProfile.Manage");
privateApi.MapDelete("/{businessId:guid}/images/{imageId:guid}",
    async (Guid businessId, Guid imageId, long version, HttpContext http, IBusinessImageUseCases images,
        CancellationToken ct) =>
    { await images.RemoveAsync(Actor(http), businessId, imageId, version, ct); return Results.NoContent(); })
    .RequireAuthorization("BusinessProfile.Manage");
privateApi.MapGet("/{businessId:guid}/appointments",
    (Guid businessId, DateOnly? date, string? status, ClaimsPrincipal user, IUrabaUseCases useCases, CancellationToken ct)
        => useCases.GetAppointmentsAsync(UserId(user), businessId, date, status, ct))
    .RequireAuthorization("Appointments.Manage");
privateApi.MapPost("/{businessId:guid}/appointments/{appointmentId:guid}/status",
    (Guid businessId, Guid appointmentId, ChangeAppointmentStatusRequest request, ClaimsPrincipal user,
        IUrabaUseCases useCases, CancellationToken ct)
        => useCases.ChangeStatusAsync(UserId(user), businessId, appointmentId, request, ct))
    .RequireAuthorization("Appointments.Manage");
privateApi.MapPost("/{businessId:guid}/appointments/{appointmentId:guid}/deposit/{action}",
    (Guid businessId, Guid appointmentId, string action, DepositCommandRequest request, ClaimsPrincipal user,
        IUrabaUseCases useCases, CancellationToken ct)
        => useCases.ChangeDepositAsync(UserId(user), businessId, appointmentId, action, request,
            user.IsInRole("PlatformAdmin"), ct))
    .RequireAuthorization("Appointments.Manage");
privateApi.MapPut("/{businessId:guid}/services/{serviceId:guid}",
    (Guid businessId, Guid serviceId, UpdateServiceRequest request, ClaimsPrincipal user,
        IUrabaUseCases useCases, CancellationToken ct)
        => useCases.UpdateServiceAsync(UserId(user), businessId, serviceId, request, ct))
    .RequireAuthorization("BusinessConfiguration.Manage");
privateApi.MapGet("/{businessId:guid}/services",
    (Guid businessId, ClaimsPrincipal user, IUrabaUseCases useCases, CancellationToken ct)
        => useCases.GetServicesAsync(UserId(user), businessId, ct))
    .RequireAuthorization("BusinessConfiguration.Manage");
privateApi.MapPost("/{businessId:guid}/services",
    async (Guid businessId, CreateServiceRequest request, ClaimsPrincipal user,
        IUrabaUseCases useCases, CancellationToken ct) =>
        Results.Created("", await useCases.CreateServiceAsync(UserId(user), businessId, request, ct)))
    .RequireAuthorization("BusinessConfiguration.Manage");
privateApi.MapDelete("/{businessId:guid}/services/{serviceId:guid}",
    async (Guid businessId, Guid serviceId, ClaimsPrincipal user, IUrabaUseCases useCases, CancellationToken ct) =>
    { await useCases.DeactivateServiceAsync(UserId(user), businessId, serviceId, ct); return Results.NoContent(); })
    .RequireAuthorization("BusinessConfiguration.Manage");
privateApi.MapGet("/{businessId:guid}/staff",
    (Guid businessId, ClaimsPrincipal user, IUrabaUseCases useCases, CancellationToken ct)
        => useCases.GetStaffAsync(UserId(user), businessId, ct))
    .RequireAuthorization("BusinessConfiguration.Manage");
privateApi.MapPost("/{businessId:guid}/staff",
    async (Guid businessId, SaveStaffMemberRequest request, ClaimsPrincipal user,
        IUrabaUseCases useCases, CancellationToken ct) =>
        Results.Created("", await useCases.CreateStaffAsync(UserId(user), businessId, request, ct)))
    .RequireAuthorization("BusinessConfiguration.Manage");
privateApi.MapPut("/{businessId:guid}/staff/{staffId:guid}",
    (Guid businessId, Guid staffId, SaveStaffMemberRequest request, ClaimsPrincipal user,
        IUrabaUseCases useCases, CancellationToken ct)
        => useCases.UpdateStaffAsync(UserId(user), businessId, staffId, request, ct))
    .RequireAuthorization("BusinessConfiguration.Manage");
privateApi.MapGet("/{businessId:guid}/hours",
    (Guid businessId, ClaimsPrincipal user, IUrabaUseCases useCases, CancellationToken ct)
        => useCases.GetBusinessHoursAsync(UserId(user), businessId, ct))
    .RequireAuthorization("BusinessConfiguration.Manage");
privateApi.MapPut("/{businessId:guid}/hours/{day}",
    (Guid businessId, DayOfWeek day, SaveBusinessHourRequest request, ClaimsPrincipal user,
        IUrabaUseCases useCases, CancellationToken ct)
        => useCases.SetBusinessHourAsync(UserId(user), businessId, day, request, ct))
    .RequireAuthorization("BusinessConfiguration.Manage");
privateApi.MapGet("/{businessId:guid}/availability-exceptions",
    (Guid businessId, DateOnly? from, ClaimsPrincipal user, IUrabaUseCases useCases, CancellationToken ct)
        => useCases.GetAvailabilityExceptionsAsync(UserId(user), businessId, from, ct))
    .RequireAuthorization("BusinessConfiguration.Manage");
privateApi.MapPost("/{businessId:guid}/availability-exceptions",
    async (Guid businessId, SaveAvailabilityExceptionRequest request, ClaimsPrincipal user,
        IUrabaUseCases useCases, CancellationToken ct) =>
        Results.Created("", await useCases.SaveAvailabilityExceptionAsync(UserId(user), businessId, request, ct)))
    .RequireAuthorization("BusinessConfiguration.Manage");
privateApi.MapDelete("/{businessId:guid}/availability-exceptions/{exceptionId:guid}",
    async (Guid businessId, Guid exceptionId, long version, ClaimsPrincipal user, IUrabaUseCases useCases, CancellationToken ct) =>
    { await useCases.DeleteAvailabilityExceptionAsync(UserId(user), businessId, exceptionId, version, ct); return Results.NoContent(); })
    .RequireAuthorization("BusinessConfiguration.Manage");
privateApi.MapGet("/{businessId:guid}/memberships",
    (Guid businessId, ClaimsPrincipal user, IUrabaUseCases useCases, CancellationToken ct)
        => useCases.ListMembersAsync(UserId(user), businessId, ct))
    .RequireAuthorization("Workers.Manage");
privateApi.MapGet("/{businessId:guid}/memberships/{membershipId:guid}",
    (Guid businessId, Guid membershipId, ClaimsPrincipal user, IUrabaUseCases useCases, CancellationToken ct)
        => useCases.GetMemberAsync(UserId(user), businessId, membershipId, ct))
    .RequireAuthorization("Workers.Manage");
privateApi.MapPost("/{businessId:guid}/memberships/link-existing",
    async (Guid businessId, LinkExistingMemberRequest request, ClaimsPrincipal user,
        IUrabaUseCases useCases, CancellationToken ct) =>
        Results.Created("", await useCases.LinkExistingMemberAsync(UserId(user), businessId, request, ct)))
    .RequireAuthorization("Workers.Manage");
if (app.Environment.IsDevelopment())
{
    privateApi.MapPost("/{businessId:guid}/memberships/create-development",
        async (Guid businessId, CreateDevelopmentMemberRequest request, ClaimsPrincipal user,
            IUrabaUseCases useCases, CancellationToken ct) =>
            Results.Created("", await useCases.CreateDevelopmentMemberAsync(UserId(user), businessId, request, ct)))
        .RequireAuthorization("Workers.Manage");
}
privateApi.MapPut("/{businessId:guid}/memberships/{membershipId:guid}/permissions",
    (Guid businessId, Guid membershipId, UpdateMemberPermissionsRequest request, ClaimsPrincipal user,
        IUrabaUseCases useCases, CancellationToken ct)
        => useCases.UpdateMemberPermissionsAsync(UserId(user), businessId, membershipId, request, ct))
    .RequireAuthorization("Workers.Manage");
privateApi.MapPost("/{businessId:guid}/memberships/{membershipId:guid}/activate",
    (Guid businessId, Guid membershipId, MembershipVersionRequest request, ClaimsPrincipal user,
        IUrabaUseCases useCases, CancellationToken ct)
        => useCases.ActivateMemberAsync(UserId(user), businessId, membershipId, request.Version, ct))
    .RequireAuthorization("Workers.Manage");
privateApi.MapPost("/{businessId:guid}/memberships/{membershipId:guid}/deactivate",
    (Guid businessId, Guid membershipId, MembershipVersionRequest request, ClaimsPrincipal user,
        IUrabaUseCases useCases, CancellationToken ct)
        => useCases.DeactivateMemberAsync(UserId(user), businessId, membershipId, request.Version, ct))
    .RequireAuthorization("Workers.Manage");
privateApi.MapPost("/{businessId:guid}/memberships/{membershipId:guid}/grant-owner",
    (Guid businessId, Guid membershipId, MembershipVersionRequest request, ClaimsPrincipal user,
        IUrabaUseCases useCases, CancellationToken ct)
        => useCases.GrantOwnershipAsync(UserId(user), businessId, membershipId, request.Version, ct))
    .RequireAuthorization("Workers.Manage");
privateApi.MapPost("/{businessId:guid}/memberships/{membershipId:guid}/revoke-owner",
    (Guid businessId, Guid membershipId, RevokeOwnershipRequest request, ClaimsPrincipal user,
        IUrabaUseCases useCases, CancellationToken ct)
        => useCases.RevokeOwnershipAsync(UserId(user), businessId, membershipId, request, ct))
    .RequireAuthorization("Workers.Manage");
privateApi.MapGet("/{businessId:guid}/memberships/{membershipId:guid}/audit",
    (Guid businessId, Guid membershipId, ClaimsPrincipal user, IUrabaUseCases useCases, CancellationToken ct)
        => useCases.ListMembershipAuditAsync(UserId(user), businessId, membershipId, ct))
    .RequireAuthorization("Workers.Manage");
privateApi.MapGet("/{businessId:guid}/queue",
    (Guid businessId, ClaimsPrincipal user, IQueueUseCases queues, CancellationToken ct)
        => queues.GetAdminAsync(UserId(user), businessId, ct));
privateApi.MapPut("/{businessId:guid}/queue-definition",
    (Guid businessId, SaveQueueDefinitionRequest request, ClaimsPrincipal user, IQueueUseCases queues, CancellationToken ct)
        => queues.SaveDefinitionAsync(UserId(user), businessId, request, ct));
privateApi.MapPost("/{businessId:guid}/queue/open",
    (Guid businessId, ClaimsPrincipal user, IQueueUseCases queues, CancellationToken ct)
        => queues.OpenAsync(UserId(user), businessId, ct));
privateApi.MapPost("/{businessId:guid}/queue/pause",
    (Guid businessId, QueueSessionCommandRequest request, ClaimsPrincipal user,
        IQueueUseCases queues, CancellationToken ct) =>
        queues.PauseAsync(UserId(user), businessId, request.Version, ct));
privateApi.MapPost("/{businessId:guid}/queue/resume",
    (Guid businessId, QueueSessionCommandRequest request, ClaimsPrincipal user,
        IQueueUseCases queues, CancellationToken ct) =>
        queues.ResumeAsync(UserId(user), businessId, request.Version, ct));
privateApi.MapPost("/{businessId:guid}/queue/close",
    (Guid businessId, QueueSessionCommandRequest request, ClaimsPrincipal user,
        IQueueUseCases queues, CancellationToken ct) =>
        queues.CloseAsync(UserId(user), businessId, request.Version, ct));
privateApi.MapPost("/{businessId:guid}/queue/tickets/walk-in",
    async (Guid businessId, CreateQueueTicketRequest request, ClaimsPrincipal user,
        IQueueUseCases queues, CancellationToken ct) =>
        Results.Created("", await queues.WalkInAsync(UserId(user), businessId, request, ct)));
privateApi.MapPost("/{businessId:guid}/queue/call-next",
    (Guid businessId, QueueSessionCommandRequest request, ClaimsPrincipal user,
        IQueueUseCases queues, CancellationToken ct) =>
        queues.CallNextAsync(UserId(user), businessId, request.Version, ct));
privateApi.MapPost("/{businessId:guid}/queue/tickets/{ticketId:guid}/{action}",
    (Guid businessId, Guid ticketId, string action, QueueTicketCommandRequest request,
        ClaimsPrincipal user, IQueueUseCases queues, CancellationToken ct) =>
        queues.ChangeTicketAsync(UserId(user), businessId, ticketId, action, request, ct));
privateApi.MapGet("/{businessId:guid}/order-settings",
    (Guid businessId, ClaimsPrincipal user, IOrderingUseCases orders, CancellationToken ct)
        => orders.GetSettingsAsync(UserId(user), businessId, ct));
privateApi.MapPut("/{businessId:guid}/order-settings",
    (Guid businessId, SavePickupOrderSettingsRequest request, ClaimsPrincipal user,
        IOrderingUseCases orders, CancellationToken ct) => orders.SaveSettingsAsync(UserId(user), businessId, request, ct));
privateApi.MapGet("/{businessId:guid}/product-categories",
    (Guid businessId, ClaimsPrincipal user, IOrderingUseCases orders, CancellationToken ct)
        => orders.GetCategoriesAsync(UserId(user), businessId, ct));
privateApi.MapPost("/{businessId:guid}/product-categories",
    async (Guid businessId, SaveProductCategoryRequest request, ClaimsPrincipal user,
        IOrderingUseCases orders, CancellationToken ct) =>
        Results.Created("", await orders.SaveCategoryAsync(UserId(user), businessId, null, request, ct)));
privateApi.MapPut("/{businessId:guid}/product-categories/{categoryId:guid}",
    (Guid businessId, Guid categoryId, SaveProductCategoryRequest request, ClaimsPrincipal user,
        IOrderingUseCases orders, CancellationToken ct)
        => orders.SaveCategoryAsync(UserId(user), businessId, categoryId, request, ct));
privateApi.MapGet("/{businessId:guid}/products",
    (Guid businessId, ClaimsPrincipal user, IOrderingUseCases orders, CancellationToken ct)
        => orders.GetProductsAsync(UserId(user), businessId, ct));
privateApi.MapPost("/{businessId:guid}/products",
    async (Guid businessId, SaveProductRequest request, ClaimsPrincipal user,
        IOrderingUseCases orders, CancellationToken ct) =>
        Results.Created("", await orders.SaveProductAsync(UserId(user), businessId, null, request, ct)));
privateApi.MapPut("/{businessId:guid}/products/{productId:guid}",
    (Guid businessId, Guid productId, SaveProductRequest request, ClaimsPrincipal user,
        IOrderingUseCases orders, CancellationToken ct)
        => orders.SaveProductAsync(UserId(user), businessId, productId, request, ct));
privateApi.MapGet("/{businessId:guid}/orders",
    (Guid businessId, string? status, DateOnly? date, ClaimsPrincipal user,
        IOrderingUseCases orders, CancellationToken ct)
        => orders.ListOrdersAsync(UserId(user), businessId, status, date, ct));
privateApi.MapPost("/{businessId:guid}/orders/{orderId:guid}/{action}",
    (Guid businessId, Guid orderId, string action, PickupOrderCommandRequest request,
        ClaimsPrincipal user, IOrderingUseCases orders, CancellationToken ct)
        => orders.ChangeStatusAsync(UserId(user), businessId, orderId, action, request, ct));

app.MapHub<QueueHub>("/hubs/queue");
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(UrabaConecta.Web.Client.Pages.Home).Assembly);
app.MapAdditionalIdentityEndpoints();
// El esquema primero, en todo ambiente: sembrar o crear cuentas contra una base sin migrar
// falla de formas que cuesta leer. Un fallo aquí no derriba el proceso, marca la readiness.
await app.Services.MigrateDatabaseAsync(app.Environment);
await app.Services.SeedDevelopmentAsync(app.Environment);
await app.Services.BootstrapDemoAdminAsync(app.Environment);
await app.Services.NormalizeDemoAccessAsync(app.Environment);
await app.Services.BootstrapProductionAdminAsync(app.Environment);
await app.RunAsync();

static Guid UserId(ClaimsPrincipal user)
    => Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

/// <summary>El rol y la IP se derivan de la petición autenticada, nunca de la carga útil del cliente.</summary>
static PlatformActor Actor(HttpContext http)
    => new(UserId(http.User), http.User.IsInRole("PlatformAdmin"), http.User.IsInRole("PartnerOperator"),
        http.Connection.RemoteIpAddress?.ToString(), http.TraceIdentifier,
        // El rol por sí solo no abre nada: cada caso de uso confirma la membresía del negocio concreto.
        http.User.IsInRole("BusinessOwner"));

public partial class Program;
