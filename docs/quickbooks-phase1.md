# QuickBooks Online: direct API push (Phase 1)

Phase 0 (`quickbooks-phase0.md`) built the invoice the way QuickBooks wants it and a preview page to key from.
Phase 1 sends that invoice to QuickBooks through Intuit's API. This file describes what is built so far and what
still has to be checked against a real sandbox company.

**Built: the connection (tasks Q1 to Q7), the sandbox checks (Q8) and the public pages Intuit asks for (Q22).**
The app itself sends nothing to QuickBooks yet. Mapping by QuickBooks id, customer matching and the push come next.

## Configuration

| Setting | Environment variable | What it is for |
|---|---|---|
| `Secrets:Key` | `Secrets__Key` | 32 random bytes, base64 (`openssl rand -base64 32`). Encrypts the Intuit client secret and the OAuth tokens before they are stored (AES-256-GCM; stored values start with `v1:`). Keep it out of the database and out of the repo: an encrypted environment variable in production, `dotnet user-secrets` locally. |
| `App:PublicBaseUrl` | `App__PublicBaseUrl` | The address people reach the app at, e.g. `https://mis.heyluckylabel.com`. Used to build the OAuth redirect URI. Empty means "use the current request's scheme and host". A misnamed variable is ignored without a word, so the keys form on Setup → QuickBooks says which of the two the redirect URI was built from. |
| `QuickBooks:*` | `QuickBooks__…` | Intuit's addresses and the API minor version (`QuickBooksOptions`). The defaults are the real ones. Override them only to point the clients at a stub server in a local smoke. |

Without `Secrets:Key` the app boots normally and Setup → QuickBooks says the key is missing; nothing can be saved or
connected. If the key is ever changed or lost, the saved client secret and tokens can no longer be read: save the keys
again and reconnect.

The app sits behind an edge that ends TLS, so it reads the forwarded protocol header (`Program.cs`). Session cookies
are `Secure` outside Development. The antiforgery cookie follows the request instead, because the host's health check
calls `/Account/Login` over plain http.

## Public pages

Intuit issues production keys only to an app that has public addresses for its terms, its privacy policy and a page
to land on after a disconnect. Three pages open without a sign-in:

| Address | What it is | Field on Intuit's app details |
|---|---|---|
| `/legal/terms` | Terms of use, with what the QuickBooks connection is used for | End-user license agreement URL |
| `/legal/privacy` | Privacy policy, with what comes from QuickBooks and how it is stored | Privacy policy URL |
| `/quickbooks/disconnected` | Where QuickBooks sends someone who disconnected LabelShop from its Apps menu | Disconnect URL |

The launch and connect/reconnect address is `/settings/quickbooks`. The two legal pages name the company from
Settings → General (`SiteOperator`), so they carry no company name of their own; with no company name set they say
"the operator of this site". The disconnected page only explains: anyone can open it, so it changes nothing, and
LabelShop learns of the disconnection the next time QuickBooks refuses its sign-in. The list of what the connection
is used for, in the terms, has to stay true as the push is built.

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

### What is stored

One row (`QuickBooksConnection`). The client secret, both tokens and the connected company's id (Intuit's realm id)
are stored encrypted with `Secrets:Key`; Intuit's security rules name the refresh token and the realm id. The client
ID, the last four characters of the secret, the company's name and the company check are stored as they are. The
card decrypts the company id to show it; the activity log does not carry it. Because the id is encrypted, the row
can't tell whether a new connect is for the same company: `QuickBooksConnectionService` compares and tells it.

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
the custom field named like a PO number when QuickBooks lists one. The verdict depends on the first two only.
Preferences lists just the three legacy text custom fields. A field made in QuickBooks' current Custom fields screen
is not reported there, so the PO row can read "Not listed" for a company that has one (the sandbox company does).

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
| Public pages | `Pages/Legal/Terms`, `Pages/Legal/Privacy`, `Pages/QuickBooks/Disconnected` |
| Reconnect banner | `ViewComponents/QuickBooksAlertViewComponent.cs` |

New code calls QuickBooks through `QuickBooksGateway`, not through `IQuickBooksApiClient` directly: the gateway is
what supplies the token and writes the log row.

## Checked against Intuit's sandbox (2026-11-11)

The connection has run against the sandbox company with real Development keys: the consent screen, the code
exchange, the company and Preferences reads, and about sixty API calls through the gateway. The Preferences names
the company check reads (`CustomTxnNumbers`, `ClassTrackingPerTxnLine`, `ClassTrackingPerTxn`, `UsingSalesTax`) are
the ones the sandbox returns.

A token refresh has also run against Intuit, made the way the daily keep-alive makes it: a new one-hour token came
back and the next read used it. Disconnect and a second Connect have run too: Intuit accepted the revoke, and the
company linked again.

Not yet exercised against Intuit itself, only against a local stand-in: a refresh set off by an expired token or by
a 401 answer (the same call, reached another way), the keep-alive on its own timer, and the refresh token changing
over days.

## Sandbox checks (task Q8)

Run 2026-11-11 against "Sandbox Company US ef89" (QuickBooks Plus, US). Each answer decides a detail of the push.
The request and the answer of every call are kept with the project's smoke scripts (`q8/responses/`, outside the
repo). The test invoices in the sandbox have numbers starting `LSQ8-`.

| # | Check | Answer |
|---|---|---|
| 1 | Tax | **Not settled here.** This sandbox company has sales tax on but no `TaxPrefs.PartnerTaxEnabled`, the flag Intuit documents for its automated sales tax engine, so the automated-tax questions need another company. What this one shows: a line sent with no tax code is stored `NON`, even for an item whose own `Taxable` is true. `TAX` lines get no tax unless the invoice names a tax code (`TxnTaxDetail.TxnTaxCodeRef`); with one named, 8% was charged, and it was charged for a customer marked not taxable too. A `TotalTax` we send is kept as sent. `Item.Taxable` can be read. |
| 2 | Rate precision | `Line.Amount` is required (error 2020 without it). When Qty and UnitPrice are both sent, Amount must equal Qty × UnitPrice rounded half up to the cent, or the invoice is refused (6070). It is not recalculated. UnitPrice is kept to 7 decimals; a 9-decimal rate was rounded to 7. Qty and Amount with no UnitPrice is accepted, and no UnitPrice comes back. |
| 3 | Invoice numbers | With custom transaction numbers on, our `DocNumber` is kept. A second invoice with the same number is refused (6140) and the message names the existing transaction id. An invoice sent with no number is created with none. Looking an invoice up by `DocNumber` works. Not tested with custom numbers off: the company check requires them on. |
| 4 | Terms | With no terms sent, the invoice's terms field stays empty and the due date follows the customer's own terms (Net 15 gave 15 days). A customer with no terms gets the company default (30 days here). Terms sent on the invoice win over the customer's. |
| 5 | PO number | **Solved for a field made in QuickBooks' current Custom fields screen.** Such a field has its own id (`1000000001` for "P.O. Number" in the sandbox company) and is invisible to plain calls: Preferences does not list it, and a plain read returns no `CustomField`. With `include=enhancedAllCustomFields` on the URL, a read or a query returns it (id, name, value); a create or a sparse update carrying `CustomField: [{DefinitionId, Type: "StringType", StringValue}]` sets it (the name is not needed); and it prints on the invoice as "P.O. Number". This works on Intuit's free tier. Without the parameter the same create succeeds and the value is silently dropped. A sparse update that sends no `CustomField` keeps the value. The id can be learned without the paid Custom Fields API: query recent invoices with the parameter and collect the fields that come back (only an invoice with a value shows the field). The legacy ids "1" to "3" are a different thing: a value written to `DefinitionId` "1" is stored and read back by plain calls, but it is not printed. Not tested: a company whose PO field is a legacy one, which Preferences would list. |
| 6 | Class on every line | `ClassRef` is kept on every line, including a line standing in for shipping. A line with no class is accepted, so LabelShop has to insist on one itself. |
| 7 | Send later | `EmailStatus: NeedToSend` is kept when the invoice carries `BillEmail`. Without `BillEmail` the create is refused (6000), even for a customer with an email on file. **Not run:** the send call, which emails someone, and a look at QuickBooks' own send queue. |
| 8 | `requestid` | A repeated create with the same id returns the first invoice, also when the body has changed: the new body is ignored. A refused create does not use the id up; a corrected body with the same id is created. |
| 9 | Update and void | A sparse update with `Id` and `SyncToken` replaces the lines and raises the sync token. A stale token is refused (5010). Void (`operation=void`) zeroes the amounts and the balance, sets the private note to "Voided", keeps the number, and the invoice can still be queried. Not tested: voiding an invoice that has a payment. |

### What the answers change

- **Rate.** Send Qty and UnitPrice only when Qty × UnitPrice, rounded half up to the cent, equals LabelShop's line
  amount. Otherwise send Qty and Amount with no UnitPrice. .NET rounds half to even unless told otherwise, so the
  comparison needs `MidpointRounding.AwayFromZero`. This replaces the earlier reading that QuickBooks recalculates
  the amount from the rate.
- **Numbers.** Always send `DocNumber`. Error 6140 means the number is already there: link to it through the lookup.
- **`requestid`.** It has to change when the payload changes, or a retry after an edit gets the old invoice back.
  Reusing it after a refusal is safe.
- **Send later.** Always send `BillEmail` with the flag.
- **Classes.** QuickBooks does not require a class per line; LabelShop's own blocker does.
- **Terms.** Sending none lets the customer's QuickBooks terms set the due date.
- **PO number.** Every invoice create, update, read and lookup carries `include=enhancedAllCustomFields`. The
  gateway's reads and queries have to take URL parameters for that; today only writes do. The field's id is a
  per-company setting. The company check should find it by looking at recent invoices with the parameter, and
  fall back to the legacy fields Preferences lists. When no invoice carries a PO number yet there is nothing to
  find: fill the box in by hand on one invoice, then test again.
- **Tax.** Without automated sales tax, tax appears only when the invoice names a tax code, and the customer's
  not-taxable flag is not applied for us. What a company on automated sales tax does is still to be checked.

### Still open

- Tax on a company that has automated sales tax (the real company's Preferences will say whether it does).
- The PO number on a company whose field is one of the three legacy ones.
- The send call and QuickBooks' send queue.
- Voiding an invoice that has a payment.

Seen and not explained: the sandbox invoices made before the P.O. Number box was first filled in by hand in the
QuickBooks screen print in QuickBooks' older layout, and the three made after it print in the newer one, whether
or not the call carried the parameter. Which layout an invoice prints in is not something the API call sets.
