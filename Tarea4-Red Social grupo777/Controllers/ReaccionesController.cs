using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
[TokenAuthorize]
public class ReaccionesController : ControllerBase
{
    private readonly Context _context;
    public ReaccionesController(Context context) => _context = context;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] int? mensajeId)
    {
        var query = _context.Reacciones.Where(r => r.Activo);
        if (mensajeId.HasValue) query = query.Where(r => r.MensajeId == mensajeId.Value);
        return Ok(await query.OrderByDescending(r => r.FechaReaccion).ToListAsync());
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var item = await _context.Reacciones.FirstOrDefaultAsync(r => r.Id == id && r.Activo);
        return item == null ? NotFound(new { mensaje = "Reacción no encontrada o inactiva." }) : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Reaccion nueva)
    {
        if (string.IsNullOrWhiteSpace(nueva.Tipo))
            return BadRequest(new { mensaje = "El tipo de reacción es obligatorio." });
        var existeMensaje = await _context.Mensajes.AnyAsync(m => m.Id == nueva.MensajeId && m.Activo);
        if (!existeMensaje) return BadRequest(new { mensaje = "El MensajeId no existe o está inactivo." });

        nueva.Id = 0;
        nueva.FechaReaccion = DateTime.UtcNow;
        nueva.Activo = true;
        if (nueva.UsuarioId <= 0) nueva.UsuarioId = await ObtenerORegistrarUsuarioActualAsync();
        _context.Reacciones.Add(nueva);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = nueva.Id }, nueva);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] Reaccion editada)
    {
        var item = await _context.Reacciones.FindAsync(id);
        if (item == null) return NotFound(new { mensaje = "Reacción no encontrada." });
        if (!item.Activo) return BadRequest(new { mensaje = "No se puede actualizar una reacción inactiva." });
        if (string.IsNullOrWhiteSpace(editada.Tipo))
            return BadRequest(new { mensaje = "El tipo de reacción es obligatorio." });

        item.Tipo = editada.Tipo.Trim();
        await _context.SaveChangesAsync();
        return Ok(item);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _context.Reacciones.FindAsync(id);
        if (item == null) return NotFound(new { mensaje = "Reacción no encontrada." });
        if (!item.Activo) return BadRequest(new { mensaje = "La reacción ya está inactiva." });

        item.Activo = false;
        await _context.SaveChangesAsync();
        return Ok(new { mensaje = "Reacción marcada como inactiva (soft delete).", id = item.Id, activo = item.Activo });
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
