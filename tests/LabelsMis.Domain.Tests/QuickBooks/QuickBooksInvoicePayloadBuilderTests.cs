using System.Text.Json;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Invoicing;
using LabelsMis.Domain.QuickBooks;

namespace LabelsMis.Domain.Tests.QuickBooks;

public class QuickBooksInvoicePayloadBuilderTests
{
    private static readonly Guid InvoiceId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid CustomerId = Guid.Parse("99999999-8888-7777-6666-555555555555");

    private static readonly QuickBooksCatalogLookup Catalog = new([
        new(QuickBooksCatalogKind.Item, "11", "Sales", "Sales", Taxable: true),
        new(QuickBooksCatalogKind.Item, "12", "Shipping", "Shipping", Taxable: false),
        new(QuickBooksCatalogKind.Item, "13", "Design", "Services:Design", Taxable: false),
        new(QuickBooksCatalogKind.Class, "21", "Roll Label", "Roll Label"),
        new(QuickBooksCatalogKind.Class, "22", "Buyout", "Buyout"),
        new(QuickBooksCatalogKind.Term, "31", "Net 30", "Net 30"),
        new(QuickBooksCatalogKind.Term, "32", "Net 15", "Net 15")
    ]);

    private static QuickBooksLineSource Line(int number, decimal qty, decimal rate, decimal amount, string item = "Sales", string cls = "Roll Label") =>
        new(number, item, cls, false, false, $"Line {number} labels", qty, rate, amount);

    private static QuickBooksInvoiceSource Source(
        IReadOnlyList<QuickBooksLineSource>? lines = null,
        decimal shipping = 35m,
        string? qbCustomerId = "58",
        string? billingEmail = "ap@example.com",
        string? po = "PO-7129",
        PaymentTerms terms = PaymentTerms.Net30,
        bool hold = false,
        string? sendTo = null,
        decimal? firstLineAmount = null) =>
        new(
            InvoiceId,
            "INV-1042",
            new DateOnly(2026, 11, 13),
            new DateOnly(2026, 12, 13),
            terms,
            CustomerId,
            "Acme Bottling",
            qbCustomerId,
            po,
            billingEmail,
            new QuickBooksAddressSource("Acme Bottling", "1 Main St", null, "Springfield", "IL", "62701", "US"),
            new QuickBooksAddressSource("Acme Dock 4", "9 River Rd", "Suite 2", "Springfield", "IL", "62702", null),
            lines ?? [Line(1, 1000, 0.50m, firstLineAmount ?? 500m)],
            "Shipping",
            "Roll Label",
            shipping,
            hold,
            sendTo);

    private static QuickBooksPushSettings Settings(
        QuickBooksSendMode mode = QuickBooksSendMode.SendLater, string? poField = "1000000001", string? net30 = "Net 30") =>
        new(net30 is null ? new Dictionary<PaymentTerms, string>() : new Dictionary<PaymentTerms, string> { [PaymentTerms.Net30] = net30 },
            poField, poField is null ? null : "P.O. Number", mode);

    private static (QuickBooksInvoicePayload Payload, JsonElement Root) BuildOk(QuickBooksInvoiceSource source, QuickBooksPushSettings? settings = null)
    {
        var result = QuickBooksInvoicePayloadBuilder.Build(source, Catalog, settings ?? Settings());
        result.Problems.Should().BeEmpty();
        result.Payload.Should().NotBeNull();
        return (result.Payload!, JsonDocument.Parse(result.Payload!.Json).RootElement.Clone());
    }

    private static JsonElement Detail(JsonElement root, int index) =>
        root.GetProperty("Line")[index].GetProperty("SalesItemLineDetail");

    [Fact]
    public void Build_RateThatDoesNotMultiplyOut_SendsQuantityAndAmountWithoutRate()
    {
        // Phase 0's case: 2,500 x 0.12345 is stored as rate 0.1235 with a line total of 308.625.
        var (payload, root) = BuildOk(Source([Line(1, 2500, 0.1235m, 308.625m)], shipping: 0m));

        var line = root.GetProperty("Line")[0];
        line.GetProperty("Amount").GetDecimal().Should().Be(308.63m);
        Detail(root, 0).GetProperty("Qty").GetDecimal().Should().Be(2500m);
        Detail(root, 0).TryGetProperty("UnitPrice", out _).Should().BeFalse("2,500 x 0.1235 = 308.75, not 308.63");
        payload.Total.Should().Be(308.63m);
    }

    [Fact]
    public void Build_RateThatMultipliesOut_SendsQuantityRateAndAmount()
    {
        var (_, root) = BuildOk(Source([Line(1, 1000, 0.50m, 500.0000m)]));

        Detail(root, 0).GetProperty("Qty").GetDecimal().Should().Be(1000m);
        Detail(root, 0).GetProperty("UnitPrice").GetDecimal().Should().Be(0.5m);
        root.GetProperty("Line")[0].GetProperty("Amount").GetRawText().Should().Be("500", "trailing zeros are dropped");
    }

    [Fact]
    public void Build_RateRule_RoundsHalfUpLikeQuickBooks()
    {
        // .NET rounds half to even by default, which would give 0.02 and 0.00 here.
        var (_, root) = BuildOk(Source([Line(1, 5, 0.0050m, 0.03m), Line(2, 5, 0.0010m, 0.01m)], shipping: 0m));

        Detail(root, 0).TryGetProperty("UnitPrice", out _).Should().BeTrue("5 x 0.005 = 0.025 rounds half up to 0.03");
        Detail(root, 1).TryGetProperty("UnitPrice", out _).Should().BeTrue("5 x 0.001 = 0.005 rounds half up to 0.01");
    }

    [Fact]
    public void Build_ShipmentBornInvoice_KeepsOrderedQuantityButBillsLabelShopsAmount()
    {
        // Ordered 1,000 at 0.50; this invoice bills the 950 that shipped.
        var (payload, root) = BuildOk(Source([Line(1, 1000, 0.50m, 475m)], shipping: 22.5m));

        Detail(root, 0).GetProperty("Qty").GetDecimal().Should().Be(1000m);
        Detail(root, 0).TryGetProperty("UnitPrice", out _).Should().BeFalse();
        root.GetProperty("Line")[0].GetProperty("Amount").GetDecimal().Should().Be(475m);
        payload.Total.Should().Be(497.5m, "QuickBooks gets LabelShop's lines plus shipping");
    }

    [Fact]
    public void Build_TotalIsLabelShopsLinesPlusShipping_AndNoTaxIsSent()
    {
        var (payload, root) = BuildOk(Source([Line(1, 1000, 0.50m, 500m), Line(2, 1, 75m, 75m, item: "Design", cls: "Buyout")], shipping: 35m));

        var lines = root.GetProperty("Line");
        lines.GetArrayLength().Should().Be(3);
        lines.EnumerateArray().Sum(l => l.GetProperty("Amount").GetDecimal()).Should().Be(610m);
        payload.Total.Should().Be(610m);
        root.TryGetProperty("TxnTaxDetail", out _).Should().BeFalse();
        payload.Json.Should().NotContain("TotalTax");
    }

    [Fact]
    public void Build_ShippingLine_UsesShippingItemAndClass_AndIsLeftOutWhenZero()
    {
        var (_, withShipping) = BuildOk(Source(shipping: 35m));
        var shipping = withShipping.GetProperty("Line")[1];
        shipping.GetProperty("Amount").GetDecimal().Should().Be(35m);
        shipping.GetProperty("SalesItemLineDetail").GetProperty("ItemRef").GetProperty("value").GetString().Should().Be("12");
        shipping.GetProperty("SalesItemLineDetail").GetProperty("ClassRef").GetProperty("value").GetString().Should().Be("21");

        var (_, without) = BuildOk(Source(shipping: 0m));
        without.GetProperty("Line").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void Build_ItemsAndClassesResolveToIds_AndTaxCodeFollowsTheItem()
    {
        var (_, root) = BuildOk(Source([Line(1, 1000, 0.50m, 500m), Line(2, 1, 75m, 75m, item: "Design", cls: "Buyout")]));

        Detail(root, 0).GetProperty("ItemRef").GetProperty("value").GetString().Should().Be("11");
        Detail(root, 0).GetProperty("ClassRef").GetProperty("value").GetString().Should().Be("21");
        Detail(root, 0).GetProperty("TaxCodeRef").GetProperty("value").GetString().Should().Be("TAX");
        Detail(root, 1).GetProperty("ItemRef").GetProperty("value").GetString().Should().Be("13", "found by its own name under a parent");
        Detail(root, 1).GetProperty("ClassRef").GetProperty("value").GetString().Should().Be("22");
        Detail(root, 1).GetProperty("TaxCodeRef").GetProperty("value").GetString().Should().Be("NON");
        Detail(root, 2).GetProperty("TaxCodeRef").GetProperty("value").GetString().Should().Be("NON", "the Shipping item isn't taxable");
    }

    [Fact]
    public void Build_HeaderFields()
    {
        var (_, root) = BuildOk(Source());

        root.GetProperty("DocNumber").GetString().Should().Be("INV-1042");
        root.GetProperty("TxnDate").GetString().Should().Be("2026-11-13");
        root.GetProperty("DueDate").GetString().Should().Be("2026-12-13");
        root.GetProperty("CustomerRef").GetProperty("value").GetString().Should().Be("58");
        root.GetProperty("BillEmail").GetProperty("Address").GetString().Should().Be("ap@example.com");
        root.GetProperty("BillAddr").GetProperty("Line1").GetString().Should().Be("Acme Bottling");
        root.GetProperty("BillAddr").GetProperty("Line2").GetString().Should().Be("1 Main St");
        root.GetProperty("BillAddr").GetProperty("CountrySubDivisionCode").GetString().Should().Be("IL");
        root.GetProperty("ShipAddr").GetProperty("Line3").GetString().Should().Be("Suite 2");
        root.GetProperty("ShipAddr").GetProperty("PostalCode").GetString().Should().Be("62702");
        root.GetProperty("ShipAddr").TryGetProperty("Country", out _).Should().BeFalse();
    }

    [Fact]
    public void Build_TermsMappedToAQuickBooksTerm_AreSent()
    {
        var (_, root) = BuildOk(Source(terms: PaymentTerms.Net30));

        root.GetProperty("SalesTermRef").GetProperty("value").GetString().Should().Be("31");
        root.GetProperty("DueDate").GetString().Should().Be("2026-12-13");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Net 45 (not in QuickBooks)")]
    public void Build_TermsNotMappedOrNotInQuickBooks_SendOnlyTheDueDate(string? mapped)
    {
        var (_, root) = BuildOk(Source(terms: PaymentTerms.Net30), Settings(net30: mapped));

        root.TryGetProperty("SalesTermRef", out _).Should().BeFalse();
        root.GetProperty("DueDate").GetString().Should().Be("2026-12-13");
    }

    [Fact]
    public void Build_PoNumber_GoesInTheCustomFieldWhenItsIdIsKnown()
    {
        var (_, root) = BuildOk(Source(po: " PO-7129 "));

        var field = root.GetProperty("CustomField")[0];
        field.GetProperty("DefinitionId").GetString().Should().Be("1000000001");
        field.GetProperty("Type").GetString().Should().Be("StringType");
        field.GetProperty("StringValue").GetString().Should().Be("PO-7129");
    }

    [Fact]
    public void Build_PoNumber_IsLeftOutWithoutAFieldIdOrWithoutAPo()
    {
        BuildOk(Source(), Settings(poField: null)).Root.TryGetProperty("CustomField", out _).Should().BeFalse();
        BuildOk(Source(po: " ")).Root.TryGetProperty("CustomField", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(QuickBooksSendMode.SendLater, false, "NeedToSend", false)]
    [InlineData(QuickBooksSendMode.Leave, false, "NotSet", false)]
    [InlineData(QuickBooksSendMode.SendNow, false, "NotSet", true)]
    [InlineData(QuickBooksSendMode.SendLater, true, "NotSet", false)]
    [InlineData(QuickBooksSendMode.SendNow, true, "NotSet", false)]
    public void Build_SendModeAndHold_SetEmailStatusAndSendNow(QuickBooksSendMode mode, bool hold, string emailStatus, bool emailNow)
    {
        var (payload, root) = BuildOk(Source(hold: hold), Settings(mode));

        root.GetProperty("EmailStatus").GetString().Should().Be(emailStatus);
        root.GetProperty("BillEmail").GetProperty("Address").GetString().Should().Be("ap@example.com", "BillEmail always goes");
        payload.EmailNow.Should().Be(emailNow);
    }

    [Fact]
    public void Build_SendToOverride_WinsOverTheBillingContact()
    {
        var (payload, root) = BuildOk(Source(sendTo: "boss@example.com"), Settings(QuickBooksSendMode.SendNow));

        root.GetProperty("BillEmail").GetProperty("Address").GetString().Should().Be("boss@example.com");
        payload.SendTo.Should().Be("boss@example.com");
        payload.BillEmail.Should().Be("boss@example.com");
    }

    [Fact]
    public void Build_UnlinkedCustomerAndNoEmail_AreProblemsWithTheCustomerPageAsFix()
    {
        var result = QuickBooksInvoicePayloadBuilder.Build(Source(qbCustomerId: " ", billingEmail: null), Catalog, Settings());

        result.Ok.Should().BeFalse();
        result.Problems.Select(p => p.Code).Should().Equal(QuickBooksErrorMessages.Customer, QuickBooksErrorMessages.Email);
        result.Problems.Should().OnlyContain(p => p.FixUrl == $"/customers/edit/{CustomerId}");
        result.Problems[0].Message.Should().Contain("Acme Bottling");
    }

    [Fact]
    public void Build_ItemOrClassNotInQuickBooks_PointsAtSetupOrAtThePreviewForAnOverride()
    {
        var lines = new[]
        {
            Line(1, 1000, 0.50m, 500m, item: "Labels (old)"),
            new QuickBooksLineSource(2, "Design", "Gone class", false, true, "Art", 1, 75m, 75m)
        };
        var result = QuickBooksInvoicePayloadBuilder.Build(Source(lines), Catalog, Settings());

        result.Problems.Should().HaveCount(2);
        result.Problems[0].Code.Should().Be(QuickBooksErrorMessages.Item);
        result.Problems[0].Message.Should().Contain("Line 1").And.Contain("Labels (old)");
        result.Problems[0].FixUrl.Should().Be("/settings/quickbooks");
        result.Problems[1].Code.Should().Be(QuickBooksErrorMessages.LineClass);
        result.Problems[1].FixUrl.Should().Be($"/invoices/{InvoiceId}/quickbooks");
    }

    [Fact]
    public void Build_ListsNeverRead_IsOneProblem()
    {
        var result = QuickBooksInvoicePayloadBuilder.Build(Source(), QuickBooksCatalogLookup.Empty, Settings());

        result.Problems.Should().ContainSingle().Which.Code.Should().Be(QuickBooksErrorMessages.Lists);
    }

    [Fact]
    public void Build_NumberLongerThanQuickBooksAllows_IsAProblem()
    {
        var result = QuickBooksInvoicePayloadBuilder.Build(Source() with { DocNumber = "INV-0123456789012345678" }, Catalog, Settings());

        result.Problems.Should().ContainSingle().Which.Code.Should().Be(QuickBooksErrorMessages.Number);
    }

    [Fact]
    public void RequestId_IsStableForTheSameInvoice_AndChangesWithThePayload()
    {
        var first = BuildOk(Source()).Payload;
        var again = BuildOk(Source()).Payload;
        var edited = BuildOk(Source(shipping: 42.5m)).Payload;

        var id = QuickBooksInvoicePayload.RequestId(InvoiceId, "create", first.Json);
        id.Should().Be(QuickBooksInvoicePayload.RequestId(InvoiceId, "create", again.Json));
        id.Should().HaveLength(QuickBooksInvoicePayload.MaxRequestIdLength).And.MatchRegex("^[0-9a-f]+$");
        QuickBooksInvoicePayload.RequestId(InvoiceId, "create", edited.Json).Should().NotBe(id);
        QuickBooksInvoicePayload.RequestId(Guid.NewGuid(), "create", first.Json).Should().NotBe(id);
        QuickBooksInvoicePayload.RequestId(InvoiceId, "update", first.Json).Should().NotBe(id);
        first.Hash.Should().Be(again.Hash).And.HaveLength(64).And.NotBe(edited.Hash);
    }

    [Fact]
    public void Hash_DoesNotDependOnTheDecimalScale()
    {
        BuildOk(Source(firstLineAmount: 500.0000m)).Payload.Hash.Should().Be(BuildOk(Source(firstLineAmount: 500m)).Payload.Hash);
    }

    [Fact]
    public void UpdateJson_IsSparseWithIdAndSyncToken_AndLeavesTheEmailStatusAlone()
    {
        var (payload, _) = BuildOk(Source());

        var update = JsonDocument.Parse(payload.UpdateJson("162", "3")).RootElement;
        update.GetProperty("Id").GetString().Should().Be("162");
        update.GetProperty("SyncToken").GetString().Should().Be("3");
        update.GetProperty("sparse").GetBoolean().Should().BeTrue();
        update.GetProperty("Line").GetArrayLength().Should().Be(2);
        update.TryGetProperty("EmailStatus", out _).Should().BeFalse();
        update.GetProperty("BillEmail").GetProperty("Address").GetString().Should().Be("ap@example.com");
        payload.Json.Should().Contain("EmailStatus", "the create body is untouched");
    }

    [Fact]
    public void VoidJson_CarriesIdAndSyncToken()
    {
        QuickBooksInvoicePayload.VoidJson("162", "1").Should().Be("""{"Id":"162","SyncToken":"1"}""");
    }
}
