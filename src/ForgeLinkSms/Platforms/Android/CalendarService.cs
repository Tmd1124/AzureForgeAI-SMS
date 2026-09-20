using Android.Content;
using Android.Provider;
using ForgeLinkSms.Core.Services;
using AndroidApp = Android.App.Application;

namespace ForgeLinkSms.Platforms.Android;

public class CalendarService : ICalendarService
{
    public Task OpenNewEventAsync(string title, DateTime start, DateTime end)
    {
        var intent = new Intent(Intent.ActionInsert);
        intent.SetData(CalendarContract.Events.ContentUri);
        intent.PutExtra(CalendarContract.ExtraEventBeginTime, new DateTimeOffset(start).ToUnixTimeMilliseconds());
        intent.PutExtra(CalendarContract.ExtraEventEndTime, new DateTimeOffset(end).ToUnixTimeMilliseconds());
        intent.PutExtra(CalendarContract.Events.InterfaceConsts.Title, title);
        intent.AddFlags(ActivityFlags.NewTask);

        var context = (Context?)Microsoft.Maui.ApplicationModel.Platform.CurrentActivity ?? AndroidApp.Context;
        context.StartActivity(intent);

        return Task.CompletedTask;
    }
}
