using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class UpdateCategorizerTests
{
    [Theory]
    [InlineData("Your verification code is 482913", "72975", UpdateCategory.Codes)]
    [InlineData("Reminder: your appt with Dr. Patel is Thu 10:30 AM. Reply Y to confirm", "32665", UpdateCategory.Appointments)]
    [InlineData("Your prescription is ready for pickup", "CVS", UpdateCategory.Appointments)]
    [InlineData("UPS: Your package is out for delivery today by 7 PM", "69877", UpdateCategory.Deliveries)]
    [InlineData("Your order has shipped! Track it here", "AMAZON", UpdateCategory.Deliveries)]
    [InlineData("Chase: A $42.18 purchase was made on your card ending 1234", "24273", UpdateCategory.Banking)]
    [InlineData("You received $50 via Zelle", "75078", UpdateCategory.Banking)]
    [InlineData("Weekend sale! 30% off everything", "88202", UpdateCategory.Promotions)]
    [InlineData("Hello, Your home coverage has an update. Contact us now", "+18335024298", UpdateCategory.Promotions)]
    [InlineData("EC: We're interested in purchasing your property", "+18335333881", UpdateCategory.Promotions)]
    [InlineData("Hi, I'm Gemini in Google Messages", "+18339913448", UpdateCategory.Other)]
    public void Categorize_sorts_automated_texts(string body, string sender, UpdateCategory expected)
    {
        Assert.Equal(expected, UpdateCategorizer.Categorize(body, sender));
    }
}
