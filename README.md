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

    dotnet build src/SmsMessenger/SmsMessenger.csproj -f net9.0-android

(`dotnet build SmsMessenger.sln -f net9.0-android` — building the whole
solution with an Android target filter — does not work, since
`SmsMessenger.Core`/`SmsMessenger.Core.Tests` are plain `net9.0` projects
that don't target Android; build the app project directly, or build the
solution with no `-f` filter.)

## Run on a device

    dotnet build src/SmsMessenger/SmsMessenger.csproj -t:Run -f net9.0-android

## Run the unit tests

    dotnet test tests/SmsMessenger.Core.Tests

Unit tests cover every model and view model in `SmsMessenger.Core` — none
of them touch the Android runtime, so they run on any machine with the .NET
SDK. The Android-specific service implementations under
`src/SmsMessenger/Platforms/Android/` call real platform APIs
(`SmsManager`, content providers, `ContactsContract`, `RoleManager`) and are
verified by hand on a physical device, per the manual verification steps in
each task of the implementation plan.

## What this app does and doesn't do

See `SMS Messenger App Requirements Document.html`, section 2 ("What Real
SMS Can & Can't Do") — SMS has no typing indicators, no cross-network read
receipts, no reactions, no edit/delete, and no true shared group threads.
These are not missing features to add later; they don't exist in the SMS
protocol.
