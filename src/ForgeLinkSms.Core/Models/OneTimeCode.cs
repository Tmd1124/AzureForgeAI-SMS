namespace ForgeLinkSms.Core.Models;

public record OneTimeCode(string Code, string Source, long ThreadId, DateTimeOffset ReceivedAt);
