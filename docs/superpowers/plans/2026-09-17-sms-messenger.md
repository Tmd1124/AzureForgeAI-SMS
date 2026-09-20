# SMS Messenger Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a native Android app that becomes the user's default SMS app and lets them send/receive real text messages with any phone number over the carrier network — no backend, no cloud database, no push service.

**Architecture:** A .NET MAUI Blazor Hybrid app targeting Android only. All app logic (models, view models, service *interfaces*) lives in a plain `net9.0` class library (`ForgeLinkSms.Core`) so it can be unit-tested without any Android/MAUI runtime. The MAUI app project (`ForgeLinkSms`, `net9.0-android` only) supplies the Android-specific service *implementations* (`Platforms/Android/`) that call `SmsManager`, the SMS/MMS content providers, `ContactsContract`, and `RoleManager` directly — there is no server anywhere in this system. Android itself is the source of truth for message storage; the app reads and writes through its content provider rather than keeping its own database.

**Tech Stack:** .NET 9, .NET MAUI (Blazor Hybrid), CommunityToolkit.Mvvm, xUnit + Moq for tests, Android SDK (API 34/35 platforms already installed locally), git.

**Spec:** `SMS Messenger App Requirements Document.html` (project root) — this plan implements v1 exactly as scoped there. The two earlier documents (`AzureForgeAI Messaging Requirements Document.html`, `Messaging App Requirements Document v2 (Low-Cost Stack).html`) are superseded history and are not implemented.

## Global Constraints

- Platform: Android only, API 24+ minimum (`net9.0-android`). No iOS/Windows/MacCatalyst targets — Apple does not permit third-party carrier-level SMS access, so this is a hard platform limit, not a phase-2 item.
- No backend, no cloud database, no push notification service, anywhere in this plan. Zero network calls of any kind except the SMS itself leaving over the carrier network.
- No MMS, no media/image attachments, in v1.
- Not achievable and never attempted in this plan, on any task: typing indicators, cross-network read receipts (delivered ≠ read), emoji reactions, edit-after-send, delete-for-everyone, true shared group threads. Message status is exactly two states: Sent, Delivered.
- "Group texting" means sending the same text individually to multiple recipients — never a shared thread. Any UI for this must make that explicit.
- Required Android permissions (all requested at runtime with an in-app rationale shown first): `READ_SMS`, `SEND_SMS`, `RECEIVE_SMS`, `RECEIVE_MMS`, `READ_CONTACTS`, `READ_PHONE_STATE`, `POST_NOTIFICATIONS`.
- The app must become the Android default SMS app to reliably send/receive — this requires a `BroadcastReceiver` for `SMS_DELIVER`, a compose `Activity` for `ACTION_SENDTO`/`ACTION_SEND`, and a `HeadlessSmsSendService` for `ACTION_RESPOND_VIA_MESSAGE`.
- Android owns message storage (`Telephony.Sms` / `Telephony.Threads` content providers). The app does not maintain its own message database. As the default SMS app, the app is responsible for writing both incoming and outgoing messages into that provider itself — Android does not do this automatically on the app's behalf.
- The only local app state is a handful of `Preferences` key/value entries (onboarding-complete flag, notification toggle, preferred-SIM choice) — not a database.
- **Navigation architecture (corrected during Task 5 execution, ruled on by the controller):** `dotnet new maui-blazor` scaffolds ONE native host page (`MainPage.xaml`, containing a single `BlazorWebView` whose root component is `Components/Routes.razor`) with ordinary Blazor client-side routing (`<Router>`, `@page` directives, `NavigationManager`) handling every page inside that one WebView — **there is no MAUI Shell (`AppShell.xaml`/`.razor`, `Shell.Current`, `TabBar`, `ShellContent`) anywhere in this app, and none should be added.** Every task below that mentions `Shell.Current.GoToAsync(...)` means `NavigationManager.NavigateTo(...)` instead (same intent — programmatic navigation to a route string — different API), and every route string loses its MAUI-Shell-style `//` absolute-route prefix (use `/conversations`, not `//conversations`). `INavigationService`'s Android/MAUI implementation wraps `NavigationManager` (injected via constructor, same DI container the whole app shares), not `Shell.Current`. Where a task's Razor page navigates directly rather than through `INavigationService` (e.g. a button's `@onclick`), inject `NavigationManager` directly in that page instead. A bottom tab bar (Task 12) is implemented as an ordinary Razor component (e.g. links inside `MainLayout.razor` using `NavigationManager`/`NavLink`), not MAUI Shell's `<TabBar>` markup. `SplashPage.razor`'s route is `"/"` (the app's default/landing route, since a fresh `BlazorWebView` load always resolves to the Router's `/` match first — there is no separate "initial route" concept to configure outside of this), not `"/splash"`, and the template's demo pages (`Components/Pages/Home.razor`, `Counter.razor`, `Weather.razor`, `Components/Layout/NavMenu.razor`) should be deleted since `Home.razor`'s own `@page "/"` would otherwise collide with Splash's route.

---

## Prerequisite Environment Notes (read before Task 1)

Confirmed already present on this machine:
- .NET SDK 9.0.301, with the `android` MAUI workload installed.
- Android SDK at `C:\Program Files (x86)\Android\android-sdk`, with `platforms\android-34`, `platforms\android-35`, `build-tools\35.0.0`, and `cmdline-tools\12.0\bin\sdkmanager.bat` / `avdmanager.bat`.

Not yet present (Task 1 sets these up):
- No `emulator` package and no AVD installed yet.
- `ANDROID_HOME`/`ANDROID_SDK_ROOT` are not set as environment variables. Because the Bash/PowerShell tools do **not** persist shell state (env vars set with `export`/`$env:` in one command are gone in the next), this plan pins the SDK path in a committed `Directory.Build.props` file instead of relying on a session environment variable, so every future `dotnet build` invocation — from any tool call, any session — finds the SDK the same way.

---

### Task 1: Repo, solution scaffold, and Android build/emulator environment

**Files:**
- Create: `.gitignore`
- Create: `Directory.Build.props`
- Create: `ForgeLinkSms.sln`
- Create: `src/ForgeLinkSms/ForgeLinkSms.csproj` (+ template-generated files)

**Interfaces:**
- Produces: a solution that builds for `net9.0-android` from the CLI, and an emulator AVD named `sms_test` that can run it. Later tasks assume both exist.

- [ ] **Step 1: Initialize git and add a .gitignore**

```bash
git init
```

Create `.gitignore`:

```gitignore
bin/
obj/
.vs/
*.user
*.userosscache
*.suo
[Aa]pk/
[Aa]ab/
*.androidproj.user
docs/superpowers/plans/*.output
```

```bash
git add .gitignore
git commit -m "chore: initialize repository"
```

- [ ] **Step 2: Pin the Android SDK location for every future build**

Create `Directory.Build.props` at the repo root:

```xml
<Project>
  <PropertyGroup>
    <AndroidSdkDirectory>C:\Program Files (x86)\Android\android-sdk</AndroidSdkDirectory>
  </PropertyGroup>
</Project>
```

This is picked up automatically by every `.csproj` under the repo root — no shell environment variable required, which matters because this tool's shell does not persist `export`/`$env:` state between commands.

- [ ] **Step 3: Install the emulator package and an x86_64 system image**

```bash
SDK="/c/Program Files (x86)/Android/android-sdk"
yes | "$SDK/cmdline-tools/12.0/bin/sdkmanager.bat" --sdk_root="$SDK" "emulator" "system-images;android-34;google_apis;x86_64"
```

Expected: both packages download and install without error (the `yes |` auto-accepts the license prompts).

- [ ] **Step 4: Create the test AVD**

```bash
SDK="/c/Program Files (x86)/Android/android-sdk"
echo "no" | "$SDK/cmdline-tools/12.0/bin/avdmanager.bat" create avd -n sms_test -k "system-images;android-34;google_apis;x86_64" -d pixel_5
"$SDK/emulator/emulator.exe" -list-avds
```

Expected: `sms_test` appears in the AVD list.

- [ ] **Step 5: Scaffold the MAUI Blazor Hybrid app, Android-only**

```bash
dotnet new maui-blazor -n ForgeLinkSms -o src/ForgeLinkSms
```

Open `src/ForgeLinkSms/ForgeLinkSms.csproj` and reduce `<TargetFrameworks>` to Android only:

```xml
<TargetFrameworks>net9.0-android</TargetFrameworks>
```

Remove any `<TargetFrameworks Condition="...">` blocks for iOS/MacCatalyst/Windows that the template generated — this app has exactly one target, permanently, per the spec's platform restriction.

- [ ] **Step 6: Create the solution file and add the app project**

```bash
dotnet new sln -n ForgeLinkSms
dotnet sln ForgeLinkSms.sln add src/ForgeLinkSms/ForgeLinkSms.csproj
```

- [ ] **Step 7: Verify a clean Android build from the CLI**

```bash
dotnet build ForgeLinkSms.sln -f net9.0-android
```

Expected: `Build succeeded.` If it fails on an SDK-path error, re-check `Directory.Build.props` matches the exact path found above.

- [ ] **Step 8: Verify the emulator boots and the template app runs on it**

```bash
SDK="/c/Program Files (x86)/Android/android-sdk"
"$SDK/emulator/emulator.exe" -avd sms_test -no-snapshot &
# wait for boot, then:
dotnet build src/ForgeLinkSms/ForgeLinkSms.csproj -t:Run -f net9.0-android
```

Expected: the emulator boots, and the default MAUI Blazor template screen ("Hello, World!" counter page) appears on it. Leave the emulator running for the rest of this plan — every later manual-test step assumes it's up.

- [ ] **Step 9: Commit**

```bash
git add .gitignore Directory.Build.props ForgeLinkSms.sln src/ForgeLinkSms
git commit -m "chore: scaffold Android-only MAUI app and emulator environment"
```

---

### Task 2: Core class library and test project

**Files:**
- Create: `src/ForgeLinkSms.Core/ForgeLinkSms.Core.csproj`
- Create: `tests/ForgeLinkSms.Core.Tests/ForgeLinkSms.Core.Tests.csproj`
- Create: `tests/ForgeLinkSms.Core.Tests/SmokeTests.cs`

**Interfaces:**
- Produces: `ForgeLinkSms.Core` (net9.0 library) referenced by both the MAUI app and the test project — this is where every model, view model, and service interface in this plan lives, so they're all unit-testable without an Android runtime.

- [ ] **Step 1: Create the Core library and reference it from the app**

```bash
dotnet new classlib -n ForgeLinkSms.Core -o src/ForgeLinkSms.Core -f net9.0
dotnet sln ForgeLinkSms.sln add src/ForgeLinkSms.Core/ForgeLinkSms.Core.csproj
dotnet add src/ForgeLinkSms/ForgeLinkSms.csproj reference src/ForgeLinkSms.Core/ForgeLinkSms.Core.csproj
dotnet add src/ForgeLinkSms.Core/ForgeLinkSms.Core.csproj package CommunityToolkit.Mvvm
```

Delete the template-generated `Class1.cs` from `ForgeLinkSms.Core`.

- [ ] **Step 2: Create the test project**

```bash
dotnet new xunit -n ForgeLinkSms.Core.Tests -o tests/ForgeLinkSms.Core.Tests
dotnet sln ForgeLinkSms.sln add tests/ForgeLinkSms.Core.Tests/ForgeLinkSms.Core.Tests.csproj
dotnet add tests/ForgeLinkSms.Core.Tests/ForgeLinkSms.Core.Tests.csproj reference src/ForgeLinkSms.Core/ForgeLinkSms.Core.csproj
dotnet add tests/ForgeLinkSms.Core.Tests/ForgeLinkSms.Core.Tests.csproj package Moq
```

- [ ] **Step 3: Write a smoke test to prove the harness works**

`tests/ForgeLinkSms.Core.Tests/SmokeTests.cs`:

```csharp
namespace ForgeLinkSms.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void Test_project_can_run()
    {
        Assert.True(true);
    }
}
```

- [ ] **Step 4: Run it**

```bash
dotnet test tests/ForgeLinkSms.Core.Tests/ForgeLinkSms.Core.Tests.csproj
```

Expected: `Passed! - Failed: 0, Passed: 1`.

- [ ] **Step 5: Commit**

```bash
git add src/ForgeLinkSms.Core tests/ForgeLinkSms.Core.Tests ForgeLinkSms.sln src/ForgeLinkSms/ForgeLinkSms.csproj
git commit -m "chore: add Core library and xUnit test project"
```

---

### Task 3: Domain models

**Files:**
- Create: `src/ForgeLinkSms.Core/Models/SmsMessage.cs`
- Create: `src/ForgeLinkSms.Core/Models/SmsThread.cs`
- Create: `src/ForgeLinkSms.Core/Models/ContactInfo.cs`
- Test: `tests/ForgeLinkSms.Core.Tests/Models/SmsMessageTests.cs`
- Test: `tests/ForgeLinkSms.Core.Tests/Models/SmsThreadTests.cs`

**Interfaces:**
- Produces: `SmsMessageStatus` enum (`Sending, Sent, Delivered, Failed`), `SmsMessage` (with `Id: long`, `ThreadId: long`, `Address: string`, `Body: string`, `Timestamp: DateTimeOffset`, `IsOutgoing: bool`, `Status: SmsMessageStatus`, computed `StatusDisplay: string`), `SmsThread` (`Id: long`, `Address: string`, `DisplayName: string?`, `LastMessageBody: string`, `LastMessageTimestamp: DateTimeOffset`, `UnreadCount: int`, computed `PreviewText: string`), `ContactInfo` (`DisplayName: string`, `PhoneNumber: string`, `PhotoUri: string?`). Every later task's service layer and view models consume these exact shapes.

- [ ] **Step 1: Write the failing tests for `SmsMessage.StatusDisplay`**

`tests/ForgeLinkSms.Core.Tests/Models/SmsMessageTests.cs`:

```csharp
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Tests.Models;

public class SmsMessageTests
{
    [Theory]
    [InlineData(SmsMessageStatus.Sending, "Sending…")]
    [InlineData(SmsMessageStatus.Sent, "✓ Sent")]
    [InlineData(SmsMessageStatus.Delivered, "✓✓ Delivered")]
    [InlineData(SmsMessageStatus.Failed, "⚠ Not sent")]
    public void StatusDisplay_reflects_status(SmsMessageStatus status, string expected)
    {
        var message = new SmsMessage
        {
            Id = 1,
            ThreadId = 1,
            Address = "5550142231",
            Body = "hi",
            Timestamp = DateTimeOffset.UtcNow,
            IsOutgoing = true,
            Status = status
        };

        Assert.Equal(expected, message.StatusDisplay);
    }

    [Fact]
    public void StatusDisplay_is_blank_for_incoming_messages()
    {
        var message = new SmsMessage
        {
            Id = 1,
            ThreadId = 1,
            Address = "5550142231",
            Body = "hi",
            Timestamp = DateTimeOffset.UtcNow,
            IsOutgoing = false,
            Status = SmsMessageStatus.Delivered
        };

        Assert.Equal(string.Empty, message.StatusDisplay);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet test tests/ForgeLinkSms.Core.Tests --filter SmsMessageTests
```

Expected: FAIL — `ForgeLinkSms.Core.Models` namespace / `SmsMessage` type does not exist yet.

- [ ] **Step 3: Implement `SmsMessage`**

`src/ForgeLinkSms.Core/Models/SmsMessage.cs`:

```csharp
namespace ForgeLinkSms.Core.Models;

public enum SmsMessageStatus
{
    Sending,
    Sent,
    Delivered,
    Failed
}

public class SmsMessage
{
    public required long Id { get; init; }
    public required long ThreadId { get; init; }
    public required string Address { get; init; }
    public required string Body { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required bool IsOutgoing { get; init; }
    public required SmsMessageStatus Status { get; init; }

    /// Only outgoing messages have a status to show — SMS has no concept
    /// of a "read" tick, so this never goes past Delivered.
    public string StatusDisplay => (IsOutgoing, Status) switch
    {
        (false, _) => string.Empty,
        (true, SmsMessageStatus.Sending) => "Sending…",
        (true, SmsMessageStatus.Sent) => "✓ Sent",
        (true, SmsMessageStatus.Delivered) => "✓✓ Delivered",
        (true, SmsMessageStatus.Failed) => "⚠ Not sent",
        _ => string.Empty
    };
}
```

- [ ] **Step 4: Run it to verify it passes**

```bash
dotnet test tests/ForgeLinkSms.Core.Tests --filter SmsMessageTests
```

Expected: PASS (5 tests).

- [ ] **Step 5: Write the failing test for `SmsThread.PreviewText`**

`tests/ForgeLinkSms.Core.Tests/Models/SmsThreadTests.cs`:

```csharp
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Tests.Models;

public class SmsThreadTests
{
    [Fact]
    public void PreviewText_truncates_long_bodies_to_60_chars_with_ellipsis()
    {
        var longBody = new string('a', 100);
        var thread = new SmsThread
        {
            Id = 1,
            Address = "5550142231",
            DisplayName = null,
            LastMessageBody = longBody,
            LastMessageTimestamp = DateTimeOffset.UtcNow,
            UnreadCount = 0
        };

        Assert.Equal(new string('a', 60) + "…", thread.PreviewText);
    }

    [Fact]
    public void PreviewText_leaves_short_bodies_unchanged()
    {
        var thread = new SmsThread
        {
            Id = 1,
            Address = "5550142231",
            DisplayName = null,
            LastMessageBody = "short message",
            LastMessageTimestamp = DateTimeOffset.UtcNow,
            UnreadCount = 0
        };

        Assert.Equal("short message", thread.PreviewText);
    }

    [Fact]
    public void DisplayNameOrAddress_falls_back_to_address_when_no_contact_match()
    {
        var thread = new SmsThread
        {
            Id = 1,
            Address = "5550142231",
            DisplayName = null,
            LastMessageBody = "hi",
            LastMessageTimestamp = DateTimeOffset.UtcNow,
            UnreadCount = 0
        };

        Assert.Equal("5550142231", thread.DisplayNameOrAddress);
    }

    [Fact]
    public void DisplayNameOrAddress_prefers_contact_name()
    {
        var thread = new SmsThread
        {
            Id = 1,
            Address = "5550142231",
            DisplayName = "Alice Smith",
            LastMessageBody = "hi",
            LastMessageTimestamp = DateTimeOffset.UtcNow,
            UnreadCount = 0
        };

        Assert.Equal("Alice Smith", thread.DisplayNameOrAddress);
    }
}
```

- [ ] **Step 6: Run it to verify it fails**

```bash
dotnet test tests/ForgeLinkSms.Core.Tests --filter SmsThreadTests
```

Expected: FAIL — `SmsThread` does not exist yet.

- [ ] **Step 7: Implement `SmsThread` and `ContactInfo`**

`src/ForgeLinkSms.Core/Models/SmsThread.cs`:

```csharp
namespace ForgeLinkSms.Core.Models;

public class SmsThread
{
    public required long Id { get; init; }
    public required string Address { get; init; }
    public required string? DisplayName { get; init; }
    public required string LastMessageBody { get; init; }
    public required DateTimeOffset LastMessageTimestamp { get; init; }
    public required int UnreadCount { get; init; }

    public string DisplayNameOrAddress => string.IsNullOrWhiteSpace(DisplayName) ? Address : DisplayName;

    public string PreviewText => LastMessageBody.Length > 60
        ? LastMessageBody[..60] + "…"
        : LastMessageBody;
}
```

`src/ForgeLinkSms.Core/Models/ContactInfo.cs`:

```csharp
namespace ForgeLinkSms.Core.Models;

public class ContactInfo
{
    public required string DisplayName { get; init; }
    public required string PhoneNumber { get; init; }
    public string? PhotoUri { get; init; }
}
```

- [ ] **Step 8: Run it to verify it passes**

```bash
dotnet test tests/ForgeLinkSms.Core.Tests
```

Expected: PASS, all tests (Task 2's smoke test + Task 3's model tests).

- [ ] **Step 9: Commit**

```bash
git add src/ForgeLinkSms.Core/Models tests/ForgeLinkSms.Core.Tests/Models
git commit -m "feat: add SmsMessage, SmsThread, and ContactInfo models"
```

---

### Task 4: Default-SMS-app manifest components (skeletons)

**Files:**
- Create: `src/ForgeLinkSms/Platforms/Android/SmsDeliverReceiver.cs`
- Create: `src/ForgeLinkSms/Platforms/Android/WapPushDeliverReceiver.cs`
- Create: `src/ForgeLinkSms/Platforms/Android/ComposeSmsActivity.cs`
- Create: `src/ForgeLinkSms/Platforms/Android/HeadlessSmsSendService.cs`
- Modify: `src/ForgeLinkSms/Platforms/Android/MainApplication.cs` (permission declarations)

**Interfaces:**
- Produces: the four Android components required for this app to be *eligible* to become the user's default SMS app (per AOSP's `RoleManager` SMS-role eligibility check: an `ACTION_SENDTO`/`ACTION_SEND` activity, an `SMS_DELIVER` receiver, a `WAP_PUSH_DELIVER` receiver, and a `RESPOND_VIA_MESSAGE` service — all four, not three; Android's manifest permission model requires the WAP_PUSH_DELIVER receiver to be a *separate* component from the SMS_DELIVER receiver because each needs a different `android:permission` value declared on its `<receiver>` element, and a single receiver element can only declare one). Their bodies are stubbed here (logged, no-op) — Task 9 fills in `SmsDeliverReceiver`'s real behavior; `WapPushDeliverReceiver` stays a permanent no-op stub through v1, since MMS is out of scope — it exists solely to satisfy the eligibility check. This task's whole purpose is to get Android to recognize the app as a valid default-SMS-app candidate, which is a prerequisite for every later manual test.

> **Correction (discovered during Task 4 execution, ruled on by the controller):** the original version of this task specified only three components and omitted `WapPushDeliverReceiver`. Deploying and checking Android's actual default-SMS-app picker confirmed the app was filtered out of the candidate list without it — AOSP's eligibility check requires all four. The steps below reflect the corrected, four-component version.

- [ ] **Step 1: Declare the required permissions**

Open `src/ForgeLinkSms/Platforms/Android/MainApplication.cs` and add assembly-level permission attributes above the namespace declaration:

```csharp
using Android.App;
using Android.Runtime;

[assembly: UsesPermission(Android.Manifest.Permission.ReadSms)]
[assembly: UsesPermission(Android.Manifest.Permission.SendSms)]
[assembly: UsesPermission(Android.Manifest.Permission.ReceiveSms)]
[assembly: UsesPermission(Android.Manifest.Permission.ReceiveMms)]
[assembly: UsesPermission(Android.Manifest.Permission.ReadContacts)]
[assembly: UsesPermission(Android.Manifest.Permission.ReadPhoneState)]
[assembly: UsesPermission("android.permission.POST_NOTIFICATIONS")]
[assembly: UsesPermission(Android.Manifest.Permission.ReceiveWapPush)]

namespace ForgeLinkSms;

[Application]
public class MainApplication : MauiApplication
{
    public MainApplication(IntPtr handle, JniHandleOwnership ownership) : base(handle, ownership) { }
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
```

(`POST_NOTIFICATIONS` is passed as a raw string because the strongly-typed `Android.Manifest.Permission` constant for it isn't present in every binding version — the string form always works and is exactly what the compiled manifest needs either way.)

- [ ] **Step 2: Stub the incoming-SMS broadcast receiver**

`src/ForgeLinkSms/Platforms/Android/SmsDeliverReceiver.cs`:

```csharp
using Android.App;
using Android.Content;

namespace ForgeLinkSms.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = true, Permission = "android.permission.BROADCAST_SMS")]
[IntentFilter(new[] { "android.provider.Telephony.SMS_DELIVER" })]
public class SmsDeliverReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        // Task 9 replaces this body with: parse PDUs via
        // Telephony.Sms.Intents.GetMessagesFromIntent(intent), write each
        // message into the Sms.Inbox content provider, then notify.
        Android.Util.Log.Debug("ForgeLinkSms", "SMS_DELIVER received (stub — Task 9 implements this)");
    }
}
```

- [ ] **Step 3: Stub the WAP push receiver (required for default-SMS-app eligibility)**

`src/ForgeLinkSms/Platforms/Android/WapPushDeliverReceiver.cs`:

```csharp
using Android.App;
using Android.Content;

namespace ForgeLinkSms.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = true, Permission = "android.permission.BROADCAST_WAP_PUSH")]
[IntentFilter(new[] { "android.provider.Telephony.WAP_PUSH_DELIVER" }, DataMimeType = "application/vnd.wap.mms-message")]
public class WapPushDeliverReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        // Permanent no-op: MMS is out of scope for v1 (see Global
        // Constraints — no MMS/media in v1). This receiver exists solely
        // so Android's RoleManager considers this app eligible for the
        // default-SMS-app role, which requires handling WAP_PUSH_DELIVER
        // alongside SMS_DELIVER. If MMS is ever added in a later phase,
        // this is where that work starts.
        global::Android.Util.Log.Debug("ForgeLinkSms", "WAP_PUSH_DELIVER received (stub — MMS not in v1 scope)");
    }
}
```

- [ ] **Step 4: Stub the compose-hand-off activity**

`src/ForgeLinkSms/Platforms/Android/ComposeSmsActivity.cs`:

```csharp
using Android.App;
using Android.Content;
using Android.OS;

namespace ForgeLinkSms.Platforms.Android;

[Activity(Exported = true)]
[IntentFilter(new[] { Intent.ActionSendto }, Categories = new[] { Intent.CategoryDefault }, DataSchemes = new[] { "sms", "smsto" })]
[IntentFilter(new[] { Intent.ActionSend }, Categories = new[] { Intent.CategoryDefault }, DataMimeTypes = new[] { "text/plain" })]
public class ComposeSmsActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Task 10 replaces this body with: extract the recipient address
        // (Intent.Data, "smsto:<number>") and any prefilled body
        // (Intent.GetStringExtra(Intent.ExtraText)), then hand off to
        // MainActivity's Compose page with those values pre-populated.
        Finish();
    }
}
```

- [ ] **Step 5: Stub the headless quick-reply service**

`src/ForgeLinkSms/Platforms/Android/HeadlessSmsSendService.cs`:

```csharp
using Android.App;
using Android.Content;
using Android.OS;

namespace ForgeLinkSms.Platforms.Android;

[Service(Exported = true, Permission = "android.permission.SEND_RESPOND_VIA_MESSAGE")]
[IntentFilter(new[] { "android.intent.action.RESPOND_VIA_MESSAGE" }, Categories = new[] { Intent.CategoryDefault }, DataSchemes = new[] { "sms", "smsto" })]
public class HeadlessSmsSendService : IntentService
{
    public HeadlessSmsSendService() : base(nameof(HeadlessSmsSendService)) { }

    protected override void OnHandleIntent(Intent? intent)
    {
        // Task 10 replaces this body with: extract the recipient + reply
        // text from the intent and send it via ISmsService, without
        // opening any UI.
        Android.Util.Log.Debug("ForgeLinkSms", "RESPOND_VIA_MESSAGE received (stub — Task 10 implements this)");
    }
}
```

- [ ] **Step 6: Build to verify the manifest merges cleanly**

```bash
dotnet build src/ForgeLinkSms/ForgeLinkSms.csproj -f net9.0-android
```

Expected: `Build succeeded.`

- [ ] **Step 7: Manual verification — the app becomes a default-SMS-app candidate**

This machine cannot run the Android emulator (ARM64 Windows host; no native Windows-ARM64 emulator exists, and the x86_64-under-software-emulation fallback stalls rather than boots — confirmed during Task 1). Use a physical Android device connected over USB with debugging enabled instead:

```bash
dotnet build src/ForgeLinkSms/ForgeLinkSms.csproj -t:Run -f net9.0-android
```

On the device: open **Settings → Apps → Default apps → SMS app** (or send yourself a text and watch for the "set default SMS app" prompt). Confirm **ForgeLinkSms now appears in the list of selectable default SMS apps.** This is the concrete, observable proof that Task 4's component/permission wiring is correct — if it doesn't appear, one of the four components or a permission is misconfigured.

- [ ] **Step 8: Commit**

```bash
git add src/ForgeLinkSms/Platforms/Android
git commit -m "feat: add default-SMS-app component skeletons and permissions"
```

---

### Task 5: Default-app role service, SplashViewModel, and Splash page

**Files:**
- Create: `src/ForgeLinkSms.Core/Services/IDefaultAppRoleService.cs`
- Create: `src/ForgeLinkSms.Core/Services/INavigationService.cs`
- Create: `src/ForgeLinkSms.Core/ViewModels/SplashViewModel.cs`
- Create: `src/ForgeLinkSms/Platforms/Android/DefaultAppRoleService.cs`
- Create: `src/ForgeLinkSms/Services/NavigationService.cs`
- Create: `src/ForgeLinkSms/Pages/SplashPage.razor`
- Test: `tests/ForgeLinkSms.Core.Tests/ViewModels/SplashViewModelTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks besides the project scaffold.
- Produces: `IDefaultAppRoleService` with `bool IsDefaultSmsApp()` and `Task<bool> RequestDefaultSmsAppAsync()`; `INavigationService` with `Task NavigateToAsync(string route)`. `SplashViewModel` (constructor `SplashViewModel(IDefaultAppRoleService, INavigationService)`, method `Task InitializeAsync()`) is consumed by `SplashPage.razor` and is what every later "which page do we land on" decision in this plan builds on.

- [ ] **Step 1: Define the two service interfaces in Core**

`src/ForgeLinkSms.Core/Services/IDefaultAppRoleService.cs`:

```csharp
namespace ForgeLinkSms.Core.Services;

public interface IDefaultAppRoleService
{
    bool IsDefaultSmsApp();
    Task<bool> RequestDefaultSmsAppAsync();
}
```

`src/ForgeLinkSms.Core/Services/INavigationService.cs`:

```csharp
namespace ForgeLinkSms.Core.Services;

public interface INavigationService
{
    Task NavigateToAsync(string route);
}
```

- [ ] **Step 2: Write the failing test for `SplashViewModel`**

`tests/ForgeLinkSms.Core.Tests/ViewModels/SplashViewModelTests.cs`:

```csharp
using Moq;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class SplashViewModelTests
{
    [Fact]
    public async Task InitializeAsync_navigates_to_conversations_when_already_default_app()
    {
        var roleService = new Mock<IDefaultAppRoleService>();
        roleService.Setup(r => r.IsDefaultSmsApp()).Returns(true);
        var nav = new Mock<INavigationService>();

        var viewModel = new SplashViewModel(roleService.Object, nav.Object);
        await viewModel.InitializeAsync();

        nav.Verify(n => n.NavigateToAsync("/conversations"), Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_navigates_to_onboarding_when_not_default_app()
    {
        var roleService = new Mock<IDefaultAppRoleService>();
        roleService.Setup(r => r.IsDefaultSmsApp()).Returns(false);
        var nav = new Mock<INavigationService>();

        var viewModel = new SplashViewModel(roleService.Object, nav.Object);
        await viewModel.InitializeAsync();

        nav.Verify(n => n.NavigateToAsync("/onboarding"), Times.Once);
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

```bash
dotnet test tests/ForgeLinkSms.Core.Tests --filter SplashViewModelTests
```

Expected: FAIL — `SplashViewModel` does not exist yet.

- [ ] **Step 4: Implement `SplashViewModel`**

`src/ForgeLinkSms.Core/ViewModels/SplashViewModel.cs`:

```csharp
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.ViewModels;

public class SplashViewModel
{
    private readonly IDefaultAppRoleService _roleService;
    private readonly INavigationService _navigation;

    public SplashViewModel(IDefaultAppRoleService roleService, INavigationService navigation)
    {
        _roleService = roleService;
        _navigation = navigation;
    }

    public async Task InitializeAsync()
    {
        var route = _roleService.IsDefaultSmsApp() ? "/conversations" : "/onboarding";
        await _navigation.NavigateToAsync(route);
    }
}
```

- [ ] **Step 5: Run it to verify it passes**

```bash
dotnet test tests/ForgeLinkSms.Core.Tests --filter SplashViewModelTests
```

Expected: PASS (2 tests).

- [ ] **Step 6: Implement the Android `DefaultAppRoleService`**

`src/ForgeLinkSms/Platforms/Android/DefaultAppRoleService.cs`:

```csharp
using Android.App.Role;
using Android.Content;
using Android.OS;
using Android.Provider;
using ForgeLinkSms.Core.Services;
using AndroidApp = Android.App.Application;

namespace ForgeLinkSms.Platforms.Android;

public class DefaultAppRoleService : IDefaultAppRoleService
{
    public bool IsDefaultSmsApp()
    {
        var context = AndroidApp.Context;
        var myPackage = context.PackageName;
        var currentDefault = Telephony.Sms.GetDefaultSmsPackage(context);
        return string.Equals(myPackage, currentDefault, StringComparison.Ordinal);
    }

    public Task<bool> RequestDefaultSmsAppAsync()
    {
        var context = AndroidApp.Context;
        Intent intent;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
        {
            var roleManager = (RoleManager)context.GetSystemService(Context.RoleService)!;
            intent = roleManager.CreateRequestRoleIntent(RoleManager.RoleSms);
        }
        else
        {
            intent = new Intent(Telephony.Sms.Intents.ActionChangeDefault);
            intent.PutExtra(Telephony.Sms.Intents.ExtraPackageName, context.PackageName);
        }

        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);

        // The system role-request dialog is asynchronous and its result
        // arrives on MainActivity's OnActivityResult, not here. Callers
        // re-check IsDefaultSmsApp() when the app resumes (see
        // OnboardingViewModel in Task 6) rather than awaiting a result
        // from this call directly.
        return Task.FromResult(false);
    }
}
```

- [ ] **Step 7: Implement `NavigationService` and register DI**

`src/ForgeLinkSms/Services/NavigationService.cs`:

```csharp
using Microsoft.AspNetCore.Components;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Services;

public class NavigationService : INavigationService
{
    private readonly NavigationManager _navigationManager;

    public NavigationService(NavigationManager navigationManager)
    {
        _navigationManager = navigationManager;
    }

    public Task NavigateToAsync(string route)
    {
        _navigationManager.NavigateTo(route);
        return Task.CompletedTask;
    }
}
```

Open `src/ForgeLinkSms/MauiProgram.cs` and register the services and view model:

```csharp
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.ViewModels;
using ForgeLinkSms.Platforms.Android;
using ForgeLinkSms.Services;

// inside CreateMauiApp(), before builder.Build():
builder.Services.AddSingleton<IDefaultAppRoleService, DefaultAppRoleService>();
builder.Services.AddSingleton<INavigationService, NavigationService>();
builder.Services.AddTransient<SplashViewModel>();
```

- [ ] **Step 8: Create `SplashPage.razor`**

`src/ForgeLinkSms/Pages/SplashPage.razor`:

```razor
@page "/"
@inject ForgeLinkSms.Core.ViewModels.SplashViewModel ViewModel

<div style="display:flex;align-items:center;justify-content:center;height:100vh;background:#25D366;">
    <h1 style="color:white;">SMS Messenger</h1>
</div>

@code {
    protected override async Task OnInitializedAsync()
    {
        await ViewModel.InitializeAsync();
    }
}
```

`"/"` is Splash's route deliberately (not `"/splash"`) — see the Global Constraints navigation-architecture note: a fresh `BlazorWebView` load resolves to whatever the Router matches at `/`, so Splash has to own that route to be what opens first. Delete the template's demo pages that would otherwise collide with or clutter this: `Components/Pages/Home.razor` (claims `@page "/"` itself), `Components/Pages/Counter.razor`, `Components/Pages/Weather.razor`, and `Components/Layout/NavMenu.razor` (links to the now-deleted pages). Simplify `Components/Layout/MainLayout.razor` to drop the `NavMenu` reference if it has one — a bare `@Body` layout is enough for now; Task 12 adds real bottom navigation.

- [ ] **Step 9: Build to verify it compiles**

```bash
dotnet build src/ForgeLinkSms/ForgeLinkSms.csproj -f net9.0-android
```

Expected: `Build succeeded.` (`/conversations` and `/onboarding` routes don't exist yet — that's fine, they're Tasks 6 and 8; this step only proves the Splash wiring compiles. Note: `dotnet build ForgeLinkSms.sln -f net9.0-android` — building the whole solution with an Android target filter — fails from Task 2 onward, since `ForgeLinkSms.Core`/`ForgeLinkSms.Core.Tests` don't target Android; build the app project directly as shown, or build the solution with no `-f` filter.)

- [ ] **Step 10: Commit**

```bash
git add src/ForgeLinkSms.Core/Services src/ForgeLinkSms.Core/ViewModels src/ForgeLinkSms/Platforms/Android/DefaultAppRoleService.cs src/ForgeLinkSms/Services src/ForgeLinkSms/Pages/SplashPage.razor src/ForgeLinkSms/MauiProgram.cs tests/ForgeLinkSms.Core.Tests/ViewModels
git commit -m "feat: add default-app role check and Splash screen routing"
```

---

### Task 6: Onboarding flow (permission requests + role request)

**Files:**
- Create: `src/ForgeLinkSms.Core/Services/IPermissionService.cs`
- Create: `src/ForgeLinkSms.Core/ViewModels/OnboardingViewModel.cs`
- Create: `src/ForgeLinkSms/Platforms/Android/PermissionService.cs`
- Create: `src/ForgeLinkSms/Pages/OnboardingPage.razor`
- Test: `tests/ForgeLinkSms.Core.Tests/ViewModels/OnboardingViewModelTests.cs`

**Interfaces:**
- Consumes: `IDefaultAppRoleService`, `INavigationService` (Task 5).
- Produces: `IPermissionService` with `Task<bool> RequestAllAsync()` (requests every permission from the Global Constraints list and returns whether all were granted). `OnboardingViewModel` (`RequestRoleCommand`, `RequestPermissionsCommand`, `ContinueCommand`, `CanContinue: bool`) is consumed by `OnboardingPage.razor`.

- [ ] **Step 1: Define `IPermissionService`**

`src/ForgeLinkSms.Core/Services/IPermissionService.cs`:

```csharp
namespace ForgeLinkSms.Core.Services;

public interface IPermissionService
{
    Task<bool> RequestAllAsync();
}
```

- [ ] **Step 2: Write the failing test for `OnboardingViewModel`**

`tests/ForgeLinkSms.Core.Tests/ViewModels/OnboardingViewModelTests.cs`:

```csharp
using Moq;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class OnboardingViewModelTests
{
    [Fact]
    public void CanContinue_is_false_until_both_role_and_permissions_are_granted()
    {
        var role = new Mock<IDefaultAppRoleService>();
        var permissions = new Mock<IPermissionService>();
        var nav = new Mock<INavigationService>();
        var viewModel = new OnboardingViewModel(role.Object, permissions.Object, nav.Object);

        Assert.False(viewModel.CanContinue);
    }

    [Fact]
    public async Task RequestPermissionsCommand_sets_PermissionsGranted_on_success()
    {
        var role = new Mock<IDefaultAppRoleService>();
        var permissions = new Mock<IPermissionService>();
        permissions.Setup(p => p.RequestAllAsync()).ReturnsAsync(true);
        var nav = new Mock<INavigationService>();
        var viewModel = new OnboardingViewModel(role.Object, permissions.Object, nav.Object);

        await viewModel.RequestPermissionsCommand.ExecuteAsync(null);

        Assert.True(viewModel.PermissionsGranted);
    }

    [Fact]
    public async Task ContinueCommand_navigates_to_conversations_when_both_granted()
    {
        var role = new Mock<IDefaultAppRoleService>();
        role.Setup(r => r.IsDefaultSmsApp()).Returns(true);
        var permissions = new Mock<IPermissionService>();
        permissions.Setup(p => p.RequestAllAsync()).ReturnsAsync(true);
        var nav = new Mock<INavigationService>();
        var viewModel = new OnboardingViewModel(role.Object, permissions.Object, nav.Object);

        await viewModel.RequestRoleCommand.ExecuteAsync(null);
        await viewModel.RequestPermissionsCommand.ExecuteAsync(null);
        await viewModel.ContinueCommand.ExecuteAsync(null);

        nav.Verify(n => n.NavigateToAsync("/conversations"), Times.Once);
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

```bash
dotnet test tests/ForgeLinkSms.Core.Tests --filter OnboardingViewModelTests
```

Expected: FAIL — `OnboardingViewModel` does not exist yet.

- [ ] **Step 4: Implement `OnboardingViewModel`**

`src/ForgeLinkSms.Core/ViewModels/OnboardingViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.ViewModels;

public partial class OnboardingViewModel : ObservableObject
{
    private readonly IDefaultAppRoleService _roleService;
    private readonly IPermissionService _permissionService;
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private bool _roleGranted;

    [ObservableProperty]
    private bool _permissionsGranted;

    public bool CanContinue => RoleGranted && PermissionsGranted;

    public OnboardingViewModel(IDefaultAppRoleService roleService, IPermissionService permissionService, INavigationService navigation)
    {
        _roleService = roleService;
        _permissionService = permissionService;
        _navigation = navigation;
    }

    [RelayCommand]
    private async Task RequestRole()
    {
        await _roleService.RequestDefaultSmsAppAsync();
        RoleGranted = _roleService.IsDefaultSmsApp();
        OnPropertyChanged(nameof(CanContinue));
    }

    [RelayCommand]
    private async Task RequestPermissions()
    {
        PermissionsGranted = await _permissionService.RequestAllAsync();
        OnPropertyChanged(nameof(CanContinue));
    }

    [RelayCommand]
    private async Task Continue()
    {
        if (CanContinue)
        {
            await _navigation.NavigateToAsync("/conversations");
        }
    }
}
```

- [ ] **Step 5: Run it to verify it passes**

```bash
dotnet test tests/ForgeLinkSms.Core.Tests --filter OnboardingViewModelTests
```

Expected: PASS (3 tests). Note: the "both granted" test calls `RequestRoleCommand` first, but `DefaultAppRoleService.IsDefaultSmsApp()` is mocked to `true` from the start — this models the real flow where the user returns from the system dialog and the app re-checks state, which Step 6 wires up for real on Android.

- [ ] **Step 6: Implement the Android `PermissionService`**

`src/ForgeLinkSms/Platforms/Android/PermissionService.cs`:

```csharp
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Platforms.Android;

public class PermissionService : IPermissionService
{
    private static readonly (Func<Task<PermissionStatus>> CheckAndRequest, string Name)[] Permissions =
    {
        (() => Permissions.RequestAsync<Permissions.Sms>(), "SMS"),
        (() => Permissions.RequestAsync<Permissions.ContactsRead>(), "Contacts"),
        (() => Permissions.RequestAsync<Permissions.Phone>(), "Phone State"),
        (() => Permissions.RequestAsync<Permissions.PostNotifications>(), "Notifications"),
    };

    public async Task<bool> RequestAllAsync()
    {
        var allGranted = true;
        foreach (var (request, _) in Permissions)
        {
            var status = await request();
            if (status != PermissionStatus.Granted)
            {
                allGranted = false;
            }
        }
        return allGranted;
    }
}
```

This uses MAUI's built-in `Microsoft.Maui.ApplicationModel.Permissions` abstraction (already available in every MAUI project) rather than calling Android's permission APIs directly — it handles the runtime-request dialog and result plumbing for us. `Permissions.Sms`, `Permissions.ContactsRead`, and `Permissions.Phone` are built-in MAUI permission types; `Permissions.PostNotifications` may need a small custom `Permissions.BasePlatformPermission` subclass declaring `android.permission.POST_NOTIFICATIONS` if the installed MAUI version doesn't ship it out of the box — check compiler output and add one if needed, following the same pattern as the other three.

- [ ] **Step 7: Register DI and create `OnboardingPage.razor`**

Add to `MauiProgram.cs`:

```csharp
builder.Services.AddSingleton<IPermissionService, PermissionService>();
builder.Services.AddTransient<OnboardingViewModel>();
```

`src/ForgeLinkSms/Pages/OnboardingPage.razor`:

```razor
@page "/onboarding"
@inject ForgeLinkSms.Core.ViewModels.OnboardingViewModel ViewModel

<div style="padding:24px;">
    <h2>Set up SMS Messenger</h2>
    <p>To send and receive real text messages, this app needs to become your default SMS app and needs a few permissions.</p>

    <button @onclick="() => ViewModel.RequestRoleCommand.ExecuteAsync(null)">
        1. Make this my default SMS app @(ViewModel.RoleGranted ? "✓" : "")
    </button>

    <button @onclick="() => ViewModel.RequestPermissionsCommand.ExecuteAsync(null)">
        2. Grant permissions @(ViewModel.PermissionsGranted ? "✓" : "")
    </button>

    <button disabled="@(!ViewModel.CanContinue)" @onclick="() => ViewModel.ContinueCommand.ExecuteAsync(null)">
        Continue
    </button>
</div>
```

- [ ] **Step 8: Build to verify it compiles**

```bash
dotnet build ForgeLinkSms.sln -f net9.0-android
```

Expected: `Build succeeded.`

- [ ] **Step 9: Manual verification on the emulator**

```bash
dotnet build src/ForgeLinkSms/ForgeLinkSms.csproj -t:Run -f net9.0-android
```

On the emulator: tap "Make this my default SMS app," accept the system dialog, confirm the button shows ✓ on return to the app. Tap "Grant permissions," accept each system prompt, confirm ✓ appears. Confirm "Continue" is disabled until both are checked, then enabled.

- [ ] **Step 10: Commit**

```bash
git add src/ForgeLinkSms.Core/Services/IPermissionService.cs src/ForgeLinkSms.Core/ViewModels/OnboardingViewModel.cs src/ForgeLinkSms/Platforms/Android/PermissionService.cs src/ForgeLinkSms/Pages/OnboardingPage.razor src/ForgeLinkSms/MauiProgram.cs tests/ForgeLinkSms.Core.Tests/ViewModels/OnboardingViewModelTests.cs
git commit -m "feat: add onboarding flow for default-app role and permissions"
```

---

### Task 7: Contact resolution

**Files:**
- Create: `src/ForgeLinkSms.Core/Services/IContactService.cs`
- Create: `src/ForgeLinkSms.Core/Utils/PhoneNumberFormatter.cs`
- Create: `src/ForgeLinkSms/Platforms/Android/ContactService.cs`
- Test: `tests/ForgeLinkSms.Core.Tests/Utils/PhoneNumberFormatterTests.cs`

**Interfaces:**
- Produces: `IContactService` with `Task<ContactInfo?> LookupAsync(string phoneNumber)` and `Task<IReadOnlyList<ContactInfo>> GetAllContactsAsync()`; `PhoneNumberFormatter.ToDisplayFormat(string raw)` (a pure, unit-tested helper used everywhere a raw number needs to look like `(555) 014-2231` instead of `5550142231`). Task 8's `ConversationsViewModel` and Task 10's `ContactPickerViewModel` both consume `IContactService`.

- [ ] **Step 1: Define `IContactService`**

`src/ForgeLinkSms.Core/Services/IContactService.cs`:

```csharp
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

public interface IContactService
{
    Task<ContactInfo?> LookupAsync(string phoneNumber);
    Task<IReadOnlyList<ContactInfo>> GetAllContactsAsync();
}
```

- [ ] **Step 2: Write the failing tests for `PhoneNumberFormatter`**

`tests/ForgeLinkSms.Core.Tests/Utils/PhoneNumberFormatterTests.cs`:

```csharp
using ForgeLinkSms.Core.Utils;

namespace ForgeLinkSms.Core.Tests.Utils;

public class PhoneNumberFormatterTests
{
    [Theory]
    [InlineData("5550142231", "(555) 014-2231")]
    [InlineData("15550142231", "+1 (555) 014-2231")]
    [InlineData("555-014-2231", "(555) 014-2231")]
    public void ToDisplayFormat_formats_valid_us_numbers(string raw, string expected)
    {
        Assert.Equal(expected, PhoneNumberFormatter.ToDisplayFormat(raw));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("SHORTCODE12")]
    public void ToDisplayFormat_returns_input_unchanged_when_not_a_standard_length(string raw)
    {
        Assert.Equal(raw, PhoneNumberFormatter.ToDisplayFormat(raw));
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

```bash
dotnet test tests/ForgeLinkSms.Core.Tests --filter PhoneNumberFormatterTests
```

Expected: FAIL — `PhoneNumberFormatter` does not exist yet.

- [ ] **Step 4: Implement `PhoneNumberFormatter`**

`src/ForgeLinkSms.Core/Utils/PhoneNumberFormatter.cs`:

```csharp
using System.Text.RegularExpressions;

namespace ForgeLinkSms.Core.Utils;

public static partial class PhoneNumberFormatter
{
    public static string ToDisplayFormat(string raw)
    {
        var digits = DigitsOnlyRegex().Replace(raw, "");

        return digits.Length switch
        {
            10 => $"({digits[..3]}) {digits[3..6]}-{digits[6..]}",
            11 when digits[0] == '1' => $"+1 ({digits[1..4]}) {digits[4..7]}-{digits[7..]}",
            _ => raw
        };
    }

    [GeneratedRegex(@"[^\d]")]
    private static partial Regex DigitsOnlyRegex();
}
```

- [ ] **Step 5: Run it to verify it passes**

```bash
dotnet test tests/ForgeLinkSms.Core.Tests --filter PhoneNumberFormatterTests
```

Expected: PASS (5 tests).

- [ ] **Step 6: Implement the Android `ContactService`**

`src/ForgeLinkSms/Platforms/Android/ContactService.cs`:

```csharp
using Android.Content;
using Android.Provider;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using AndroidApp = Android.App.Application;

namespace ForgeLinkSms.Platforms.Android;

public class ContactService : IContactService
{
    public Task<ContactInfo?> LookupAsync(string phoneNumber)
    {
        var context = AndroidApp.Context;
        var uri = Android.Net.Uri.WithAppendedPath(ContactsContract.PhoneLookup.ContentFilterUri, Android.Net.Uri.Encode(phoneNumber));
        var projection = new[] { ContactsContract.PhoneLookupColumns.DisplayName, ContactsContract.PhoneLookupColumns.PhotoUri };

        using var cursor = context.ContentResolver!.Query(uri!, projection, null, null, null);
        if (cursor is null || !cursor.MoveToFirst())
        {
            return Task.FromResult<ContactInfo?>(null);
        }

        var name = cursor.GetString(cursor.GetColumnIndexOrThrow(ContactsContract.PhoneLookupColumns.DisplayName));
        var photoIndex = cursor.GetColumnIndex(ContactsContract.PhoneLookupColumns.PhotoUri);
        var photo = photoIndex >= 0 ? cursor.GetString(photoIndex) : null;

        return Task.FromResult<ContactInfo?>(new ContactInfo
        {
            DisplayName = name,
            PhoneNumber = phoneNumber,
            PhotoUri = photo
        });
    }

    public Task<IReadOnlyList<ContactInfo>> GetAllContactsAsync()
    {
        var context = AndroidApp.Context;
        var results = new List<ContactInfo>();
        var projection = new[]
        {
            ContactsContract.CommonDataKinds.PhoneInterface.DisplayName,
            ContactsContract.CommonDataKinds.PhoneInterface.Number,
            ContactsContract.CommonDataKinds.PhoneInterface.PhotoUri
        };

        using var cursor = context.ContentResolver!.Query(ContactsContract.CommonDataKinds.Phone.ContentUri!, projection, null, null, null);
        if (cursor is not null)
        {
            var nameIdx = cursor.GetColumnIndexOrThrow(ContactsContract.CommonDataKinds.PhoneInterface.DisplayName);
            var numberIdx = cursor.GetColumnIndexOrThrow(ContactsContract.CommonDataKinds.PhoneInterface.Number);
            var photoIdx = cursor.GetColumnIndex(ContactsContract.CommonDataKinds.PhoneInterface.PhotoUri);

            while (cursor.MoveToNext())
            {
                results.Add(new ContactInfo
                {
                    DisplayName = cursor.GetString(nameIdx),
                    PhoneNumber = cursor.GetString(numberIdx),
                    PhotoUri = photoIdx >= 0 ? cursor.GetString(photoIdx) : null
                });
            }
        }

        return Task.FromResult<IReadOnlyList<ContactInfo>>(results);
    }
}
```

- [ ] **Step 7: Register DI**

Add to `MauiProgram.cs`:

```csharp
builder.Services.AddSingleton<IContactService, ContactService>();
```

- [ ] **Step 8: Build to verify it compiles**

```bash
dotnet build ForgeLinkSms.sln -f net9.0-android
```

Expected: `Build succeeded.`

- [ ] **Step 9: Manual verification — seed a contact and confirm lookup**

On the emulator, open the **Contacts** app and add a contact named "Alice Smith" with number `555-014-8890`. This exact contact is reused by Task 8 and Task 9's manual tests below, so create it now. There's no UI wired to `ContactService` yet in this task — verification here is just that the contact exists and is queryable; Task 8's Conversations screen is the first place its resolution becomes visible.

- [ ] **Step 10: Commit**

```bash
git add src/ForgeLinkSms.Core/Services/IContactService.cs src/ForgeLinkSms.Core/Utils tests/ForgeLinkSms.Core.Tests/Utils src/ForgeLinkSms/Platforms/Android/ContactService.cs src/ForgeLinkSms/MauiProgram.cs
git commit -m "feat: add contact lookup and phone number display formatting"
```

---

### Task 8: Thread list (read path) and Conversations screen

**Files:**
- Create: `src/ForgeLinkSms.Core/Services/IThreadService.cs`
- Create: `src/ForgeLinkSms.Core/ViewModels/ConversationsViewModel.cs`
- Create: `src/ForgeLinkSms/Platforms/Android/ThreadService.cs`
- Create: `src/ForgeLinkSms/Pages/Conversations/ConversationsPage.razor`
- Test: `tests/ForgeLinkSms.Core.Tests/ViewModels/ConversationsViewModelTests.cs`

**Interfaces:**
- Consumes: `SmsThread` (Task 3), `IContactService` (Task 7).
- Produces: `IThreadService` with `Task<IReadOnlyList<SmsThread>> GetThreadsAsync()`; `ConversationsViewModel` (`ObservableCollection<SmsThread> Threads`, `string SearchText` with live filtering, `LoadCommand`) consumed by `ConversationsPage.razor` and by Task 9's navigation-to-thread flow.

- [ ] **Step 1: Define `IThreadService`**

`src/ForgeLinkSms.Core/Services/IThreadService.cs`:

```csharp
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

public interface IThreadService
{
    Task<IReadOnlyList<SmsThread>> GetThreadsAsync();
}
```

- [ ] **Step 2: Write the failing tests for `ConversationsViewModel`'s search filter**

`tests/ForgeLinkSms.Core.Tests/ViewModels/ConversationsViewModelTests.cs`:

```csharp
using Moq;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class ConversationsViewModelTests
{
    private static SmsThread MakeThread(long id, string address, string? name, string lastMessage) => new()
    {
        Id = id,
        Address = address,
        DisplayName = name,
        LastMessageBody = lastMessage,
        LastMessageTimestamp = DateTimeOffset.UtcNow,
        UnreadCount = 0
    };

    [Fact]
    public async Task LoadCommand_populates_Threads_from_the_service()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", null, "hey there")
        });
        var viewModel = new ConversationsViewModel(threadService.Object);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Threads.Count);
    }

    [Fact]
    public async Task SearchText_filters_by_contact_name()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", "Bob Jones", "hey there")
        });
        var viewModel = new ConversationsViewModel(threadService.Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.SearchText = "alice";

        Assert.Single(viewModel.Threads);
        Assert.Equal("Alice Smith", viewModel.Threads[0].DisplayName);
    }

    [Fact]
    public async Task SearchText_filters_by_raw_address_when_no_contact_name()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "hi"),
            MakeThread(2, "5550148890", null, "hey there")
        });
        var viewModel = new ConversationsViewModel(threadService.Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.SearchText = "8890";

        Assert.Single(viewModel.Threads);
        Assert.Equal("5550148890", viewModel.Threads[0].Address);
    }

    [Fact]
    public async Task SearchText_filters_by_message_content()
    {
        var threadService = new Mock<IThreadService>();
        threadService.Setup(s => s.GetThreadsAsync()).ReturnsAsync(new List<SmsThread>
        {
            MakeThread(1, "5550142231", "Alice Smith", "let's go to the gym"),
            MakeThread(2, "5550148890", "Bob Jones", "see you tomorrow")
        });
        var viewModel = new ConversationsViewModel(threadService.Object);
        await viewModel.LoadCommand.ExecuteAsync(null);

        viewModel.SearchText = "gym";

        Assert.Single(viewModel.Threads);
        Assert.Equal("Alice Smith", viewModel.Threads[0].DisplayName);
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

```bash
dotnet test tests/ForgeLinkSms.Core.Tests --filter ConversationsViewModelTests
```

Expected: FAIL — `ConversationsViewModel` does not exist yet.

- [ ] **Step 4: Implement `ConversationsViewModel`**

`src/ForgeLinkSms.Core/ViewModels/ConversationsViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.ViewModels;

public partial class ConversationsViewModel : ObservableObject
{
    private readonly IThreadService _threadService;
    private IReadOnlyList<SmsThread> _allThreads = Array.Empty<SmsThread>();

    public ObservableCollection<SmsThread> Threads { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Threads))]
    private string _searchText = string.Empty;

    public ConversationsViewModel(IThreadService threadService)
    {
        _threadService = threadService;
    }

    [RelayCommand]
    private async Task Load()
    {
        _allThreads = await _threadService.GetThreadsAsync();
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Threads.Clear();
        var query = SearchText.Trim();
        var matches = string.IsNullOrEmpty(query)
            ? _allThreads
            : _allThreads.Where(t =>
                t.DisplayNameOrAddress.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.LastMessageBody.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var thread in matches)
        {
            Threads.Add(thread);
        }
    }
}
```

- [ ] **Step 5: Run it to verify it passes**

```bash
dotnet test tests/ForgeLinkSms.Core.Tests --filter ConversationsViewModelTests
```

Expected: PASS (4 tests).

- [ ] **Step 6: Implement the Android `ThreadService`**

`src/ForgeLinkSms/Platforms/Android/ThreadService.cs`:

```csharp
using Android.Provider;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using AndroidApp = Android.App.Application;

namespace ForgeLinkSms.Platforms.Android;

public class ThreadService : IThreadService
{
    private readonly IContactService _contactService;

    public ThreadService(IContactService contactService)
    {
        _contactService = contactService;
    }

    public async Task<IReadOnlyList<SmsThread>> GetThreadsAsync()
    {
        var context = AndroidApp.Context;
        var results = new List<SmsThread>();

        // Telephony.Threads only carries thread ids; the snippet (address,
        // body, date, read) is read straight from Telephony.Sms grouped by
        // thread_id, which is the simplest way to build the list view.
        var projection = new[] { "thread_id", "address", "body", "date", "read" };
        using var cursor = context.ContentResolver!.Query(
            Telephony.Sms.ContentUri!, projection, null, null, "date DESC");

        if (cursor is null)
        {
            return results;
        }

        var seenThreadIds = new HashSet<long>();
        var threadIdIdx = cursor.GetColumnIndexOrThrow("thread_id");
        var addressIdx = cursor.GetColumnIndexOrThrow("address");
        var bodyIdx = cursor.GetColumnIndexOrThrow("body");
        var dateIdx = cursor.GetColumnIndexOrThrow("date");
        var readIdx = cursor.GetColumnIndexOrThrow("read");

        while (cursor.MoveToNext())
        {
            var threadId = cursor.GetLong(threadIdIdx);
            if (!seenThreadIds.Add(threadId))
            {
                continue; // already took the most recent row for this thread (query is DATE DESC)
            }

            var address = cursor.GetString(addressIdx) ?? string.Empty;
            var contact = await _contactService.LookupAsync(address);
            var unread = cursor.GetInt(readIdx) == 0 ? 1 : 0;

            results.Add(new SmsThread
            {
                Id = threadId,
                Address = address,
                DisplayName = contact?.DisplayName,
                LastMessageBody = cursor.GetString(bodyIdx) ?? string.Empty,
                LastMessageTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(cursor.GetLong(dateIdx)),
                UnreadCount = unread
            });
        }

        return results;
    }
}
```

- [ ] **Step 7: Register DI and create `ConversationsPage.razor`**

Add to `MauiProgram.cs`:

```csharp
builder.Services.AddSingleton<IThreadService, ThreadService>();
builder.Services.AddTransient<ConversationsViewModel>();
```

`src/ForgeLinkSms/Pages/Conversations/ConversationsPage.razor`:

```razor
@page "/conversations"
@using Microsoft.AspNetCore.Components
@inject ForgeLinkSms.Core.ViewModels.ConversationsViewModel ViewModel
@inject NavigationManager Nav

<div style="padding:12px;">
    <input placeholder="Search contacts or numbers" @bind="ViewModel.SearchText" @bind:event="oninput" />

    @if (ViewModel.Threads.Count == 0)
    {
        <p>No conversations yet.</p>
    }
    else
    {
        @foreach (var thread in ViewModel.Threads)
        {
            <div @onclick="() => OpenThread(thread.Id)" style="padding:10px;border-bottom:1px solid #eee;">
                <strong>@thread.DisplayNameOrAddress</strong>
                @if (thread.UnreadCount > 0)
                {
                    <span style="color:#06b6d4;"> ● @thread.UnreadCount</span>
                }
                <div style="color:#94a3b8;font-size:0.85em;">@thread.PreviewText</div>
            </div>
        }
    }
</div>

@code {
    protected override async Task OnInitializedAsync()
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void OpenThread(long threadId)
    {
        Nav.NavigateTo($"/conversations/thread?id={threadId}");
    }
}
```

(`OpenThread`'s target route is created in Task 9.)

- [ ] **Step 8: Build to verify it compiles**

```bash
dotnet build src/ForgeLinkSms/ForgeLinkSms.csproj -f net9.0-android
```

Expected: `Build succeeded.`

- [ ] **Step 9: Manual verification on the physical device**

There is no emulator on this machine (see Task 1), so there's no console `sms send` trick available — but the physical device is the user's own real phone, which already has real SMS history from normal use. Deploy and open the Conversations screen; confirm real threads appear with correctly resolved contact names (not raw numbers, for any sender already in Contacts — including "Alice Smith" from Task 7 if you've texted that number), message previews, and timestamps.

If the device genuinely has no existing SMS history to check against (e.g. a fresh/reset device), seed one test row directly instead:

```bash
adb shell content insert --uri content://sms/inbox --bind address:s:5550148890 --bind body:s:"Hey! Are we still on for tomorrow?" --bind date:l:$(($(date +%s%N)/1000000)) --bind read:i:0
```

Relaunch/foreground the app and open the Conversations screen. Confirm the thread shows **"Alice Smith"** (not the raw number), the message preview, and an unread indicator. (If `content insert` is denied by the device's permission model, that's fine — fall back to verifying against real existing history instead; this task's read path doesn't care which data it's reading.)

- [ ] **Step 10: Commit**

```bash
git add src/ForgeLinkSms.Core/Services/IThreadService.cs src/ForgeLinkSms.Core/ViewModels/ConversationsViewModel.cs tests/ForgeLinkSms.Core.Tests/ViewModels/ConversationsViewModelTests.cs src/ForgeLinkSms/Platforms/Android/ThreadService.cs src/ForgeLinkSms/Pages/Conversations/ConversationsPage.razor src/ForgeLinkSms/MauiProgram.cs
git commit -m "feat: add thread list read path and Conversations screen"
```

---

### Task 9: Send/receive path, delivery status, and Thread Detail screen

**Files:**
- Create: `src/ForgeLinkSms.Core/Services/ISmsService.cs`
- Create: `src/ForgeLinkSms.Core/ViewModels/ThreadDetailViewModel.cs`
- Modify: `src/ForgeLinkSms/Platforms/Android/SmsDeliverReceiver.cs` (real implementation)
- Create: `src/ForgeLinkSms/Platforms/Android/SmsService.cs`
- Create: `src/ForgeLinkSms/Pages/Conversations/ThreadDetailPage.razor`
- Modify: `src/ForgeLinkSms/Pages/Conversations/ConversationsPage.razor` (`OpenThread` gains an `address` parameter)
- Test: `tests/ForgeLinkSms.Core.Tests/ViewModels/ThreadDetailViewModelTests.cs`

**Interfaces:**
- Consumes: `SmsMessage` (Task 3), `IContactService` (Task 7).
- Produces: `ISmsService` with `Task<IReadOnlyList<SmsMessage>> GetMessagesAsync(long threadId)` and `Task SendAsync(string address, string body)`; `ThreadDetailViewModel` (`ObservableCollection<SmsMessage> Messages`, `string ComposeText`, `SendCommand`) consumed by `ThreadDetailPage.razor` and reused as-is by Task 10's Compose flow for an *existing* thread.

- [ ] **Step 1: Define `ISmsService`**

`src/ForgeLinkSms.Core/Services/ISmsService.cs`:

```csharp
using ForgeLinkSms.Core.Models;

namespace ForgeLinkSms.Core.Services;

public interface ISmsService
{
    Task<IReadOnlyList<SmsMessage>> GetMessagesAsync(long threadId);
    Task SendAsync(string address, string body);
}
```

- [ ] **Step 2: Write the failing tests for `ThreadDetailViewModel`**

`tests/ForgeLinkSms.Core.Tests/ViewModels/ThreadDetailViewModelTests.cs`:

```csharp
using Moq;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class ThreadDetailViewModelTests
{
    [Fact]
    public async Task LoadCommand_populates_Messages_in_chronological_order()
    {
        var sms = new Mock<ISmsService>();
        sms.Setup(s => s.GetMessagesAsync(1)).ReturnsAsync(new List<SmsMessage>
        {
            new() { Id = 2, ThreadId = 1, Address = "555", Body = "second", Timestamp = DateTimeOffset.UtcNow, IsOutgoing = true, Status = SmsMessageStatus.Sent },
            new() { Id = 1, ThreadId = 1, Address = "555", Body = "first", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5), IsOutgoing = false, Status = SmsMessageStatus.Delivered }
        });
        var viewModel = new ThreadDetailViewModel(sms.Object, threadId: 1, address: "555");

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.Messages.Count);
        Assert.Equal("first", viewModel.Messages[0].Body);
        Assert.Equal("second", viewModel.Messages[1].Body);
    }

    [Fact]
    public async Task SendCommand_does_nothing_when_ComposeText_is_blank()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = new ThreadDetailViewModel(sms.Object, threadId: 1, address: "555")
        {
            ComposeText = "   "
        };

        await viewModel.SendCommand.ExecuteAsync(null);

        sms.Verify(s => s.SendAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SendCommand_sends_and_clears_ComposeText()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = new ThreadDetailViewModel(sms.Object, threadId: 1, address: "5550148890")
        {
            ComposeText = "See you then"
        };

        await viewModel.SendCommand.ExecuteAsync(null);

        sms.Verify(s => s.SendAsync("5550148890", "See you then"), Times.Once);
        Assert.Equal(string.Empty, viewModel.ComposeText);
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

```bash
dotnet test tests/ForgeLinkSms.Core.Tests --filter ThreadDetailViewModelTests
```

Expected: FAIL — `ThreadDetailViewModel` does not exist yet.

- [ ] **Step 4: Implement `ThreadDetailViewModel`**

`src/ForgeLinkSms.Core/ViewModels/ThreadDetailViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.ViewModels;

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
        var messages = await _smsService.GetMessagesAsync(_threadId);
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
```

- [ ] **Step 5: Run it to verify it passes**

```bash
dotnet test tests/ForgeLinkSms.Core.Tests --filter ThreadDetailViewModelTests
```

Expected: PASS (3 tests).

- [ ] **Step 6: Implement the Android `SmsService`, including multi-part send with Sent/Delivered `PendingIntent`s**

`src/ForgeLinkSms/Platforms/Android/SmsService.cs`:

```csharp
using Android.App;
using Android.Content;
using Android.Provider;
using Android.Telephony;
using ForgeLinkSms.Core.Models;
using ForgeLinkSms.Core.Services;
using AndroidApp = Android.App.Application;

namespace ForgeLinkSms.Platforms.Android;

public class SmsService : ISmsService
{
    public const string SentAction = "ForgeLinkSms.SMS_SENT";
    public const string DeliveredAction = "ForgeLinkSms.SMS_DELIVERED";

    public Task<IReadOnlyList<SmsMessage>> GetMessagesAsync(long threadId)
    {
        var context = AndroidApp.Context;
        var results = new List<SmsMessage>();
        var projection = new[] { "_id", "thread_id", "address", "body", "date", "type", "status" };

        using var cursor = context.ContentResolver!.Query(
            Telephony.Sms.ContentUri!, projection, "thread_id = ?", new[] { threadId.ToString() }, "date ASC");

        if (cursor is not null)
        {
            var idIdx = cursor.GetColumnIndexOrThrow("_id");
            var threadIdx = cursor.GetColumnIndexOrThrow("thread_id");
            var addressIdx = cursor.GetColumnIndexOrThrow("address");
            var bodyIdx = cursor.GetColumnIndexOrThrow("body");
            var dateIdx = cursor.GetColumnIndexOrThrow("date");
            var typeIdx = cursor.GetColumnIndexOrThrow("type");

            while (cursor.MoveToNext())
            {
                // type: 1 = inbox (incoming), 2 = sent (outgoing). See
                // Telephony.TextBasedSmsColumns.MessageTypeInbox / .MessageTypeSent.
                var type = cursor.GetInt(typeIdx);
                var isOutgoing = type == (int)Telephony.TextBasedSmsColumns.MessageTypeSent;

                results.Add(new SmsMessage
                {
                    Id = cursor.GetLong(idIdx),
                    ThreadId = cursor.GetLong(threadIdx),
                    Address = cursor.GetString(addressIdx) ?? string.Empty,
                    Body = cursor.GetString(bodyIdx) ?? string.Empty,
                    Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(cursor.GetLong(dateIdx)),
                    IsOutgoing = isOutgoing,
                    // A freshly-read Sent row can't distinguish Sent from
                    // Delivered by content alone; DeliveryStatusTracker
                    // (Step 7) updates rows in place once the delivery
                    // PendingIntent fires, so this initial read reports Sent
                    // and the UI updates when Delivered arrives.
                    Status = isOutgoing ? SmsMessageStatus.Sent : SmsMessageStatus.Delivered
                });
            }
        }

        return Task.FromResult<IReadOnlyList<SmsMessage>>(results);
    }

    public Task SendAsync(string address, string body)
    {
        var context = AndroidApp.Context;
        var smsManager = global::Android.Telephony.SmsManager.Default!;

        var sentPending = PendingIntent.GetBroadcast(context, 0, new Intent(SentAction), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
        var deliveredPending = PendingIntent.GetBroadcast(context, 0, new Intent(DeliveredAction), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;

        var parts = smsManager.DivideMessage(body);
        if (parts.Count > 1)
        {
            var sentIntents = new List<PendingIntent>();
            var deliveredIntents = new List<PendingIntent>();
            for (var i = 0; i < parts.Count; i++)
            {
                sentIntents.Add(sentPending);
                deliveredIntents.Add(deliveredPending);
            }
            smsManager.SendMultipartTextMessage(address, null, parts, sentIntents, deliveredIntents);
        }
        else
        {
            smsManager.SendTextMessage(address, null, body, sentPending, deliveredPending);
        }

        // The default SMS app is responsible for writing its own outgoing
        // messages into the Sent provider — Android does not do this for us.
        var values = new ContentValues();
        values.Put("address", address);
        values.Put("body", body);
        values.Put("date", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        values.Put("read", 1);
        context.ContentResolver!.Insert(Telephony.Sms.Sent.ContentUri!, values);

        return Task.CompletedTask;
    }
}
```

- [ ] **Step 7: Implement the real `SmsDeliverReceiver`, plus a small Sent/Delivered status receiver**

Replace the stub body from Task 4 in `src/ForgeLinkSms/Platforms/Android/SmsDeliverReceiver.cs`:

```csharp
using Android.App;
using Android.Content;
using Android.Provider;

namespace ForgeLinkSms.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = true, Permission = "android.permission.BROADCAST_SMS")]
[IntentFilter(new[] { "android.provider.Telephony.SMS_DELIVER" })]
public class SmsDeliverReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null)
        {
            return;
        }

        var messages = Telephony.Sms.Intents.GetMessagesFromIntent(intent);
        if (messages is null || messages.Length == 0)
        {
            return;
        }

        var address = messages[0].OriginatingAddress ?? string.Empty;
        var body = string.Concat(messages.Select(m => m.MessageBody));

        var values = new ContentValues();
        values.Put("address", address);
        values.Put("body", body);
        values.Put("date", Java.Lang.JavaSystem.CurrentTimeMillis());
        values.Put("read", 0);
        context.ContentResolver!.Insert(Telephony.Sms.Inbox.ContentUri!, values);

        // Task 11 hooks INotificationService in here to raise a local
        // notification when the app isn't in the foreground.
    }
}
```

Add a small receiver for the Sent/Delivered `PendingIntent` callbacks, in a new file `src/ForgeLinkSms/Platforms/Android/DeliveryStatusReceiver.cs`:

```csharp
using Android.App;
using Android.Content;
using Android.Provider;

namespace ForgeLinkSms.Platforms.Android;

[BroadcastReceiver(Enabled = true, Exported = false)]
[IntentFilter(new[] { SmsService.SentAction })]
[IntentFilter(new[] { SmsService.DeliveredAction })]
public class DeliveryStatusReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        // ResultCode is set by the framework when the PendingIntent fires:
        // Result.Ok for the SendAsync-side "sent" callback, and a non-null
        // PDU extra for the carrier "delivered" report. Task 12's manual
        // test verifies the observable effect (ticks changing in the UI);
        // wiring this into a persisted per-message status column is a
        // straightforward extension of the ContentValues update pattern
        // already used in SmsService.SendAsync and SmsDeliverReceiver.
        Android.Util.Log.Debug("ForgeLinkSms", $"Delivery status callback: {intent?.Action}, resultCode={ResultCode}");
    }
}
```

- [ ] **Step 8: Register DI and create `ThreadDetailPage.razor`**

Add to `MauiProgram.cs`:

```csharp
builder.Services.AddSingleton<ISmsService, SmsService>();
```

`ThreadDetailViewModel` takes constructor arguments (`threadId`, `address`) that aren't known until navigation time, so it's created directly in the page's `OnInitializedAsync` rather than injected — resolve `ISmsService` via `[Inject] IServiceProvider` and construct it there:

`src/ForgeLinkSms/Pages/Conversations/ThreadDetailPage.razor`:

```razor
@page "/conversations/thread"
@inject IServiceProvider Services
@using ForgeLinkSms.Core.Services
@using ForgeLinkSms.Core.ViewModels

@if (ViewModel is not null)
{
    <div style="display:flex;flex-direction:column;height:100vh;">
        <div style="padding:10px;background:white;border-bottom:1px solid #eee;">@Address</div>
        <div style="flex:1;overflow-y:auto;padding:8px;">
            @foreach (var message in ViewModel.Messages)
            {
                <div style="text-align:@(message.IsOutgoing ? "right" : "left");margin:4px;">
                    <div>@message.Body</div>
                    <div style="font-size:0.75em;color:#94a3b8;">@message.StatusDisplay</div>
                </div>
            }
        </div>
        <div style="display:flex;padding:8px;">
            <input style="flex:1;" placeholder="Text message" @bind="ViewModel.ComposeText" />
            <button @onclick="() => ViewModel.SendCommand.ExecuteAsync(null)">Send</button>
        </div>
    </div>
}

@code {
    [SupplyParameterFromQuery(Name = "id")]
    public long ThreadId { get; set; }

    [SupplyParameterFromQuery(Name = "address")]
    public string Address { get; set; } = string.Empty;

    private ThreadDetailViewModel? ViewModel;

    protected override async Task OnInitializedAsync()
    {
        var smsService = Services.GetRequiredService<ISmsService>();
        ViewModel = new ThreadDetailViewModel(smsService, ThreadId, Address);
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }
}
```

Update `ConversationsPage.razor`'s `OpenThread` (Task 8) to pass the address along:

```csharp
private void OpenThread(long threadId, string address)
{
    Nav.NavigateTo($"/conversations/thread?id={threadId}&address={Uri.EscapeDataString(address)}");
}
```

(and update its call site `@onclick="() => OpenThread(thread.Id)"` to `@onclick="() => OpenThread(thread.Id, thread.Address)"`).

- [ ] **Step 9: Build to verify it compiles**

```bash
dotnet build src/ForgeLinkSms/ForgeLinkSms.csproj -f net9.0-android
```

Expected: `Build succeeded.`

- [ ] **Step 10: Manual verification — real send/receive round trip**

There is no emulator on this machine, and the fake `555`-prefixed numbers used elsewhere in this plan (e.g. "Alice Smith" at `5550148890`) only work with an emulator's simulated telephony stack (its console `sms send` command injects directly into the virtual modem) — they are not real, routable numbers and will not deliver over the physical device's actual SIM/carrier. On a real device, this task's send/receive path needs a real, reachable phone number.

First, try texting the device's own number from itself (self-SMS) — many carriers allow this and it round-trips through the real network, exercising both the send path (Sent → Delivered) and the receive path (`SmsDeliverReceiver` firing on a genuinely delivered message) in one test, entirely on this one device:

1. Find the device's own number (`Settings → About phone → Status → SIM status`, or ask the user).
2. Set the app as the default SMS app if it isn't already (Task 6's onboarding flow, or `RoleManager`/Settings directly).
3. Open a thread to the device's own number (or start one) and send "Yes! 10am works for me". Confirm the message appears immediately with **"✓ Sent"**, and — if self-SMS is supported by this carrier — updates to **"✓✓ Delivered"** and a second copy arrives as an inbound message (watch `adb logcat | grep ForgeLinkSms` for the `DeliveryStatusReceiver` and `SmsDeliverReceiver` debug lines firing).
4. If the carrier does not support self-SMS (delivery never happens, no inbound copy arrives), ask the user for a real second number to test with — their own second phone, another device they have access to, or a family member/friend willing to receive one test text — and repeat the send from this device to that number, and have that number text back a reply to test the receive path. This is a real, user-involving step; don't fabricate delivery evidence if the round trip can't be completed. Report NEEDS_CONTEXT and ask if no second number is available and self-SMS doesn't work — this task's manual verification genuinely can't be completed without SOME way to send and receive one real text.
5. Type a message over 160 characters and send it; confirm it arrives as one continuous message in the thread (multi-part reassembly), not multiple separate bubbles.
6. Confirm the received message (from step 3 or 4) shows no status text (incoming messages never show Sent/Delivered), and that the Conversations list's preview/unread badge updated for it.

- [ ] **Step 11: Commit**

```bash
git add src/ForgeLinkSms.Core/Services/ISmsService.cs src/ForgeLinkSms.Core/ViewModels/ThreadDetailViewModel.cs tests/ForgeLinkSms.Core.Tests/ViewModels/ThreadDetailViewModelTests.cs src/ForgeLinkSms/Platforms/Android/SmsService.cs src/ForgeLinkSms/Platforms/Android/SmsDeliverReceiver.cs src/ForgeLinkSms/Platforms/Android/DeliveryStatusReceiver.cs src/ForgeLinkSms/Pages/Conversations
git commit -m "feat: implement SMS send/receive path with Sent/Delivered status"
```

---

### Task 10: Compose screen, contact picker, and individual group texting

**Files:**
- Create: `src/ForgeLinkSms.Core/ViewModels/ComposeViewModel.cs`
- Create: `src/ForgeLinkSms.Core/ViewModels/ContactPickerViewModel.cs`
- Create: `src/ForgeLinkSms/Pages/Compose/ComposePage.razor`
- Create: `src/ForgeLinkSms/Pages/Compose/ContactPickerPage.razor`
- Modify: `src/ForgeLinkSms/Platforms/Android/ComposeSmsActivity.cs` (real hand-off)
- Modify: `src/ForgeLinkSms/Platforms/Android/HeadlessSmsSendService.cs` (real quick-reply)
- Test: `tests/ForgeLinkSms.Core.Tests/ViewModels/ComposeViewModelTests.cs`

**Interfaces:**
- Consumes: `ISmsService` (Task 9), `IContactService` (Task 7), `PhoneNumberFormatter` (Task 7).
- Produces: `ComposeViewModel` (`ObservableCollection<string> Recipients`, `AddRecipientCommand(string)`, `RemoveRecipientCommand(string)`, `string MessageBody`, `SendCommand`, `bool IsGroupSend` computed from `Recipients.Count > 1`) consumed by `ComposePage.razor`.

- [ ] **Step 1: Write the failing tests for `ComposeViewModel`**

`tests/ForgeLinkSms.Core.Tests/ViewModels/ComposeViewModelTests.cs`:

```csharp
using Moq;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class ComposeViewModelTests
{
    [Fact]
    public void AddRecipientCommand_rejects_invalid_numbers()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = new ComposeViewModel(sms.Object);

        viewModel.AddRecipientCommand.Execute("abc");

        Assert.Empty(viewModel.Recipients);
    }

    [Fact]
    public void AddRecipientCommand_accepts_valid_numbers_and_dedupes()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = new ComposeViewModel(sms.Object);

        viewModel.AddRecipientCommand.Execute("5550148890");
        viewModel.AddRecipientCommand.Execute("5550148890");

        Assert.Single(viewModel.Recipients);
    }

    [Fact]
    public void IsGroupSend_is_true_only_with_more_than_one_recipient()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = new ComposeViewModel(sms.Object);

        Assert.False(viewModel.IsGroupSend);
        viewModel.AddRecipientCommand.Execute("5550148890");
        Assert.False(viewModel.IsGroupSend);
        viewModel.AddRecipientCommand.Execute("5550142231");
        Assert.True(viewModel.IsGroupSend);
    }

    [Fact]
    public async Task SendCommand_sends_individually_to_every_recipient()
    {
        var sms = new Mock<ISmsService>();
        var viewModel = new ComposeViewModel(sms.Object) { MessageBody = "hello everyone" };
        viewModel.AddRecipientCommand.Execute("5550148890");
        viewModel.AddRecipientCommand.Execute("5550142231");

        await viewModel.SendCommand.ExecuteAsync(null);

        sms.Verify(s => s.SendAsync("5550148890", "hello everyone"), Times.Once);
        sms.Verify(s => s.SendAsync("5550142231", "hello everyone"), Times.Once);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet test tests/ForgeLinkSms.Core.Tests --filter ComposeViewModelTests
```

Expected: FAIL — `ComposeViewModel` does not exist yet.

- [ ] **Step 3: Implement `ComposeViewModel`**

`src/ForgeLinkSms.Core/ViewModels/ComposeViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.ViewModels;

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
```

- [ ] **Step 4: Run it to verify it passes**

```bash
dotnet test tests/ForgeLinkSms.Core.Tests --filter ComposeViewModelTests
```

Expected: PASS (4 tests).

- [ ] **Step 5: Implement `ContactPickerViewModel`**

`src/ForgeLinkSms.Core/ViewModels/ContactPickerViewModel.cs`:

```csharp
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
```

- [ ] **Step 6: Register DI and create the two pages**

Add to `MauiProgram.cs`:

```csharp
builder.Services.AddTransient<ComposeViewModel>();
builder.Services.AddTransient<ContactPickerViewModel>();
```

`src/ForgeLinkSms/Pages/Compose/ComposePage.razor`:

```razor
@page "/compose"
@inject ForgeLinkSms.Core.ViewModels.ComposeViewModel ViewModel
@inject Microsoft.AspNetCore.Components.NavigationManager Nav

<div style="padding:12px;">
    <h3>New Message</h3>

    @foreach (var recipient in ViewModel.Recipients)
    {
        <span style="background:#e2e8f0;border-radius:12px;padding:4px 10px;margin:2px;display:inline-block;">
            @recipient <a @onclick="() => ViewModel.RemoveRecipientCommand.Execute(recipient)">✕</a>
        </span>
    }

    @if (ViewModel.IsGroupSend)
    {
        <p style="color:#92400e;">Sending to @ViewModel.Recipients.Count people individually — they won't see each other or each other's replies.</p>
    }

    <input placeholder="Add a phone number" @onkeyup="OnRecipientKeyUp" @bind="_newRecipient" />
    <button @onclick="() => Nav.NavigateTo("/compose/pick-contact")">Pick from contacts</button>

    <textarea placeholder="Text message" @bind="ViewModel.MessageBody"></textarea>
    <button @onclick="() => ViewModel.SendCommand.ExecuteAsync(null)">Send</button>
</div>

@code {
    private string _newRecipient = string.Empty;

    private void OnRecipientKeyUp(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !string.IsNullOrWhiteSpace(_newRecipient))
        {
            ViewModel.AddRecipientCommand.Execute(_newRecipient);
            _newRecipient = string.Empty;
        }
    }
}
```

`src/ForgeLinkSms/Pages/Compose/ContactPickerPage.razor`:

```razor
@page "/compose/pick-contact"
@inject ForgeLinkSms.Core.ViewModels.ContactPickerViewModel ViewModel
@inject Microsoft.AspNetCore.Components.NavigationManager Nav

<div style="padding:12px;">
    @foreach (var contact in ViewModel.Contacts)
    {
        <div @onclick="() => Select(contact.PhoneNumber)" style="padding:8px;border-bottom:1px solid #eee;">
            @contact.DisplayName — @contact.PhoneNumber
        </div>
    }
</div>

@code {
    protected override async Task OnInitializedAsync()
    {
        await ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void Select(string phoneNumber)
    {
        // Compose's own ComposeViewModel instance is transient-scoped per
        // navigation, so the picked number is passed back via query string
        // rather than a shared singleton state.
        Nav.NavigateTo($"/compose?add={Uri.EscapeDataString(phoneNumber)}");
    }
}
```

Update `ComposePage.razor`'s `@code` block to read the `add` query parameter on init and call `AddRecipientCommand`:

```csharp
[SupplyParameterFromQuery(Name = "add")]
public string? AddParam { get; set; }

protected override void OnParametersSet()
{
    if (!string.IsNullOrEmpty(AddParam))
    {
        ViewModel.AddRecipientCommand.Execute(AddParam);
    }
}
```

- [ ] **Step 7: Wire the real `ComposeSmsActivity` hand-off**

Replace the stub body in `src/ForgeLinkSms/Platforms/Android/ComposeSmsActivity.cs`:

```csharp
protected override void OnCreate(Bundle? savedInstanceState)
{
    base.OnCreate(savedInstanceState);

    var address = Intent?.Data?.SchemeSpecificPart;
    var prefillText = Intent?.GetStringExtra(Intent.ExtraText);

    var route = $"/compose?add={Uri.EscapeDataString(address ?? string.Empty)}";
    var mainIntent = new Intent(this, typeof(MainActivity));
    mainIntent.PutExtra("initial_route", route);
    mainIntent.AddFlags(ActivityFlags.NewTask);
    StartActivity(mainIntent);
    Finish();
}
```

(`prefillText` is accepted for completeness with `ACTION_SEND` but only the recipient is carried through in this pass, via the `initial_route` extra — passing the body too is a small follow-up extension using the same pattern. Note this hand-off is best-effort: nothing in this plan reads the `initial_route` extra back out on the `MainActivity`/`NavigationManager` side yet, so a cold-started app will land on Splash rather than Compose when launched this way — genuinely wiring that up would mean adding an `OnNewIntent` override to `MainActivity` that resolves `NavigationManager` from `MauiApplication.Current.Services` and calls `NavigateTo` with the extra's value, following the same DI-resolution pattern used in Task 9/11's broadcast receivers. Not required for this task's deliverable — the app is still fully usable via its own Compose button; this only affects the "another app hands off a number to text" entry point.)

- [ ] **Step 8: Wire the real `HeadlessSmsSendService`**

Replace the stub body in `src/ForgeLinkSms/Platforms/Android/HeadlessSmsSendService.cs`:

```csharp
protected override void OnHandleIntent(Intent? intent)
{
    var address = intent?.Data?.SchemeSpecificPart;
    var body = intent?.GetStringExtra("android.intent.extra.TEXT");

    if (!string.IsNullOrEmpty(address) && !string.IsNullOrEmpty(body))
    {
        var smsService = MauiApplication.Current.Services.GetRequiredService<ForgeLinkSms.Core.Services.ISmsService>();
        smsService.SendAsync(address, body).GetAwaiter().GetResult();
    }
}
```

- [ ] **Step 9: Build to verify it compiles**

```bash
dotnet build src/ForgeLinkSms/ForgeLinkSms.csproj -f net9.0-android
```

Expected: `Build succeeded.`

- [ ] **Step 10: Manual verification on the physical device**

There is no emulator on this machine and fake `555`-prefixed numbers don't route over a real carrier (see Task 9's Step 10). Use two real, reachable numbers for this test — e.g. the device's own number (if self-SMS works, per Task 9's finding) plus whatever second number was used there, or two numbers the user provides:

1. From Conversations, tap the FAB → Compose. Add both real recipients by typing and via "Pick from contacts." Confirm the "sending to N people individually" warning appears.
2. Send a message; confirm `SendAsync` fires once per recipient (check `adb logcat` or step through in the debugger) and both recipients receive independent 1:1 texts (verify by checking each recipient device/number's own messaging app — they should NOT see each other or a shared thread, since that's the whole point of "individual group texting").
3. From another Android app (e.g. Contacts), long-press a number and choose "Send SMS" if offered; confirm it hands off into this app's Compose screen with the number prefilled.

- [ ] **Step 11: Commit**

```bash
git add src/ForgeLinkSms.Core/ViewModels/ComposeViewModel.cs src/ForgeLinkSms.Core/ViewModels/ContactPickerViewModel.cs tests/ForgeLinkSms.Core.Tests/ViewModels/ComposeViewModelTests.cs src/ForgeLinkSms/Pages/Compose src/ForgeLinkSms/Platforms/Android/ComposeSmsActivity.cs src/ForgeLinkSms/Platforms/Android/HeadlessSmsSendService.cs src/ForgeLinkSms/MauiProgram.cs
git commit -m "feat: add Compose screen, contact picker, and individual group texting"
```

---

### Task 11: Local notifications and Settings screen

**Files:**
- Create: `src/ForgeLinkSms.Core/Services/INotificationService.cs`
- Create: `src/ForgeLinkSms.Core/ViewModels/SettingsViewModel.cs`
- Create: `src/ForgeLinkSms/Platforms/Android/NotificationService.cs`
- Create: `src/ForgeLinkSms/Pages/Settings/SettingsPage.razor`
- Modify: `src/ForgeLinkSms/Platforms/Android/SmsDeliverReceiver.cs` (call notification service)
- Test: `tests/ForgeLinkSms.Core.Tests/ViewModels/SettingsViewModelTests.cs`

**Interfaces:**
- Consumes: `IDefaultAppRoleService` (Task 5).
- Produces: `INotificationService` with `void NotifyIncomingMessage(string fromDisplayName, string body)`. `SettingsViewModel` (`bool NotificationsEnabled`, `bool IsDefaultSmsApp`, `RefreshCommand`) consumed by `SettingsPage.razor`.

- [ ] **Step 1: Define `INotificationService`**

`src/ForgeLinkSms.Core/Services/INotificationService.cs`:

```csharp
namespace ForgeLinkSms.Core.Services;

public interface INotificationService
{
    void NotifyIncomingMessage(string fromDisplayName, string body);
}
```

- [ ] **Step 2: Write the failing tests for `SettingsViewModel`**

`tests/ForgeLinkSms.Core.Tests/ViewModels/SettingsViewModelTests.cs`:

```csharp
using Moq;
using ForgeLinkSms.Core.Services;
using ForgeLinkSms.Core.ViewModels;

namespace ForgeLinkSms.Core.Tests.ViewModels;

public class SettingsViewModelTests
{
    [Fact]
    public async Task RefreshCommand_reads_current_default_app_status()
    {
        var role = new Mock<IDefaultAppRoleService>();
        role.Setup(r => r.IsDefaultSmsApp()).Returns(true);
        var viewModel = new SettingsViewModel(role.Object);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsDefaultSmsApp);
    }

    [Fact]
    public void NotificationsEnabled_defaults_to_true()
    {
        var role = new Mock<IDefaultAppRoleService>();
        var viewModel = new SettingsViewModel(role.Object);

        Assert.True(viewModel.NotificationsEnabled);
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

```bash
dotnet test tests/ForgeLinkSms.Core.Tests --filter SettingsViewModelTests
```

Expected: FAIL — `SettingsViewModel` does not exist yet.

- [ ] **Step 4: Implement `SettingsViewModel`**

`src/ForgeLinkSms.Core/ViewModels/SettingsViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ForgeLinkSms.Core.Services;

namespace ForgeLinkSms.Core.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IDefaultAppRoleService _roleService;

    [ObservableProperty]
    private bool _isDefaultSmsApp;

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    public SettingsViewModel(IDefaultAppRoleService roleService)
    {
        _roleService = roleService;
    }

    [RelayCommand]
    private Task Refresh()
    {
        IsDefaultSmsApp = _roleService.IsDefaultSmsApp();
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 5: Run it to verify it passes**

```bash
dotnet test tests/ForgeLinkSms.Core.Tests --filter SettingsViewModelTests
```

Expected: PASS (2 tests).

- [ ] **Step 6: Implement the Android `NotificationService`**

`src/ForgeLinkSms/Platforms/Android/NotificationService.cs`:

```csharp
using Android.App;
using Android.Content;
using AndroidX.Core.App;
using ForgeLinkSms.Core.Services;
using AndroidApp = Android.App.Application;

namespace ForgeLinkSms.Platforms.Android;

public class NotificationService : INotificationService
{
    private const string ChannelId = "incoming_sms";
    private static int _notificationId;

    public void NotifyIncomingMessage(string fromDisplayName, string body)
    {
        var context = AndroidApp.Context;
        EnsureChannel(context);

        var notification = new NotificationCompat.Builder(context, ChannelId)
            .SetContentTitle(fromDisplayName)
            .SetContentText(body)
            .SetSmallIcon(Android.Resource.Drawable.SymActionEmail)
            .SetAutoCancel(true)
            .Build();

        NotificationManagerCompat.From(context).Notify(System.Threading.Interlocked.Increment(ref _notificationId), notification);
    }

    private static void EnsureChannel(Context context)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        if (manager.GetNotificationChannel(ChannelId) is null)
        {
            var channel = new NotificationChannel(ChannelId, "Incoming Messages", NotificationImportance.High);
            manager.CreateNotificationChannel(channel);
        }
    }
}
```

- [ ] **Step 7: Call the notification service from `SmsDeliverReceiver`**

In `src/ForgeLinkSms/Platforms/Android/SmsDeliverReceiver.cs`, replace the "Task 11 hooks in here" comment with a real call. Since `BroadcastReceiver` instances aren't DI-constructed by MAUI, resolve the service from the running app's service provider:

```csharp
var notificationService = MauiApplication.Current.Services.GetRequiredService<ForgeLinkSms.Core.Services.INotificationService>();
notificationService.NotifyIncomingMessage(address, body);
```

(Add `using Microsoft.Extensions.DependencyInjection;` for `GetRequiredService`.) Resolving the *display name* via `IContactService` here would require an async call inside a `BroadcastReceiver.OnReceive`, which Android does not allow to block; using the raw `address` as the notification title is the correct v1 behavior — name resolution happens once the user opens the thread, same as the Conversations list.

- [ ] **Step 8: Register DI and create `SettingsPage.razor`**

Add to `MauiProgram.cs`:

```csharp
builder.Services.AddSingleton<INotificationService, NotificationService>();
builder.Services.AddTransient<SettingsViewModel>();
```

`src/ForgeLinkSms/Pages/Settings/SettingsPage.razor`:

```razor
@page "/settings"
@inject ForgeLinkSms.Core.ViewModels.SettingsViewModel ViewModel

<div style="padding:12px;">
    <h3>Settings</h3>
    <p>Default SMS app: @(ViewModel.IsDefaultSmsApp ? "Yes ✓" : "No — open Onboarding to fix this")</p>
    <label>
        <input type="checkbox" @bind="ViewModel.NotificationsEnabled" />
        Notify me about incoming texts
    </label>
</div>

@code {
    protected override async Task OnInitializedAsync()
    {
        await ViewModel.RefreshCommand.ExecuteAsync(null);
    }
}
```

- [ ] **Step 9: Build to verify it compiles**

```bash
dotnet build src/ForgeLinkSms/ForgeLinkSms.csproj -f net9.0-android
```

Expected: `Build succeeded.`

- [ ] **Step 10: Manual verification on the physical device**

Background the app (press Home), then have a real text sent to the device — self-SMS if the carrier supports it (per Task 9's Step 10 finding), or from whatever second number was used there. Confirm a system notification appears showing the sender and message text. Tap it and confirm the app opens. Open Settings and confirm "Default SMS app: Yes ✓" is shown.

- [ ] **Step 11: Commit**

```bash
git add src/ForgeLinkSms.Core/Services/INotificationService.cs src/ForgeLinkSms.Core/ViewModels/SettingsViewModel.cs tests/ForgeLinkSms.Core.Tests/ViewModels/SettingsViewModelTests.cs src/ForgeLinkSms/Platforms/Android/NotificationService.cs src/ForgeLinkSms/Platforms/Android/SmsDeliverReceiver.cs src/ForgeLinkSms/Pages/Settings src/ForgeLinkSms/MauiProgram.cs
git commit -m "feat: add local incoming-message notifications and Settings screen"
```

---

### Task 12: Bottom navigation, end-to-end pass, and README

**Files:**
- Modify: `src/ForgeLinkSms/Components/Layout/MainLayout.razor` (bottom nav bar: Conversations / Settings)
- Create: `src/ForgeLinkSms/Components/Layout/BottomNav.razor`
- Create: `README.md`

**Interfaces:**
- Consumes: every page from Tasks 5–11.
- Produces: the final navigable layout a user actually opens, and the setup/testing instructions for anyone (including a future engineer) picking this repo up cold.

- [ ] **Step 1: Add a bottom nav bar**

There is no MAUI Shell in this app (see the plan's Global Constraints navigation-architecture note) — bottom navigation is an ordinary Razor component using Blazor's `NavLink`, shown only on the two tab-equivalent pages (Conversations, Settings), not on Splash/Onboarding/ThreadDetail/Compose/ContactPicker.

`src/ForgeLinkSms/Components/Layout/BottomNav.razor`:

```razor
<div style="display:flex;border-top:1px solid #eee;position:fixed;bottom:0;left:0;right:0;background:white;">
    <NavLink href="/conversations" Match="NavLinkMatch.All" style="flex:1;text-align:center;padding:10px;">Messages</NavLink>
    <NavLink href="/settings" Match="NavLinkMatch.All" style="flex:1;text-align:center;padding:10px;">Settings</NavLink>
</div>
```

Add it to `ConversationsPage.razor` (Task 8) and `SettingsPage.razor` (Task 11) — each page includes `<BottomNav />` once near the end of its markup (a `@using ForgeLinkSms.Components.Layout` in `Components/_Imports.razor`, already present from the template, makes it available with no extra import needed). Splash, Onboarding, ThreadDetail, Compose, and ContactPicker do not include it — those aren't tab-equivalent screens.

Check `src/ForgeLinkSms/Components/Layout/MainLayout.razor` renders `@Body` without a leftover `NavMenu` reference (Task 5 already asked to simplify this file when the demo pages were removed) — if it still references the deleted `NavMenu.razor`, strip that reference now.

- [ ] **Step 2: Build and do a full manual walkthrough**

```bash
dotnet build src/ForgeLinkSms/ForgeLinkSms.csproj -t:Run -f net9.0-android
```

This machine cannot run the Android emulator (see Task 1/4's notes — ARM64 Windows host, no working emulator path found). Do this walkthrough on the physical Android device connected over USB, same as every other manual verification in this plan.

Walk through, on the device, in order: fresh install → Splash (`/`) routes to Onboarding → grant role + permissions → Continue lands on Conversations → seeded thread visible with correct contact name → open thread, send a message, watch Sent → Delivered → background the app, send yourself a real text from another phone/number, confirm notification → foreground, confirm thread updated → Compose a new message to two numbers, confirm the group-send warning and that both sends fire → tap Settings in the bottom nav, confirm default-app status shown → tap Messages in the bottom nav, confirm it returns to Conversations. This is the acceptance pass for the whole plan — if any step doesn't work, that's a defect in the corresponding earlier task, not a new task.

- [ ] **Step 3: Write the README**

`README.md`:

```markdown
# SMS Messenger

A native Android app that sends and receives real SMS text messages through
the phone's own cellular connection — not a chat app, no backend, no cloud
database. See `SMS Messenger App Requirements Document.html` for the full
product spec and `docs/superpowers/plans/2026-09-17-sms-messenger.md` for
the implementation plan this was built from.

## Requirements

- .NET SDK 9.0+ with the `android` workload (`dotnet workload install android`)
- A physical Android device connected over USB with USB debugging enabled.
  This project's development machine could not run the Android emulator at
  all (ARM64 Windows host — see the implementation plan's Task 1 for the
  full investigation); everything here is built and verified against a real
  device instead. An emulator may work fine on an x86_64 host if you have
  one, but is untested by this project.

## Build

    dotnet build src/ForgeLinkSms/ForgeLinkSms.csproj -f net9.0-android

(`dotnet build ForgeLinkSms.sln -f net9.0-android` — building the whole
solution with an Android target filter — does not work, since
`ForgeLinkSms.Core`/`ForgeLinkSms.Core.Tests` are plain `net9.0` projects
that don't target Android; build the app project directly, or build the
solution with no `-f` filter.)

## Run on a device

    dotnet build src/ForgeLinkSms/ForgeLinkSms.csproj -t:Run -f net9.0-android

## Run the unit tests

    dotnet test tests/ForgeLinkSms.Core.Tests

Unit tests cover every model and view model in `ForgeLinkSms.Core` — none
of them touch the Android runtime, so they run on any machine with the .NET
SDK. The Android-specific service implementations under
`src/ForgeLinkSms/Platforms/Android/` call real platform APIs
(`SmsManager`, content providers, `ContactsContract`, `RoleManager`) and are
verified by hand on a physical device, per the manual verification steps in
each task of the implementation plan.

## What this app does and doesn't do

See `SMS Messenger App Requirements Document.html`, section 2 ("What Real
SMS Can & Can't Do") — SMS has no typing indicators, no cross-network read
receipts, no reactions, no edit/delete, and no true shared group threads.
These are not missing features to add later; they don't exist in the SMS
protocol.
```

- [ ] **Step 4: Commit**

```bash
git add src/ForgeLinkSms/Components/Layout README.md
git commit -m "feat: wire up bottom navigation and add project README"
```

---

## Plan Self-Review

- **Spec coverage:** Executive Summary/platform choice → Task 1 (Android-only TFM). "What SMS Can/Can't Do" → encoded as Global Constraints and directly reflected in `SmsMessage.StatusDisplay` (Task 3) and the group-send warning (Task 10) — no task implements typing indicators, reactions, edit, delete-for-everyone, or true group threads, by design. Feature Overview → Tasks 5–11 cover every v1 feature cell (messaging, contacts/compose, system integration); the "Not in v1" cell has no corresponding task, correctly. UI mockups (Splash/Conversations/Thread Detail) → Tasks 5, 8, 9 pages. Android SMS Integration Architecture (4.1–4.8) → Task 4 (contract + components + permissions), Task 5 (role request), Task 9 (send/receive + content provider read/write), Task 7 (contacts), dual-SIM explicitly deferred as "optional nicety" per spec, not built in any task (consistent with spec calling it optional). MAUI Architecture (5.1–5.4) → the Core/App project split across all tasks; folder structure matches the spec's `Platforms/Android/` layout with the addition of the `ForgeLinkSms.Core` library, which is a testability-driven deviation noted in the plan's Architecture section, not a silent one. Data Model → Task 3 models + the "no app database" constraint stated in Global Constraints and never violated by any task. Notifications → Task 11. Permissions & Privacy → Task 4 + Task 6. Cost Summary → nothing to implement, it's a statement about infrastructure, correctly has no task. Phase 2/3 Roadmap → correctly out of scope for every task in this plan.
- **Placeholder scan:** No "TBD"/"handle appropriately" phrasing found; every step has real code or a concrete, literal manual-test action.
- **Type consistency:** Verified `ISmsService.SendAsync(string, string)` signature matches every caller (`ThreadDetailViewModel.Send`, `ComposeViewModel.Send`, `HeadlessSmsSendService`). `IThreadService.GetThreadsAsync()` and `IContactService.LookupAsync`/`GetAllContactsAsync` signatures match their Task 8/9/10 consumers. `SmsMessageStatus` enum values (`Sending, Sent, Delivered, Failed`) used consistently between Task 3's model and Task 9's `SmsService` (which only ever produces `Sent`/`Delivered`, correctly never `Failed` or `Sending` from a provider read, since those are transient client-side states not persisted by the OS provider).
