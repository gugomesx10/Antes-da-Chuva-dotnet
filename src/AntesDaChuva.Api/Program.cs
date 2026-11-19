
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using AntesDaChuva.Api.Api;
using AntesDaChuva.Api.Data;
using AntesDaChuva.Api.Domain;
using AntesDaChuva.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

bool seedGestorRequested = args.Contains("--seed-gestor", StringComparer.OrdinalIgnoreCase);
var builder = WebApplication.CreateBuilder(args.Where(a => !a.Equals("--seed-gestor", StringComparison.OrdinalIgnoreCase)).ToArray());

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Antes da Chuva - API",
        Version = "v1",
        Description = "API REST de zeladoria urbana desenvolvida em ASP.NET Core .NET 8. Permite que cidadãos registrem ocorrências relacionadas à drenagem e alagamentos e que gestores acompanhem os atendimentos."
    });

    options.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Informe o token JWT obtido no login."
    });

    options.OperationFilter<SwaggerDescriptionsFilter>();
});

builder.Services.AddProblemDetails();

string dbFolder = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dbFolder);

string sqliteConnection = builder.Configuration.GetConnectionString("Default")
    ?? $"Data Source={Path.Combine(dbFolder, "antesdachuva.db")}";

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(sqliteConnection));
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();

var signingKey = builder.Configuration["Jwt:Key"];

if (string.IsNullOrWhiteSpace(signingKey))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Configure Jwt__Key com uma chave Base64 aleatória de pelo menos 32 bytes.");

    signingKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    Console.WriteLine("AVISO: chave JWT temporária gerada no modo Development. Tokens expiram ao reiniciar o servidor.");
}

byte[] keyBytes;

try
{
    keyBytes = Convert.FromBase64String(signingKey);
}
catch (FormatException ex)
{
    throw new InvalidOperationException("Jwt:Key deve ser Base64 válido.", ex);
}

if (keyBytes.Length < 32)
    throw new InvalidOperationException("Jwt:Key deve conter pelo menos 32 bytes após decodificar o Base64.");

var jwtSettings = new JwtSettings(
    signingKey,
    builder.Configuration["Jwt:Issuer"] ?? "AntesDaChuva.DotNet",
    builder.Configuration["Jwt:Audience"] ?? "AntesDaChuva.Clientes",
    builder.Configuration.GetValue<int?>("Jwt:ExpirationMinutes") ?? 120);

builder.Services.AddSingleton(jwtSettings);
builder.Services.AddScoped<JwtService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtSettings.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30)
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Cidadao", p => p.RequireRole(nameof(UserRole.CIDADAO)));
    options.AddPolicy("Gestor", p => p.RequireRole(nameof(UserRole.GESTOR)));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { error = "Muitas requisições. Tente novamente mais tarde." }, ct);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

builder.Services.AddHealthChecks()
    .AddCheck("api", () => HealthCheckResult.Healthy("API disponível"), tags: ["live"])
    .AddCheck<SqliteHealthCheck>("sqlite", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapCategoryEndpoints();
app.MapIncidentEndpoints();

app.MapHealthChecks("/health/live",
    new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") })
    .WithName("HealthLive")
    .WithTags("Saúde");

app.MapHealthChecks("/health/ready",
    new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") })
    .WithName("HealthReady")
    .WithTags("Saúde");

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DbInitializer.InitializeAsync(db);

    if (seedGestorRequested)
    {
        if (!app.Environment.IsDevelopment())
            throw new InvalidOperationException("Seed de gestor permitido somente no modo Development.");

        string email = (Environment.GetEnvironmentVariable("DEMO_GESTOR_EMAIL") ?? "")
            .Trim().ToLowerInvariant();

        string password = Environment.GetEnvironmentVariable("DEMO_GESTOR_PASSWORD") ?? "";

        if (email.Length < 5 || !email.Contains('@') || password.Length < 8)
            throw new InvalidOperationException("Configure DEMO_GESTOR_EMAIL e DEMO_GESTOR_PASSWORD (mínimo 8 caracteres).");

        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email);

        if (user is null)
        {
            user = new AppUser
            {
                Name = "Gestor de demonstração",
                Email = email,
                Neighborhood = "",
                Role = UserRole.GESTOR
            };

            user.PasswordHash = scope.ServiceProvider
                .GetRequiredService<IPasswordHasher<AppUser>>()
                .HashPassword(user, password);

            db.Users.Add(user);
        }
        else
        {
            if (user.Role != UserRole.GESTOR)
                throw new InvalidOperationException("O e-mail informado pertence a outra conta. Use um e-mail de demonstração novo.");

            user.PasswordHash = scope.ServiceProvider
                .GetRequiredService<IPasswordHasher<AppUser>>()
                .HashPassword(user, password);
        }

        await db.SaveChangesAsync();
        Console.WriteLine($"GESTOR de demonstração configurado: {email}");
        return;
    }
}

app.Run();

public partial class Program { }

public sealed class SwaggerDescriptionsFilter : IOperationFilter
{
    private static readonly Dictionary<string, (string Title, string Description)> Descriptions = new()
    {
        ["Register"] = (
            "Cadastrar cidadão",
            "Cria uma conta de cidadão no sistema. Valida os dados informados, verifica se o e-mail já está cadastrado e retorna um token JWT."
        ),

        ["Login"] = (
            "Realizar login",
            "Autentica o usuário por e-mail e senha e retorna um token JWT para acessar os endpoints protegidos."
        ),

        ["Me"] = (
            "Consultar meu perfil",
            "Retorna os dados do usuário autenticado a partir do token JWT enviado na requisição."
        ),

        ["ListCategories"] = (
            "Listar categorias",
            "Retorna todas as categorias ativas disponíveis para o registro de ocorrências urbanas."
        ),

        ["GetCategory"] = (
            "Consultar categoria",
            "Busca uma categoria ativa pelo seu identificador."
        ),

        ["CreateCategory"] = (
            "Cadastrar categoria",
            "Permite que um gestor cadastre uma nova categoria de ocorrência, informando nome, descrição e peso base."
        ),

        ["UpdateCategory"] = (
            "Atualizar categoria",
            "Permite que um gestor altere as informações de uma categoria existente."
        ),

        ["DeleteCategory"] = (
            "Desativar categoria",
            "Permite que um gestor desative uma categoria sem apagar seu registro do banco de dados."
        ),

        ["ListIncidents"] = (
            "Listar ocorrências",
            "Retorna uma lista paginada das ocorrências cadastradas. Permite filtrar por status, categoria e bairro."
        ),

        ["MyIncidents"] = (
            "Consultar minhas ocorrências",
            "Retorna somente as ocorrências cadastradas pelo cidadão autenticado."
        ),

        ["GetIncident"] = (
            "Consultar ocorrência",
            "Busca uma ocorrência pelo identificador e apresenta seus dados e o status atual de atendimento."
        ),

        ["CreateIncident"] = (
            "Registrar ocorrência",
            "Permite que um cidadão registre um problema urbano. Exige o cabeçalho Idempotency-Key para evitar registros duplicados quando a mesma requisição é enviada novamente."
        ),

        ["UpdateIncident"] = (
            "Editar ocorrência",
            "Permite que o cidadão responsável atualize uma ocorrência própria enquanto ela estiver com status OPEN."
        ),

        ["CancelIncident"] = (
            "Cancelar ocorrência",
            "Permite que o cidadão responsável cancele uma ocorrência própria ainda aberta. O registro permanece armazenado com status CANCELLED."
        ),

        ["ListIncidentUpdates"] = (
            "Consultar histórico de atendimento",
            "Apresenta o histórico de alterações de status de uma ocorrência, incluindo as justificativas registradas."
        ),

        ["ChangeIncidentStatus"] = (
            "Atualizar status de atendimento",
            "Permite que um gestor altere o status de uma ocorrência seguindo as transições permitidas e registre uma justificativa."
        ),

        ["HealthLive"] = (
            "Verificar disponibilidade da API",
            "Verifica se a aplicação está ativa e respondendo às requisições."
        ),

        ["HealthReady"] = (
            "Verificar disponibilidade do banco",
            "Verifica se o banco SQLite está acessível e funcionando corretamente."
        )
    };

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        string name = operation.OperationId ?? "";

        if (Descriptions.TryGetValue(name, out var description))
        {
            operation.Summary = description.Title;
            operation.Description = description.Description;
        }

        if (name is "Register" or "Login" or "HealthLive" or "HealthReady")
            return;

        operation.Security ??= new List<OpenApiSecurityRequirement>();

        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "bearer"
                }
            }] = Array.Empty<string>()
        });
    }
}
