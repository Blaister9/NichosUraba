using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Playwright;
using Testcontainers.PostgreSql;

namespace UrabaConecta.EndToEndTests;

public sealed class BrowserFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("urabaconecta_e2e").WithUsername("e2e").WithPassword("e2e-only-password").Build();
    private Process? _app;
    private IPlaywright? _playwright;
    public IBrowser Browser { get; private set; } = default!;
    public string BaseUrl { get; private set; } = "";

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var root = FindRepositoryRoot();
        var webDll = Path.Combine(root, "src", "UrabaConecta.Web", "UrabaConecta.Web", "bin", "Debug",
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
        start.Environment["ConnectionStrings__DefaultConnection"] = _postgres.GetConnectionString();
        start.Environment["URABACONECTA_TRACKING_HMAC_KEY"] = "e2e-test-hmac-key-with-at-least-32-bytes";
        _app = Process.Start(start) ?? throw new InvalidOperationException("No fue posible iniciar la aplicación.");
        _app.OutputDataReceived += (_, _) => { }; _app.ErrorDataReceived += (_, _) => { };
        _app.BeginOutputReadLine(); _app.BeginErrorReadLine();
        await WaitUntilReady();
        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.DisposeAsync();
        _playwright?.Dispose();
        if (_app is { HasExited: false }) { _app.Kill(entireProcessTree: true); await _app.WaitForExitAsync(); }
        _app?.Dispose();
        await _postgres.DisposeAsync();
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
