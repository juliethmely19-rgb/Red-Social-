using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
[TokenAuthorize]
public class ComentariosController : ControllerBase
{
    private readonly Context _context;
    public ComentariosController(Context context) => _context = context;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? mensajeId)
    {
        var query = _context.Comentarios.Where(c => c.Activo);
        if (mensajeId.HasValue) query = query.Where(c => c.MensajeId == mensajeId.Value);
        return Ok(await query.OrderByDescending(c => c.FechaComentario).ToListAsync());
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var item = await _context.Comentarios.FirstOrDefaultAsync(c => c.Id == id && c.Activo);
        return item == null ? NotFound(new { mensaje = "Comentario no encontrado o inactivo." }) : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Comentario nuevo)
    {
        if (string.IsNullOrWhiteSpace(nuevo.Texto))
            return BadRequest(new { mensaje = "El texto es obligatorio." });
        var existeMensaje = await _context.Mensajes.AnyAsync(m => m.Id == nuevo.MensajeId && m.Activo);
        if (!existeMensaje) return BadRequest(new { mensaje = "El MensajeId no existe o está inactivo." });

        nuevo.Id = 0;
        nuevo.FechaComentario = DateTime.UtcNow;
        nuevo.Activo = true;
        if (nuevo.UsuarioId <= 0) nuevo.UsuarioId = await ObtenerORegistrarUsuarioActualAsync();
        _context.Comentarios.Add(nuevo);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = nuevo.Id }, nuevo);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] Comentario editado)
    {
        var item = await _context.Comentarios.FindAsync(id);
        if (item == null) return NotFound(new { mensaje = "Comentario no encontrado." });
        if (!item.Activo) return BadRequest(new { mensaje = "No se puede actualizar un comentario inactivo." });
        if (string.IsNullOrWhiteSpace(editado.Texto))
            return BadRequest(new { mensaje = "El texto es obligatorio." });

        item.Texto = editado.Texto.Trim();
        await _context.SaveChangesAsync();
        return Ok(item);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _context.Comentarios.FindAsync(id);
        if (item == null) return NotFound(new { mensaje = "Comentario no encontrado." });
        if (!item.Activo) return BadRequest(new { mensaje = "El comentario ya está inactivo." });

        item.Activo = false;
        await _context.SaveChangesAsync();
        return Ok(new { mensaje = "Comentario marcado como inactivo (soft delete).", id = item.Id, activo = item.Activo });
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
