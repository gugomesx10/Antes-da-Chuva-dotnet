using AntesDaChuva.Api.Contracts;
using AntesDaChuva.Api.Domain;
using AntesDaChuva.Api.Services;
using Xunit;

namespace AntesDaChuva.Tests;

public class IncidentRulesTests
{
    [Theory]
    [InlineData(IncidentStatus.OPEN, IncidentStatus.IN_REVIEW, true)]
    [InlineData(IncidentStatus.IN_REVIEW, IncidentStatus.IN_PROGRESS, true)]
    [InlineData(IncidentStatus.IN_PROGRESS, IncidentStatus.RESOLVED, true)]
    [InlineData(IncidentStatus.OPEN, IncidentStatus.RESOLVED, false)]
    [InlineData(IncidentStatus.RESOLVED, IncidentStatus.OPEN, false)]
    [InlineData(IncidentStatus.CANCELLED, IncidentStatus.IN_PROGRESS, false)]
    public void ShouldControlStatusTransitions(IncidentStatus from, IncidentStatus to, bool expected)
    {
        Assert.Equal(expected, IncidentRules.CanTransition(from, to));
    }

    [Fact]
    public void ShouldHashIdenticalPayloadsWithSameFingerprint()
    {
        var request = new IncidentRequest(1, "Bueiro obstruído", "Há lixo no bueiro", "Rua das Flores 100", "Mooca", -23.5, -46.6);
        string fingerprint = IncidentRules.Fingerprint(request);
        Assert.Equal(fingerprint, IncidentRules.Fingerprint(request));
        Assert.NotEqual(fingerprint, IncidentRules.Fingerprint(request with { Title = "Título diferente" }));
        Assert.Equal(64, fingerprint.Length);
    }
}
