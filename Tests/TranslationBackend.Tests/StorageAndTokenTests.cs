using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using TranslationBackend.Application;
using TranslationBackend.Domain.Enum;
using TranslationBackend.Domain.Model;
using TranslationBackend.Infrastructure.Auth;

namespace TranslationBackend.Tests;

public class StorageTests : IDisposable
{
    private readonly string _root;
    private readonly TranslationBackend.Infrastructure.Storage.LocalFileStorage _storage;

    public StorageTests() => _storage = TestData.NewStorage(out _root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void Paths_that_leave_the_root_are_refused()
    {
        Assert.Null(_storage.Resolve("../outside.txt"));
        Assert.Null(_storage.Resolve("a/../../outside.txt"));
        Assert.Null(_storage.Resolve(""));
        Assert.NotNull(_storage.Resolve("job/input/source.pdf"));
        Assert.Null(_storage.ToRelative(Path.Combine(Path.GetTempPath(), "elsewhere", "x.docx")));
    }

    [Fact]
    public void Absolute_paths_from_the_service_become_relative_with_forward_slashes()
    {
        var absolute = Path.Combine(_root, "abc", "output", "r.docx");
        Assert.Equal("abc/output/r.docx", _storage.ToRelative(absolute));
        Assert.Equal("abc/output/r.docx", _storage.ToRelative("abc/output/r.docx"));
        Assert.Null(_storage.ToRelative(null));
    }

    [Fact]
    public async Task An_upload_lands_in_its_own_job_folder_and_the_folder_can_be_deleted()
    {
        var id = Guid.NewGuid();
        var relative = await _storage.SaveUploadAsync(id, ".PDF", new MemoryStream(TestData.Pdf()));
        Assert.Equal($"{id:D}/input/source.pdf", relative);
        Assert.True(File.Exists(_storage.Resolve(relative)));

        _storage.DeleteJobFolder(id);
        Assert.False(Directory.Exists(Path.Combine(_root, id.ToString("D"))));
        _storage.DeleteJobFolder(id);                                   // deleting twice is harmless
    }
}

public class JwtTokenServiceTests
{
    private static JwtTokenService Service(string key = "0123456789012345678901234567890123456789")
        => new(Options.Create(new JwtOptions { Key = key, Issuer = "iss", Audience = "aud", ExpiryInMinutes = 60 }));

    private static Users User(Role role) => new()
    {
        Id = "u1", UserName = "amna_ms", Email = "amna@avanceon.ae", Name = "Amna", Role = role,
    };

    [Fact]
    public void The_token_carries_the_claims_the_filters_and_the_hub_read()
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(Service().CreateToken(User(Role.SuperUser)));

        Assert.Equal("iss", token.Issuer);
        Assert.Contains("aud", token.Audiences);
        Assert.Contains(token.Claims, c => (c.Type == "nameid" || c.Type == ClaimTypes.NameIdentifier) && c.Value == "u1");   // the handler writes the short JWT name; the API maps it back
        Assert.Contains(token.Claims, c => c.Type == "Role" && c.Value == "SuperUser");
        Assert.Contains(token.Claims, c => c.Type == "FullName" && c.Value == "Amna");
        Assert.InRange(token.ValidTo, DateTime.UtcNow.AddMinutes(58), DateTime.UtcNow.AddMinutes(62));
    }

    [Fact]
    public void A_short_signing_key_is_refused()
        => Assert.Throws<InvalidOperationException>(() => Service("too short"));
}
