using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Playwright;
using Testcontainers.PostgreSql;

namespace UrabaConecta.EndToEndTests;

public sealed class BrowserFixture : IAsyncLifetime
{
    /// <summary>
    /// Igual que en las pruebas de integración: contenedor por omisión, o la base que indique
    /// URABACONECTA_TEST_PG cuando Docker no está disponible en la máquina.
    /// </summary>
    private readonly Lazy<PostgreSqlContainer> _contenedor = new(() => new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("urabaconecta_e2e").WithUsername("e2e").WithPassword("e2e-only-password").Build());
    private PostgreSqlContainer _postgres => _contenedor.Value;
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _log = new();
    private Process? _app;
    private IPlaywright? _playwright;
    public IBrowser Browser { get; private set; } = default!;
    public string BaseUrl { get; private set; } = "";

    /// <summary>
    /// La misma base que usa la aplicación. La expone para los escenarios que el sembrado no cubre
    /// —una socia que además es propietaria, por ejemplo— y que sólo se pueden montar añadiendo el
    /// dato: la aplicación corre en otro proceso, así que no hay contenedor de servicios compartido.
    /// </summary>
    public string ConnectionString { get; private set; } = "";

    private static readonly string? Externa = Environment.GetEnvironmentVariable("URABACONECTA_TEST_PG");

    public async Task InitializeAsync()
    {
        if (Externa is null) await _postgres.StartAsync();
        var root = FindRepositoryRoot();
        var configuration = AppContext.BaseDirectory.Contains(
            $"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";
        var webDll = Path.Combine(root, "src", "UrabaConecta.Web", "UrabaConecta.Web", "bin", configuration,
            "net10.0", "UrabaConecta.Web.dll");
        if (!File.Exists(webDll)) throw new InvalidOperationException($"Compile primero la solución. No existe {webDll}");
        var port = FreePort();
        BaseUrl = $"http://127.0.0.1:{port}";
        var start = new ProcessStartInfo("dotnet", $"\"{webDll}\"")
        {
            WorkingDirectory = Path.GetDirectoryName(webDll)!,
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["ASPNETCORE_URLS"] = BaseUrl;
        ConnectionString = Externa ?? _postgres.GetConnectionString();
        start.Environment["ConnectionStrings__DefaultConnection"] = ConnectionString;
        start.Environment["URABACONECTA_TRACKING_HMAC_KEY"] = "e2e-test-hmac-key-with-at-least-32-bytes";
        // Demo y Production sí tienen Web Push configurado, así que sin estas claves el navegador
        // recibía "avisos no disponibles" y la mitad de la pantalla de seguimiento no existía en
        // las pruebas. No se envía nada: ninguna prueba llega a suscribirse a un servicio real.
        start.Environment["WebPush__Subject"] = "mailto:e2e@urabaconecta.test";
        start.Environment["WebPush__PublicKey"] = "BJ0dHVpsUbYyO0BdEwoBNtZUCVOr0YrPPqLNTOhOBc9SPXBRcYE0BSzZpsYFDqSjnbfWLxpjLzMFhcbXHWpUmSA";
        start.Environment["WebPush__PrivateKey"] = "Mm9CQ1RhWVVpOFVFbUJnQzlLc2NnV3RkUjFNSXlKV1E";
        start.Environment["RateLimits__PublicWritesPerMinute"] = "200";
        _app = Process.Start(start) ?? throw new InvalidOperationException("No fue posible iniciar la aplicación.");
        _app.OutputDataReceived += (_, e) => Capture(e.Data);
        _app.ErrorDataReceived += (_, e) => Capture(e.Data);
        _app.BeginOutputReadLine(); _app.BeginErrorReadLine();
        await WaitUntilReady();
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    /// <summary>Últimas líneas del registro de la aplicación, para diagnosticar un fallo del servidor.</summary>
    public string RecentLog => string.Join(Environment.NewLine, _log);

    /// <summary>
    /// Cuántas veces aparece un fragmento en el registro. Sirve para contar sentencias SQL de una
    /// carga real de página: es la única forma de observar juntas las dos fases de InteractiveServer
    /// —prerender y circuito—, que desde una prueba de integración no se pueden provocar a la vez.
    /// </summary>
    public int CountInLog(string needle)
    {
        var total = 0;
        foreach (var line in _log) if (line.Contains(needle, StringComparison.Ordinal)) total++;
        return total;
    }

    private void Capture(string? line)
    {
        if (line is null) return;
        _log.Enqueue(line);
        // Holgado a propósito: una carga de /panel escribe cientos de líneas y el conteo de
        // sentencias dejaría de ser fiable si el registro se recortara a mitad de la medición.
        while (_log.Count > 20000) _log.TryDequeue(out _);
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.DisposeAsync();
        _playwright?.Dispose();
        if (_app is { HasExited: false }) { _app.Kill(entireProcessTree: true); await _app.WaitForExitAsync(); }
        _app?.Dispose();
        if (_contenedor.IsValueCreated) await _postgres.DisposeAsync();
    }

    private async Task WaitUntilReady()
    {
        using var http = new HttpClient();
        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (_app?.HasExited == true) throw new InvalidOperationException($"La aplicación terminó con código {_app.ExitCode}.");
            try
            {
                if ((await http.GetAsync($"{BaseUrl}/health/ready")).IsSuccessStatusCode) return;
            }
            catch (HttpRequestException) { }
            await Task.Delay(500);
        }
        throw new TimeoutException("La aplicación no estuvo lista en 30 segundos.");
    }

    private static int FreePort()
    {
        var listener = new TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
    }
    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "UrabaConecta.slnx"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }
}
