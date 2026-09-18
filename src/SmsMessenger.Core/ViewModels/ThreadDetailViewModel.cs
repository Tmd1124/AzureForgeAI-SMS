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
}
