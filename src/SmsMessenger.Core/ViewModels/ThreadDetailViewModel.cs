using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Core.ViewModels;

public partial class ThreadDetailViewModel : ObservableObject
{
    private readonly ISmsService _smsService;
    private readonly long _threadId;
    private readonly string _address;

    public ObservableCollection<Models.SmsMessage> Messages { get; } = new();

    [ObservableProperty]
    private string _composeText = string.Empty;

    public ThreadDetailViewModel(ISmsService smsService, long threadId, string address)
    {
        _smsService = smsService;
        _threadId = threadId;
        _address = address;
    }

    [RelayCommand]
    private async Task Load()
    {
        Messages.Clear();
        var messages = await _smsService.GetMessagesAsync(_threadId) ?? Array.Empty<Models.SmsMessage>();
        foreach (var message in messages.OrderBy(m => m.Timestamp))
        {
            Messages.Add(message);
        }
    }

    [RelayCommand]
    private async Task Send()
    {
        var text = ComposeText.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        await _smsService.SendAsync(_address, text);
        ComposeText = string.Empty;
        await Load();
    }

    [RelayCommand]
    private async Task SendReaction((string Emoji, string TargetMessageBody) reaction)
    {
        await _smsService.SendAsync(_address, FormatReaction(reaction.Emoji, reaction.TargetMessageBody));
        await Load();
    }

    // Plain SMS has no reaction/tapback concept, so a reaction is sent as a real new
    // message quoting the target text — the closest a recipient can see without any
    // special client support on their end.
    private static string FormatReaction(string emoji, string targetMessageBody)
    {
        const int maxQuoteLength = 40;
        var quoted = targetMessageBody.Length > maxQuoteLength
            ? targetMessageBody[..maxQuoteLength] + "…"
            : targetMessageBody;
        return $"{emoji} to \"{quoted}\"";
    }
}
