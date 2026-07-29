using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

/// <summary>
/// El sembrado corre antes de que la aplicación empiece a escuchar. Una excepción aquí no
/// degrada una página: impide arrancar el contenedor entero, y el despliegue queda en 502.
/// </summary>
public sealed class SeederStartupTests(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    [Fact]
    public async Task Seeding_survives_an_archived_business_without_short_description()
    {
        _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Reproduce el estado que dejó caída la Demo: un piloto archivado cuya descripción
        // breve quedó vacía. El dominio prohíbe editar un archivado, así que el sembrado
        // debe saltárselo en lugar de intentarlo.
        var municipalityId = await db.Municipalities.Select(x => x.Id).FirstAsync();
        var categoryId = await db.Categories.Select(x => x.Id).FirstAsync();
        var archived = new Business(Guid.NewGuid(), $"archivado-{Guid.NewGuid():N}", "Piloto archivado",
            municipalityId, categoryId, "Descripción completa del piloto archivado.",
            "Calle 1 # 1-1", "3000000000");
        archived.Archive(DateTimeOffset.UtcNow, archived.Version);
        db.Add(archived);
        await db.SaveChangesAsync();
        Assert.Equal(BusinessStatus.Archived, archived.Status);
        Assert.Equal("", archived.ShortDescription);

        // Volver a sembrar equivale a un arranque nuevo del contenedor.
        await factory.Services.SeedDevelopmentAsync(new TestEnvironment("Development"));

        // El archivado sigue intacto y sin descripción breve: se omitió, no se editó.
        var reloaded = await db.Businesses.AsNoTracking().SingleAsync(x => x.Id == archived.Id);
        Assert.Equal(BusinessStatus.Archived, reloaded.Status);
        Assert.Equal("", reloaded.ShortDescription);

        // Y los negocios vivos sí reciben su descripción breve.
        Assert.False(await db.Businesses.AsNoTracking()
            .AnyAsync(x => x.ShortDescription == "" && x.Status != BusinessStatus.Archived));
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "UrabaConecta.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
