using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
[TokenAuthorize]
public class PublicacionesController : ControllerBase
{
    private readonly Context _context;

    public PublicacionesController(Context context) => _context = context;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var publicaciones = await _context.Publicaciones
            .Where(p => p.Activo)
            .OrderByDescending(p => p.FechaCreacion)
            .ToListAsync();

        return Ok(publicaciones);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var publicacion = await _context.Publicaciones
            .FirstOrDefaultAsync(p => p.Id == id && p.Activo);

        if (publicacion == null)
            return NotFound(new { mensaje = "Publicación no encontrada o inactiva." });

        return Ok(publicacion);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Publicacion nueva)
    {
        if (string.IsNullOrWhiteSpace(nueva.Contenido))
            return BadRequest(new { mensaje = "El contenido es obligatorio." });

        bool existeUsuario = await _context.Usuarios.AnyAsync(u => u.Id == nueva.UsuarioId);
        if (!existeUsuario)
            return BadRequest(new { mensaje = "El UsuarioId no existe." });

        bool existeCategoria = await _context.CategoriasPublicacion
            .AnyAsync(c => c.Id == nueva.CategoriaPublicacionId && c.Activo);
        if (!existeCategoria)
            return BadRequest(new { mensaje = "El CategoriaPublicacionId no existe o está inactiva." });

        nueva.Id = 0;
        nueva.FechaCreacion = DateTime.UtcNow;
        nueva.Activo = true;
        _context.Publicaciones.Add(nueva);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = nueva.Id }, nueva);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] Publicacion editada)
    {
        if (id <= 0)
            return BadRequest(new { mensaje = "El id es obligatorio." });

        var publicacion = await _context.Publicaciones.FindAsync(id);
        if (publicacion == null)
            return NotFound(new { mensaje = "Publicación no encontrada." });
        if (!publicacion.Activo)
            return BadRequest(new { mensaje = "No se puede actualizar una publicación inactiva." });
        if (string.IsNullOrWhiteSpace(editada.Contenido))
            return BadRequest(new { mensaje = "El contenido es obligatorio." });

        bool existeCategoria = await _context.CategoriasPublicacion
            .AnyAsync(c => c.Id == editada.CategoriaPublicacionId && c.Activo);
        if (!existeCategoria)
            return BadRequest(new { mensaje = "El CategoriaPublicacionId no existe o está inactiva." });

        publicacion.Contenido = editada.Contenido.Trim();
        publicacion.CategoriaPublicacionId = editada.CategoriaPublicacionId;
        await _context.SaveChangesAsync();

        return Ok(publicacion);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (id <= 0)
            return BadRequest(new { mensaje = "El id es obligatorio." });

        var publicacion = await _context.Publicaciones.FindAsync(id);
        if (publicacion == null)
            return NotFound(new { mensaje = "Publicación no encontrada." });
        if (!publicacion.Activo)
            return BadRequest(new { mensaje = "La publicación ya está inactiva." });

        publicacion.Activo = false;
        await _context.SaveChangesAsync();

        return Ok(new { mensaje = "Publicación marcada como inactiva.", id = publicacion.Id, activo = publicacion.Activo });
    }
}
