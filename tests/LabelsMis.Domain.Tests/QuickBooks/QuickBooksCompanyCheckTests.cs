using LabelsMis.Domain.QuickBooks;

namespace LabelsMis.Domain.Tests.QuickBooks;

public class QuickBooksCompanyCheckTests
{
    private static string Preferences(
        bool customNumbers = true, bool classPerLine = true, bool classPerTxn = false,
        bool salesTax = true, bool automatedTax = true, string customFields = "") => $$"""
        {"Preferences":{
          "AccountingInfoPrefs":{"TrackDepartments":false,"ClassTrackingPerTxn":{{Json(classPerTxn)}},"ClassTrackingPerTxnLine":{{Json(classPerLine)}},"CustomerTerminology":"Customers"},
          "SalesFormsPrefs":{"CustomField":[{{customFields}}],"CustomTxnNumbers":{{Json(customNumbers)}},"AllowShipping":false},
          "TaxPrefs":{"UsingSalesTax":{{Json(salesTax)}},"PartnerTaxEnabled":{{Json(automatedTax)}}}
        },"time":"2026-11-10T12:00:00.000-08:00"}
        """;

    private const string PoFieldOn = """
        {"CustomField":[{"Name":"SalesFormsPrefs.UseSalesCustom1","Type":"BooleanType","BooleanValue":true},{"Name":"SalesFormsPrefs.UseSalesCustom2","Type":"BooleanType","BooleanValue":false},{"Name":"SalesFormsPrefs.UseSalesCustom3","Type":"BooleanType","BooleanValue":false}]},
        {"CustomField":[{"Name":"SalesFormsPrefs.SalesCustomName1","Type":"StringType","StringValue":"P.O. Number"}]}
        """;

    private static string Json(bool value) => value ? "true" : "false";

    [Fact]
    public void FromPreferences_ReadsTheSwitchesAnInvoicePushNeeds()
    {
        var check = QuickBooksCompanyCheck.FromPreferences(Preferences(customFields: PoFieldOn));

        check.Should().Satisfy<QuickBooksCompanyCheck>(c =>
        {
            c.CustomTransactionNumbers.Should().BeTrue();
            c.ClassTrackingPerLine.Should().BeTrue();
            c.ClassTrackingPerTransaction.Should().BeFalse();
            c.SalesTaxOn.Should().BeTrue();
            c.AutomatedSalesTax.Should().BeTrue();
            c.PoNumberField.Should().Be(new QuickBooksCustomFieldSlot("1", "P.O. Number", true));
            c.ReadyForInvoices.Should().BeTrue();
        });
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ReadyForInvoices_NeedsCustomNumbersAndClassPerLine(bool customNumbers, bool classPerLine)
    {
        var check = QuickBooksCompanyCheck.FromPreferences(
            Preferences(customNumbers: customNumbers, classPerLine: classPerLine, customFields: PoFieldOn));

        check.ReadyForInvoices.Should().BeFalse();
    }

    [Fact]
    public void ReadyForInvoices_DoesNotWaitOnThePoField()
    {
        // Preferences lists only legacy custom fields, so a missing one proves nothing.
        var check = QuickBooksCompanyCheck.FromPreferences(Preferences());

        check.PoNumberField.Should().BeNull();
        check.ReadyForInvoices.Should().BeTrue();
    }

    /// <summary>What the sandbox company really answered (trimmed to the sections the check reads).
    /// It has a "P.O. Number" invoice field made in the current Custom fields screen, and none of
    /// it shows: all three legacy fields are off and no names are listed.</summary>
    private const string SandboxCompanyAnswer = """
        {
          "Preferences": {
            "AccountingInfoPrefs": {
              "UseAccountNumbers": false,
              "TrackDepartments": false,
              "ClassTrackingPerTxn": false,
              "ClassTrackingPerTxnLine": true,
              "FirstMonthOfFiscalYear": "January",
              "TaxYearMonth": "January",
              "TaxForm": "6",
              "CustomerTerminology": "Customers"
            },
            "SalesFormsPrefs": {
              "CustomField": [
                {
                  "CustomField": [
                    {
                      "Name": "SalesFormsPrefs.UseSalesCustom3",
                      "Type": "BooleanType",
                      "BooleanValue": false
                    },
                    {
                      "Name": "SalesFormsPrefs.UseSalesCustom1",
                      "Type": "BooleanType",
                      "BooleanValue": false
                    },
                    {
                      "Name": "SalesFormsPrefs.UseSalesCustom2",
                      "Type": "BooleanType",
                      "BooleanValue": false
                    }
                  ]
                }
              ],
              "CustomTxnNumbers": true,
              "AllowShipping": false,
              "DefaultTerms": {
                "value": "3"
              }
            },
            "TaxPrefs": {
              "UsingSalesTax": true,
              "TaxGroupCodeRef": {
                "value": "2"
              }
            }
          }
        }
        """;

    [Fact]
    public void FromPreferences_WithTheSandboxCompanysRealAnswer_ReadsItsSwitches()
    {
        var check = QuickBooksCompanyCheck.FromPreferences(SandboxCompanyAnswer);

        check.Should().Satisfy<QuickBooksCompanyCheck>(c =>
        {
            c.CustomTransactionNumbers.Should().BeTrue();
            c.ClassTrackingPerLine.Should().BeTrue();
            c.ClassTrackingPerTransaction.Should().BeFalse();
            c.SalesTaxOn.Should().BeTrue();
            c.AutomatedSalesTax.Should().BeFalse("this company's TaxPrefs has no PartnerTaxEnabled");
            c.CustomFields.Should().BeEmpty();
            c.PoNumberField.Should().BeNull();
            c.ReadyForInvoices.Should().BeTrue();
        });
    }

    [Fact]
    public void ClassTrackingPerTransaction_IsNotTheSameAsPerLine()
    {
        var check = QuickBooksCompanyCheck.FromPreferences(
            Preferences(classPerLine: false, classPerTxn: true, customFields: PoFieldOn));

        check.ClassTrackingPerLine.Should().BeFalse();
        check.ClassTrackingPerTransaction.Should().BeTrue();
        check.ReadyForInvoices.Should().BeFalse();
    }

    [Fact]
    public void PoNumberField_IgnoresAFieldThatIsSwitchedOff()
    {
        var fields = """
            {"CustomField":[{"Name":"SalesFormsPrefs.UseSalesCustom1","Type":"BooleanType","BooleanValue":false}]},
            {"CustomField":[{"Name":"SalesFormsPrefs.SalesCustomName1","Type":"StringType","StringValue":"P.O. Number"}]}
            """;

        var check = QuickBooksCompanyCheck.FromPreferences(Preferences(customFields: fields));

        check.PoNumberField.Should().BeNull();
        check.CustomFields.Should().ContainSingle().Which.Enabled.Should().BeFalse();
    }

    [Fact]
    public void PoNumberField_FindsItInTheSecondOrThirdSlot()
    {
        var fields = """
            {"CustomField":[{"Name":"SalesFormsPrefs.UseSalesCustom1","Type":"BooleanType","BooleanValue":true},{"Name":"SalesFormsPrefs.UseSalesCustom3","Type":"BooleanType","BooleanValue":true}]},
            {"CustomField":[{"Name":"SalesFormsPrefs.SalesCustomName1","Type":"StringType","StringValue":"Sales Rep"},{"Name":"SalesFormsPrefs.SalesCustomName3","Type":"StringType","StringValue":"PO #"}]}
            """;

        var check = QuickBooksCompanyCheck.FromPreferences(Preferences(customFields: fields));

        check.PoNumberField!.DefinitionId.Should().Be("3");
        check.CustomFields.Select(f => f.Name).Should().Equal("Sales Rep", "PO #");
    }

    [Fact]
    public void CustomField_WithANameButNoFlag_CountsAsInUse()
    {
        var fields = """{"CustomField":[{"Name":"SalesFormsPrefs.SalesCustomName2","Type":"StringType","StringValue":"Purchase Order"}]}""";

        var check = QuickBooksCompanyCheck.FromPreferences(Preferences(customFields: fields));

        check.PoNumberField.Should().Be(new QuickBooksCustomFieldSlot("2", "Purchase Order", true));
    }

    [Theory]
    [InlineData("P.O. Number", true)]
    [InlineData("PO Number", true)]
    [InlineData("PO #", true)]
    [InlineData("po no.", true)]
    [InlineData("PO", true)]
    [InlineData("Purchase Order", true)]
    [InlineData("Purchase Order Number", true)]
    [InlineData("Customer PO", true)]
    [InlineData("Sales Rep", false)]
    [InlineData("Postal code", false)]
    [InlineData("Crew #", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikePoNumber_MatchesTheUsualSpellingsOnly(string? name, bool expected)
    {
        QuickBooksCompanyCheck.LooksLikePoNumber(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"Preferences":{}}""")]
    [InlineData("""{"Preferences":{"SalesFormsPrefs":{"CustomField":"unexpected"}}}""")]
    public void FromPreferences_WithAnUnexpectedShape_ReadsEverythingAsOff(string json)
    {
        var check = QuickBooksCompanyCheck.FromPreferences(json);

        check.Should().Satisfy<QuickBooksCompanyCheck>(c =>
        {
            c.CustomTransactionNumbers.Should().BeFalse();
            c.ClassTrackingPerLine.Should().BeFalse();
            c.SalesTaxOn.Should().BeFalse();
            c.CustomFields.Should().BeEmpty();
            c.ReadyForInvoices.Should().BeFalse();
        });
    }

    [Fact]
    public void ToJson_ThenFromJson_GivesTheSameCheckBack()
    {
        var check = QuickBooksCompanyCheck.FromPreferences(Preferences(customFields: PoFieldOn));

        var json = check.ToJson();
        var back = QuickBooksCompanyCheck.FromJson(json);

        back.Should().BeEquivalentTo(check);
        back!.PoNumberField.Should().Be(new QuickBooksCustomFieldSlot("1", "P.O. Number", true));
        json.Should().NotContain("poNumberField").And.NotContain("readyForInvoices");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void FromJson_WithNothingUsable_IsNull(string? json)
    {
        QuickBooksCompanyCheck.FromJson(json).Should().BeNull();
    }
}
