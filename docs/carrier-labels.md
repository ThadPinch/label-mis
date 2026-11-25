# Carrier labels: UPS through Worldwide Express (SpeedShip API)

The shop's UPS account is resold by Worldwide Express (WWEX). Labels, rates, voids and tracking for it go through
WWEX's own **SpeedShip API**, not through UPS's developer API: it is the one that returns the WWEX price, which is
what billed shipping is derived from (carrier cost × the multiplier in Setup → Shipping carrier).

This file has two parts: what the API does, as proven on WWEX's staging environment on 2026-11-24 (task U1), and how
LabelShop uses it (tasks U2 to U7).

Sources: WWEX's Postman collection "SpeedshipAPI V4 Staging" (sent by WWEX Integration Support on 2026-11-23, the
only documentation there is), SpeedShip's own public configuration file, and the staging answers themselves. Where
this file says **Staging, 2026-11-24** the fact was observed, not read.

## Environments

| | Staging | Production |
|---|---|---|
| Token | `https://auth.staging-wwex.com/oauth/token` | `https://auth.wwex.com/oauth/token` |
| API | `https://speedship.staging-wwex.com/svc/` | `https://www.speedship.com/svc/` |
| Audience | `staging-wwex-apig` | `wwex-apig` |
| Credentials | issued 2026-11-23 | issued by WWEX after they are told development is complete |

- The staging row is proven. The production row comes from SpeedShip's public configuration
  (`/assets/environments/config.json` on each host; the staging file names exactly the staging values above) and from
  the one production address in the Postman collection. **It has not been exercised: there are no production
  credentials yet.** Setup → Shipping carrier → Test connection is the check on the day they arrive.
- Staging credentials sign in as a shared WWEX test customer ("Test API Integration", UPS account `V01714`, Dallas),
  not as Hey Lucky. Staging prices are that account's, so they say nothing about Hey Lucky's rates, and a search of
  staging shipments returns other integrators' test shipments too.
- **Staging labels are samples.** They print "SAMPLE" and "UPS TRACKING NUMBER BARCODE PRINTS HERE" in place of the
  barcodes, and nothing is billed. Staging tracking only ever reports `CREATED` and `VOID`.

## Signing in

`POST` to the token address, form-encoded: `grant_type=client_credentials`, `client_id`, `client_secret`,
`audience`. The answer is a bearer token good for 24 hours (`expires_in: 86400`). There is no refresh token: a new
token is asked for with the same call. Every API call carries `Authorization: Bearer <token>`. A missing or bad token
answers `401 {"message":"Unauthorized"}`.

## The calls

Every call is a `POST` of `{"request": {...}}` to `<API>/<flowName>` and answers
`{"clientStatus": {...}, "correlationId": "...", "response": {...}}`. `correlationId` is what WWEX support asks for.

| Step | Flow | What goes in | What comes back |
|---|---|---|---|
| Rate | `shopFlow` | `productType: SMALLPACK`, ship-from and ship-to (company, street, city, state, zip, phone, a contact), ship date, one `handlingUnitList` entry per box (weight, optional dimensions, up to two references, optional insured value), residential flag | `response.offerList`: one offer per UPS service. Each has `offerId`, `productTransactionId` (the same for every offer of the request), `totalOfferPrice`, and one `offeredProductList` entry **per box** with its own price, its charges (`chargeItemList`: `FRT` freight, `FSC` fuel, `RDC` delivery area, `HRS` residential, `DVS` declared value, `SUR`) and `shopRQShipment` (`timeInTransit`: service code, name, transit days, delivery date; billed weight; `billToInfoList`: the UPS account and postal code to bill; the box's `handlingUnitId`). |
| Book | `integratedOrderFlow` | `orderRQList`: `offerId`, `productTransactionId`, `billToAccountNbr`, `billToPostalCode`, `billToType: SENDER`, `billToCountryCode` | `response.shipmentOrderResponse.order`: `orderId` (WWEX's number, `NUD…`), `status: ORDERED`, and one `orderedItemList` entry per box: `orderedItemId`, the tracking number (`secondaryTxnIdList`, type `TRACKING_ID`), and `handlingUnitIdList` (which box it is). **No prices**: the cost is the rate that was booked. |
| Label, one box | `generateUPSReportsFromLabelFlow` | `productTransactionId`, `trackingId`, `labelSize: UPS_4X6`, `thermalType` (`null` = PDF, `"ZPL"` = ZPL) | `response.reportList[0].report.documentBinary`, base64. The PDF is one 6 × 4 in page with the label turned a quarter turn; the ZPL is one label, 812 dots wide. |
| Labels, all boxes | `documentDownloadFlow` | `docTypes: [UPS_LABEL_ONLY]`, `transactionType: SMALLPACK`, `referenceMap.PRODUCT_TRANSACTION_ID` | `response.fileContent`, base64: a Letter-size PDF, one label per page, for plain paper. Also a short-lived download link. |
| Void | `integratedCancelFlow` | `cancelRQList: [{productTransactionId}]` | `response.shipmentOrderCancelRS.orderCancelStatus: VOIDED`. Voids every box of the shipment. |
| Track | `searchShipmentsFlow` | `trackingInfoList: [one tracking number]`, `type: TRACKING_ID` | `response.orderedItemFulfillmentStatusList`: one entry per box of that shipment with `orderedItemId`, `status`, and `fulfillmentStatusList` (events, newest first: `code`, `eventDateTime`, `timeZoneOffset`). |
| List shipments | `searchIndexDocumentFlow` | `index: producttransaction`, product type, a date range, optional status | One document per box: tracking number, status, service, cost, references, `productTransactionId`, `orderedItemId`. |
| Check an address | `addressValidationFlow` | address, `productType: SMALLPACK` | Staging answers `UNKNOWN` for a real address, so this is unproven. |

UPS service codes: `GND` Ground, `3DS` 3 Day Select, `2DA` 2nd Day Air, `2DM` 2nd Day Air A.M., `1DP` Next Day Air
Saver, `1DA` Next Day Air, `1DM` Next Day Air Early. (International codes are in the collection; LabelShop ships
domestic.)

Tracking status codes, from the collection's own comment: `CREATED`, `PENDING_PICKUP`, `IN_TRANSIT`,
`OUT_FOR_DELIVERY`, `DELIVERED`, `EXCEPTION`, `VOID`.

## Staging checks (2026-11-24)

| # | Question | Answer |
|---|---|---|
| 1 | Do the credentials work? | Yes. Token in under a second; role `Customer_API_M2M`, plan `small`. |
| 2 | What does a rate request return? | Seven UPS services for a domestic box, each with price, charge breakdown, transit days, delivery date and billed weight. A 12 × 10 × 8 in, 5 lb box bills at 7 lb (dimensional). Without dimensions the request is accepted and bills the actual weight. |
| 3 | Is the UPS account number needed up front? | No. Every offer names the account and postal code to bill (`billToInfoList`), and those are what the booking takes. |
| 4 | Several boxes on one shipment? | One request, one booking, one void. Each box gets its own price in the offer and its own tracking number and label in the booking. A box is matched to its tracking number by `handlingUnitId`, which is the same in every offer of the request. |
| 5 | Can a label be bought twice by accident? | No. A second booking of the same request answers `200` with `response.message: "Shipment already has order placed"` and no order; booking another service of the same request answers `422` "no offers found". **A `200` is not proof of a booking: the order has to be in the answer.** |
| 6 | Label formats? | 4 × 6 PDF and ZPL per box, and a Letter PDF of all boxes. References 1 and 2 print at the foot of the label. |
| 7 | Void? | `VOIDED` on the first call. A second void of the same shipment answers `200` in another shape (`isPtrAlreadyVoided: true`, `shipmentPTRFinalStatus: VOID`). Voiding a request that was never booked answers `500`. A voided shipment's label can still be downloaded, so LabelShop has to refuse the reprint itself. |
| 8 | Tracking? | One call returns every box of the shipment. Asking for several tracking numbers at once returns only the first shipment, so it is one call per shipment. An unknown number answers `200` with `response.message: "No shipments found for TRACKING_ID"`. Only `TRACKING_ID` is accepted as the search type. |
| 9 | What do refusals look like? | `422` with `clientStatus.message` ("Weight of the package is more than 150; exception: AppException") and `fieldList`. Company name and street are required on both addresses; a destination phone and contact are not required to rate. An impossible postal code answers `500` ("action did not complete normally…"). |
| 10 | Residential, declared value, dates? | The residential flag adds `HRS` ($6.50 on Ground in staging). A declared value of $500 adds `DVS` ($4); $100 adds nothing. A ship date in the past or the future is accepted. |
| 11 | How fast? | Rate about 0.4 s, book 0.4 to 2.7 s, label under 1 s, track and void about 0.3 s. |

The scripts and every saved answer are outside the repo, in the project folder:
`~/Documents/LabelShop/smoke/2026-11-24-wwex-spike/` (`wwex.py` refuses any host but the two staging ones). Four
sample labels were booked there and all four are voided (`verify_voided.py`).

## How LabelShop uses it (as built, U2 to U7)

### Where the code is

| Piece | Where | What it does |
|---|---|---|
| Request bodies, answer readers | `LabelsMis.Domain/Carriers/Wwex/` (`WwexRequests`, `WwexAnswers`) | Pure functions, tested against the staging answers saved in `tests/LabelsMis.Domain.Tests/Carriers/Fixtures/`. An answer in a shape that was not expected throws `CarrierException`; nothing is half-read. |
| The seam | `LabelsMis.Domain/Carriers/ICarrierClient.cs` | Sign in, rates, book, label, void, track. One implementation. Nothing above it names WWEX. |
| The client | `LabelsMis.Infrastructure/Carriers/` (`WwexCarrierClient`, `WwexTokenCache`, `WwexOptions`) | HTTP, the 24-hour token, a 30 second limit per call. |
| The one door | `LabelsMis.Web/Services/Carriers/CarrierGateway.cs` | Every call goes through it: the saved keys, the staging/production check, and one `CarrierLog` row per attempt. |
| Labels on shipments | `LabelsMis.Web/Services/Carriers/CarrierShipmentService.cs` | Rates, buy, settle an unanswered purchase, print, void, tracking. |
| Background tracking | `LabelsMis.Web/Background/ShipmentTrackingPoller.cs` | See Tracking below. |
| Pages | Setup → Shipping carrier (`/settings/carrier`, administrators), Record shipment, the shipment page | |

Three migrations: `AddCarrierSettingsAndLog` (two new tables), `AddShippingMethodCarrier` (one nullable column, below) and `AddCarrierLabels` (nullable or defaulted columns on
`Shipment`, `ShipmentPackage`, `TrackingEvent`, `CarrierSettings`, and two partial unique indexes). All are additive.

### Keys and environments

- The keys are saved in Setup → Shipping carrier, not in app settings. The client secret is encrypted with the app's
  secret protector (`Secrets__Key`); only its last four characters are kept readable.
- The page says whether the keys are staging or production keys. Each shipment remembers which kind bought its
  label (`Shipment.CarrierEnvironment`), and a label is only ever printed, voided or tracked with keys of that kind.
  Switching the shop to production keys therefore leaves the staging samples readable and unreachable, which is
  what is wanted.
- With no keys saved nothing changes for the shop: Record shipment is the typed-tracking form alone, and no
  background work calls anyone.

### Which price, and what the customer is billed

- The carrier cost is `totalOfferPrice` of the offer that was bought: the sum of its `chargeItemList[].customerPrice`
  lines (freight, fuel, surcharges). It is the price WWEX quotes the account that signed in. Every offer names its
  contract as `primaryVendorContractType: VENDOR_CSP_CONTRACT`, and the answer carries no UPS list or retail price.
  (`traMinCharge` / `traMaxCharge` are a band of about 1.08 to 1.54 times the price; LabelShop does not read them.)
- Staging signs in as WWEX's shared test account, so those prices are not Hey Lucky's. What Hey Lucky's production
  keys return is checked on go-live day against a WWEX invoice line (step 5 of Going live).
- Billed shipping = carrier cost × `GeneralSettings.CarrierCostMultiplier` (default 1.5), rounded to cents, for
  every shipment on a carrier method or no method, bought or typed. The multiplier is edited on Setup → Shipping
  carrier (it is still stored on `GeneralSettings`; Settings → General shows it and links there).

### Which orders start on a label

`ShippingMethod.Carrier` (nullable `Carrier` enum, carrier methods only) says who ships a method's orders, and
`RecordShipmentDefaults` turns it into where Record shipment starts: label mode for UPS or "not said" (when keys
are saved), typed tracking with that carrier selected for FedEx, USPS, DHL and Other. Either can be switched on the
page. Migration `AddShippingMethodCarrier` adds the column and sets it once from each carrier method's name
(`FedEx…` → FedEx, `UPS…` → UPS, `USPS…`, `DHL…`; anything else stays not said). After that it is a setting on the
method, never read off the name. Setup → Shipping carrier lists the carrier methods and where each one starts.

### Buying a label

1. **Get rates** sends the boxes to `shopFlow` and shows every UPS service with the carrier cost, the arrival date
   and the billed shipping (cost × the multiplier in Setup → Shipping carrier).
2. The rates are sealed with the app's secret key and carried by the page. **Buy label** trusts only that sealed
   copy for the price, and refuses it when the address, the ship date or a box changed since, when it is more than
   30 minutes old, or when the keys changed between staging and production.
3. The shipment is saved as pending **before** WWEX is asked to book.
   - Booked: the shipment is recorded through the same path as a hand-entered one (jobs to Shipped, the order
     rolled up, the draft invoice's shipping updated), with the tracking numbers and the carrier cost filled in.
   - Refused: the pending shipment is removed and the reason is shown.
   - No answer (timeout, `5xx`): it is kept as **Label not confirmed**. Nothing counts as shipped and no other
     label can be bought for the order. **Check with carrier** asks for the same booking again: booked now means
     recorded; "already has order placed" means the first answer was lost, and since the tracking numbers went with
     it, that order is voided at WWEX and the shipment removed.
4. A WWEX request is booked once: unique index on `Shipment.CarrierQuoteId`.

### Printing

4 × 6 PDF and ZPL per package, Letter PDF for all packages. The shop's usual format is fetched when the label is
bought and kept in file storage under `carrier-labels/<shipment id>/` (Spaces in production, like artwork; the local
disk in development). Any other format is fetched once, the first time it is printed, and kept. Reprinting never
asks WWEX to buy anything.

### Voiding

- **Void label** (a label bought here) asks WWEX first and touches the shipment only once WWEX has confirmed. An
  unconfirmed void changes nothing and can be repeated; WWEX answers a repeat with "already voided", which counts.
- **Void shipment** (recorded by hand) calls no one.
- The shipment becomes `Voided` (`ShipmentStatus` 5) and stays on file. Its jobs go back to ready to ship unless
  another shipment still carries their line, an order that is no longer fully shipped goes back to In production,
  and the draft invoice's shipping is derived again, in one transaction.
- Refused, before WWEX is asked, while the order's invoice is in QuickBooks, on its way there, or sent to the
  customer.
- The stored label files are removed and a voided label is never served again, although WWEX would still send it.
- Every "shipped" count in the app leaves out voided shipments.

### Tracking

- **Check tracking now** on the shipment page makes one `searchShipmentsFlow` call, which covers every box. Each
  box keeps its status; each event is stored once (unique index on box, WWEX's code and its time); the shipment
  turns Delivered once every box is.
- WWEX's tracking runs behind its booking: a label bought seconds ago is "not found" for about ten seconds on
  staging. That answer changes nothing and says so.
- A label voided in SpeedShip rather than in LabelShop is **flagged, not reversed**: a banner on the shipment asks
  for Void label, which then completes on WWEX's "already voided".
- A status word LabelShop doesn't know is kept and shown as WWEX sent it.
- `ShipmentTrackingPoller` asks in the background every 30 minutes, about real labels only: bought with production
  keys, still in transit, at least 30 minutes old, not asked about in the last 4 hours, shipped within the last 30
  days. At most 25 shipments a pass, one call a second, and the pass stops at the first sign WWEX is down. An
  answer with nothing new writes no log row. Staging samples never move, so they are never polled. The switch is
  in Setup → Shipping carrier.

### The guide and its walkthroughs

Setup → Documentation has a **UPS labels** section (`#carrier-labels`): how it works, and two walkthroughs that
run on the real pages.

- `wwwroot/js/walkthrough.js` is the engine: a card that points at one element at a time, follows the person from
  page to page (its place is kept in `sessionStorage`, so one browser tab), and leaves the page usable. It is
  loaded on every page; it does nothing until a walkthrough is started. The header comment lists the step fields.
- `wwwroot/js/walkthroughs.js` holds the two walkthroughs, `carrier-setup` (administrators, Setup → Shipping
  carrier) and `carrier-usage` (Production → Shipping → Record shipment → the shipment page). Steps point at
  element ids on those pages: **renaming an id there means changing it here**. A step whose element is missing
  shows its fallback text, so a rename degrades the walkthrough without breaking the page.
- Neither walkthrough presses anything for the person. The shipping one never buys a label: Next on the Buy step
  opens the newest shipment that already has one (`CarrierShipmentService.GetNewestLabelShipmentIdAsync`, asked
  through `/setup/documentation?handler=WalkthroughTargets`), and says so when there is none.
- Who is offered which: the setup walkthrough to administrators; the shipping one to Shipping and administrators
  once keys are saved. Everyone else sees the buttons greyed out with the reason.
- A walkthrough for another feature is one more `register(...)` call and a button with
  `data-walkthrough-start="<id>"`.

### How it was checked

- Unit and integration tests: Domain (parsers against the saved staging answers, shipment rules), Web
  (`Carriers/` : settings, purchase, void, tracking and the poller, against a scripted fake carrier and a real
  PostgreSQL database).
- Through the real pages against WWEX staging, with scripts kept outside the repo in
  `~/Documents/LabelShop/smoke/2026-11-24-carrier-labels/`: `s3_setup.py`, `s4_buy.py`, `s6_void.py`,
  `s7_track.py`. Every sample label they bought is voided, except the one two-box sample kept for printing checks.
- The walkthroughs, in a real browser: `~/Documents/LabelShop/smoke/2026-11-25-walkthroughs/` (`w1_tours.py`,
  `w2_no_keys.py`).

## Not proven

- **Hey Lucky's own rates.** Staging signs in as WWEX's test account. Rating a shipment the shop has already been
  billed for, and comparing it with that WWEX invoice line, needs production credentials.
- **The production addresses** (see Environments).
- **Tracking beyond `CREATED` and `VOID`.** The other codes are documented by WWEX, not observed; where an event's
  location or description would appear is unknown.
- **Address validation** (staging answers `UNKNOWN`).
- **How long WWEX keeps a label downloadable**, and until when a label can be voided. LabelShop stores the label
  files itself for that reason.
- **Rate limits.** None were hit and none are documented.

## Going live

1. Deploy. With no keys saved, production behaves as it did, apart from the wording changes and the void buttons.
2. Tell WWEX Integration Support that development is complete; they issue production keys.
3. In Setup → Shipping methods, set the **Carrier** of the method the shop ships UPS on to UPS (the seeded method is
   named FedEx and is set to FedEx by the migration; rename it too). Until then orders on it open Record shipment on
   typed tracking numbers, as they do today.
4. An administrator saves them in Setup → Shipping carrier with **Keys are for: Production**, sets the ship-from
   contact and phone, and presses **Test connection**. That is the first time the production addresses are used:
   it has to sign in, return rates, and show Hey Lucky's own UPS account.
5. Rate a shipment the shop has already been billed for and compare it with that WWEX invoice line.
6. Buy one real label for a real order, print it in the shop's format, and watch it track to Delivered.
