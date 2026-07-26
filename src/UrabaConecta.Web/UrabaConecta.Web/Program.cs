using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using UrabaConecta.Application;
using UrabaConecta.Contracts;
using UrabaConecta.Infrastructure.Identity;
using UrabaConecta.Infrastructure.Persistence;
using UrabaConecta.Infrastructure.Security;
using UrabaConecta.Web.Components;
using UrabaConecta.Web.Components.Account;
using UrabaConecta.Web.Services;

var builder = WebApplication.CreateBuilder(args);
if (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var railwayPort))
    builder.WebHost.UseUrls($"http://0.0.0.0:{railwayPort}");
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.SignalR", LogLevel.Warning);
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
if (!builder.Environment.IsDevelopment())
{
    builder.Services.ConfigureApplicationCookie(options =>
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always);
}
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("BusinessMember", policy => policy.RequireAuthenticatedUser())
    .AddPolicy("BusinessOwner", policy => policy.RequireRole("BusinessOwner"))
    .AddPolicy("Appointments.Manage", policy => policy.RequireRole("BusinessOwner", "BusinessWorker"))
    .AddPolicy("BusinessProfile.Manage", policy => policy.RequireRole("BusinessOwner"))
    .AddPolicy("BusinessConfiguration.Manage", policy => policy.RequireRole("BusinessOwner", "BusinessWorker"))
    .AddPolicy("Workers.Manage", policy => policy.RequireRole("BusinessOwner", "BusinessWorker"))
    .AddPolicy("PlatformAdmin", policy => policy.RequireRole("PlatformAdmin"));
builder.Services.AddIdentityCore<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = true;
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 10;
}).AddRoles<IdentityRole<Guid>>()
  .AddEntityFrameworkStores<AppDbContext>()
  .AddSignInManager()
  .AddDefaultTokenProviders();
builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("UrabaConecta");
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
else if (builder.Environment.IsEnvironment("Demo"))
    throw new InvalidOperationException("Falta DataProtection:KeysPath para el ambiente Demo.");
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddScoped<IUrabaStore, UrabaStore>();
builder.Services.AddScoped<IMembershipAdministrationStore, MembershipAdministrationStore>();
builder.Services.AddScoped<IQueueStore, QueueStore>();
builder.Services.AddScoped<IOrderingStore, OrderingStore>();
builder.Services.AddScoped<IPlatformAdministrationStore, PlatformAdministrationStore>();
builder.Services.AddScoped<IIdentityAccountManager, IdentityAccountManager>();
builder.Services.AddScoped<IPublicCodeService, PublicCodeService>();
builder.Services.AddScoped<UrabaConecta.Application.IPersonalDataProtector, PersonalDataProtector>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IUrabaUseCases, UrabaUseCases>();
builder.Services.AddScoped<IQueueUseCases, QueueUseCases>();
builder.Services.AddScoped<IOrderingUseCases, OrderingUseCases>();
builder.Services.AddScoped<IPlatformAdministrationUseCases, PlatformAdministrationUseCases>();
builder.Services.AddScoped<IQueueChangeNotifier, SignalRQueueChangeNotifier>();
builder.Services.AddScoped<IUrabaConectaApi, ServerUrabaConectaApi>();
builder.Services.AddSignalR();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("postgresql", tags: ["ready"]);
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
app.UseForwardedHeaders();
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
    context.Response.Headers.ContentSecurityPolicy =
        "default-src 'self'; style-src 'self' 'unsafe-inline'; script-src 'self' 'wasm-unsafe-eval'; img-src 'self' data:; connect-src 'self' ws: wss:";
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

var platformApi = app.MapGroup("/api/v1/admin").RequireAuthorization("PlatformAdmin");
platformApi.MapGet("/businesses",
    (string? q, string? municipality, string? status, string? module,
        IPlatformAdministrationUseCases useCases, CancellationToken ct) =>
        useCases.ListAsync(q, municipality, status, module, ct));
platformApi.MapGet("/businesses/{businessId:guid}",
    (Guid businessId, IPlatformAdministrationUseCases useCases, CancellationToken ct) =>
        useCases.GetAsync(businessId, ct));
platformApi.MapPost("/businesses",
    async (CreatePlatformBusinessRequest request, ClaimsPrincipal user,
        IPlatformAdministrationUseCases useCases, CancellationToken ct) =>
        Results.Created("", await useCases.CreateAsync(UserId(user), request, ct)));
platformApi.MapPut("/businesses/{businessId:guid}",
    (Guid businessId, UpdatePlatformBusinessRequest request, ClaimsPrincipal user,
        IPlatformAdministrationUseCases useCases, CancellationToken ct) =>
        useCases.UpdateAsync(UserId(user), businessId, request, ct));
platformApi.MapPost("/businesses/{businessId:guid}/{action}",
    (Guid businessId, string action, PlatformBusinessStateRequest request, ClaimsPrincipal user,
        IPlatformAdministrationUseCases useCases, CancellationToken ct) =>
        useCases.ChangeStateAsync(UserId(user), businessId, action, request, ct));
platformApi.MapPut("/businesses/{businessId:guid}/modules",
    (Guid businessId, UpdatePlatformModulesRequest request, ClaimsPrincipal user,
        IPlatformAdministrationUseCases useCases, CancellationToken ct) =>
        useCases.UpdateModulesAsync(UserId(user), businessId, request, ct));

var privateApi = app.MapGroup("/api/v1/businesses").RequireAuthorization("BusinessMember");
privateApi.MapGet("/mine", (ClaimsPrincipal user, IUrabaUseCases useCases, CancellationToken ct)
    => useCases.GetMyBusinessesAsync(UserId(user), ct));
privateApi.MapGet("/{businessId:guid}/appointments",
    (Guid businessId, DateOnly? date, string? status, ClaimsPrincipal user, IUrabaUseCases useCases, CancellationToken ct)
        => useCases.GetAppointmentsAsync(UserId(user), businessId, date, status, ct))
    .RequireAuthorization("Appointments.Manage");
privateApi.MapPost("/{businessId:guid}/appointments/{appointmentId:guid}/status",
    (Guid businessId, Guid appointmentId, ChangeAppointmentStatusRequest request, ClaimsPrincipal user,
        IUrabaUseCases useCases, CancellationToken ct)
        => useCases.ChangeStatusAsync(UserId(user), businessId, appointmentId, request, ct))
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
await app.Services.SeedDevelopmentAsync(app.Environment);
await app.RunAsync();

static Guid UserId(ClaimsPrincipal user)
    => Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

public partial class Program;
