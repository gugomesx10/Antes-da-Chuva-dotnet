using System.ComponentModel.DataAnnotations;
using AntesDaChuva.Api.Domain;

namespace AntesDaChuva.Api.Contracts;

public sealed record RegisterRequest(
    [property: Required, StringLength(100, MinimumLength = 3)] string Name,
    [property: Required, EmailAddress, StringLength(150)] string Email,
    [property: Required, StringLength(72, MinimumLength = 8)] string Password,
    [property: Required, StringLength(80)] string Neighborhood);

public sealed record LoginRequest(
    [property: Required, EmailAddress] string Email,
    [property: Required] string Password);

public sealed record CategoryRequest(
    [property: Required, StringLength(80, MinimumLength = 3)] string Name,
    [property: StringLength(500)] string? Description,
    [property: Range(1, 5)] short BaseWeight);

public sealed record IncidentRequest(
    [property: Range(1, int.MaxValue)] int CategoryId,
    [property: Required, StringLength(120, MinimumLength = 5)] string Title,
    [property: Required, StringLength(1500, MinimumLength = 10)] string Description,
    [property: Required, StringLength(180, MinimumLength = 5)] string Address,
    [property: Required, StringLength(80)] string Neighborhood,
    [property: Range(-90, 90)] double? Latitude,
    [property: Range(-180, 180)] double? Longitude);

public sealed record StatusUpdateRequest(
    [property: Required] IncidentStatus NewStatus,
    [property: Required, StringLength(500, MinimumLength = 4)] string Note);
