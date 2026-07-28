using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using UrabaConecta.Application;
using UrabaConecta.Infrastructure;
using UrabaConecta.Infrastructure.Persistence;
using UrabaConecta.Infrastructure.Storage;

namespace UrabaConecta.Domain.Tests;

public sealed class StartupGuardTests
{
    private static IConfiguration Configuration(params (string Key, string Value)[] entries)
        => new ConfigurationBuilder().AddInMemoryCollection(
            entries.Select(x => new KeyValuePair<string, string?>(x.Key, x.Value))).Build();

    private static LegalOptions CompleteLegal() => new()
    {
        ResponsibleName = "Responsable Real S.A.S.", Identification = "NIT 900.000.000-0",
        Address = "Calle 1 # 1-1, Apartadó", PrivacyEmail = "privacidad@ejemplo.co",
        SupportEmail = "soporte@ejemplo.co", PolicyVersion = "2026-1", PolicyEffectiveDate = "2026-08-01"
    };

    private static ObjectStorageOptions CompleteStorage() => new()
    {
        Provider = ObjectStorageOptions.S3Provider, ServiceUrl = "https://cuenta.r2.cloudflarestorage.com",
        Bucket = "urabaconecta", AccessKey = "clave", SecretKey = "secreto",
        PublicBaseUrl = "https://imagenes.ejemplo.co", Region = "auto"
    };

    private static IConfiguration ProductionConfiguration(params (string Key, string Value)[] overrides)
    {
        var entries = new List<(string, string)>
        {
            ("URABACONECTA_TRACKING_HMAC_KEY", "clave-hmac-de-produccion-de-al-menos-32-bytes"),
            ("DemoSeed:Enabled", "false"),
            ("DataProtection:KeysPath", "/app/keys"),
            ("ConnectionStrings:DefaultConnection", "Host=db;Database=urabaconecta_prod;Username=app")
        };
        foreach (var item in overrides)
        {
            entries.RemoveAll(x => x.Item1 == item.Key);
            entries.Add((item.Key, item.Value));
        }
        return Configuration([.. entries]);
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "UrabaConecta.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private static readonly IHostEnvironment Production = new Environment("Production");
    private static readonly IHostEnvironment Demo = new Environment("Demo");
    private static readonly IHostEnvironment Development = new Environment("Development");

    [Fact]
    public void A_correctly_configured_production_passes()
    {
        Assert.Empty(StartupGuard.Validate(ProductionConfiguration(), Production, CompleteLegal(), CompleteStorage()));
    }

    [Fact]
    public void Production_refuses_to_start_with_the_demo_seed_enabled()
    {
        var problems = StartupGuard.Validate(ProductionConfiguration(("DemoSeed:Enabled", "true")),
            Production, CompleteLegal(), CompleteStorage());
        Assert.Contains(problems, x => x.Contains("DemoSeed__Enabled"));
    }

    [Fact]
    public void Production_refuses_to_start_with_the_demo_bootstrap_enabled()
    {
        var problems = StartupGuard.Validate(
            ProductionConfiguration(("DemoBootstrap:Enabled", "true")),
            Production, CompleteLegal(), CompleteStorage());
        Assert.Contains(problems, x => x.Contains("DemoBootstrap__Enabled"));
    }

    [Fact]
    public void Production_refuses_to_start_with_a_known_demo_password()
    {
        var problems = StartupGuard.Validate(
            ProductionConfiguration(("DemoSeed:AdminPassword", DevelopmentSeeder.DemoPassword)),
            Production, CompleteLegal(), CompleteStorage());
        Assert.Contains(problems, x => x.Contains("contraseña de demostración"));
    }

    [Fact]
    public void Production_refuses_to_start_without_the_legal_variables()
    {
        var problems = StartupGuard.Validate(ProductionConfiguration(), Production,
            new LegalOptions(), CompleteStorage());
        Assert.Contains(problems, x => x.Contains("Legal__ResponsibleName"));
        Assert.Contains(problems, x => x.Contains("Legal__PolicyVersion"));
    }

    [Fact]
    public void Production_refuses_to_start_without_object_storage()
    {
        var problems = StartupGuard.Validate(ProductionConfiguration(), Production, CompleteLegal(),
            new ObjectStorageOptions { Provider = ObjectStorageOptions.S3Provider });
        Assert.Contains(problems, x => x.Contains("ObjectStorage__Bucket"));
    }

    [Fact]
    public void Production_refuses_the_ephemeral_local_storage_provider()
    {
        var problems = StartupGuard.Validate(ProductionConfiguration(), Production, CompleteLegal(),
            new ObjectStorageOptions { Provider = ObjectStorageOptions.LocalProvider, LocalRootPath = "/data" });
        Assert.Contains(problems, x => x.Contains("ObjectStorage__Provider=S3"));
    }

    [Fact]
    public void Production_refuses_to_point_at_the_demo_database()
    {
        var problems = StartupGuard.Validate(
            ProductionConfiguration(("ConnectionStrings:DefaultConnection",
                "Host=db;Database=urabaconecta_demo;Username=app")),
            Production, CompleteLegal(), CompleteStorage());
        Assert.Contains(problems, x => x.Contains("Demo"));
    }

    [Fact]
    public void Production_refuses_to_start_without_persistent_data_protection_keys()
    {
        var problems = StartupGuard.Validate(ProductionConfiguration(("DataProtection:KeysPath", "")),
            Production, CompleteLegal(), CompleteStorage());
        Assert.Contains(problems, x => x.Contains("DataProtection__KeysPath"));
    }

    [Fact]
    public void The_hmac_key_is_demanded_at_startup_outside_development()
    {
        Assert.Contains(StartupGuard.Validate(Configuration(), Demo, new LegalOptions(), new ObjectStorageOptions()),
            x => x.Contains("URABACONECTA_TRACKING_HMAC_KEY"));
        // Development trae una clave local y no exige el resto de la configuración productiva.
        Assert.Empty(StartupGuard.Validate(Configuration(), Development, new LegalOptions(),
            new ObjectStorageOptions()));
    }

    [Fact]
    public void Throwing_reports_every_problem_at_once()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => StartupGuard.ThrowIfInvalid(
            ProductionConfiguration(("DemoSeed:Enabled", "true")), Production,
            new LegalOptions(), new ObjectStorageOptions { Provider = ObjectStorageOptions.S3Provider }));
        Assert.Contains("DemoSeed__Enabled", exception.Message);
        Assert.Contains("Legal__ResponsibleName", exception.Message);
        Assert.Contains("ObjectStorage__Bucket", exception.Message);
    }
}
