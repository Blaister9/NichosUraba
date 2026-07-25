using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
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
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("BusinessMember", policy => policy.RequireAuthenticatedUser())
    .AddPolicy("BusinessOwner", policy => policy.RequireRole("BusinessOwner"))
    .AddPolicy("Appointments.Manage", policy => policy.RequireRole("BusinessOwner", "BusinessWorker"))
    .AddPolicy("BusinessProfile.Manage", policy => policy.RequireRole("BusinessOwner"))
    .AddPolicy("BusinessConfiguration.Manage", policy => policy.RequireRole("BusinessOwner", "BusinessWorker"));
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
builder.Services.AddDataProtection();
builder.Services.AddScoped<IUrabaStore, UrabaStore>();
builder.Services.AddScoped<IPublicCodeService, PublicCodeService>();
builder.Services.AddScoped<UrabaConecta.Application.IPersonalDataProtector, PersonalDataProtector>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IUrabaUseCases, UrabaUseCases>();
builder.Services.AddScoped<IUrabaConectaApi, ServerUrabaConectaApi>();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("postgresql", tags: ["ready"]);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("public-write", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 12, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();
if (app.Environment.IsDevelopment()) app.UseWebAssemblyDebugging();
else { app.UseExceptionHandler(); app.UseHsts(); }
app.UseExceptionHandler();
app.UseStatusCodePagesWithReExecute("/not-found");
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
    .RequireRateLimiting("public-write");
publicApi.MapPost("/appointments/{code}/cancel",
    async (string code, IUrabaUseCases useCases, CancellationToken ct) =>
    { await useCases.CancelAsync(code, ct); return Results.NoContent(); }).RequireRateLimiting("public-write");

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
