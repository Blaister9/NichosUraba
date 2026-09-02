using Microsoft.Playwright;

namespace UrabaConecta.EndToEndTests;

// Prueba interna del motor: los gates de producto están en QueueJourneyTests y usan la API real.
public sealed class LiveStateEngineTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    [Fact]
    public async Task Same_values_interrupts_and_preference_changes_settle_without_stale_decorations()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        using var http = new HttpClient { BaseAddress = new Uri(fixture.BaseUrl) };
        await page.SetContentAsync("""
            <section data-live-state="turno" data-live-etapa="espera">
              <strong data-live-value data-live-delta>4</strong>
            </section>
            """);
        await page.AddStyleTagAsync(new() { Content = await http.GetStringAsync("/app.css") });
        await page.AddScriptTagAsync(new() { Content = await http.GetStringAsync("/live-state.js") });
        var value = page.Locator("[data-live-value]");
        await Assertions.Expect(value).ToHaveAttributeAsync("data-uc-live-visto", "4");
        await value.EvaluateAsync("e => e.textContent = '4'");
        Assert.Null(await value.GetAttributeAsync("data-uc-live-n"));

        await value.EvaluateAsync("e => e.textContent = '3'");
        await Assertions.Expect(value).ToHaveAttributeAsync("data-uc-live-antes", "4");
        await page.WaitForTimeoutAsync(80);
        await value.EvaluateAsync("e => e.textContent = '2'");
        await Assertions.Expect(value).ToHaveAttributeAsync("data-uc-live-antes", "3");
        await Assertions.Expect(value).ToHaveAttributeAsync("data-uc-live-n", "2");
        await page.WaitForTimeoutAsync(260);
        Assert.Equal("−1", await value.GetAttributeAsync("data-uc-live-delta"));
        Assert.DoesNotContain("−1", await value.AriaSnapshotAsync());
        await page.WaitForTimeoutAsync(800);
        await Assertions.Expect(value).ToHaveTextAsync("2");
        Assert.Null(await value.GetAttributeAsync("data-uc-live-anim"));
        Assert.Null(await value.GetAttributeAsync("data-uc-live-antes"));
        Assert.Null(await value.GetAttributeAsync("data-uc-live-delta"));

        await value.EvaluateAsync("e => e.textContent = '1'");
        await Assertions.Expect(value).ToHaveAttributeAsync("data-uc-live-anim", "1");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await Assertions.Expect(page.Locator("[data-uc-live-anim]")).ToHaveCountAsync(0);
        await value.EvaluateAsync("e => e.textContent = '0'");
        await Assertions.Expect(value).ToHaveAttributeAsync("data-uc-live-visto", "0");
        Assert.Equal(0, await value.EvaluateAsync<int>("e => e.getAnimations().length"));
        Assert.Null(await value.GetAttributeAsync("data-uc-live-antes"));
    }
}
