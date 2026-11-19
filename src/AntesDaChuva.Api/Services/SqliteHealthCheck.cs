using AntesDaChuva.Api.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

namespace AntesDaChuva.Api.Services;

public sealed class SqliteHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            bool connected = await db.Database.CanConnectAsync(cancellationToken);
            return connected ? HealthCheckResult.Healthy("SQLite disponível")
                             : HealthCheckResult.Unhealthy("SQLite indisponível");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Falha de conexão com SQLite", exception);
        }
    }
}
