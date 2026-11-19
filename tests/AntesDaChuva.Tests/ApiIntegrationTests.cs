using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace AntesDaChuva.Tests;

public sealed class ApiIntegrationTests : IDisposable
{
    private readonly ApiFactory _factory = new();
    private readonly HttpClient _client;

    public ApiIntegrationTests() => _client = _factory.CreateClient();

    [Fact]
    public async Task RegistrationAndLoginShouldProvideJwtAndProtectedMe()
    {
        var unauthorized = await _client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        await RegisterAsync();
        string token = await LoginAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await _client.GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var json = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
        Assert.Equal("CIDADAO", json.RootElement.GetProperty("role").GetString());
    }

    [Fact]
    public async Task DuplicateIdempotencyKeyShouldReturnOriginalAndRejectDifferentPayload()
    {
        await AuthenticateCitizenAsync();
        var body = IncidentRequest();
        var first = await CreateIncidentAsync(body, "same-key-12345678");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var second = await CreateIncidentAsync(body, "same-key-12345678");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(1, _factory.IncidentCount());
        var changed = await CreateIncidentAsync(new
        {
            categoryId = 1, title = "Ocorrência diferente", description = "Descrição totalmente diferente",
            address = "Rua das Flores 101", neighborhood = "Mooca", latitude = -23.5, longitude = -46.6
        }, "same-key-12345678");
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal(1, _factory.IncidentCount());
    }

    [Fact]
    public async Task CitizenCannotManageCategoriesButGestorCanAndCanUpdateIncidentStatus()
    {
        await AuthenticateCitizenAsync();
        var forbidden = await _client.PostAsJsonAsync("/categories", new { name = "Canaletas", description = "Teste", baseWeight = 4 });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var incidentResponse = await CreateIncidentAsync(IncidentRequest(), "owner-key-12345678");
        Assert.Equal(HttpStatusCode.Created, incidentResponse.StatusCode);
        using var created = JsonDocument.Parse(await incidentResponse.Content.ReadAsStringAsync());
        int id = created.RootElement.GetProperty("id").GetInt32();

        await RegisterAsync("gestor.teste@example.com");
        _factory.PromoteUserToGestor("gestor.teste@example.com");
        string token = await LoginAsync("gestor.teste@example.com");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var ok = await _client.PostAsJsonAsync("/categories", new { name = "Canaletas", description = "Teste", baseWeight = 4 });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var first = await _client.PostAsJsonAsync($"/incidents/{id}/updates", new { newStatus = "IN_REVIEW", note = "Equipe recebendo chamado" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var invalid = await _client.PostAsJsonAsync($"/incidents/{id}/updates", new { newStatus = "RESOLVED", note = "Transição inválida" });
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        var second = await _client.PostAsJsonAsync($"/incidents/{id}/updates", new { newStatus = "IN_PROGRESS", note = "Equipe em campo" });
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var final = await _client.PostAsJsonAsync($"/incidents/{id}/updates", new { newStatus = "RESOLVED", note = "Atendimento encerrado" });
        Assert.Equal(HttpStatusCode.Created, final.StatusCode);
        var timeline = await _client.GetAsync($"/incidents/{id}/updates");
        Assert.Equal(HttpStatusCode.OK, timeline.StatusCode);
        using var arr = JsonDocument.Parse(await timeline.Content.ReadAsStringAsync());
        Assert.Equal(3, arr.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task HealthChecksShouldReturnHealthyAndBadInputShouldReturn400()
    {
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health/ready")).StatusCode);
        await AuthenticateCitizenAsync();
        var invalid = await CreateIncidentAsync(new
        {
            categoryId = 1, title = "", description = "", address = "", neighborhood = ""
        }, "invalid-key-12345678");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task RateLimiterShouldReturn429OnSixthLoginWithinMinute()
    {
        for (int i = 0; i < 5; i++)
        {
            var r = await _client.PostAsJsonAsync("/auth/login", new { email = "unknown@example.com", password = "incorrect" });
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }
        var limited = await _client.PostAsJsonAsync("/auth/login", new { email = "unknown@example.com", password = "incorrect" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    private async Task AuthenticateCitizenAsync()
    {
        await RegisterAsync();
        string token = await LoginAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task RegisterAsync(string email = "cidadao.teste@example.com")
    {
        var response = await _client.PostAsJsonAsync("/auth/register", new
        {
            name = "Cidadão de Teste", email, password = "SenhaForte@2026", neighborhood = "Mooca"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private async Task<string> LoginAsync(string email = "cidadao.teste@example.com")
    {
        var response = await _client.PostAsJsonAsync("/auth/login", new { email, password = "SenhaForte@2026" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("accessToken").GetString()!;
    }

    private static object IncidentRequest() => new
    {
        categoryId = 1, title = "Bueiro completamente entupido", description = "Há água acumulada após as chuvas",
        address = "Rua das Flores 100", neighborhood = "Mooca", latitude = -23.5, longitude = -46.6
    };

    private async Task<HttpResponseMessage> CreateIncidentAsync(object body, string key)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/incidents");
        message.Headers.Add("Idempotency-Key", key);
        message.Content = JsonContent.Create(body);
        return await _client.SendAsync(message);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }
}
