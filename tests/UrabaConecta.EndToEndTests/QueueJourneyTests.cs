using Microsoft.Playwright;
using Microsoft.AspNetCore.SignalR.Client;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.EndToEndTests;

public sealed class QueueJourneyTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    [Fact]
    public async Task Seven_queue_scenarios_work_in_real_chromium()
    {
        await using var visitorContext = await MobileContext();
        var visitor = await visitorContext.NewPageAsync();

        // 1. Directorio -> perfil -> fila.
        await visitor.GotoAsync(fixture.BaseUrl);
        await visitor.GetByLabel("Qué buscas").FillAsync("El Corte");
        await visitor.GetByRole(AriaRole.Button, new() { Name = "Buscar" }).ClickAsync();
        await Expect(visitor.GetByText("Barbería El Corte")).ToBeVisibleAsync();
        await visitor.GotoAsync($"{fixture.BaseUrl}/negocios/barberia-el-corte");
        await Expect(visitor.GetByRole(AriaRole.Heading, new() { Name = "Barbería El Corte" })).ToBeVisibleAsync();
        await Expect(visitor.GetByRole(AriaRole.Link, new() { Name = "Ver fila virtual" })).ToBeVisibleAsync();
        await visitor.GotoAsync($"{fixture.BaseUrl}/negocios/barberia-el-corte/turnos");
        await Expect(visitor.GetByRole(AriaRole.Heading, new() { Name = "Atención general" })).ToBeVisibleAsync();
        await using var publicHub = new HubConnectionBuilder().WithUrl($"{fixture.BaseUrl}/hubs/queue").Build();
        var publicChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        publicHub.On("QueueChanged", () => publicChanged.TrySetResult());
        await publicHub.StartAsync();
        await publicHub.InvokeAsync("SubscribePublic", "barberia-el-corte");

        // 2. Toma y conserva su código individual.
        await visitor.GetByLabel("Alias corto (opcional)").FillAsync("E2E");
        // Desde V5 el turno público también exige aceptar el aviso de tratamiento de datos.
        await visitor.GetByLabel("Acepto que este alias se use para gestionar mi turno y anunciar el llamado.")
            .CheckAsync();
        await visitor.GetByRole(AriaRole.Button, new() { Name = "Tomar turno" }).ClickAsync();
        await Expect(visitor.GetByTestId("queue-created")).ToBeVisibleAsync();
        await publicChanged.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await visitor.GetByRole(AriaRole.Link, new() { Name = "Seguir mi turno" }).ClickAsync();
        // El tablero de la fila nombra la cifra en singular o en plural según cuánta gente hay:
        // "En espera" era una etiqueta de panel, no algo que alguien diga.
        await Expect(visitor.GetByText("Personas esperando", new() { Exact = true })
            .Or(visitor.GetByText("Persona esperando", new() { Exact = true })).First)
            .ToBeVisibleAsync();
        var trackingCode = visitor.Url.Split('/').Last();
        await using var ticketHub = new HubConnectionBuilder().WithUrl($"{fixture.BaseUrl}/hubs/queue").Build();
        var ticketChanged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ticketHub.On("TicketChanged", () => ticketChanged.TrySetResult());
        await ticketHub.StartAsync();
        await ticketHub.InvokeAsync("SubscribeTicket", trackingCode);

        // 3. Operador autorizado abre el panel móvil.
        await using var operatorContext = await MobileContext();
        var operations = await operatorContext.NewPageAsync();
        await Login(operations, DevelopmentSeeder.CorteQueueWorkerEmail);
        await operations.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.CorteBusinessId}/turnos");
        await Expect(operations.GetByRole(AriaRole.Heading, new() { Name = "Turnos virtuales" })).ToBeVisibleAsync();

        // 4. Llamado desde el panel operativo.
        await operations.GetByRole(AriaRole.Button, new() { Name = "Llamar siguiente" }).ClickAsync();
        // En el tablero del negocio el turno está "Llamado"; "Te estamos llamando" es la frase que lee
        // el cliente en su seguimiento, y en esta pantalla se leía como si el turno hablara solo.
        await Expect(operations.Locator(".queue-row").Filter(new() { HasTextString = "Llamado" })).ToBeVisibleAsync();
        await ticketChanged.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // 5. Atención y cierre de turno.
        await operations.GetByRole(AriaRole.Button, new() { Name = "Iniciar atención" }).ClickAsync();
        await Expect(operations.Locator(".queue-row").Filter(new() { HasTextString = "En atención" })).ToBeVisibleAsync();
        await operations.GetByRole(AriaRole.Button, new() { Name = "Completar" }).ClickAsync();
        await visitor.ReloadAsync();
        await Expect(visitor.GetByText("Atendido")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // 6. Pausa, reanudación y configuración sin cancelar historia.
        await operations.GetByRole(AriaRole.Button, new() { Name = "Pausar" }).ClickAsync();
        await Expect(operations.GetByText("Pausada")).ToBeVisibleAsync();
        await operations.GetByRole(AriaRole.Button, new() { Name = "Reanudar" }).ClickAsync();
        await Expect(operations.GetByRole(AriaRole.Link, new() { Name = "Configurar" })).ToBeVisibleAsync();
        await operations.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.CorteBusinessId}/configuracion/turnos");
        await Expect(operations.GetByRole(AriaRole.Heading, new() { Name = "Fila virtual" })).ToBeVisibleAsync();
        await operations.GetByLabel("Mensaje público").FillAsync("Mensaje E2E sin cancelar turnos.");
        await operations.GetByRole(AriaRole.Button, new() { Name = "Guardar configuración" }).ClickAsync();
        await Expect(operations.GetByText("Configuración guardada.")).ToBeVisibleAsync();

        // 7. Aislamiento y diseño a 360 px.
        await using var deniedContext = await MobileContext();
        var denied = await deniedContext.NewPageAsync();
        await Login(denied, DevelopmentSeeder.OtherOwnerEmail);
        await denied.GotoAsync($"{fixture.BaseUrl}/panel/{DevelopmentSeeder.CorteBusinessId}/turnos");
        await Expect(denied.GetByText("No tiene permiso para administrar turnos.")).ToBeVisibleAsync();
        Assert.False(await visitor.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"));
        Assert.False(await operations.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"));
    }

    private async Task<IBrowserContext> MobileContext() => await fixture.Browser.NewContextAsync(new()
    { ViewportSize = new() { Width = 360, Height = 800 } });
    private async Task Login(IPage page, string email)
    {
        await page.GotoAsync($"{fixture.BaseUrl}/Account/Login");
        await page.GetByLabel("Correo").FillAsync(email);
        await page.GetByLabel("Contraseña").FillAsync(DevelopmentSeeder.DemoPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Ingresar" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/panel"));
    }
    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
