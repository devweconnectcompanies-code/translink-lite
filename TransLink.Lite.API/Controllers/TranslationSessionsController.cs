using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransLink.Lite.Application.TranslationSessions;
using TransLink.Lite.Application.TranslationSessions.DTOs;

namespace TransLink.Lite.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class TranslationSessionsController : ControllerBase
{
    private readonly ITranslationSessionService _translationSessionService;

    public TranslationSessionsController(ITranslationSessionService translationSessionService)
    {
        _translationSessionService = translationSessionService;
    }

    [HttpPost]
    public async Task<ActionResult<TranslationSessionResponse>> CreateTranslationSession(
        CreateTranslationSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var response = await _translationSessionService.CreateAsync(
            userId,
            request,
            cancellationToken);

        return Ok(response);
    }

    [HttpGet]
    public async Task<ActionResult<List<TranslationSessionResponse>>> GetTranslationSessions(
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        return Ok(await _translationSessionService.GetByUserIdAsync(userId, cancellationToken));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TranslationSessionResponse>> GetTranslationSession(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var response = await _translationSessionService.GetByIdAsync(id, userId, cancellationToken);
        return Ok(response);
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var claimValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claimValue, out userId);
    }
}
