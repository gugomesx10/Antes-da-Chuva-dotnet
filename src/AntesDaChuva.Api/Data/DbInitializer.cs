using AntesDaChuva.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AntesDaChuva.Api.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.EnsureCreatedAsync(ct);
        if (await db.Categories.AnyAsync(ct)) return;
        db.Categories.AddRange(
            new Category { Name = "Bueiro entupido", Description = "Obstrução de drenagem urbana", BaseWeight = 4 },
            new Category { Name = "Entulho em via pública", Description = "Descarte inadequado de resíduos", BaseWeight = 3 },
            new Category { Name = "Ponto de alagamento", Description = "Acúmulo frequente de água", BaseWeight = 5 },
            new Category { Name = "Córrego assoreado", Description = "Assoreamento do curso de água", BaseWeight = 4 },
            new Category { Name = "Árvore em risco", Description = "Risco de queda de árvore", BaseWeight = 3 }
        );
        await db.SaveChangesAsync(ct);
    }
}
