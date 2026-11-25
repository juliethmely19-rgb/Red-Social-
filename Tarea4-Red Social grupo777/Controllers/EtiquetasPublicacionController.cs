using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
[TokenAuthorize]
public class EtiquetasPublicacionController : ControllerBase
{
    private readonly Context _context;

    public EtiquetasPublicacionController(Context context) => _context = context;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var etiquetas = await _context.EtiquetasPublicacion
            .Where(e => e.Activo)
            .OrderByDescending(e => e.FechaCreacion)
            .ToListAsync();

        return Ok(etiquetas);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var etiqueta = await _context.EtiquetasPublicacion
            .FirstOrDefaultAsync(e => e.Id == id && e.Activo);

        if (etiqueta == null)
            return NotFound(new { mensaje = "Etiqueta no encontrada o inactiva." });

        return Ok(etiqueta);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] EtiquetaPublicacion nueva)
    {
        if (string.IsNullOrWhiteSpace(nueva.Nombre))
            return BadRequest(new { mensaje = "El nombre es obligatorio." });

        bool existePublicacion = await _context.Publicaciones
            .AnyAsync(p => p.Id == nueva.PublicacionId && p.Activo);
        if (!existePublicacion)
            return BadRequest(new { mensaje = "El PublicacionId no existe o está inactiva." });

        nueva.Id = 0;
        nueva.FechaCreacion = DateTime.UtcNow;
        nueva.Activo = true;
        _context.EtiquetasPublicacion.Add(nueva);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = nueva.Id }, nueva);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] EtiquetaPublicacion editada)
    {
        if (id <= 0)
            return BadRequest(new { mensaje = "El id es obligatorio." });

        var etiqueta = await _context.EtiquetasPublicacion.FindAsync(id);
        if (etiqueta == null)
            return NotFound(new { mensaje = "Etiqueta no encontrada." });
        if (!etiqueta.Activo)
            return BadRequest(new { mensaje = "No se puede actualizar una etiqueta inactiva." });
        if (string.IsNullOrWhiteSpace(editada.Nombre))
            return BadRequest(new { mensaje = "El nombre es obligatorio." });

        etiqueta.Nombre = editada.Nombre.Trim();
        await _context.SaveChangesAsync();

        return Ok(etiqueta);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (id <= 0)
            return BadRequest(new { mensaje = "El id es obligatorio." });

        var etiqueta = await _context.EtiquetasPublicacion.FindAsync(id);
        if (etiqueta == null)
            return NotFound(new { mensaje = "Etiqueta no encontrada." });
        if (!etiqueta.Activo)
            return BadRequest(new { mensaje = "La etiqueta ya está inactiva." });

        etiqueta.Activo = false;
        await _context.SaveChangesAsync();

        return Ok(new { mensaje = "Etiqueta marcada como inactiva.", id = etiqueta.Id, activo = etiqueta.Activo });
    }
}
