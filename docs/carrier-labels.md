# Carrier labels: UPS through Worldwide Express (SpeedShip API)

The shop's UPS account is resold by Worldwide Express (WWEX). Labels, rates, voids and tracking for it go through
WWEX's own **SpeedShip API**, not through UPS's developer API: it is the one that returns the WWEX price, which is
what billed shipping is derived from (carrier cost × the multiplier in Settings → General).

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
