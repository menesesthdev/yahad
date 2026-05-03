using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddEndpointsApiExplorer();

var connectionString = builder.Configuration.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException(
        "ConnectionString 'Default' não configurada. Crie 'appsettings.Local.json' a partir de 'appsettings.Local.example.json' ou defina a variável de ambiente ConnectionStrings__Default.");

builder.Services.AddDbContext<AppDbContext>(opt => opt.UseNpgsql(connectionString));
builder.Services.AddScoped<IRoleRepository, EfRoleRepository>();
builder.Services.AddScoped<IUsuarioRepository, EfUsuarioRepository>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { status = "ok", servico = "yahad-api" }));

app.MapRolesEndpoints();
app.MapUsuariosEndpoints();

app.Run();


public record RoleCreateDto(string Nome);
public record RoleResponse(int Id, string Nome);

public record UsuarioCreateDto(string Nome, string Email, string Senha, int RoleId);
public record UsuarioUpdateDto(string Nome, string Email, int RoleId);
public record UsuarioResponse(int Id, string Nome, string Email, int RoleId, string? RoleNome);


public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Role> Roles => Set<Role>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>(e =>
        {
            e.ToTable("roles");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasColumnName("id");
            e.Property(r => r.Nome).HasColumnName("nome").HasMaxLength(50).IsRequired();
            e.HasIndex(r => r.Nome).IsUnique();
        });

        modelBuilder.Entity<Usuario>(e =>
        {
            e.ToTable("usuarios");
            e.HasKey(u => u.Id);
            e.Property(u => u.Id).HasColumnName("id");
            e.Property(u => u.Nome).HasColumnName("nome").HasMaxLength(120).IsRequired();
            e.Property(u => u.Email).HasColumnName("email").HasMaxLength(160).IsRequired();
            e.Property(u => u.SenhaHash).HasColumnName("senha_hash").HasMaxLength(256).IsRequired();
            e.Property(u => u.RoleId).HasColumnName("role_id");
            e.HasIndex(u => u.Email).IsUnique();
            e.HasOne(u => u.Role)
                .WithMany(r => r.Usuarios)
                .HasForeignKey(u => u.RoleId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}


public interface IRoleRepository
{
    Task<IEnumerable<Role>> GetAllAsync(CancellationToken ct = default);
    Task<Role?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<Role> AddAsync(Role role, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, Role role, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}

public interface IUsuarioRepository
{
    Task<IEnumerable<Usuario>> GetAllAsync(CancellationToken ct = default);
    Task<Usuario?> GetByIdAsync(int id, CancellationToken ct = default);
    Task<bool> EmailExisteAsync(string email, int? ignorarId = null, CancellationToken ct = default);
    Task<Usuario> AddAsync(Usuario usuario, CancellationToken ct = default);
    Task<bool> UpdateAsync(int id, Usuario usuario, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
}


public class EfRoleRepository : IRoleRepository
{
    private readonly AppDbContext _db;
    public EfRoleRepository(AppDbContext db) => _db = db;

    public async Task<IEnumerable<Role>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Roles.AsNoTracking().OrderBy(r => r.Id).ToListAsync(ct);

    public Task<Role?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<Role> AddAsync(Role role, CancellationToken ct = default)
    {
        _db.Roles.Add(role);
        await _db.SaveChangesAsync(ct);
        return role;
    }

    public async Task<bool> UpdateAsync(int id, Role role, CancellationToken ct = default)
    {
        var existing = await _db.Roles.FindAsync([id], ct);
        if (existing is null) return false;
        existing.Nome = role.Nome;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var existing = await _db.Roles.FindAsync([id], ct);
        if (existing is null) return false;
        _db.Roles.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}

public class EfUsuarioRepository : IUsuarioRepository
{
    private readonly AppDbContext _db;
    public EfUsuarioRepository(AppDbContext db) => _db = db;

    public async Task<IEnumerable<Usuario>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Usuarios.AsNoTracking().Include(u => u.Role).OrderBy(u => u.Id).ToListAsync(ct);

    public Task<Usuario?> GetByIdAsync(int id, CancellationToken ct = default) =>
        _db.Usuarios.AsNoTracking().Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<bool> EmailExisteAsync(string email, int? ignorarId = null, CancellationToken ct = default)
    {
        var lower = email.ToLower();
        return _db.Usuarios.AnyAsync(u =>
            u.Email.ToLower() == lower &&
            (ignorarId == null || u.Id != ignorarId), ct);
    }

    public async Task<Usuario> AddAsync(Usuario usuario, CancellationToken ct = default)
    {
        _db.Usuarios.Add(usuario);
        await _db.SaveChangesAsync(ct);
        return usuario;
    }

    public async Task<bool> UpdateAsync(int id, Usuario usuario, CancellationToken ct = default)
    {
        var existing = await _db.Usuarios.FindAsync([id], ct);
        if (existing is null) return false;
        existing.Nome = usuario.Nome;
        existing.Email = usuario.Email;
        existing.RoleId = usuario.RoleId;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var existing = await _db.Usuarios.FindAsync([id], ct);
        if (existing is null) return false;
        _db.Usuarios.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return true;
    }
}


public static class PasswordHasher
{
    public static string Hash(string senha)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(senha));
        return Convert.ToHexString(bytes);
    }
}


public static class RolesEndpoints
{
    public static IEndpointRouteBuilder MapRolesEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/roles").WithTags("Roles");

        grupo.MapGet("/", async (IRoleRepository repo, CancellationToken ct) =>
            Results.Ok((await repo.GetAllAsync(ct)).Select(ToResponse)));

        grupo.MapGet("/{id:int}", async (int id, IRoleRepository repo, CancellationToken ct) =>
            await repo.GetByIdAsync(id, ct) is { } r ? Results.Ok(ToResponse(r)) : Results.NotFound());

        grupo.MapPost("/", async (RoleCreateDto dto, IRoleRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Nome))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Nome"] = ["O nome é obrigatório."]
                });

            var role = await repo.AddAsync(new Role { Nome = dto.Nome.Trim() }, ct);
            return Results.Created($"/roles/{role.Id}", ToResponse(role));
        });

        grupo.MapPut("/{id:int}", async (int id, RoleCreateDto dto, IRoleRepository repo, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Nome))
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["Nome"] = ["O nome é obrigatório."]
                });

            return await repo.UpdateAsync(id, new Role { Nome = dto.Nome.Trim() }, ct)
                ? Results.NoContent()
                : Results.NotFound();
        });

        grupo.MapDelete("/{id:int}", async (int id, IRoleRepository repo, CancellationToken ct) =>
            await repo.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        return app;
    }

    private static RoleResponse ToResponse(Role r) => new(r.Id, r.Nome);
}


public static class UsuariosEndpoints
{
    public static IEndpointRouteBuilder MapUsuariosEndpoints(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/usuarios").WithTags("Usuarios");

        grupo.MapGet("/", async (IUsuarioRepository repo, CancellationToken ct) =>
            Results.Ok((await repo.GetAllAsync(ct)).Select(ToResponse)));

        grupo.MapGet("/{id:int}", async (int id, IUsuarioRepository repo, CancellationToken ct) =>
            await repo.GetByIdAsync(id, ct) is { } u ? Results.Ok(ToResponse(u)) : Results.NotFound());

        grupo.MapPost("/", async (UsuarioCreateDto dto, IUsuarioRepository repo, IRoleRepository roles, CancellationToken ct) =>
        {
            var erros = await ValidarCriacaoAsync(dto, repo, roles, ct);
            if (erros.Count > 0) return Results.ValidationProblem(erros);

            var usuario = await repo.AddAsync(new Usuario
            {
                Nome = dto.Nome.Trim(),
                Email = dto.Email.Trim(),
                SenhaHash = PasswordHasher.Hash(dto.Senha),
                RoleId = dto.RoleId
            }, ct);

            var criado = await repo.GetByIdAsync(usuario.Id, ct);
            return Results.Created($"/usuarios/{usuario.Id}", ToResponse(criado!));
        });

        grupo.MapPut("/{id:int}", async (int id, UsuarioUpdateDto dto, IUsuarioRepository repo, IRoleRepository roles, CancellationToken ct) =>
        {
            if (await repo.GetByIdAsync(id, ct) is null) return Results.NotFound();

            var erros = await ValidarAtualizacaoAsync(dto, id, repo, roles, ct);
            if (erros.Count > 0) return Results.ValidationProblem(erros);

            await repo.UpdateAsync(id, new Usuario
            {
                Nome = dto.Nome.Trim(),
                Email = dto.Email.Trim(),
                RoleId = dto.RoleId
            }, ct);
            return Results.NoContent();
        });

        grupo.MapDelete("/{id:int}", async (int id, IUsuarioRepository repo, CancellationToken ct) =>
            await repo.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());

        return app;
    }

    private static async Task<Dictionary<string, string[]>> ValidarCriacaoAsync(
        UsuarioCreateDto dto, IUsuarioRepository repo, IRoleRepository roles, CancellationToken ct)
    {
        var erros = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(dto.Nome)) erros["Nome"] = ["O nome é obrigatório."];
        if (string.IsNullOrWhiteSpace(dto.Email)) erros["Email"] = ["O email é obrigatório."];
        else if (await repo.EmailExisteAsync(dto.Email, null, ct)) erros["Email"] = ["Email já cadastrado."];
        if (string.IsNullOrWhiteSpace(dto.Senha) || dto.Senha.Length < 6)
            erros["Senha"] = ["A senha deve ter no mínimo 6 caracteres."];
        if (await roles.GetByIdAsync(dto.RoleId, ct) is null) erros["RoleId"] = ["Role inexistente."];
        return erros;
    }

    private static async Task<Dictionary<string, string[]>> ValidarAtualizacaoAsync(
        UsuarioUpdateDto dto, int id, IUsuarioRepository repo, IRoleRepository roles, CancellationToken ct)
    {
        var erros = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(dto.Nome)) erros["Nome"] = ["O nome é obrigatório."];
        if (string.IsNullOrWhiteSpace(dto.Email)) erros["Email"] = ["O email é obrigatório."];
        else if (await repo.EmailExisteAsync(dto.Email, id, ct)) erros["Email"] = ["Email já cadastrado."];
        if (await roles.GetByIdAsync(dto.RoleId, ct) is null) erros["RoleId"] = ["Role inexistente."];
        return erros;
    }

    private static UsuarioResponse ToResponse(Usuario u) =>
        new(u.Id, u.Nome, u.Email, u.RoleId, u.Role?.Nome);
}
