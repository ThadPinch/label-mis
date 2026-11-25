// The walkthroughs the guide offers (Setup → Documentation → UPS labels). The engine and the
// meaning of each step field are in walkthrough.js. The text here is the shop's manual in small
// pieces: keep it in step with Pages/Setup/Documentation.cshtml.
(function () {
    'use strict';

    const tours = window.LabelShopWalkthroughs;
    if (!tours) {
        return;
    }

    const guide = '/setup/documentation#carrier-labels';
    const lookupUrl = '/setup/documentation?handler=WalkthroughTargets';

    // ------------------------------------------------------------------ setting the carrier up

    const setupPage = '/settings/carrier';

    tours.register('carrier-setup', {
        name: 'Set up the shipping carrier',
        finishUrl: guide,
        lookupUrl: lookupUrl,
        steps: [
            {
                page: setupPage,
                url: setupPage,
                target: '#carrier-connection',
                title: 'Connect LabelShop to Worldwide Express',
                body: '<p>This page connects LabelShop to the shop\'s Worldwide Express account, so that Record shipment '
                    + 'can get UPS rates and buy labels.</p>'
                    + '<p>There are four parts: the keys, a test, the ship-from details and the defaults. '
                    + '<strong>Nothing on this page buys anything.</strong></p>'
                    + '<p>The page stays usable while the walkthrough is open: fill things in as you go.</p>'
            },
            {
                page: setupPage,
                target: '#carrier-keys',
                title: '1. Save the keys',
                body: '<p>Worldwide Express Integration Support issues a <strong>Client ID</strong> and a '
                    + '<strong>Client secret</strong>. Paste both here.</p>'
                    + '<p><strong>Keys are for</strong> has to match the keys you were given:</p>'
                    + '<ul><li><strong>Staging</strong> keys make sample labels: they can\'t ship and nothing is billed.</li>'
                    + '<li><strong>Production</strong> keys buy real labels, billed to the shop.</li></ul>'
                    + '<p>Press <strong>Save</strong>. The secret is stored encrypted and is never shown again: '
                    + 'afterwards the page shows its last four characters only.</p>',
                alt: [{
                    when: '#carrier-key-missing',
                    target: '#carrier-key-missing',
                    title: '1. The keys can\'t be saved yet',
                    body: '<p>LabelShop stores the client secret encrypted, and this server has no encryption key yet. '
                        + 'That is a one-time server setting (the yellow box says which one). Once it is there, the '
                        + 'keys can be saved here.</p>'
                }]
            },
            {
                page: setupPage,
                target: ['#carrier-test', '#carrier-connection'],
                title: '2. Test the connection',
                body: '<p><strong>Test connection</strong> signs in to Worldwide Express and asks for the rate of a 1 lb box '
                    + 'sent from the shop. If that works, everything a label needs works.</p>'
                    + '<p>The answer appears just above the button, with the <strong>UPS account the labels are billed to</strong>. '
                    + 'Check that it is the shop\'s account.</p>'
                    + '<p>Test again after any change to the keys.</p>',
                alt: [{
                    whenNot: '#carrier-test',
                    target: '#carrier-connection',
                    body: '<p>Once keys are saved, a <strong>Test connection</strong> button appears here. It signs in to '
                        + 'Worldwide Express and asks for the rate of a 1 lb box sent from the shop: if that works, '
                        + 'everything a label needs works.</p>'
                        + '<p>The answer names the <strong>UPS account the labels are billed to</strong>. Check that it is the shop\'s.</p>'
                }]
            },
            {
                page: setupPage,
                target: '#carrier-ship-from',
                title: '3. Ship-from details',
                body: '<p>Labels are printed with the shop\'s address, a contact name and a phone number.</p>'
                    + '<ul><li>The <strong>address</strong> is the company address in Settings → General. Change it there.</li>'
                    + '<li>The <strong>contact</strong> and <strong>phone</strong> are set here. A label needs a 10-digit phone number.</li></ul>'
                    + '<p>Anything missing is listed in a yellow box in this section, and labels can\'t be bought until it is fixed.</p>'
            },
            {
                page: setupPage,
                target: '#carrier-defaults',
                title: '4. Defaults for Record shipment',
                body: '<ul><li><strong>Service selected first</strong>: the UPS service that is already ticked when rates come back. Shipping can pick another each time.</li>'
                    + '<li><strong>Label file to print from</strong>: 4 × 6 PDF or ZPL for a label printer, Letter for an office printer. '
                    + 'It decides which Print button is highlighted; the others stay available.</li>'
                    + '<li><strong>Check tracking in the background</strong>: LabelShop asks where real labels are every few hours and turns shipments Delivered by itself.</li>'
                    + '<li><strong>Usual boxes</strong>: name the boxes the shop ships in. Shipping then picks a box instead of typing three sizes, and rates match what UPS bills.</li></ul>'
                    + '<p>Press <strong>Save defaults</strong> after changing any of them.</p>'
            },
            {
                page: setupPage,
                target: '#carrier-log',
                title: 'Carrier activity',
                body: '<p>Every call LabelShop makes to Worldwide Express is listed here: who did it, what it was, how it went, '
                    + 'and Worldwide Express\'s own <strong>call ID</strong>.</p>'
                    + '<p>When something goes wrong with a label, this is the first place to look, and the call ID is what '
                    + 'Worldwide Express support asks for.</p>'
            },
            {
                page: setupPage,
                title: 'That is the setup',
                body: '<p>With keys saved and the test passing, Record shipment shows <strong>Buy a UPS label</strong>.</p>'
                    + '<p><strong>Going live:</strong> start with staging keys and try a few sample labels. When Worldwide Express '
                    + 'issues production keys, come back here, set <strong>Keys are for</strong> to Production, save them, and '
                    + 'press Test connection once more. Sample labels bought before the switch stay on file and can\'t be used.</p>'
                    + '<p>The second walkthrough shows shipping an order.</p>',
                actions: [{ label: 'Ship an order', start: 'carrier-usage' }]
            }
        ]
    });

    // ------------------------------------------------------------------------ shipping an order

    const recordPage = '/shipments/record';
    const shipmentPage = /^\/shipments\/[0-9a-f-]{36}$/;
    const recordAddress = () => {
        const order = document.querySelector('#record-form [name="SalesOrderId"]');
        return order && order.value ? recordPage + '?salesOrderId=' + encodeURIComponent(order.value) : null;
    };
    const noLabelYet = '<p>No shipment in LabelShop has a UPS label yet, so there is no shipment page to show.</p>'
        + '<p>After a label is bought, its shipment page is where it is printed, tracked and voided. '
        + 'The guide describes each of those under <strong>UPS labels</strong>.</p>';

    tours.register('carrier-usage', {
        name: 'Ship an order with a UPS label',
        finishUrl: guide,
        lookupUrl: lookupUrl,
        steps: [
            {
                page: '/production/shipping',
                url: '/production/shipping',
                target: ['#shipping-ready a[href*="/shipments/record"]', '#shipping-ready', '#shipping-empty'],
                title: 'Start from Production → Shipping',
                body: '<p>Every order with items finished and ready to ship is listed here.</p>'
                    + '<p>Press <strong>Record shipment</strong> on the order you are shipping. '
                    + '<strong>Next</strong> opens the first one in the list, to look around: nothing is saved or bought until you say so.</p>',
                alt: [{
                    when: '#shipping-empty',
                    body: '<p>Orders with items finished and ready to ship are listed here, each with a '
                        + '<strong>Record shipment</strong> button. Nothing is ready to ship right now.</p>'
                }]
            },
            {
                page: recordPage,
                link: '#shipping-ready a[href*="/shipments/record"]',
                here: recordAddress,
                target: '#ship-mode',
                title: 'Two ways to record a shipment',
                body: '<ul><li><strong>Buy a UPS label</strong>: LabelShop gets the rates, buys the label and fills in the tracking numbers and the cost.</li>'
                    + '<li><strong>I have the tracking numbers</strong>: for a label made somewhere else. You type the numbers in, as before.</li></ul>'
                    + '<p>Leave <strong>Buy a UPS label</strong> selected. A yellow badge beside it means the shop is on staging keys and labels are samples.</p>',
                missing: '<p>This page would offer <strong>Buy a UPS label</strong> here, but the shipping carrier isn\'t connected yet, '
                    + 'so shipments are recorded with typed tracking numbers. An administrator connects it in Setup → Shipping carrier.</p>',
                unavailableTitle: 'No order is ready to ship',
                unavailable: '<p>Record shipment opens from an order in this list, and nothing is ready to ship right now, so the walkthrough '
                    + 'can\'t open it.</p><p>On that page you enter the boxes, press <strong>Get rates</strong>, pick a UPS service and press '
                    + '<strong>Buy label</strong>.</p><p><strong>Continue</strong> goes on to what happens after a label is bought.</p>'
            },
            {
                page: recordPage,
                here: recordAddress,
                target: '#record-ship-to',
                title: 'Check where it is going',
                body: '<p>The address comes from the order. UPS prints exactly what is here, so fix it now if it is wrong.</p>'
                    + '<ul><li><strong>Contact</strong> and <strong>Phone</strong> are required for a label.</li>'
                    + '<li>Tick <strong>A home, not a business</strong> when it is one: UPS charges more for homes, and the rate includes it only when this is ticked.</li></ul>'
            },
            {
                page: recordPage,
                here: recordAddress,
                target: ['#record-items', '#record-items-title'],
                title: 'What is in this shipment',
                body: '<p>Everything that is ready is ticked. Untick an item, or lower its quantity, to ship part of the order now: '
                    + 'the rest stays open for another shipment.</p>'
            },
            {
                page: recordPage,
                here: recordAddress,
                target: '#package-rows',
                title: 'One row per box',
                body: '<p>Enter the <strong>weight</strong> of each box and its <strong>size</strong>, or pick one of the shop\'s usual boxes '
                    + 'to fill the size in. <strong>+ Add package</strong> adds a box.</p>'
                    + '<p>UPS bills by size as well as weight. A box with no size is rated on weight alone, and the bill can come in higher.</p>'
            },
            {
                page: recordPage,
                here: recordAddress,
                target: ['#get-rates', '#label-card'],
                doneWhen: '#rates-table',
                title: 'Get rates',
                body: '<p>Press <strong>Get rates</strong>. LabelShop sends the address and the boxes to Worldwide Express and lists every UPS service.</p>'
                    + '<p><strong>Nothing is bought.</strong> You can change a box and get rates again as often as you like.</p>',
                missing: '<p>With <strong>Buy a UPS label</strong> selected at the top of the page, a <strong>Get rates</strong> button is here. '
                    + 'It prices the boxes on every UPS service and buys nothing.</p>'
            },
            {
                page: recordPage,
                here: recordAddress,
                target: ['#rates-table', '#label-card'],
                title: 'Pick a service',
                body: '<p>Each row is a UPS service with:</p>'
                    + '<ul><li><strong>Arrives</strong>: the day UPS expects to deliver.</li>'
                    + '<li><strong>Carrier cost</strong>: what the shop pays.</li>'
                    + '<li><strong>Billed shipping</strong>: what goes on the customer\'s invoice.</li></ul>'
                    + '<p>Tick the one you want. If you change the address, the date or a box after this, LabelShop asks you to get rates again before buying.</p>',
                alt: [{
                    whenNot: '#rates-table',
                    target: '#label-card',
                    body: '<p>After <strong>Get rates</strong>, the UPS services are listed here, each with the day it arrives, the '
                        + '<strong>carrier cost</strong> (what the shop pays) and the <strong>billed shipping</strong> (what goes on the customer\'s invoice).</p>'
                        + '<p>Press Get rates to see them, or Next to carry on.</p>'
                }]
            },
            {
                page: recordPage,
                here: recordAddress,
                target: ['#buy-label', '#label-card'],
                title: 'Buy the label',
                body: '<p><strong>Buy label</strong> buys the service you ticked, at the price on the screen. '
                    + 'With production keys this is a real purchase, billed to the shop. With staging keys it is a sample.</p>'
                    + '<p>The shipment is then recorded for you: tracking numbers and carrier cost filled in, the jobs marked Shipped, '
                    + 'the shipping put on the invoice.</p>'
                    + '<p><strong>You don\'t have to buy one to carry on.</strong> Next shows a shipment whose label was bought earlier.</p>',
                alt: [{
                    whenNot: '#buy-label',
                    target: '#label-card',
                    body: '<p>Once rates are listed, a <strong>Buy label</strong> button appears here. It buys the service you ticked, at the '
                        + 'price on the screen: a real purchase with production keys, a sample with staging keys.</p>'
                        + '<p>The shipment is then recorded for you: tracking numbers and carrier cost filled in, the jobs marked Shipped, '
                        + 'the shipping put on the invoice.</p>'
                        + '<p><strong>You don\'t have to buy one to carry on.</strong> Next shows a shipment whose label was bought earlier.</p>'
                }]
            },
            {
                page: shipmentPage,
                lookup: 'labelShipment',
                target: ['#shipment-label', '#shipment-voided'],
                title: 'The shipment page',
                body: '<p>Buying a label lands here: the shipment, with its UPS service, the Worldwide Express order number and the '
                    + 'UPS account it was billed to.</p>'
                    + '<p>Everything that happens to a label afterwards happens on this page. It is under '
                    + '<strong>Shipping → all shipments</strong> whenever you need it again.</p>',
                missing: '<p>This shipment was recorded with typed tracking numbers, so it has no UPS label section. '
                    + 'A shipment with a label bought in LabelShop shows the label here, with buttons to print it.</p>',
                alt: [{
                    when: '#label-unconfirmed',
                    target: '#label-unconfirmed',
                    title: 'Worldwide Express didn\'t answer',
                    body: '<p>The purchase was sent and no answer came back, so LabelShop doesn\'t know whether the label was bought. '
                        + 'Nothing counts as shipped yet.</p>'
                        + '<p>Press <strong>Check with carrier</strong>. It either completes the purchase or clears it, and it never leaves '
                        + 'a label that is paid for and not recorded.</p>'
                }],
                unavailableTitle: 'No label has been bought yet',
                unavailable: noLabelYet
            },
            {
                page: shipmentPage,
                target: ['#label-print', '#shipment-label'],
                title: 'Print the label',
                body: '<p><strong>Print on Letter paper</strong> prints every box\'s label on an office printer. For a label printer, '
                    + 'each box has its own <strong>Print 4 × 6</strong> and <strong>ZPL</strong> button in the Packages table below.</p>'
                    + '<p>The shop\'s usual format is the solid button.</p>'
                    + '<p><strong>Printing again never buys again.</strong> A jammed or lost label is simply printed once more.</p>',
                missing: '<p>The Print buttons are here while a label is live. A voided label can\'t be printed.</p>'
            },
            {
                page: shipmentPage,
                target: ['#label-tracking', '#shipment-packages'],
                title: 'Tracking',
                body: '<p><strong>Check tracking now</strong> asks Worldwide Express where the boxes are. Each box gets a status in the '
                    + 'Packages table, and every scan is listed under Tracking history.</p>'
                    + '<p>For real labels LabelShop also asks by itself every few hours, and turns the shipment '
                    + '<strong>Delivered</strong> once every box has arrived.</p>'
                    + '<p>A label bought a minute ago may not be listed yet. Try again a little later.</p>',
                missing: '<p>A shipment with a live label has a Tracking line here, with a <strong>Check tracking now</strong> button.</p>'
            },
            {
                page: shipmentPage,
                target: '#shipment-billing',
                title: 'What the customer is billed',
                body: '<p>The <strong>carrier cost</strong> came from the rate the label was bought at, so nobody types it in. '
                    + '<strong>Billed shipping</strong> is worked out from it by the order\'s shipping method (the rule is on the last line) '
                    + 'and goes on the order\'s invoice.</p>'
                    + '<p>If the Worldwide Express bill later differs, correct the carrier cost here.</p>'
            },
            {
                page: shipmentPage,
                target: ['#shipment-void', '#shipment-voided'],
                title: 'Voiding a label',
                body: '<p>Bought by mistake, or the order isn\'t going out after all? <strong>Void label</strong> voids it with '
                    + 'Worldwide Express, so it isn\'t billed, and takes the shipment back: the items are ready to ship again and the '
                    + 'shipping comes off the invoice.</p>'
                    + '<p>Do it <strong>before the box leaves the building</strong>.</p>'
                    + '<p>Always void here, not on speedship.com: LabelShop only takes a shipment back when it is voided from this page.</p>',
                alt: [
                    {
                        when: '#void-blocked',
                        target: '#shipment-void',
                        body: '<p><strong>Void label</strong> voids a label with Worldwide Express and takes the shipment back. '
                            + 'It is held for this shipment because its invoice has already gone out: the message here says which invoice. '
                            + 'Accounting voids that invoice first.</p>'
                            + '<p>Normally you void <strong>before the box leaves the building</strong>, and always here, not on speedship.com.</p>'
                    },
                    {
                        when: '#shipment-voided',
                        target: '#shipment-voided',
                        body: '<p>This shipment was voided: it stays on file with who voided it, when and why, and no longer counts as shipped.</p>'
                            + '<p>On a live shipment, <strong>Void label</strong> is at the bottom of the page. Use it before the box leaves '
                            + 'the building, and always here, not on speedship.com.</p>'
                    }
                ],
                missing: '<p>While a shipment is in transit, <strong>Void label</strong> is at the bottom of this page for Shipping and '
                    + 'administrators. It voids the label with Worldwide Express and takes the shipment back.</p>'
            },
            {
                page: shipmentPage,
                title: 'That is the whole trip',
                body: '<p>Record shipment → boxes → Get rates → Buy label → print. Tracking and the invoice look after themselves.</p>'
                    + '<p>The guide has all of this in writing under <strong>UPS labels</strong>, and this walkthrough can be run again from there at any time.</p>'
            }
        ]
    });
})();
