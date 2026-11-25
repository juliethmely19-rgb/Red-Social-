using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
[TokenAuthorize]
public class HashtagsController : ControllerBase
{
    private readonly Context _context;
    public HashtagsController(Context context) => _context = context;

    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await _context.Hashtags.Where(h => h.Activo).OrderByDescending(h => h.FechaCreacion).ToListAsync());

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var item = await _context.Hashtags.FirstOrDefaultAsync(h => h.Id == id && h.Activo);
        return item == null ? NotFound(new { mensaje = "Hashtag no encontrado o inactivo." }) : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Hashtag nuevo)
    {
        if (string.IsNullOrWhiteSpace(nuevo.Nombre))
            return BadRequest(new { mensaje = "El nombre del hashtag es obligatorio." });
        var limpio = nuevo.Nombre.Trim();
        nuevo.Nombre = limpio.StartsWith("#") ? limpio : $"#{limpio}";
        nuevo.Id = 0;
        nuevo.FechaCreacion = DateTime.UtcNow;
        nuevo.Activo = true;
        _context.Hashtags.Add(nuevo);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = nuevo.Id }, nuevo);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] Hashtag editado)
    {
        var item = await _context.Hashtags.FindAsync(id);
        if (item == null) return NotFound(new { mensaje = "Hashtag no encontrado." });
        if (!item.Activo) return BadRequest(new { mensaje = "No se puede actualizar un hashtag inactivo." });
        if (string.IsNullOrWhiteSpace(editado.Nombre))
            return BadRequest(new { mensaje = "El nombre del hashtag es obligatorio." });

        var limpio = editado.Nombre.Trim();
        item.Nombre = limpio.StartsWith("#") ? limpio : $"#{limpio}";
        await _context.SaveChangesAsync();
        return Ok(item);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _context.Hashtags.FindAsync(id);
        if (item == null) return NotFound(new { mensaje = "Hashtag no encontrado." });
        if (!item.Activo) return BadRequest(new { mensaje = "El hashtag ya está inactivo." });

        item.Activo = false;
        await _context.SaveChangesAsync();
        return Ok(new { mensaje = "Hashtag marcado como inactivo (soft delete).", id = item.Id, activo = item.Activo });
    }
}
