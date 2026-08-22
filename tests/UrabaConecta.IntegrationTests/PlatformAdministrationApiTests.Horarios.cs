using System.Net;
using System.Net.Http.Json;
using UrabaConecta.Contracts;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

/// <summary>
/// El horario de un negocio que sólo despacha pedidos.
///
/// Hasta la cohorte 01 el horario era asunto de las citas: el alta sólo lo creaba con
/// <c>Appointments</c> y el checklist sólo lo exigía con <c>Appointments</c>. Pero las franjas
/// para recoger se calculan cruzando el horario del negocio con la ventana de pedidos, así que un
/// negocio de sólo pedidos se daba de alta, llegaba al 100 % del checklist, se publicaba… y su
/// pantalla de pedido no ofrecía una sola hora. Le pasó al primer negocio real del piloto.
///
/// Estas pruebas recorren la cadena entera —alta, checklist, publicación y franjas públicas—
/// porque el defecto no vivía en ninguna de esas piezas por separado: vivía en la costura.
/// </summary>
public sealed partial class PlatformAdministrationApiTests
{
    [Fact]
    public async Task An_order_only_business_is_born_with_hours_and_can_be_ordered_from()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var catalog = (await admin.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json))!;
        var creado = await CrearSoloPedidosAsync(admin, catalog);

        Assert.Contains("PickupOrders", creado.Modules);
        Assert.DoesNotContain("Appointments", creado.Modules);

        // 1. El alta le deja horario aunque no abra la agenda.
        var horario = (await admin.GetFromJsonAsync<List<BusinessHourAdminDto>>(
            $"/api/v1/admin/businesses/{creado.Id}/hours", Json))!;
        Assert.All(new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
            DayOfWeek.Friday, DayOfWeek.Saturday },
            dia => Assert.Contains(horario, x => x.Day == dia && !x.IsClosed));
        Assert.Contains(horario, x => x.Day == DayOfWeek.Sunday && x.IsClosed);

        // 2. El checklist lo cuenta como requisito suyo, y lo da por cumplido.
        Assert.Contains(creado.Readiness, x => x.Key == "hours" && x.IsApplicable && x.IsComplete);

        // 3. Publicado, la pantalla de pedido ofrece franjas el día que abre…
        var publicado = await PublicarAsync(admin, creado, catalog);
        Assert.True(publicado.IsPublished);
        using var publico = Client();
        var abierto = await FranjasAsync(publico, publicado.Slug, ProximoDia(DayOfWeek.Tuesday));
        Assert.NotEmpty(abierto);

        // …y ninguna el día que cierra, que es la otra mitad de la misma regla.
        var cerrado = await FranjasAsync(publico, publicado.Slug, ProximoDia(DayOfWeek.Sunday));
        Assert.Empty(cerrado);
    }

    [Fact]
    public async Task Closing_every_day_leaves_the_order_only_business_without_slots_and_off_the_checklist()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var catalog = (await admin.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json))!;
        var creado = await CrearSoloPedidosAsync(admin, catalog);

        // Cerrar todos los días es la forma de reproducir el estado en el que nacían antes: con
        // pedidos abiertos y sin una sola hora de atención registrada.
        foreach (var dia in Enum.GetValues<DayOfWeek>())
        {
            var actual = (await admin.GetFromJsonAsync<List<BusinessHourAdminDto>>(
                $"/api/v1/admin/businesses/{creado.Id}/hours", Json))!.Single(x => x.Day == dia);
            if (actual.IsClosed) continue;
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(
                $"/api/v1/admin/businesses/{creado.Id}/hours/{dia}",
                new SaveBusinessHourRequest { IsClosed = true, Version = actual.Version }, Json)).StatusCode);
        }

        // El checklist deja de darlo por listo, que es lo que antes no pasaba.
        var revisado = (await admin.GetFromJsonAsync<PlatformBusinessDto>(
            $"/api/v1/admin/businesses/{creado.Id}", Json))!;
        Assert.Contains(revisado.Readiness, x => x.Key == "hours" && x.IsApplicable && !x.IsComplete);
        Assert.False(revisado.IsReady);
        Assert.Contains("Configure el horario de atención.", revisado.MissingLabels ?? []);

        // Y la administración ya no puede mandarlo a revisión sin resolverlo.
        var enviado = await admin.PostAsJsonAsync($"/api/v1/admin/businesses/{creado.Id}/submit-review",
            new SubmitForReviewRequest { Version = revisado.Version }, Json);
        Assert.Equal(HttpStatusCode.Conflict, enviado.StatusCode);
    }

    /// <summary>Alta de un negocio con pedidos y sin citas, ya con una fila de catálogo.</summary>
    private async Task<PlatformBusinessDto> CrearSoloPedidosAsync(HttpClient admin, PlatformBusinessListDto catalog)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/admin/businesses", new CreatePlatformBusinessRequest
        {
            Name = "Tienda de sólo pedidos", Slug = $"solo-pedidos-{Guid.NewGuid():N}",
            MunicipalityId = catalog.Municipalities[0].Id, CategoryId = catalog.Categories[0].Id,
            ShortDescription = "Catálogo para recoger, sin agenda.",
            Description = "Negocio de prueba que sólo despacha pedidos para recoger.",
            Appointments = false, VirtualQueues = false, PickupOrders = true,
            InitialProductCategory = "Despensa", InitialProductName = "Producto de prueba",
            InitialProductPrice = 12000,
            ExistingOwnerEmail = DevelopmentSeeder.BellaOwnerEmail, SaveAsDraft = true,
        }, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var creado = (await response.Content.ReadFromJsonAsync<PlatformBusinessCreatedDto>(Json))!.Business;
        return (await admin.GetFromJsonAsync<PlatformBusinessDto>(
            $"/api/v1/admin/businesses/{creado.Id}", Json))!;
    }

    private static async Task<PlatformBusinessDto> PublicarAsync(HttpClient admin, PlatformBusinessDto negocio,
        PlatformBusinessListDto catalog)
    {
        var listo = await CompleteChecklistAsync(admin, negocio, catalog);
        Assert.True(listo.IsReady, "Falta completar: " + string.Join(", ", listo.MissingLabels ?? []));
        var activado = await admin.PostAsJsonAsync($"/api/v1/admin/businesses/{listo.Id}/activate",
            new PlatformBusinessStateRequest { Version = listo.Version }, Json);
        Assert.Equal(HttpStatusCode.OK, activado.StatusCode);
        return (await activado.Content.ReadFromJsonAsync<PlatformBusinessDto>(Json))!;
    }

    private static async Task<IReadOnlyList<PickupSlotDto>> FranjasAsync(HttpClient client, string slug, DateOnly fecha)
        => (await client.GetFromJsonAsync<PickupSlotListDto>(
            $"/api/v1/public/businesses/{slug}/pickup-slots?date={fecha:yyyy-MM-dd}", Json))!.Slots;

    /// <summary>
    /// El próximo día de la semana pedido, siempre a más de un día vista: las franjas anteriores a
    /// la preparación mínima no se generan, y un mismo día podría quedarse sin ninguna por la hora
    /// a la que corra la prueba.
    /// </summary>
    private static DateOnly ProximoDia(DayOfWeek dia)
    {
        var fecha = DateOnly.FromDateTime(DateTime.UtcNow.Date).AddDays(2);
        while (fecha.DayOfWeek != dia) fecha = fecha.AddDays(1);
        return fecha;
    }
}
