using UpdateWatch2.Server.Agents;

namespace UpdateWatch2.Server.Tests.Agents;

public class AgentSettingsValidatorTests
{
    [Theory]
    [InlineData("DEBUG")]
    [InlineData("Info")]
    [InlineData("warning")]
    [InlineData("ERROR")]
    public void IsValidLogLevel_accepts_a_known_level_case_insensitively(string value)
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
    [InlineData(1)]
    [InlineData(240)]
    [InlineData(10_080)]
    public void IsValidUpdateCheckIntervalMinutes_accepts_a_value_within_range(int value)
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
    [InlineData(0)]
    [InlineData(300)]
    [InlineData(3_600)]
    public void IsValidUpdateCheckJitterSeconds_accepts_a_value_within_range(int value)
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

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(1_440)]
    public void IsValidAliveIntervalMinutes_accepts_a_value_within_range(int value)
    {
        Assert.True(AgentSettingsValidator.IsValidAliveIntervalMinutes(value));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_441)]
    public void IsValidAliveIntervalMinutes_rejects_out_of_range_values(int value)
    {
        Assert.False(AgentSettingsValidator.IsValidAliveIntervalMinutes(value));
    }

    [Fact]
    public void IsValid_accepts_a_request_with_every_field_within_range()
    {
        Assert.True(AgentSettingsValidator.IsValid(new UpdateAgentSettingsRequest("DEBUG", 240, 300, 5)));
    }

    [Fact]
    public void IsValid_rejects_a_request_with_any_invalid_field()
    {
        Assert.False(AgentSettingsValidator.IsValid(new UpdateAgentSettingsRequest("NOT-A-LEVEL", 240, 300, 5)));
        Assert.False(AgentSettingsValidator.IsValid(new UpdateAgentSettingsRequest("DEBUG", 0, 300, 5)));
        Assert.False(AgentSettingsValidator.IsValid(new UpdateAgentSettingsRequest("DEBUG", 240, -1, 5)));
        Assert.False(AgentSettingsValidator.IsValid(new UpdateAgentSettingsRequest("DEBUG", 240, 300, 0)));
    }

    [Fact]
    public void IsValidBulkRequest_rejects_a_request_with_every_field_null()
    {
        Assert.False(AgentSettingsValidator.IsValidBulkRequest(new BulkUpdateAgentSettingsRequest(["host-1"], null, null, null, null)));
    }

    [Fact]
    public void IsValidBulkRequest_accepts_a_request_with_only_one_field_set()
    {
        Assert.True(AgentSettingsValidator.IsValidBulkRequest(new BulkUpdateAgentSettingsRequest(["host-1"], "DEBUG", null, null, null)));
        Assert.True(AgentSettingsValidator.IsValidBulkRequest(new BulkUpdateAgentSettingsRequest(["host-1"], null, null, null, 5)));
    }

    [Fact]
    public void IsValidBulkRequest_rejects_a_provided_field_that_is_out_of_range()
    {
        Assert.False(AgentSettingsValidator.IsValidBulkRequest(new BulkUpdateAgentSettingsRequest(["host-1"], "NOT-A-LEVEL", null, null, null)));
        Assert.False(AgentSettingsValidator.IsValidBulkRequest(new BulkUpdateAgentSettingsRequest(["host-1"], null, 0, null, null)));
        Assert.False(AgentSettingsValidator.IsValidBulkRequest(new BulkUpdateAgentSettingsRequest(["host-1"], null, null, -1, null)));
        Assert.False(AgentSettingsValidator.IsValidBulkRequest(new BulkUpdateAgentSettingsRequest(["host-1"], null, null, null, 0)));
    }
}
