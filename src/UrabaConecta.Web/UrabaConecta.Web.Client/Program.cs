using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using UrabaConecta.Contracts;
using UrabaConecta.Web.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<IUrabaConectaApi, HttpUrabaConectaApi>();
builder.Services.AddScoped<BusinessAccess>();
// En WebAssembly no hay petición donde leer la cookie de municipio. La pantalla resuelve igual su
// dependencia y arranca sin preferencia, que es la verdad en ese contexto.
builder.Services.AddScoped<IPlacePreference, UnknownPlacePreference>();

await builder.Build().RunAsync();
