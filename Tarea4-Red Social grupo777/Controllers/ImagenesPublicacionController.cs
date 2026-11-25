using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
[TokenAuthorize]
public class ImagenesPublicacionController : ControllerBase
{
    private readonly Context _context;

    public ImagenesPublicacionController(Context context) => _context = context;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var imagenes = await _context.ImagenesPublicacion
            .Where(i => i.Activo)
            .OrderByDescending(i => i.FechaCreacion)
            .ToListAsync();

        return Ok(imagenes);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var imagen = await _context.ImagenesPublicacion
            .FirstOrDefaultAsync(i => i.Id == id && i.Activo);

        if (imagen == null)
            return NotFound(new { mensaje = "Imagen no encontrada o inactiva." });

        return Ok(imagen);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ImagenPublicacion nueva)
    {
        if (string.IsNullOrWhiteSpace(nueva.UrlImagen))
            return BadRequest(new { mensaje = "La UrlImagen es obligatoria." });

        bool existePublicacion = await _context.Publicaciones
            .AnyAsync(p => p.Id == nueva.PublicacionId && p.Activo);
        if (!existePublicacion)
            return BadRequest(new { mensaje = "El PublicacionId no existe o está inactiva." });

        nueva.Id = 0;
        nueva.FechaCreacion = DateTime.UtcNow;
        nueva.Activo = true;
        _context.ImagenesPublicacion.Add(nueva);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = nueva.Id }, nueva);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] ImagenPublicacion editada)
    {
        if (id <= 0)
            return BadRequest(new { mensaje = "El id es obligatorio." });

        var imagen = await _context.ImagenesPublicacion.FindAsync(id);
        if (imagen == null)
            return NotFound(new { mensaje = "Imagen no encontrada." });
        if (!imagen.Activo)
            return BadRequest(new { mensaje = "No se puede actualizar una imagen inactiva." });
        if (string.IsNullOrWhiteSpace(editada.UrlImagen))
            return BadRequest(new { mensaje = "La UrlImagen es obligatoria." });

        imagen.UrlImagen = editada.UrlImagen.Trim();
        await _context.SaveChangesAsync();

        return Ok(imagen);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (id <= 0)
            return BadRequest(new { mensaje = "El id es obligatorio." });

        var imagen = await _context.ImagenesPublicacion.FindAsync(id);
        if (imagen == null)
            return NotFound(new { mensaje = "Imagen no encontrada." });
        if (!imagen.Activo)
            return BadRequest(new { mensaje = "La imagen ya está inactiva." });

        imagen.Activo = false;
        await _context.SaveChangesAsync();

        return Ok(new { mensaje = "Imagen marcada como inactiva.", id = imagen.Id, activo = imagen.Activo });
    }
}
