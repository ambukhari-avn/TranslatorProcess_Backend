using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TranslationBackend.API.Filters;
using TranslationBackend.Application;
using TranslationBackend.Application.Interface.IService;
using TranslationBackend.Domain.Model;

namespace TranslationBackend.Infrastructure.Auth;

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.Key) || _options.Key.Length < 32)
            throw new InvalidOperationException("Jwt:Key must be set to a secret of at least 32 characters.");
    }

    /// <summary>
    /// Same claims as the SupplyChain app: the id, user name, email, full name and a custom "Role" claim that drives
    /// [RequireRole] and the SignalR groups (and the standard role claim, so [Authorize(Roles=...)] also works).
    /// </summary>
    public string CreateToken(Users user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName!),
            new(ClaimTypes.Email, user.Email!),
            new("FullName", user.Name),
            new(RequireRoleAttribute.ClaimType, user.Role.ToString()),
            new(ClaimTypes.Role, user.Role.ToString()),
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            Expires = DateTime.UtcNow.AddMinutes(_options.ExpiryInMinutes),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)),
                SecurityAlgorithms.HmacSha256Signature),
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }
}
