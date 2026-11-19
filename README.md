# Antes da Chuva — API .NET

**Global Solution FIAP 2026/2 · Advanced Business Development with .NET**  
API de registro e acompanhamento de ocorrências urbanas voltada ao tema **CitizenTech**.

Este é o backend **.NET** do *Antes da Chuva*. O cidadão registra bueiros entupidos, alagamentos e outros riscos de zeladoria; o gestor administra categorias e acompanha o andamento das ocorrências. A implementação usa **ASP.NET Core Minimal APIs + EF Core + SQLite** para facilitar a execução sem serviços externos. O backend Java + Gemini é um projeto complementar do grupo e não é requisito para iniciar esta API.

**Repositório Java do grupo:** https://github.com/gugomesx10/Antes-da-Chuva

## Tecnologias e requisitos FIAP

| Requisito | Implementação |
| --- | --- |
| Minimal API REST e validações | Endpoints `MapGet`, `MapPost`, `MapPut`, `MapDelete`; DTOs com DataAnnotations e respostas HTTP consistentes |
| OpenAPI/Swagger | Swashbuckle; `http://localhost:5095/swagger` e autenticação Bearer |
| EF Core | SQLite local, quatro entidades relacionadas, índices e seed de categorias |
| Autenticação e autorização | JWT HS256; perfis `CIDADAO` e `GESTOR` |
| Idempotência | Cabeçalho obrigatório `Idempotency-Key` em POST de ocorrências; índice único `(OwnerId, IdempotencyKey)` e SHA-256 do corpo |
| Rate Limiting | Limite global por IP e até **5 logins/minuto**; excesso gera HTTP `429` |
| Health Checks | `/health/live` (API) e `/health/ready` (banco SQLite) |
| Testes | xUnit: unitários e integração HTTP com `WebApplicationFactory` e SQLite isolado por teste |

## Pré-requisitos

O projeto foi configurado especificamente para **.NET 8**, com `TargetFramework` `net8.0`, pacotes Microsoft 8.0.34 e Swashbuckle 6.6.2. O GitHub Actions também utiliza .NET 8.

- **.NET SDK 8.0** (LTS): https://dotnet.microsoft.com/download/dotnet/8.0
- Windows, Linux ou macOS; uma IDE opcional (Visual Studio / Rider / VS Code)
- Internet na primeira execução para restaurar as dependências NuGet

## Início rápido — Windows (PowerShell)

Na **raiz deste repositório**:

```powershell
dotnet restore
dotnet build
dotnet run --project src/AntesDaChuva.Api --launch-profile http
```

Depois, acesse **http://localhost:5095/swagger**.

O SQLite é criado automaticamente na pasta `App_Data/antesdachuva.db` do diretório de conteúdo da aplicação (a localização pode variar conforme a forma de iniciar o projeto). As tabelas e cinco categorias de demonstração são geradas na primeira execução. O caminho `App_Data` é ignorado pelo Git.

### Criar um gestor de demonstração (sem senha hardcoded no repositório)

Pare a API e execute no PowerShell:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:DEMO_GESTOR_EMAIL = "gestor.demo@example.com"
$env:DEMO_GESTOR_PASSWORD = "EscolhaUmaSenhaForte123!"
dotnet run --project src/AntesDaChuva.Api -- --seed-gestor
```

O comando cria (ou atualiza a senha de) um usuário `GESTOR` **somente em Development**. Substitua o e-mail e a senha fictícios antes de executar. Depois inicie a API normalmente e entre com esse gestor em `POST /auth/login`.

**Cidadão:** crie diretamente em `POST /auth/register`; o cadastro público nunca cria `GESTOR`.

### Segurança e configuração

No ambiente **Development**, a API gera automaticamente uma chave JWT aleatória em memória. Isso facilita testar localmente, mas invalida tokens após reiniciar a API.

Para utilizar uma chave persistente, configure `Jwt__Key` com o **Base64 de 32 ou mais bytes aleatórios**, fora do Git:

```powershell
# Gere um segredo aleatório, exclusivo para seu ambiente:
$env:Jwt__Key = [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

Em ambientes que não sejam `Development`, essa variável é **obrigatória**. Opcionalmente, use `ConnectionStrings__Default` para apontar para outro arquivo SQLite. Nunca publique arquivos `.env`, o banco local, senhas, JWTs ou chaves.

## Domínio e papéis

- `AppUser`: usuário, senha com `PasswordHasher` (formato Microsoft Identity), perfil e bairro.
- `Category`: categoria de ocorrência; ativação/desativação lógica.
- `Incident`: ocorrência com categoria, autor, status e chave de idempotência.
- `IncidentUpdate`: histórico de mudanças de status feito por gestor.
- `CIDADAO`: registra e acompanha suas ocorrências; pode editar ou cancelar enquanto estão `OPEN`.
- `GESTOR`: gerencia categorias, consulta ocorrências e registra histórico de andamento.

Fluxo de status: `OPEN → IN_REVIEW → IN_PROGRESS → RESOLVED`; é possível usar `REJECTED` a partir dos estados não finais. A entidade `IncidentUpdate` preserva o status anterior, o novo, o responsável, a justificativa e a data.

## Endpoints

| Método | Rota | Permissão | Resposta principal |
| --- | --- | --- | --- |
| POST | `/auth/register` | Pública | `201` + JWT |
| POST | `/auth/login` | Pública | `200` + JWT; `401`, `429` |
| GET | `/auth/me` | Autenticado | `200` |
| GET | `/categories` | Autenticado | `200` |
| GET | `/categories/{id}` | Autenticado | `200` ou `404` |
| POST | `/categories` | GESTOR | `201` |
| PUT | `/categories/{id}` | GESTOR | `200` |
| DELETE | `/categories/{id}` | GESTOR | `204` (soft delete) |
| GET | `/incidents` | Autenticado | `200`, filtros e paginação |
| GET | `/incidents/mine` | CIDADAO | `200`, paginação |
| GET | `/incidents/{id}` | Autenticado | `200` |
| POST | `/incidents` | CIDADAO | `201`, repetição `200`, conflito `409` |
| PUT | `/incidents/{id}` | CIDADAO/autor | `200` |
| DELETE | `/incidents/{id}` | CIDADAO/autor | `204` (cancelamento lógico) |
| GET | `/incidents/{id}/updates` | Autenticado | `200` |
| POST | `/incidents/{id}/updates` | GESTOR | `201` |
| GET | `/health/live` | Pública | `200` se API saudável |
| GET | `/health/ready` | Pública | `200` se SQLite disponível |

As rotas `GET /incidents` aceitam `status`, `categoryId`, `neighborhood`, `page` e `size` (`1..100`). DTOs inválidos retornam `400`; rotas sem autenticação retornam `401`, acesso proibido `403`.

### Exemplo: criar ocorrência idempotente

Primeiro, cadastre-se ou entre em `POST /auth/login` e copie `accessToken`. No botão **Authorize** do Swagger, cole o JWT.

Em **POST `/incidents`**, clique em **Try it out**, preencha o cabeçalho `Idempotency-Key` com um identificador como `teste-alagamento-001` e envie:

```json
{
  "categoryId": 3,
  "title": "Ponto de alagamento na Mooca",
  "description": "A rua alagou após uma chuva forte e impede a circulação de moradores.",
  "address": "Rua das Flores, 200",
  "neighborhood": "Mooca",
  "latitude": -23.55,
  "longitude": -46.63
}
```

- Primeira chamada com a chave: **`201 Created`**; grava uma ocorrência.
- Repetição com a mesma chave **e mesmo corpo**: **`200 OK`**; retorna o mesmo ID sem novo INSERT.
- Mesma chave **e corpo diferente**: **`409 Conflict`**.
- Sem a chave ou com chave inválida: **`400 Bad Request`**.

O fingerprint SHA-256 é calculado sobre o DTO recebido. A chave é única por autor e há um índice único no banco, protegendo contra duplicação mesmo diante de requisições concorrentes.

### Exemplo: atualização de status pelo gestor

Execute `POST /incidents/{id}/updates`:

```json
{ "newStatus": "IN_REVIEW", "note": "Equipe de zeladoria recebeu a solicitação." }
```

Continue com `IN_PROGRESS` e depois `RESOLVED`. Transições inválidas são recusadas com `409`. O histórico fica em `GET /incidents/{id}/updates`.

## Rate Limiting

A API usa o middleware nativo do ASP.NET Core:

- Limite global: **120 solicitações por IP por minuto**.
- Limite adicional de login: **5 chamadas por minuto**.
- Ao exceder: **HTTP 429** e JSON com mensagem de erro.

Isso evita abuso básico e protege especialmente a rota pública de autenticação. O armazenamento de contadores é em memória: para muitas instâncias, seria necessário um gateway ou limiter distribuído.

## Health Checks

- `GET /health/live`: confirma que a aplicação está respondendo.
- `GET /health/ready`: executa verificação de conectividade real com o SQLite.

Se o banco ficar indisponível, a verificação de prontidão retorna status não saudável. Não são declarados serviços externos inexistentes.

## Testes (xUnit)

Na raiz do repositório:

```powershell
dotnet test
```

Os testes unitários verificam transições de status e fingerprints de idempotência. Os testes de integração inicializam a API com `WebApplicationFactory` e um SQLite temporário individual, cobrindo:

- Cadastro, login JWT e endpoint protegido (`401` sem autenticação).
- Repetição idempotente (primeira `201`, repetida `200`, payload diferente `409`, apenas uma ocorrência persistida).
- Restrição de papéis (`403` para cidadão em categoria; gestor autorizado).
- Histórico de status, transição inválida `409` e encerramento.
- Validação de DTO (`400`), limite de login (`429`) e health checks (`200`).

**Importante:** o código foi preparado para rodar com SDK 8; se o ambiente de avaliação não tiver internet, restaure os pacotes NuGet previamente. Veja mais em [docs/TESTES_SWAGGER.md](docs/TESTES_SWAGGER.md).

## Organização do repositório

```text
AntesDaChuva-DotNet/
├── AntesDaChuva.DotNet.sln
├── README.md
├── src/AntesDaChuva.Api/
│   ├── Api/                  # mapeamento de Minimal APIs
│   ├── Contracts/            # contratos de entrada e saída
│   ├── Data/                 # EF Core + seed SQLite
│   ├── Domain/               # entidades e enumerações
│   ├── Services/             # JWT, validação, regras e health
│   ├── Program.cs
│   └── appsettings.json
├── tests/AntesDaChuva.Tests/ # xUnit + WebApplicationFactory
├── docs/
│   ├── TESTES_SWAGGER.md
│   └── APRESENTACAO_NET.md
└── entrega/ENTREGA_NET.txt   # completar com dados reais do grupo
```

## Limitações e decisões técnicas

- O SQLite elimina dependência de Oracle/servidor remoto e permite um projeto reproduzível no computador do avaliador. O EF Core permite migrar para outro provedor posteriormente.
- A inicialização usa `EnsureCreated` e seed idempotente das categorias, suficiente para a demonstração. Para evolução de schema em produção, usar EF Migrations.
- Não integra automaticamente o backend Java nem classifica ocorrências por IA: o foco desta API são os requisitos específicos da disciplina .NET.
- Não existe uma conta gestora fixa publicada com senha: a criação de gestor é explícita, local e protegida pelo modo Development.
- A idempotência é persistida no banco, mas uso em múltiplas réplicas e alta concorrência exige testes adicionais; o rate limit é por processo.

## Contribuição para CitizenTech

O Antes da Chuva reduz o trabalho de registrar e acompanhar ocorrências urbanas relacionadas a drenagem. A separação entre cidadão e gestor e o histórico de status ajudam a organizar solicitações e a tornar o atendimento mais rastreável. A API não é um alerta da Defesa Civil nem substitui a avaliação de riscos por autoridades.

## Declaração de uso de IA generativa

Ferramentas de IA podem ter sido usadas como apoio na estruturação inicial, revisão de documentação e código. O grupo deve revisar, testar e compreender o conteúdo antes da entrega, informando o uso efetivo conforme o regulamento FIAP.

## Grupo

**Global Solution FIAP 2026/2 — Antes da Chuva**  
**Integrantes / RM / turma:** preencher com os dados reais antes de entregar.  
**Disciplina:** Advanced Business Development with .NET.
