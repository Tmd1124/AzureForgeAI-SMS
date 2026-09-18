using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Core.ViewModels;

public partial class ComposeViewModel : ObservableObject
{
    private readonly ISmsService _smsService;

    public ObservableCollection<string> Recipients { get; } = new();

    [ObservableProperty]
    private string _messageBody = string.Empty;

    public bool IsGroupSend => Recipients.Count > 1;

    public ComposeViewModel(ISmsService smsService)
    {
        _smsService = smsService;
        Recipients.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsGroupSend));
    }

    [RelayCommand]
    private void AddRecipient(string rawNumber)
    {
        var digits = Regex.Replace(rawNumber, @"[^\d]", "");
        if (digits.Length is not (10 or 11))
        {
            return; // not a valid US-style number; reject silently, UI shows its own validation message
        }
        if (!Recipients.Contains(digits))
        {
            Recipients.Add(digits);
        }
    }

    [RelayCommand]
    private void RemoveRecipient(string number) => Recipients.Remove(number);

    [RelayCommand]
    private async Task Send()
    {
        var text = MessageBody.Trim();
        if (string.IsNullOrEmpty(text) || Recipients.Count == 0)
        {
            return;
        }

        foreach (var recipient in Recipients)
        {
            await _smsService.SendAsync(recipient, text);
        }
        MessageBody = string.Empty;
    }
}
