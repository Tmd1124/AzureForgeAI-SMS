using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.ViewModels;

public partial class ContactPickerViewModel : ObservableObject
{
    private readonly IContactService _contactService;

    public ObservableCollection<ContactInfo> Contacts { get; } = new();

    public ContactPickerViewModel(IContactService contactService)
    {
        _contactService = contactService;
    }

    [RelayCommand]
    private async Task Load()
    {
        Contacts.Clear();
        foreach (var contact in await _contactService.GetAllContactsAsync())
        {
            Contacts.Add(contact);
        }
    }
}
