using AntesDaChuva.Api.Contracts;
using AntesDaChuva.Api.Data;
using AntesDaChuva.Api.Domain;
using AntesDaChuva.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AntesDaChuva.Api.Api;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Autenticação");
        group.MapPost("/register", RegisterAsync).WithName("Register")
            .WithSummary("Registra um cidadão")
            .Produces<AuthResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status409Conflict).ProducesValidationProblem();
        group.MapPost("/login", LoginAsync).WithName("Login")
            .WithSummary("Autentica e retorna um token JWT")
            .RequireRateLimiting("login")
            .Produces<AuthResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized).Produces(StatusCodes.Status429TooManyRequests);
        group.MapGet("/me", MeAsync).WithName("Me")
            .RequireAuthorization()
            .Produces<UserResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);
        return app;
    }

    private static async Task<IResult> RegisterAsync(RegisterRequest request, AppDbContext db,
        IPasswordHasher<AppUser> hasher, JwtService jwt, CancellationToken ct)
    {
        var validation = ApiValidation.Validate(request);
        if (validation is not null) return validation;
        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return Results.Conflict(new { error = "E-mail já cadastrado" });
        var user = new AppUser
        {
            Name = request.Name.Trim(), Email = email,
            Neighborhood = request.Neighborhood.Trim(), Role = UserRole.CIDADAO
        };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Results.Conflict(new { error = "E-mail já cadastrado" }); }
        return Results.Created($"/auth/me", jwt.Generate(user));
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, AppDbContext db,
        IPasswordHasher<AppUser> hasher, JwtService jwt, CancellationToken ct)
    {
        var validation = ApiValidation.Validate(request);
        if (validation is not null) return validation;
        string email = request.Email.Trim().ToLowerInvariant();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email, ct);
        if (user is null || !user.Active ||
            hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
            return Results.Unauthorized();
        return Results.Ok(jwt.Generate(user));
    }

    private static async Task<IResult> MeAsync(System.Security.Claims.ClaimsPrincipal principal,
        AppDbContext db, CancellationToken ct)
    {
        int userId = JwtService.CurrentUserId(principal);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.Active, ct);
        return user is null ? Results.Unauthorized() : Results.Ok(UserResponse.From(user));
    }
}
