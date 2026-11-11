# QuickBooks Online: direct API push (Phase 1)

Phase 0 (`quickbooks-phase0.md`) built the invoice the way QuickBooks wants it and a preview page to key from.
Phase 1 sends that invoice to QuickBooks through Intuit's API. This file describes what is built so far and what
still has to be checked against a real sandbox company.

**Built: the connection (tasks Q1 to Q7).** Nothing is sent to QuickBooks yet. Mapping by QuickBooks id, customer
matching and the push itself come next.

## Configuration

| Setting | Environment variable | What it is for |
|---|---|---|
| `Secrets:Key` | `Secrets__Key` | 32 random bytes, base64 (`openssl rand -base64 32`). Encrypts the Intuit client secret and the OAuth tokens before they are stored (AES-256-GCM; stored values start with `v1:`). Keep it out of the database and out of the repo: an encrypted environment variable in production, `dotnet user-secrets` locally. |
| `App:PublicBaseUrl` | `App__PublicBaseUrl` | The address people reach the app at, e.g. `https://mis.heyluckylabel.com`. Used to build the OAuth redirect URI. Empty means "use the current request's scheme and host". |
| `QuickBooks:*` | `QuickBooks__…` | Intuit's addresses and the API minor version (`QuickBooksOptions`). The defaults are the real ones. Override them only to point the clients at a stub server in a local smoke. |

Without `Secrets:Key` the app boots normally and Setup → QuickBooks says the key is missing; nothing can be saved or
connected. If the key is ever changed or lost, the saved client secret and tokens can no longer be read: save the keys
again and reconnect.

The app sits behind an edge that ends TLS, so it reads the forwarded protocol header (`Program.cs`). Session cookies
are `Secure` outside Development. The antiforgery cookie follows the request instead, because the host's health check
calls `/Account/Login` over plain http.

## The connection

Setup → QuickBooks (Admin and Accounting). The Intuit app keys are Admin only.

1. **Keys.** Client ID and secret from the Intuit app's *Keys and credentials* page, and whether they are Development
   (sandbox) or Production keys. The secret is stored encrypted; the page shows its last four characters. Changing the
   client ID or the environment drops the company link, because tokens belong to the app that issued them. A new
   secret for the same app keeps it.
2. **Connect to QuickBooks.** The browser goes to Intuit's consent screen and comes back to `/quickbooks/callback`.
   That address must be listed in the Intuit app's redirect URIs exactly; the keys section shows the one this
   deployment sends. Only the accounting scope is requested.
3. **Callback.** A one-time `state` value is kept in a ten-minute cookie (`HttpOnly`, `SameSite=Lax`, path
   `/quickbooks`) and must come back unchanged. Every path through the callback answers with a redirect to Setup →
   QuickBooks, never a page, as Intuit's security rules require.
4. **Test connection** reads the company's name and settings, which proves the saved sign-in works.
5. **Disconnect** asks Intuit to cancel the tokens and removes them here. If Intuit can't be reached the tokens are
   still removed, and the page says to remove the app from the company's connected apps in QuickBooks.

### Tokens

- Access tokens last an hour. `QuickBooksTokenService` hands out a live one and refreshes when the saved one has two
  minutes or less left.
- Intuit can invalidate a refresh token that is used twice at once. Refreshes therefore go one at a time: a gate
  inside the process, and `SELECT … FOR UPDATE` on the connection row for the minutes during a deploy when two
  containers run side by side.
- The new pair is committed before the access token is used, and that save ignores request cancellation: once Intuit
  has issued a new refresh token, the old one is gone.
- `QuickBooksKeepAliveService` wakes every 15 minutes and refreshes when the last refresh is a day old. With no
  company connected a pass is one single-row read; Intuit is not called.
- When QuickBooks answers 401 to a token that should be good, `QuickBooksGateway` refreshes once and tries once
  more.

### When QuickBooks refuses the saved sign-in

Intuit's token service also answers `invalid_grant` during some of its own outages. So:

- The first refusal keeps the connection. The time is recorded (`RefusedSince`) and the settings page says so.
- A refusal ten minutes or more after the first one flags **Reconnect needed**. The keep-alive makes that second try
  on its own. The invoice pages then show a banner, and nothing asks Intuit again until a person acts.
- On a flagged connection, *Try the saved sign-in again* (the Test button) makes one more attempt with the stored
  refresh token. If Intuit accepts it, the connection is back without the consent screen. Otherwise: *Connect again*.
- A failure that isn't a refusal (Intuit unreachable, 5xx) is recorded as the last problem and changes nothing else.

### Company check

Connect and Test read QuickBooks' Preferences and keep the result on the connection row, so the page never waits on
Intuit just to be shown. Four rows: custom transaction numbers, class tracking with one class per row, sales tax, and
which invoice custom field is named like a PO number. The API can reach only the three legacy text custom fields;
their names are set in the QuickBooks screens.

### Activity log

Setup → QuickBooks → Activity log (`/settings/quickbooks/log`). Every attempt leaves a row: what, who (or
"automatic"), result, QuickBooks' fault code, HTTP status, duration and Intuit's trace id (`intuit_tid`), which their
support asks for. Rows hold ids and error text, never a token and never the contents of a QuickBooks record. Each row
is written through its own database scope, so it survives a rollback of the work it describes.

The server log gets one line per API call from `QuickBooksApiClient`: method, resource name, status, duration, trace
id. The HttpClient factory's own request logging is switched off for the two Intuit clients, because it prints full
URLs, which carry company ids and query text.

## Code map

| Piece | Where |
|---|---|
| Interfaces, token answer, exceptions, company check parser | `Domain/QuickBooks/` |
| Connection row, activity log row | `Domain/Entities/QuickBooksConnection.cs`, `QuickBooksSyncLog.cs` |
| Intuit OAuth client, Accounting API client, options | `Infrastructure/QuickBooks/` |
| Encryption | `Infrastructure/Security/AesGcmSecretProtector.cs` |
| Connect, test, disconnect | `Web/Services/QuickBooks/QuickBooksConnectionService.cs` |
| Tokens and refresh | `Web/Services/QuickBooks/QuickBooksTokenService.cs` |
| Every API call (token, 401 retry, log row) | `Web/Services/QuickBooks/QuickBooksGateway.cs` |
| Keep-alive | `Web/Background/QuickBooksKeepAliveService.cs` |
| Pages | `Pages/Settings/QuickBooks`, `Pages/Settings/QuickBooksLog`, `Pages/QuickBooks/Callback` |
| Reconnect banner | `ViewComponents/QuickBooksAlertViewComponent.cs` |

New code calls QuickBooks through `QuickBooksGateway`, not through `IQuickBooksApiClient` directly: the gateway is
what supplies the token and writes the log row.

## Not verified against a real company yet

The connection has been exercised end to end against a local stand-in for Intuit, not against Intuit itself. Open
until the sandbox company is connected:

- The whole round trip with real keys (consent screen, code exchange, refresh, revoke).
- Two of the Preferences names the company check reads: class tracking (`AccountingInfoPrefs.ClassTrackingPerTxnLine`
  and `ClassTrackingPerTxn`) and `TaxPrefs.UsingSalesTax`. They are written from the API's Preferences object as
  remembered, not from a page read while building this. `SalesFormsPrefs.CustomTxnNumbers`,
  `TaxPrefs.PartnerTaxEnabled` and the custom field arrays are taken from Intuit's own pages. A wrong name shows up
  as an "Off" row, since the parser reads anything missing as off.
- Whether a custom field created in QuickBooks' current Custom fields screen appears among the three legacy fields.

## Sandbox checks (task Q8)

Each decides a detail of the push. Answers and a saved response per check go here once the sandbox is connected.

| # | Check | Answer |
|---|---|---|
| 1 | Tax: lines marked `TAX`, no amount sent, ship-to sent. Is tax calculated? Is a tax-exempt customer left untaxed? What does a line with no tax code get? Can the item's own taxable setting be read? | open |
| 2 | Rate precision: a 7-decimal rate versus quantity and amount with no rate. What does QuickBooks store and show? | open |
| 3 | Invoice numbers: custom transaction numbers on and off; what a duplicate does. | open |
| 4 | Terms: when none are sent, does QuickBooks use the customer's own? | open |
| 5 | PO number: read the field's id from Preferences, write it on an invoice, see it on the form. | open |
| 6 | Class on every line, including the shipping line. | open |
| 7 | "Send later" plus billing email: does the invoice show in QuickBooks' send queue? Does the send call use the company's template? | open |
| 8 | `requestid`: does a repeated create return the first invoice? | open |
| 9 | Void and update with the sync token. | open |
