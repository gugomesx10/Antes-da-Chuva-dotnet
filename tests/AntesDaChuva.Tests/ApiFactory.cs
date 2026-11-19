using AntesDaChuva.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;

namespace AntesDaChuva.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbFile = Path.Combine(Path.GetTempPath(), $"antesdachuva-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("testing_key_for_dotnet_gs_2026_32_bytes_or_more"))
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={_dbFile}"));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            foreach (string path in new[] { _dbFile, _dbFile + "-shm", _dbFile + "-wal" })
            {
                try { File.Delete(path); }
                catch (IOException) { }
            }
        }
    }

    public void PromoteUserToGestor(string email)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = db.Users.Single(u => u.Email == email);
        user.Role = AntesDaChuva.Api.Domain.UserRole.GESTOR;
        db.SaveChanges();
    }

    public int IncidentCount()
    {
        using var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().Incidents.Count();
    }
}
