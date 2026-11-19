using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AntesDaChuva.Api.Contracts;
using AntesDaChuva.Api.Domain;

namespace AntesDaChuva.Api.Services;

public static class IncidentRules
{
    public static bool CanTransition(IncidentStatus from, IncidentStatus to) => from switch
    {
        IncidentStatus.OPEN => to is IncidentStatus.IN_REVIEW or IncidentStatus.REJECTED,
        IncidentStatus.IN_REVIEW => to is IncidentStatus.IN_PROGRESS or IncidentStatus.REJECTED,
        IncidentStatus.IN_PROGRESS => to is IncidentStatus.RESOLVED or IncidentStatus.REJECTED,
        _ => false
    };

    public static string Fingerprint(IncidentRequest request)
    {
        var serialized = JsonSerializer.Serialize(request);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized)));
    }
}
