using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace UsuarioApi.Controller;
[TokenAuthorize]
[ApiController]
[Route("api/[controller]")]
public class UsuarioController : ControllerBase
{
    private readonly Context _context;

    public UsuarioController(Context context)
    {
        _context = context;
    }

    // GET: api/Usuario/Listar
    [HttpGet("Listar")]
    public async Task<IActionResult> Listar()
    {
        var usuarios = await (
            from u in _context.Usuarios
            where u.Estado == "Activo"
            select u
        ).ToListAsync();

        return Ok(usuarios);
    }

    // GET: api/Usuario/Buscar/yeymi
    [HttpGet("Buscar/{user}")]
    public async Task<IActionResult> Buscar(string user)
    {
        var usuario = await (
            from u in _context.Usuarios
            where u.User.ToLower() == user.ToLower()
            select u
        ).FirstOrDefaultAsync();

        if (usuario == null)
            return NotFound("Usuario no encontrado.");

        return Ok(usuario);
    }

    // POST: api/Usuario/Crear
    [HttpPost("Crear")]
    public async Task<IActionResult> Crear(string nombre,string email,string biografia)
    {
        bool existe = await (
            from u in _context.Usuarios
            where u.User.ToLower() == nombre.ToLower()
            select u
        ).AnyAsync();

        if (existe)
            return Conflict("El nombre de usuario ya existe.");

        Usuario usuario=new Usuario();
        usuario.Biografia=biografia;
        usuario.User=nombre;
        usuario.email=email;
        usuario.Fecha=DateOnly.FromDateTime(DateTime.Now).ToString();
        usuario.Estado="Activo";
        _context.Usuarios.Add(usuario);

        await _context.SaveChangesAsync();

        return Ok(usuario);
    }

    // PUT: api/Usuario/Actualizar
    [HttpPut("Actualizar")]
    public async Task<IActionResult> Actualizar(CambiarUsuario datos)
    {
        var usuario = await (
            from u in _context.Usuarios
            where u.User.ToLower() == datos.UserAntiguo.ToLower()
            select u
        ).FirstOrDefaultAsync();

        if (usuario == null)
            return NotFound("El usuario antiguo no existe.");

        bool existe = await (
            from u in _context.Usuarios
            where u.User.ToLower() == datos.UserNuevo.ToLower()
            select u
        ).AnyAsync();

        if (existe)
            return Conflict("El nuevo nombre de usuario ya está ocupado.");

        usuario.User = datos.UserNuevo;

        await _context.SaveChangesAsync();

        return Ok(usuario);
    }

    // DELETE: api/Usuario/Eliminar/yeymi
    [HttpDelete("Eliminar/{user}")]
    public async Task<IActionResult> Eliminar(string user)
    {
        var usuario = await (
            from u in _context.Usuarios
            where u.User.ToLower() == user.ToLower()
            select u
        ).FirstOrDefaultAsync();

        if (usuario == null)
            return NotFound("Usuario no encontrado.");

        usuario.Estado = "Inactivo";

        await _context.SaveChangesAsync();

        return Ok("La cuenta ha sido desactivada correctamente.");
    }
}