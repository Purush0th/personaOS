using PersonaOS.Application.Auth;
using PersonaOS.Application.Common.Interfaces;
using PersonaOS.Tests.TestSupport;

namespace PersonaOS.Tests.Auth;

/// <summary>
/// Login lookup behaviour. The case-insensitivity cases exist because moving the store
/// from SQL Server (case-insensitive default collation) to SQLite (BINARY, case-sensitive)
/// silently broke mixed-case logins: the query was a plain `==`, so the behaviour was the
/// provider's to decide. AuthService now lower-cases both sides explicitly, and the column
/// is COLLATE NOCASE so the unique index agrees.
/// </summary>
public class AuthServiceTests
{
    private const string Password = "test-password";

    private static async Task<(AuthService Auth, FakePasswordHasher Hasher)> SetupAsync(string username = "admin")
    {
        var db = TestDbContext.Create();
        var hasher = new FakePasswordHasher();
        var auth = new AuthService(db, hasher, new FakeJwtTokenGenerator());
        await auth.CreateAdminAsync(username, Password);
        return (auth, hasher);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("Admin")]
    [InlineData("ADMIN")]
    [InlineData("aDmIn")]
    public async Task Login_succeeds_regardless_of_username_casing(string attempted)
    {
        var (auth, _) = await SetupAsync("admin");

        var result = await auth.LoginAsync(attempted, Password);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Login_matches_when_the_stored_username_is_the_mixed_case_one()
    {
        var (auth, _) = await SetupAsync("Admin");

        var result = await auth.LoginAsync("admin", Password);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Login_returns_the_stored_casing_not_what_was_typed()
    {
        var (auth, _) = await SetupAsync("Admin");

        var result = await auth.LoginAsync("ADMIN", Password);

        Assert.Equal("Admin", result!.Username);
    }

    [Fact]
    public async Task Login_ignores_surrounding_whitespace()
    {
        var (auth, _) = await SetupAsync();

        var result = await auth.LoginAsync("  aDmIn  ", Password);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task Login_still_rejects_a_wrong_password_whatever_the_casing()
    {
        var (auth, _) = await SetupAsync();

        Assert.Null(await auth.LoginAsync("ADMIN", "not-the-password"));
    }

    [Fact]
    public async Task Login_returns_null_for_an_unknown_user()
    {
        var (auth, _) = await SetupAsync();

        Assert.Null(await auth.LoginAsync("someone-else", Password));
    }

    [Fact]
    public async Task Login_rehashes_when_the_hasher_says_the_hash_is_outdated()
    {
        var (auth, hasher) = await SetupAsync();
        hasher.Hashed.Clear();
        hasher.NextVerifyResult = PasswordVerifyResult.SuccessRehashNeeded;

        var result = await auth.LoginAsync("ADMIN", Password);

        Assert.NotNull(result);
        Assert.Single(hasher.Hashed);
    }
}
