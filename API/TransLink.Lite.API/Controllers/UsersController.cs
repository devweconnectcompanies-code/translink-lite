using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransLink.Lite.Application.Auth.Interfaces;
using TransLink.Lite.Application.Users.DTOs;
using TransLink.Lite.Domain.Entities;
using TransLink.Lite.Infrastructure.Persistence;

namespace TransLink.Lite.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IPasswordHasher _passwordHasher;

    public UsersController(AppDbContext context, IPasswordHasher passwordHasher)
    {
        _context = context;
        _passwordHasher = passwordHasher;
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
        var user = MapToEntity(request, _passwordHasher.HashPassword(request.Password));

        _context.Users.Add(user);

        await _context.SaveChangesAsync();

        return Ok(MapToResponse(user));
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> GetCurrentUser()
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var user = await _context.Users.FindAsync(userId);
        if (user is null)
        {
            return NotFound();
        }

        return Ok(MapToResponse(user));
    }

    [HttpPut("me")]
    public async Task<ActionResult<UserResponse>> UpdateCurrentUser(UpdateUserProfileRequest request)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var user = await _context.Users.FindAsync(userId);
        if (user is null)
        {
            return NotFound();
        }

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.PreferredLanguage = request.PreferredLanguage;

        await _context.SaveChangesAsync();

        return Ok(MapToResponse(user));
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claimValue, out userId);
    }

    private static User MapToEntity(CreateUserRequest request, string passwordHash) => new()
    {
        Id = Guid.NewGuid(),
        FirstName = request.FirstName,
        LastName = request.LastName,
        Email = request.Email,
        PasswordHash = passwordHash,
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
