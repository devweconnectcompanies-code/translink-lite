using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransLink.Lite.Domain.Entities;
using TransLink.Lite.Infrastructure.Persistence;

namespace TransLink.Lite.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _context;

    public UsersController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _context.Users.ToListAsync();

        return Ok(users);
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser(User user)
    {
        _context.Users.Add(user);

        await _context.SaveChangesAsync();

        return Ok(user);
    }
}