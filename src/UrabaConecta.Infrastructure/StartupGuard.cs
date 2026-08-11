using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using UrabaConecta.Application;
using UrabaConecta.Infrastructure.Persistence;
using UrabaConecta.Infrastructure.Storage;

namespace UrabaConecta.Infrastructure;

/// <summary>
/// Comprobaciones que se ejecutan antes de servir la primera petición. En Production una
/// configuración insegura debe impedir el arranque, no degradarse en silencio.
/// </summary>
public static class StartupGuard
{
    /// <summary>Contraseñas de demostración que nunca deben existir en una instalación productiva.</summary>
    public static readonly string[] ForbiddenProductionPasswords =
        [DevelopmentSeeder.DemoPassword, "UrabaDemo!2026", "Password1!", "Admin123!"];

    public static IReadOnlyList<string> Validate(IConfiguration configuration, IHostEnvironment environment,
        LegalOptions legal, ObjectStorageOptions storage)
    {
        var problems = new List<string>();

        // La clave HMAC se exige en todo ambiente distinto de Development, y al arrancar, no en la
        // primera petición: una instancia sin clave no debe reportarse como saludable.
        if (!environment.IsDevelopment() &&
            string.IsNullOrWhiteSpace(configuration["URABACONECTA_TRACKING_HMAC_KEY"]))
            problems.Add("Falta URABACONECTA_TRACKING_HMAC_KEY.");

        if (!environment.IsProduction()) return problems;

        if (configuration.GetValue<bool>("DemoSeed:Enabled"))
            problems.Add("DemoSeed__Enabled debe ser false en Production.");
        if (configuration.GetValue<bool>("DemoBootstrap:Enabled"))
            problems.Add("DemoBootstrap__Enabled debe ser false en Production.");
        if (!string.IsNullOrWhiteSpace(configuration["DemoAccess:SharedPassword"]))
            problems.Add("DemoAccess__SharedPassword no debe estar definida en Production.");
        foreach (var key in new[] { "DemoSeed:AdminPassword", "DemoSeed:BusinessPassword" })
            if (!string.IsNullOrWhiteSpace(configuration[key]))
                problems.Add($"{key.Replace(':', '_').Replace("_", "__")} no debe estar definida en Production.");
        foreach (var key in new[] { "DemoBootstrap:AdminEmail", "DemoBootstrap:AdminPassword" })
            if (!string.IsNullOrWhiteSpace(configuration[key]))
                problems.Add($"{key.Replace(':', '_').Replace("_", "__")} no debe estar definida en Production.");

        var missingLegal = legal.MissingKeys();
        if (missingLegal.Count > 0)
            problems.Add("Faltan variables jurídicas obligatorias: " + string.Join(", ", missingLegal) + ".");

        var missingStorage = storage.MissingKeys();
        if (missingStorage.Count > 0)
            problems.Add("Falta configurar el almacenamiento de objetos: " + string.Join(", ", missingStorage) + ".");
        if (!storage.UsesS3)
            problems.Add("Production exige ObjectStorage__Provider=S3; el proveedor local es efímero.");
        // Compartir el bucket con Demo mezclaría imágenes ficticias con las de negocios reales, y
        // un borrado de la demostración se llevaría por delante material productivo.
        if (PointsToDemo(storage.Bucket) || PointsToDemo(storage.PublicBaseUrl))
            problems.Add("El almacenamiento de objetos apunta a un bucket Demo.");

        // Un rastro de pila en la respuesta revela rutas, versiones y consultas. En Production la
        // aplicación responde ProblemDetails y el detalle queda sólo en el registro.
        if (configuration.GetValue<bool>("DetailedErrors"))
            problems.Add("DetailedErrors debe ser false en Production: expondría rastros de pila.");

        if (string.IsNullOrWhiteSpace(configuration["DataProtection:KeysPath"]))
            problems.Add("Falta DataProtection__KeysPath: las cookies no sobrevivirían a un reinicio.");

        var connection = configuration.GetConnectionString("DefaultConnection") ?? "";
        if (PointsToDemoDatabase(connection))
            problems.Add("La cadena de conexión apunta a una base de datos Demo.");

        foreach (var password in DeclaredPasswords(configuration))
            if (ForbiddenProductionPasswords.Contains(password, StringComparer.Ordinal))
                problems.Add("Se detectó una contraseña de demostración conocida en la configuración.");

        return problems;
    }

    public static void ThrowIfInvalid(IConfiguration configuration, IHostEnvironment environment,
        LegalOptions legal, ObjectStorageOptions storage)
    {
        var problems = Validate(configuration, environment, legal, storage);
        if (problems.Count == 0) return;
        throw new InvalidOperationException(
            $"La configuración de {environment.EnvironmentName} no es apta para operar:{Environment.NewLine}" +
            string.Join(Environment.NewLine, problems.Select(x => " - " + x)));
    }

    private static bool PointsToDemoDatabase(string connectionString)
    {
        var lowered = connectionString.ToLowerInvariant();
        return lowered.Contains("database=urabaconecta_demo") || lowered.Contains("database=demo")
            || lowered.Contains("_demo;") || lowered.EndsWith("_demo");
    }

    /// <summary>Reconoce los nombres con los que se rotuló el material de la demostración.</summary>
    private static bool PointsToDemo(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var lowered = value.ToLowerInvariant();
        return lowered.Contains("-demo") || lowered.Contains("demo-") || lowered.Contains("_demo")
            || lowered.Contains("demo.") || lowered.Contains("/demo") || lowered == "demo";
    }

    private static IEnumerable<string> DeclaredPasswords(IConfiguration configuration)
    {
        foreach (var key in new[]
                 {
                     "DemoSeed:AdminPassword", "DemoSeed:BusinessPassword",
                     "DemoBootstrap:AdminPassword", "DemoAccess:SharedPassword",
                     ProductionAdminBootstrap.PasswordKey
                 })
            if (configuration[key] is { Length: > 0 } value) yield return value;
    }
}
