using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Data;
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.ViewModels;

public partial class QuickRepliesViewModel : ObservableObject
{
    private readonly IQuickReplyRepository _repository;

    public ObservableCollection<QuickReply> Replies { get; } = new();

    public QuickRepliesViewModel(IQuickReplyRepository repository)
    {
        _repository = repository;
    }

    [RelayCommand]
    private async Task Load()
    {
        Replies.Clear();
        foreach (var reply in await _repository.GetAllAsync())
        {
            Replies.Add(reply);
        }
    }

    [RelayCommand]
    private async Task Add(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }
        await _repository.AddAsync(trimmed);
        await Load();
    }

    // Clearing a reply's text while editing it is treated as deleting it.
    [RelayCommand]
    private async Task Update((long Id, string Text) edit)
    {
        var trimmed = edit.Text.Trim();
        if (trimmed.Length == 0)
        {
            await _repository.DeleteAsync(edit.Id);
        }
        else
        {
            await _repository.UpdateAsync(edit.Id, trimmed);
        }
        await Load();
    }

    [RelayCommand]
    private async Task Delete(long id)
    {
        await _repository.DeleteAsync(id);
        await Load();
    }
}
