using UpdateWatch2.Server.Agents;

namespace UpdateWatch2.Server.Tests.Agents;

public class AgentSettingsValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("DEBUG")]
    [InlineData("Info")]
    [InlineData("warning")]
    [InlineData("ERROR")]
    public void IsValidLogLevel_accepts_null_or_a_known_level_case_insensitively(string? value)
    {
        Assert.True(AgentSettingsValidator.IsValidLogLevel(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("VERBOSE")]
    [InlineData("debugg")]
    public void IsValidLogLevel_rejects_anything_else(string value)
    {
        Assert.False(AgentSettingsValidator.IsValidLogLevel(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    [InlineData(240)]
    [InlineData(10_080)]
    public void IsValidUpdateCheckIntervalMinutes_accepts_null_or_a_value_within_range(int? value)
    {
        Assert.True(AgentSettingsValidator.IsValidUpdateCheckIntervalMinutes(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_081)]
    public void IsValidUpdateCheckIntervalMinutes_rejects_out_of_range_values(int value)
    {
        Assert.False(AgentSettingsValidator.IsValidUpdateCheckIntervalMinutes(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(300)]
    [InlineData(3_600)]
    public void IsValidUpdateCheckJitterSeconds_accepts_null_or_a_value_within_range(int? value)
    {
        Assert.True(AgentSettingsValidator.IsValidUpdateCheckJitterSeconds(value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3_601)]
    public void IsValidUpdateCheckJitterSeconds_rejects_out_of_range_values(int value)
    {
        Assert.False(AgentSettingsValidator.IsValidUpdateCheckJitterSeconds(value));
    }

    [Fact]
    public void IsValid_accepts_a_request_with_every_field_null()
    {
        Assert.True(AgentSettingsValidator.IsValid(new UpdateAgentSettingsRequest(null, null, null)));
    }

    [Fact]
    public void IsValid_rejects_a_request_with_any_invalid_field()
    {
        Assert.False(AgentSettingsValidator.IsValid(new UpdateAgentSettingsRequest("NOT-A-LEVEL", null, null)));
        Assert.False(AgentSettingsValidator.IsValid(new UpdateAgentSettingsRequest(null, 0, null)));
        Assert.False(AgentSettingsValidator.IsValid(new UpdateAgentSettingsRequest(null, null, -1)));
    }
}
