using Microsoft.EntityFrameworkCore;
using Revisu.Data;
using Revisu.Domain.Entities;

public class UsuarioRepository 
{
    private readonly AppDbContext _context;

    public UsuarioRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Usuario> ObterPorEmailAsync(string email)
    {
        return await _context.Usuarios.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
    }

    public async Task<Usuario> ObterPorIdAsync(Guid id)
    {
        return await _context.Usuarios.FindAsync(id);
    }

    public async Task<bool> CadastrarAsync(Usuario usuario)
    {
        await _context.Usuarios.AddAsync(usuario);
        return await _context.SaveChangesAsync() > 0;
    }

    public async Task<bool> AtualizarAsync(Usuario usuario)
    {
        _context.Usuarios.Update(usuario);
        return await _context.SaveChangesAsync() > 0;
    }
}
