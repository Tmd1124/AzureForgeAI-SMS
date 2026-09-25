using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class GroupNamesTests
{
    [Theory]
    [InlineData(new[] { "Kim Donnelly", "Ever Harris" }, "Kim & Ever")]
    [InlineData(new[] { "Kim Donnelly", "Ever Harris", "Dare Harris" }, "Kim, Ever & Dare")]
    [InlineData(new[] { "Kim Donnelly", "Ever Harris", "Dare Harris", "MacKay Donnelly", "Tiffani Donnelly" }, "Kim, Ever, Dare +2")]
    [InlineData(new[] { "Kim Donnelly", "(678) 262-8755" }, "Kim & (678) 262-8755")]
    public void Format_lists_first_names_and_collapses_long_groups(string[] names, string expected)
    {
        Assert.Equal(expected, GroupNames.Format(names));
    }

    [Fact]
    public void Format_of_one_name_is_the_full_name()
    {
        Assert.Equal("Kim Donnelly", GroupNames.Format(new[] { "Kim Donnelly" }));
    }
}
