using UpdateWatch2.Server.Certificates;

namespace UpdateWatch2.Server.Tests.Certificates;

public class RegistrationTokenHasherTests
{
    [Fact]
    public void Generated_token_verifies_against_its_own_hash()
    {
        var (raw, hash) = RegistrationTokenHasher.GenerateToken();

        Assert.True(RegistrationTokenHasher.Verify(raw, hash));
    }

    [Fact]
    public void A_different_token_does_not_verify_against_someone_elses_hash()
    {
        var (_, hash) = RegistrationTokenHasher.GenerateToken();
        var (otherRaw, _) = RegistrationTokenHasher.GenerateToken();

        Assert.False(RegistrationTokenHasher.Verify(otherRaw, hash));
    }

    [Fact]
    public void Two_generated_tokens_are_never_the_same()
    {
        var (rawA, _) = RegistrationTokenHasher.GenerateToken();
        var (rawB, _) = RegistrationTokenHasher.GenerateToken();

        Assert.NotEqual(rawA, rawB);
    }

    [Fact]
    public void The_hash_is_never_the_raw_token_itself()
    {
        var (raw, hash) = RegistrationTokenHasher.GenerateToken();

        Assert.NotEqual(raw, hash);
    }
}
