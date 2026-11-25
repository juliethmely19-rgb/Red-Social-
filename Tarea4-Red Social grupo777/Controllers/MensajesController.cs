using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
[TokenAuthorize]
public class MensajesController : ControllerBase
{
    private readonly Context _context;
    public MensajesController(Context context) => _context = context;

    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _context.Mensajes.Where(m => m.Activo).OrderByDescending(m => m.FechaCreacion).ToListAsync());

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var item = await _context.Mensajes.FirstOrDefaultAsync(m => m.Id == id && m.Activo);
        return item == null ? NotFound(new { mensaje = "Mensaje no encontrado o inactivo." }) : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Mensaje nuevo)
    {
        if (string.IsNullOrWhiteSpace(nuevo.Contenido))
            return BadRequest(new { mensaje = "El contenido es obligatorio." });
        nuevo.Id = 0;
        nuevo.FechaCreacion = DateTime.UtcNow;
        nuevo.Activo = true;
        if (nuevo.UsuarioId <= 0) nuevo.UsuarioId = await ObtenerORegistrarUsuarioActualAsync();
        _context.Mensajes.Add(nuevo);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = nuevo.Id }, nuevo);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] Mensaje editado)
    {
        var item = await _context.Mensajes.FindAsync(id);
        if (item == null) return NotFound(new { mensaje = "Mensaje no encontrado." });
        if (!item.Activo) return BadRequest(new { mensaje = "No se puede actualizar un mensaje eliminado (inactivo)." });
        if (string.IsNullOrWhiteSpace(editado.Contenido))
            return BadRequest(new { mensaje = "El contenido es obligatorio." });

        item.Contenido = editado.Contenido.Trim();
        await _context.SaveChangesAsync();
        return Ok(item);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _context.Mensajes.FindAsync(id);
        if (item == null) return NotFound(new { mensaje = "Mensaje no encontrado." });
        if (!item.Activo) return BadRequest(new { mensaje = "El mensaje ya está inactivo." });

        item.Activo = false;
        await _context.SaveChangesAsync();
        return Ok(new { mensaje = "Mensaje marcado como inactivo (soft delete).", id = item.Id, activo = item.Activo });
    }

    private async Task<int> ObtenerORegistrarUsuarioActualAsync()
    {
        // El access token de OpenIddict lleva el Id del usuario local en el claim "sub".
        var sub = User.FindFirst("sub")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(sub, out var idToken) &&
            await _context.Usuarios.AnyAsync(u => u.Id == idToken))
        {
            return idToken;
        }

        var email = User.FindFirst("email")?.Value ?? User.FindFirst(ClaimTypes.Email)?.Value ?? "oauth@redsocial.local";
        var nombre = User.Identity?.Name ?? "UsuarioOAuth";
        var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.email == email);
        if (usuario == null)
        {
            usuario = new Usuario
            {
                User = nombre,
                email = email,
                Biografia = $"Autenticado con {User.FindFirst("provider")?.Value ?? User.FindFirst("proveedor")?.Value ?? "OAuth"}",
                Fecha = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                Estado = "Activo"
            };
            _context.Usuarios.Add(usuario);
            await _context.SaveChangesAsync();
        }
        return usuario.Id;
    }
}
