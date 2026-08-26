using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    public UsersController(AppDbContext context)
    {
        _context = context;
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
