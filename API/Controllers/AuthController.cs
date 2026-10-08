using Microsoft.AspNetCore.Mvc;
using TranslationBackend.Application.DTOs;
using TranslationBackend.Application.Interface.IService;

namespace TranslationBackend.API.Controllers;

/// <summary>
/// Microsoft sign-in. The endpoints and response shapes are the SupplyChain app's, so its frontend auth screens
/// (login, register, callback, "awaiting approval", "no access") work unchanged.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService auth, ILogger<AuthController> logger)
    {
        _auth = auth;
        _logger = logger;
    }

    /// <summary>Public: the Developers a person waiting for approval can contact.</summary>
    [HttpGet("developers")]
    public IActionResult GetDevelopers() => Ok(_auth.GetDevelopers().Select(o => new { name = o.Name, email = o.Email }));

    [HttpGet("microsoft/login")]
    public IActionResult MicrosoftLogin() => Ok(new MicrosoftAuthUrlDto { AuthUrl = _auth.BuildAuthUrl("LOGIN") });

    [HttpGet("microsoft/register")]
    public IActionResult MicrosoftRegister() => Ok(new MicrosoftAuthUrlDto { AuthUrl = _auth.BuildAuthUrl("REGISTER") });

    [HttpGet("microsoft/callback")]
    public async Task<IActionResult> MicrosoftCallback([FromQuery] string? code, [FromQuery] string? state, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return BadRequest(new { message = "Invalid callback parameters" });

        try
        {
            var result = await _auth.SignInWithMicrosoftAsync(code, ct);
            var user = result.User;

            return result.Outcome switch
            {
                SignInOutcome.Success => Ok(new
                {
                    token = result.Token,
                    username = user!.UserName,
                    fullName = user.Name,
                    email = user.Email,
                    role = user.Role.ToString(),
                }),
                SignInOutcome.Deactivated => Unauthorized(new { deactivated = true, message = result.Message }),
                _ => BadRequest(new { message = result.Message }),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Microsoft sign-in failed");
            return StatusCode(500, new { message = "An error occurred during Microsoft authentication" });
        }
    }
}
