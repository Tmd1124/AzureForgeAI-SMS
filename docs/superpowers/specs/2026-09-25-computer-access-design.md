# Computer Access (Home Wi-Fi) — Design

**Status:** Design approved in chat; spec awaiting review. Spike passed: managed `HttpListener` on `http://*:8765/` serves on the phone and was reached from the PC over home Wi-Fi (192.168.1.130 → 192.168.1.220).

## Goal

Read and send texts from a computer's web browser while the phone is on the same
Wi-Fi, with no cloud service, account, or cost. The browser page is built so a
later cloud relay (HostGator or Azure) can reuse it unchanged.

## How it works

- Menu → **Computer access** turns it on. The phone shows the address to open
  (`http://<phone-wifi-ip>:8765`) and a 6-digit pairing code.
- The phone runs a small HTTP server inside ForgeLink. While it's on, an Android
  foreground-service notification ("ForgeLink is sharing messages with your
  computer", with **Turn off**) keeps it running in the background and makes
  its state visible.
- The browser shows a pairing screen; entering the code issues a session token
  (stored in the browser) that authorizes every later request. **Unpair all** on
  the phone revokes every token.
- New messages reach the browser within a couple of seconds through a
  long-lived event stream (Server-Sent Events); the page reconnects on its own.
- Replies typed in the browser are sent by the phone exactly as if typed there
  (same `ISmsService` / `ConversationReplySender` paths, groups included).

## First version scope

In: conversation list (Chats lane first, Updates/Screener as tabs), open a
conversation with text, photos (thumbnails via the existing
`IMediaThumbnailService`), group sender names; reply; start a new text to a
contact or number; search across conversations; live updates; mark read when
opened in the browser.

Out (stays phone-only for now): sending photos/voice from the computer,
scheduling, filters, archive/trash/snooze management.

## Security

- Only the pairing code shown on the phone grants access; tokens are random
  (128-bit), stored hashed on the phone, revocable with **Unpair all**.
- Wrong-code attempts are rate limited (5 per minute, then a 1-minute lockout).
- The server only answers requests from private-network addresses (10.x,
  172.16–31.x, 192.168.x, link-local) and refuses to start when the phone
  isn't on Wi-Fi.
- Plain HTTP (no certificate on a home network). The phone screen warns not to
  use it on public Wi-Fi.

## Architecture

- `ForgeLinkSms.Core/Web/` (testable, no Android types):
  - `PairingService`: code generation, attempt limiting, token issue/verify/revoke.
  - `WebApi`: request routing (`GET /api/conversations`, `GET /api/conversations/{id}/messages?before=`,
    `POST /api/conversations/{id}/reply`, `POST /api/messages/new`, `GET /api/search?q=`,
    `GET /api/media/{partId}`, `GET /api/events`, `POST /api/pair`) that maps to the
    existing services and returns JSON DTOs. Takes an abstract request/response so
    it runs in tests without a socket.
  - `WebEventHub`: fans `IIncomingMessageNotifier` events out to connected streams.
- `ForgeLinkSms/Platforms/Android/`:
  - `ComputerAccessServer`: the socket-level HTTP server (managed
    `System.Net.HttpListener`, confirmed by a spike) hosting `WebApi`.
  - `ComputerAccessService`: Android foreground service owning the server's lifetime.
- `ForgeLinkSms/Resources/Raw/web/`: the single-page browser app (HTML/CSS/JS),
  served from the app package.
- `Pages/ComputerAccess/ComputerAccessPage.razor`: on/off, address, code, paired
  browsers count, Unpair all, public-Wi-Fi warning.

## Risks

- Android may throttle a background server; the foreground service is the
  standard answer, verified on the phone.
- ~~`HttpListener` support on .NET for Android~~ — confirmed by the spike.

## Testing

Unit tests for `PairingService` (codes, limits, tokens, revoke) and `WebApi`
(each route's JSON, auth rejection, reply routing to groups vs. one-to-one,
paging). Device checks: pair from the PC browser, read, reply, live update,
background survival, Turn off from the notification.
