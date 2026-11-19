using AntesDaChuva.Api.Domain;

namespace AntesDaChuva.Api.Contracts;

public sealed record UserResponse(int Id, string Name, string Email, UserRole Role, string Neighborhood)
{
    public static UserResponse From(AppUser u) => new(u.Id, u.Name, u.Email, u.Role, u.Neighborhood);
}

public sealed record AuthResponse(string AccessToken, string TokenType, int ExpiresIn, UserResponse User);

public sealed record CategoryResponse(int Id, string Name, string Description, short BaseWeight, bool Active)
{
    public static CategoryResponse From(Category c) => new(c.Id, c.Name, c.Description, c.BaseWeight, c.Active);
}

public sealed record IncidentResponse(int Id, int CategoryId, string Category,
    int OwnerId, string Title, string Description, string Address, string Neighborhood,
    double? Latitude, double? Longitude, IncidentStatus Status, DateTime CreatedAt, DateTime? ResolvedAt)
{
    public static IncidentResponse From(Incident i) => new(i.Id, i.CategoryId, i.Category.Name, i.OwnerId,
        i.Title, i.Description, i.Address, i.Neighborhood,
        i.Latitude, i.Longitude, i.Status, i.CreatedAt, i.ResolvedAt);
}

public sealed record StatusUpdateResponse(int Id, int IncidentId, int ActorId, IncidentStatus PreviousStatus,
    IncidentStatus NewStatus, string Note, DateTime CreatedAt)
{
    public static StatusUpdateResponse From(IncidentUpdate u) => new(u.Id, u.IncidentId, u.ActorId,
        u.PreviousStatus, u.NewStatus, u.Note, u.CreatedAt);
}

public sealed record PagedResponse<T>(int Page, int Size, int Total, IReadOnlyList<T> Items);
