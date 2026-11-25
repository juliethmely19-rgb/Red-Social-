using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
namespace UsuarioApi.Controller;
[TokenAuthorize]
[ApiController]
[Route("api/[controller]")]
public class SeguidoresController : ControllerBase
{
    private readonly Context _context;

    public SeguidoresController(Context context)
    {
        _context = context;
    }
    [HttpGet("Listar")]
    public async Task<IActionResult> Listar()
    {
        var seguidores = await (
            from s in _context.Seguidores
            where s.Estado == "Activo"
            select s
        ).ToListAsync();

        return Ok(seguidores);
    }

    // GET: api/Seguidores/Buscar/1
    [HttpGet("Buscar/{id}")]
    public async Task<IActionResult> Buscar(int id)
    {
        var seguidor = await (
            from s in _context.Seguidores
            where s.UsuarioSeguidoId == id
            select s
        ).FirstOrDefaultAsync();

        if (seguidor == null)
            return NotFound("Seguidor no encontrado.");

        return Ok(seguidor);
    }

    // POST: api/Usuario/Crear
    [HttpPost("Crear")]
    public async Task<IActionResult> Crear(int usuarioSeguidorId, int usuarioSeguidoId)
    {
        bool existe = await (
            from s in _context.Seguidores
            where s.UsuarioSeguidorId == usuarioSeguidorId && s.UsuarioSeguidoId == usuarioSeguidoId
            select s
        ).AnyAsync();

        if (existe)
            return Conflict("El nombre de usuario ya existe.");

        Seguidores seguidor = new Seguidores();
        seguidor.UsuarioSeguidorId = usuarioSeguidorId;
        seguidor.UsuarioSeguidoId = usuarioSeguidoId;
        _context.Seguidores.Add(seguidor);

        await _context.SaveChangesAsync();

        return Ok(seguidor);
    }
    // PUT: api/Usuario/Actualizar
    [HttpPut("Actualizar")]
    public async Task<IActionResult> Actualizar(int nvuSID, int UsuarioSeguidoId, int UsuarioSeguidorId)
    {
        var seguidor = await (
            from s in _context.Seguidores
            where s.UsuarioSeguidoId == UsuarioSeguidoId && s.UsuarioSeguidorId == UsuarioSeguidorId
            select s
        ).FirstOrDefaultAsync();

        if (seguidor == null)
            return NotFound("El seguidor no existe.");
        seguidor.UsuarioSeguidorId = nvuSID;
        await _context.SaveChangesAsync();
        return Ok(seguidor);
    }
    [HttpDelete("Eliminar/{user}")]
    public async Task<IActionResult> Eliminar(int UsuarioSeguidorId, int UsuarioSeguidoId)
    {
        var seguidor = await (
            from s in _context.Seguidores
            where s.UsuarioSeguidoId == UsuarioSeguidoId && s.UsuarioSeguidorId == UsuarioSeguidorId
            select s
        ).FirstOrDefaultAsync();

        if (seguidor == null)
            return NotFound("no se encontró el seguidor con los IDs proporcionados.");

        seguidor.Estado = "Inactivo";
        await _context.SaveChangesAsync();

        return Ok("El seguidor ha sido eliminado correctamente.");
    }
   
}