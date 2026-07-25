using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using UrabaConecta.Contracts;
using UrabaConecta.Web.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<IUrabaConectaApi, HttpUrabaConectaApi>();

await builder.Build().RunAsync();
