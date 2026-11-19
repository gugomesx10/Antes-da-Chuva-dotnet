using AntesDaChuva.Api.Contracts;
using AntesDaChuva.Api.Data;
using AntesDaChuva.Api.Domain;
using AntesDaChuva.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace AntesDaChuva.Api.Api;

public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/categories").WithTags("Categorias").RequireAuthorization();
        group.MapGet("/", ListAsync).WithName("ListCategories").Produces<IReadOnlyList<CategoryResponse>>();
        group.MapGet("/{id:int}", GetAsync).WithName("GetCategory").Produces<CategoryResponse>();
        group.MapPost("/", CreateAsync).WithName("CreateCategory").RequireAuthorization("Gestor")
            .Produces<CategoryResponse>(201).ProducesValidationProblem();
        group.MapPut("/{id:int}", UpdateAsync).WithName("UpdateCategory").RequireAuthorization("Gestor")
            .Produces<CategoryResponse>();
        group.MapDelete("/{id:int}", DeleteAsync).WithName("DeleteCategory").RequireAuthorization("Gestor")
            .Produces(204);
        return app;
    }

    private static async Task<IResult> ListAsync(AppDbContext db, CancellationToken ct) =>
        Results.Ok((await db.Categories.AsNoTracking().Where(x => x.Active)
            .OrderBy(x => x.Name).ToListAsync(ct)).Select(CategoryResponse.From));

    private static async Task<IResult> GetAsync(int id, AppDbContext db, CancellationToken ct)
    {
        var c = await db.Categories.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Active, ct);
        return c is null ? Results.NotFound() : Results.Ok(CategoryResponse.From(c));
    }

    private static async Task<IResult> CreateAsync(CategoryRequest request, AppDbContext db, CancellationToken ct)
    {
        var validation = ApiValidation.Validate(request);
        if (validation is not null) return validation;
        var name = request.Name.Trim();
        if (await db.Categories.AnyAsync(x => x.Name == name, ct))
            return Results.Conflict(new { error = "Categoria já existe" });
        var category = new Category { Name = name, Description = request.Description?.Trim() ?? "", BaseWeight = request.BaseWeight };
        db.Categories.Add(category);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Results.Conflict(new { error = "Categoria já existe" }); }
        return Results.Created($"/categories/{category.Id}", CategoryResponse.From(category));
    }

    private static async Task<IResult> UpdateAsync(int id, CategoryRequest request, AppDbContext db, CancellationToken ct)
    {
        var validation = ApiValidation.Validate(request);
        if (validation is not null) return validation;
        var category = await db.Categories.SingleOrDefaultAsync(x => x.Id == id && x.Active, ct);
        if (category is null) return Results.NotFound();
        var name = request.Name.Trim();
        if (await db.Categories.AnyAsync(x => x.Name == name && x.Id != id, ct))
            return Results.Conflict(new { error = "Categoria já existe" });
        category.Name = name;
        category.Description = request.Description?.Trim() ?? "";
        category.BaseWeight = request.BaseWeight;
        await db.SaveChangesAsync(ct);
        return Results.Ok(CategoryResponse.From(category));
    }

    private static async Task<IResult> DeleteAsync(int id, AppDbContext db, CancellationToken ct)
    {
        var category = await db.Categories.SingleOrDefaultAsync(x => x.Id == id && x.Active, ct);
        if (category is null) return Results.NotFound();
        category.Active = false;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
