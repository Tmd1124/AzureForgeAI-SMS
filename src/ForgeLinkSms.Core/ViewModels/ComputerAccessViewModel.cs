using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Web;

namespace ForgeLinkSms.Core.ViewModels;

public partial class ComputerAccessViewModel : ObservableObject
{
    private readonly IComputerAccessController _controller;
    private readonly PairingService _pairing;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string? _address;

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private int _pairedCount;

    [ObservableProperty]
    private string? _message;

    public ComputerAccessViewModel(IComputerAccessController controller, PairingService pairing)
    {
        _controller = controller;
        _pairing = pairing;
    }

    [RelayCommand]
    private async Task Refresh()
    {
        IsRunning = _controller.IsRunning;
        Address = _controller.Address;
        Code = _pairing.Code;
        PairedCount = await _pairing.PairedCountAsync();
    }

    [RelayCommand]
    private async Task Toggle()
    {
        Message = null;
        if (_controller.IsRunning)
        {
            await _controller.StopAsync();
        }
        else if (!await _controller.StartAsync())
        {
            Message = "Connect your phone to Wi-Fi first.";
        }
        await Refresh();
    }

    [RelayCommand]
    private async Task UnpairAll()
    {
        await _pairing.UnpairAllAsync();
        await Refresh();
    }
}
