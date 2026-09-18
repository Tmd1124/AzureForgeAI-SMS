using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmsMessenger.Core.Services;

namespace SmsMessenger.Core.ViewModels;

public partial class BlockedViewModel : ObservableObject
{
    private readonly IContactBlockService _blockService;

    public ObservableCollection<string> BlockedNumbers { get; } = new();

    public BlockedViewModel(IContactBlockService blockService)
    {
        _blockService = blockService;
    }

    [RelayCommand]
    private async Task Load()
    {
        BlockedNumbers.Clear();
        foreach (var number in await _blockService.GetBlockedNumbersAsync())
        {
            BlockedNumbers.Add(number);
        }
    }

    [RelayCommand]
    private async Task Unblock(string phoneNumber)
    {
        await _blockService.UnblockAsync(phoneNumber);
        await Load();
    }
}
