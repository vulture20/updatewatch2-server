using UpdateWatch2.Server.Db.Entities;
using UpdateWatch2.Server.UpdateFilters;

namespace UpdateWatch2.Server.Tests.UpdateFilters;

public class UpdateFilterMatcherTests
{
    [Fact]
    public void IsExcluded_matches_a_plain_substring_pattern_case_insensitively()
    {
        var filters = new[] { new UpdateFilter { Name = "Defender", Pattern = "security intelligence-update" } };

        Assert.True(UpdateFilterMatcher.IsExcluded("Security Intelligence-Update für Microsoft Defender Antivirus", filters));
    }

    [Fact]
    public void IsExcluded_is_false_when_no_filter_matches()
    {
        var filters = new[] { new UpdateFilter { Name = "Defender", Pattern = "security intelligence-update" } };

        Assert.False(UpdateFilterMatcher.IsExcluded("2026-08 Kumulatives Update für Windows 11", filters));
    }

    [Fact]
    public void IsExcluded_is_false_for_an_empty_filter_list()
    {
        Assert.False(UpdateFilterMatcher.IsExcluded("Anything", []));
    }

    [Fact]
    public void IsExcluded_supports_real_regex_patterns_not_just_literal_substrings()
    {
        var filters = new[] { new UpdateFilter { Name = "KB numbers", Pattern = @"KB5\d{6}" } };

        Assert.True(UpdateFilterMatcher.IsExcluded("2026-08 Kumulatives Update für Windows 11 (KB5041585)", filters));
        Assert.False(UpdateFilterMatcher.IsExcluded("openssl Sicherheitsaktualisierung", filters));
    }

    [Fact]
    public void IsExcluded_treats_any_one_matching_filter_among_several_as_a_match()
    {
        var filters = new[]
        {
            new UpdateFilter { Name = "Defender", Pattern = "security intelligence-update" },
            new UpdateFilter { Name = "Edge", Pattern = "Microsoft Edge" },
        };

        Assert.True(UpdateFilterMatcher.IsExcluded("Sicherheitsupdate für Microsoft Edge", filters));
    }

    [Fact]
    public void Matches_treats_an_invalid_pattern_as_a_non_match_rather_than_throwing()
    {
        // Defensive only — UpdateFilterService's own create/update validation
        // is what actually keeps an invalid pattern out of the DB in the
        // first place; this is what stops a somehow-invalid stored one from
        // taking the whole updates display down.
        Assert.False(UpdateFilterMatcher.Matches("(", "anything"));
    }
}
