using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.ViewModels;

public partial class ConversationMediaViewModel : ObservableObject
{
    private const int MaxLinkMessages = 500;

    private readonly ISmsService _smsService;
    private readonly long _threadId;

    public ObservableCollection<SharedMedia> Media { get; } = new();
    public ObservableCollection<SharedLink> Links { get; } = new();

    public ConversationMediaViewModel(ISmsService smsService, long threadId)
    {
        _smsService = smsService;
        _threadId = threadId;
    }

    [RelayCommand]
    private async Task Load()
    {
        Media.Clear();
        foreach (var item in (await _smsService.GetSharedMediaAsync(_threadId)).OrderByDescending(m => m.Timestamp))
        {
            Media.Add(item);
        }

        // Only messages that could contain a link need parsing, so reuse the message search.
        var candidates = (await _smsService.SearchMessagesAsync(_threadId, "http", MaxLinkMessages))
            .Concat(await _smsService.SearchMessagesAsync(_threadId, "www.", MaxLinkMessages))
            .DistinctBy(m => (m.Id, m.IsMms))
            .OrderByDescending(m => m.Timestamp);

        Links.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var message in candidates)
        {
            foreach (var segment in MessageLinkParser.Parse(message.Body).Where(s => s.Kind == MessageLinkKind.Url))
            {
                var url = segment.Data as string ?? segment.Text;
                if (seen.Add(url.TrimEnd('/')))
                {
                    Links.Add(new SharedLink(segment.Text, url, DomainOf(url), message.Timestamp, message.IsOutgoing));
                }
            }
        }
    }

    private static string DomainOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? (uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host)
            : url;
}
