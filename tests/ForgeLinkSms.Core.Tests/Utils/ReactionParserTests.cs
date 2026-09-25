using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class ReactionParserTests
{
    [Theory]
    [InlineData("Loved “Sounds good”", "❤️", "Sounds good")]
    [InlineData("Liked \"See you at 6\"", "👍", "See you at 6")]
    [InlineData("Disliked “Traffic is bad”", "👎", "Traffic is bad")]
    [InlineData("Laughed at “lol ok”", "😂", "lol ok")]
    [InlineData("Emphasized “Don't forget”", "‼️", "Don't forget")]
    [InlineData("Questioned “Dinner at 8?”", "❓", "Dinner at 8?")]
    [InlineData("Reacted 🎉 to “We won”", "🎉", "We won")]
    [InlineData("👍 to \"You still coming over\"", "👍", "You still coming over")]
    public void Parse_recognizes_reactions_sent_as_texts(string body, string emoji, string quoted)
    {
        var reaction = ReactionParser.Parse(body);

        Assert.NotNull(reaction);
        Assert.Equal(emoji, reaction.Emoji);
        Assert.Equal(quoted, reaction.QuotedText);
        Assert.False(reaction.IsRemoval);
    }

    [Theory]
    [InlineData("Loved an image", "❤️")]
    [InlineData("Laughed at an image", "😂")]
    public void Parse_recognizes_reactions_to_photos(string body, string emoji)
    {
        var reaction = ReactionParser.Parse(body);

        Assert.NotNull(reaction);
        Assert.Equal(emoji, reaction.Emoji);
        Assert.True(reaction.TargetsImage);
    }

    [Theory]
    [InlineData("Removed a heart from “Sounds good”", "❤️")]
    [InlineData("Removed a like from “Sounds good”", "👍")]
    [InlineData("Removed a laugh from “Sounds good”", "😂")]
    public void Parse_recognizes_removed_reactions(string body, string emoji)
    {
        var reaction = ReactionParser.Parse(body);

        Assert.NotNull(reaction);
        Assert.Equal(emoji, reaction.Emoji);
        Assert.True(reaction.IsRemoval);
    }

    [Theory]
    [InlineData("I loved “Dune”, you should see it")]
    [InlineData("Liked the movie")]
    [InlineData("Loved it")]
    [InlineData("Meet me to \"talk\"")]
    [InlineData("")]
    public void Parse_ignores_ordinary_messages(string body)
    {
        Assert.Null(ReactionParser.Parse(body));
    }
}
