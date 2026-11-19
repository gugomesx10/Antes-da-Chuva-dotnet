using System.ComponentModel.DataAnnotations;

namespace AntesDaChuva.Api.Services;

public static class ApiValidation
{
    public static IResult? Validate<T>(T request) where T : class
    {
        var errors = new List<ValidationResult>();
        var context = new ValidationContext(request);
        if (Validator.TryValidateObject(request, context, errors, validateAllProperties: true)) return null;
        var grouped = errors
            .SelectMany(e => e.MemberNames.DefaultIfEmpty("request"), (error, member) => (error, member))
            .GroupBy(x => x.member, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.error.ErrorMessage ?? "Valor inválido").ToArray());
        return Results.ValidationProblem(grouped);
    }
}
