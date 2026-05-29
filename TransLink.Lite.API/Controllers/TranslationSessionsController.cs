using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransLink.Lite.Application.TranslationSessions.DTOs;
using TransLink.Lite.Domain.Entities;
using TransLink.Lite.Infrastructure.Persistence;

namespace TransLink.Lite.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class TranslationSessionsController : ControllerBase
{
    private readonly AppDbContext _context;

    public TranslationSessionsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpPost]
    public async Task<ActionResult<TranslationSessionResponse>> CreateTranslationSession(
        CreateTranslationSessionRequest request)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var session = MapToEntity(request, userId);

        _context.TranslationSessions.Add(session);
        await _context.SaveChangesAsync();

        return Ok(MapToResponse(session));
    }

    [HttpGet]
    public async Task<ActionResult<List<TranslationSessionResponse>>> GetTranslationSessions()
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var sessions = await _context.TranslationSessions
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        return Ok(sessions.Select(MapToResponse).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TranslationSessionResponse>> GetTranslationSession(Guid id)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var session = await _context.TranslationSessions
            .SingleOrDefaultAsync(s => s.Id == id && s.UserId == userId);

        if (session is null)
        {
            return NotFound();
        }

        return Ok(MapToResponse(session));
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claimValue, out userId);
    }

    private static TranslationSession MapToEntity(CreateTranslationSessionRequest request, Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Title = request.Title,
        SourceLanguage = request.SourceLanguage,
        TargetLanguage = request.TargetLanguage,
        Status = "Draft",
        CreatedAt = DateTime.UtcNow,
    };

    private static TranslationSessionResponse MapToResponse(TranslationSession session) => new()
    {
        Id = session.Id,
        UserId = session.UserId,
        Title = session.Title,
        SourceLanguage = session.SourceLanguage,
        TargetLanguage = session.TargetLanguage,
        Status = session.Status,
        CreatedAt = session.CreatedAt,
        StartedAt = session.StartedAt,
        EndedAt = session.EndedAt,
    };
}
