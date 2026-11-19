using System.Security.Claims;
using AntesDaChuva.Api.Contracts;
using AntesDaChuva.Api.Data;
using AntesDaChuva.Api.Domain;
using AntesDaChuva.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace AntesDaChuva.Api.Api;

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/incidents").WithTags("Ocorrências").RequireAuthorization();
        group.MapGet("/", ListAsync).WithName("ListIncidents").Produces<PagedResponse<IncidentResponse>>();
        group.MapGet("/mine", MineAsync).WithName("MyIncidents").RequireAuthorization("Cidadao")
            .Produces<PagedResponse<IncidentResponse>>();
        group.MapGet("/{id:int}", GetAsync).WithName("GetIncident").Produces<IncidentResponse>();
        group.MapPost("/", CreateAsync).WithName("CreateIncident").RequireAuthorization("Cidadao")
            .WithSummary("Cria uma ocorrência com Idempotency-Key obrigatória")
            .Produces<IncidentResponse>(201).Produces<IncidentResponse>(200).Produces(409).ProducesValidationProblem();
        group.MapPut("/{id:int}", UpdateAsync).WithName("UpdateIncident").RequireAuthorization("Cidadao")
            .Produces<IncidentResponse>();
        group.MapDelete("/{id:int}", CancelAsync).WithName("CancelIncident").RequireAuthorization("Cidadao")
            .Produces(204);
        group.MapGet("/{id:int}/updates", ListUpdatesAsync).WithName("ListIncidentUpdates")
            .Produces<IReadOnlyList<StatusUpdateResponse>>();
        group.MapPost("/{id:int}/updates", ChangeStatusAsync).WithName("ChangeIncidentStatus")
            .RequireAuthorization("Gestor").Produces<StatusUpdateResponse>(201);
        return app;
    }

    private static async Task<IResult> ListAsync(AppDbContext db, IncidentStatus? status,
        int? categoryId, string? neighborhood, int page = 0, int size = 10, CancellationToken ct = default)
    {
        if (page < 0 || size is < 1 or > 100) return Results.BadRequest(new { error = "page deve ser >= 0 e size entre 1 e 100" });
        var query = db.Incidents.AsNoTracking().Include(x => x.Category).AsQueryable();
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (categoryId.HasValue) query = query.Where(x => x.CategoryId == categoryId.Value);
        if (!string.IsNullOrWhiteSpace(neighborhood)) query = query.Where(x => x.Neighborhood == neighborhood);
        return Results.Ok(await PagedAsync(query, page, size, ct));
    }

    private static async Task<IResult> MineAsync(ClaimsPrincipal principal, AppDbContext db,
        int page = 0, int size = 10, CancellationToken ct = default)
    {
        if (page < 0 || size is < 1 or > 100) return Results.BadRequest(new { error = "Paginação inválida" });
        int ownerId = JwtService.CurrentUserId(principal);
        var query = db.Incidents.AsNoTracking().Include(x => x.Category).Where(x => x.OwnerId == ownerId);
        return Results.Ok(await PagedAsync(query, page, size, ct));
    }

    private static async Task<PagedResponse<IncidentResponse>> PagedAsync(IQueryable<Incident> query,
        int page, int size, CancellationToken ct)
    {
        int total = await query.CountAsync(ct);
        var incidents = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip(page * size).Take(size).ToListAsync(ct);
        return new PagedResponse<IncidentResponse>(page, size, total, incidents.Select(IncidentResponse.From).ToList());
    }

    private static async Task<IResult> GetAsync(int id, AppDbContext db, CancellationToken ct)
    {
        var incident = await db.Incidents.AsNoTracking().Include(x => x.Category)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        return incident is null ? Results.NotFound() : Results.Ok(IncidentResponse.From(incident));
    }

    private static async Task<IResult> CreateAsync(IncidentRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var validation = ApiValidation.Validate(request);
        if (validation is not null) return validation;
        string key = idempotencyKey?.Trim() ?? "";
        if (key.Length is < 8 or > 128)
            return Results.BadRequest(new { error = "Envie Idempotency-Key no cabeçalho (8 a 128 caracteres)" });
        int ownerId = JwtService.CurrentUserId(principal);
        string fingerprint = IncidentRules.Fingerprint(request);
        var existing = await db.Incidents.AsNoTracking().Include(x => x.Category)
            .SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.IdempotencyKey == key, ct);
        if (existing is not null) return Replay(existing, fingerprint);

        var category = await db.Categories.SingleOrDefaultAsync(x => x.Id == request.CategoryId && x.Active, ct);
        if (category is null) return Results.BadRequest(new { error = "Categoria inválida ou inativa" });
        var incident = new Incident
        {
            OwnerId = ownerId, CategoryId = category.Id, Category = category,
            Title = request.Title.Trim(), Description = request.Description.Trim(),
            Address = request.Address.Trim(), Neighborhood = request.Neighborhood.Trim(),
            Latitude = request.Latitude, Longitude = request.Longitude,
            Status = IncidentStatus.OPEN, IdempotencyKey = key, RequestHash = fingerprint
        };
        db.Incidents.Add(incident);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Entre duas instâncias concorrentes, o índice único impede o segundo INSERT.
            db.Entry(incident).State = EntityState.Detached;
            var saved = await db.Incidents.AsNoTracking().Include(x => x.Category)
                .SingleOrDefaultAsync(x => x.OwnerId == ownerId && x.IdempotencyKey == key, ct);
            if (saved is not null) return Replay(saved, fingerprint);
            throw;
        }
        return Results.Created($"/incidents/{incident.Id}", IncidentResponse.From(incident));
    }

    private static IResult Replay(Incident existing, string fingerprint) =>
        existing.RequestHash == fingerprint
            ? Results.Ok(IncidentResponse.From(existing))
            : Results.Conflict(new { error = "Idempotency-Key reutilizada com conteúdo diferente" });

    private static async Task<IResult> UpdateAsync(int id, IncidentRequest request, ClaimsPrincipal principal,
        AppDbContext db, CancellationToken ct)
    {
        var validation = ApiValidation.Validate(request);
        if (validation is not null) return validation;
        var incident = await db.Incidents.Include(x => x.Category).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (incident is null) return Results.NotFound();
        if (incident.OwnerId != JwtService.CurrentUserId(principal)) return Results.Forbid();
        if (incident.Status != IncidentStatus.OPEN) return Results.Conflict(new { error = "Somente ocorrências OPEN podem ser editadas" });
        var category = await db.Categories.SingleOrDefaultAsync(x => x.Id == request.CategoryId && x.Active, ct);
        if (category is null) return Results.BadRequest(new { error = "Categoria inválida" });
        incident.Category = category;
        incident.CategoryId = category.Id;
        incident.Title = request.Title.Trim();
        incident.Description = request.Description.Trim();
        incident.Address = request.Address.Trim();
        incident.Neighborhood = request.Neighborhood.Trim();
        incident.Latitude = request.Latitude;
        incident.Longitude = request.Longitude;
        await db.SaveChangesAsync(ct);
        return Results.Ok(IncidentResponse.From(incident));
    }

    private static async Task<IResult> CancelAsync(int id, ClaimsPrincipal principal,
        AppDbContext db, CancellationToken ct)
    {
        var incident = await db.Incidents.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (incident is null) return Results.NotFound();
        if (incident.OwnerId != JwtService.CurrentUserId(principal)) return Results.Forbid();
        if (incident.Status != IncidentStatus.OPEN) return Results.Conflict(new { error = "Somente ocorrências OPEN podem ser canceladas" });
        incident.Status = IncidentStatus.CANCELLED;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ListUpdatesAsync(int id, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Incidents.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
        var updates = await db.IncidentUpdates.AsNoTracking().Where(x => x.IncidentId == id)
            .OrderBy(x => x.CreatedAt).ToListAsync(ct);
        return Results.Ok(updates.Select(StatusUpdateResponse.From));
    }

    private static async Task<IResult> ChangeStatusAsync(int id, StatusUpdateRequest request,
        ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var validation = ApiValidation.Validate(request);
        if (validation is not null) return validation;
        var incident = await db.Incidents.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (incident is null) return Results.NotFound();
        if (!IncidentRules.CanTransition(incident.Status, request.NewStatus))
            return Results.Conflict(new { error = "Transição de status não permitida" });
        var previous = incident.Status;
        incident.Status = request.NewStatus;
        if (incident.Status == IncidentStatus.RESOLVED) incident.ResolvedAt = DateTime.UtcNow;
        var update = new IncidentUpdate
        {
            IncidentId = incident.Id, ActorId = JwtService.CurrentUserId(principal),
            PreviousStatus = previous, NewStatus = request.NewStatus, Note = request.Note.Trim()
        };
        db.IncidentUpdates.Add(update);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/incidents/{id}/updates", StatusUpdateResponse.From(update));
    }
}
