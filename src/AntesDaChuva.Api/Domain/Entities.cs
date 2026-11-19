namespace AntesDaChuva.Api.Domain;

public enum UserRole { CIDADAO, GESTOR }
public enum IncidentStatus { OPEN, IN_REVIEW, IN_PROGRESS, RESOLVED, REJECTED, CANCELLED }

public sealed class AppUser
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Neighborhood { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.CIDADAO;
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<Incident> Incidents { get; set; } = new List<Incident>();
}

public sealed class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public short BaseWeight { get; set; }
    public bool Active { get; set; } = true;
    public ICollection<Incident> Incidents { get; set; } = new List<Incident>();
}

public sealed class Incident
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public AppUser Owner { get; set; } = null!;
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Address { get; set; } = "";
    public string Neighborhood { get; set; } = "";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public IncidentStatus Status { get; set; } = IncidentStatus.OPEN;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    // Unicidade por usuário, persistida no banco e protegida por índice único.
    public string IdempotencyKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public ICollection<IncidentUpdate> Updates { get; set; } = new List<IncidentUpdate>();
}

public sealed class IncidentUpdate
{
    public int Id { get; set; }
    public int IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;
    public int ActorId { get; set; }
    public AppUser Actor { get; set; } = null!;
    public IncidentStatus PreviousStatus { get; set; }
    public IncidentStatus NewStatus { get; set; }
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
