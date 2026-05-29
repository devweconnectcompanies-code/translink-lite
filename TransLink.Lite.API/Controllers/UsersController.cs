using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransLink.Lite.Application.Users.DTOs;
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
    public async Task<ActionResult<List<UserResponse>>> GetUsers()
    {
        var users = await _context.Users.ToListAsync();

        return Ok(users.Select(MapToResponse).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<UserResponse>> CreateUser(CreateUserRequest request)
    {
        var user = MapToEntity(request);

        _context.Users.Add(user);

        await _context.SaveChangesAsync();

        return Ok(MapToResponse(user));
    }

    private static User MapToEntity(CreateUserRequest request) => new()
    {
        Id = Guid.NewGuid(),
        FirstName = request.FirstName,
        LastName = request.LastName,
        Email = request.Email,
        PasswordHash = request.Password,
        PreferredLanguage = request.PreferredLanguage,
        CreatedAt = DateTime.UtcNow,
    };

    private static UserResponse MapToResponse(User user) => new()
    {
        Id = user.Id,
        FirstName = user.FirstName,
        LastName = user.LastName,
        Email = user.Email,
        PreferredLanguage = user.PreferredLanguage,
        CreatedAt = user.CreatedAt,
    };
}
