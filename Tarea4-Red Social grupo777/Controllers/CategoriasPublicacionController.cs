using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
[TokenAuthorize]
public class CategoriasPublicacionController : ControllerBase
{
    private readonly Context _context;

    public CategoriasPublicacionController(Context context) => _context = context;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var categorias = await _context.CategoriasPublicacion
            .Where(c => c.Activo)
            .OrderBy(c => c.Nombre)
            .ToListAsync();

        return Ok(categorias);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var categoria = await _context.CategoriasPublicacion
            .FirstOrDefaultAsync(c => c.Id == id && c.Activo);

        if (categoria == null)
            return NotFound(new { mensaje = "Categoría no encontrada o inactiva." });

        return Ok(categoria);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CategoriaPublicacion nueva)
    {
        if (string.IsNullOrWhiteSpace(nueva.Nombre))
            return BadRequest(new { mensaje = "El nombre es obligatorio." });

        nueva.Id = 0;
        nueva.Activo = true;
        _context.CategoriasPublicacion.Add(nueva);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = nueva.Id }, nueva);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] CategoriaPublicacion editada)
    {
        if (id <= 0)
            return BadRequest(new { mensaje = "El id es obligatorio." });

        var categoria = await _context.CategoriasPublicacion.FindAsync(id);
        if (categoria == null)
            return NotFound(new { mensaje = "Categoría no encontrada." });
        if (!categoria.Activo)
            return BadRequest(new { mensaje = "No se puede actualizar una categoría inactiva." });
        if (string.IsNullOrWhiteSpace(editada.Nombre))
            return BadRequest(new { mensaje = "El nombre es obligatorio." });

        categoria.Nombre = editada.Nombre.Trim();
        categoria.Descripcion = editada.Descripcion;
        await _context.SaveChangesAsync();

        return Ok(categoria);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (id <= 0)
            return BadRequest(new { mensaje = "El id es obligatorio." });

        var categoria = await _context.CategoriasPublicacion.FindAsync(id);
        if (categoria == null)
            return NotFound(new { mensaje = "Categoría no encontrada." });
        if (!categoria.Activo)
            return BadRequest(new { mensaje = "La categoría ya está inactiva." });

        categoria.Activo = false;
        await _context.SaveChangesAsync();

        return Ok(new { mensaje = "Categoría marcada como inactiva.", id = categoria.Id, activo = categoria.Activo });
    }
}
