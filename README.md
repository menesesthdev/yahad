# Yahad – Documentação Técnica

Documento técnico de arquitetura e decisões do projeto Yahad.

## 1. Visão Geral

O Yahad é uma plataforma modular para organização da Escola Bíblica Dominical, com arquitetura preparada para expansão futura para outros módulos da igreja.

## 2. Stack Tecnológica

- **Frontend**: Angular (SPA)
- **Backend**: ASP.NET Core 10 — Minimal API
- **ORM**: Entity Framework Core 10 (provider Npgsql)
- **Banco de Dados**: PostgreSQL (servidor local, porta 5432)
- **Serviços auxiliares**: Python para automações e IA

## 3. Autenticação e Segurança

A autenticação será baseada em **JWT** (JSON Web Token). O backend gerará tokens assinados após login válido.

- Todas as requisições protegidas exigirão o header `Authorization: Bearer <token>`.
- Autorização baseada em papéis: **Administrador**, **Superintendente de EBD**, **Professor**.
- Senhas armazenadas como **hash** (SHA-256 hoje, evolução planejada para BCrypt/Argon2). O texto puro nunca é persistido.

> Status atual: hash de senha implementado. Endpoint de login + emissão/validação de JWT ainda **pendentes**.

## 4. Backend (.NET)

Responsável pelas regras de negócio, controle de acesso, persistência e orquestração de serviços.

Arquitetura adotada: **Minimal API** com separação por responsabilidade dentro do `Program.cs`:

- **DTOs** (`records`) — contratos de entrada/saída desacoplados das entities. Garante que campos sensíveis (ex.: `SenhaHash`) nunca vazem no JSON.
- **Entities** — classes em `models/` (`Usuario`, `Role`).
- **DbContext** (`AppDbContext`) — mapeamento Fluent API com `snake_case` nas colunas, índices únicos e relacionamento `Usuario → Role` com `OnDelete(Restrict)`.
- **Repositórios** (`IRoleRepository`, `IUsuarioRepository`) — abstração sobre o EF Core; permite trocar implementação (ex.: Dapper para hotspots) sem mexer nos endpoints.
- **Endpoints agrupados** via `MapGroup` em extension methods (`MapRolesEndpoints`, `MapUsuariosEndpoints`).
- **Validação** inline com `Results.ValidationProblem` (Problem Details RFC 7807).
- **Async** ponta a ponta com `CancellationToken`.

### 4.1. Endpoints atuais

#### Health
- `GET /` → `{ status: "ok", servico: "yahad-api" }`

#### Roles (`/roles`)
| Método | Rota | Descrição |
|---|---|---|
| GET | `/roles` | Lista todas |
| GET | `/roles/{id}` | Busca por id |
| POST | `/roles` | Cria role |
| PUT | `/roles/{id}` | Atualiza role |
| DELETE | `/roles/{id}` | Remove role |

#### Usuários (`/usuarios`)
| Método | Rota | Descrição |
|---|---|---|
| GET | `/usuarios` | Lista todos (com `roleNome` via `Include`) |
| GET | `/usuarios/{id}` | Busca por id |
| POST | `/usuarios` | Cria usuário (gera hash da senha) |
| PUT | `/usuarios/{id}` | Atualiza dados (não altera senha) |
| DELETE | `/usuarios/{id}` | Remove usuário |

Regras de validação (POST/PUT de usuário): `Nome` e `Email` obrigatórios, email único, senha mínima de 6 caracteres no POST, `RoleId` deve existir.

### 4.2. Estrutura de pastas

```
back_yahad/
├── Program.cs                  # Minimal API completa (DI, DbContext, repos, endpoints)
├── back_yahad.csproj
├── appsettings.json            # ConnectionStrings:Default
├── appsettings.Development.json
├── models/
│   ├── RoleModel.cs
│   └── UsuarioModel.cs
├── Migrations/                 # geradas pelo dotnet ef
└── Properties/launchSettings.json
```

## 5. Banco de Dados

Banco PostgreSQL relacional, com separação lógica por igreja (multi-tenant lógico) prevista para fases futuras.

### 5.1. Schema atual

**`roles`**
| Coluna | Tipo | Constraints |
|---|---|---|
| id | `serial` | PK |
| nome | `varchar(50)` | NOT NULL, único |

**`usuarios`**
| Coluna | Tipo | Constraints |
|---|---|---|
| id | `serial` | PK |
| nome | `varchar(120)` | NOT NULL |
| email | `varchar(160)` | NOT NULL, único |
| senha_hash | `varchar(256)` | NOT NULL |
| role_id | `integer` | NOT NULL, FK → `roles.id` (`ON DELETE RESTRICT`) |

Versionamento de schema via **EF Core Migrations** (pasta `Migrations/`).

## 6. Como subir o ambiente local

### 6.1. Pré-requisitos
- .NET SDK 10
- PostgreSQL acessível (container ou nativo) em `localhost:5432`
- CLI do EF Core: `dotnet tool install --global dotnet-ef`

### 6.2. Configuração

Credenciais de banco **não** ficam no `appsettings.json`. Use uma das opções abaixo:

**Opção A — `appsettings.Local.json` (recomendada para dev):**

Copie o template e preencha com seus dados:
```bash
cp back_yahad/appsettings.Local.example.json back_yahad/appsettings.Local.json
```

Edite `back_yahad/appsettings.Local.json`:
```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=yahadDb;Username=SEU_USUARIO;Password=SUA_SENHA"
  }
}
```

Esse arquivo é carregado automaticamente pelo `Program.cs` e está no `.gitignore` — não será commitado.

**Opção B — variável de ambiente (recomendada para produção):**

```bash
export ConnectionStrings__Default="Host=...;Port=5432;Database=yahadDb;Username=...;Password=..."
```

> Em hipótese alguma comitar credenciais reais em `appsettings.json` ou `appsettings.Development.json`. Esses dois arquivos são versionados.

### 6.3. Aplicar migrations

```bash
cd back_yahad
dotnet ef database update
```

### 6.4. Rodar a API

```bash
cd back_yahad
dotnet run
```

API sobe em `http://localhost:5014`.

### 6.5. Smoke test

```bash
# 1) Cria roles
curl -X POST http://localhost:5014/roles -H "Content-Type: application/json" -d '{"nome":"admin"}'
curl -X POST http://localhost:5014/roles -H "Content-Type: application/json" -d '{"nome":"usuario"}'

# 2) Cria usuário de teste
curl -X POST http://localhost:5014/usuarios \
  -H "Content-Type: application/json" \
  -d '{"nome":"João Teste","email":"joao.teste@yahad.dev","senha":"senha123","roleId":2}'

# 3) Lista
curl http://localhost:5014/usuarios
```

## 7. Serviços em Python

Utilizados para automações, processamento assíncrono e integrações com IA. Comunicação com o backend via HTTP ou mensageria futura.

## 8. Mensageria (Futuro)

Planejada para comunicação assíncrona, eventos de sistema e tarefas em background.

## 9. Infraestrutura

Servidor local hospedando banco de dados, APIs .NET, serviços Python e futuramente mensageria.

## 10. Princípios de Arquitetura

Modularidade, segurança, escalabilidade gradual, baixa dependência e manutenção simples.

## 11. Evolução Planejada

- **Curto prazo**: autenticação JWT (login + middleware), troca de hash para BCrypt, fluxo de "esqueci minha senha".
- **Médio prazo**: módulo EBD funcional (turmas, presença, lições), automações e relatórios.
- **Longo prazo**: mensageria, IA e novos módulos da igreja.

## 12. Histórico recente

**2026-05-03 — Backend inicial**
- Minimal API em `back_yahad/Program.cs` com endpoints CRUD de `/roles` e `/usuarios`.
- Integração EF Core 10 + Npgsql apontando para PostgreSQL local.
- Primeira migration (`first-migration`) aplicada — schema `roles` + `usuarios` criado.
- Hash de senha (SHA-256) e DTOs de resposta sem exposição de `SenhaHash`.
- Validação de unicidade de email e existência de Role na criação/atualização de usuário.
