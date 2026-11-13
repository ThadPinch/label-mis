using LabelsMis.Domain.QuickBooks;

namespace LabelsMis.Domain.Tests.QuickBooks;

/// <summary>Bodies are the sandbox's own answers from the Q8 checks (c9-create, c9-void, c3-lookup).</summary>
public class QuickBooksInvoiceAnswerTests
{
    private const string Created = """
        {
          "Invoice": {
            "AllowIPNPayment": false,
            "AllowOnlinePayment": true,
            "AllowOnlineCreditCardPayment": true,
            "AllowOnlineACHPayment": true,
            "AllowOnlinePayPalPayment": false,
            "AllowOnlineAffirmPayment": false,
            "domain": "QBO",
            "sparse": false,
            "Id": "162",
            "SyncToken": "0",
            "MetaData": {
              "CreateTime": "2026-11-10T22:09:04-08:00",
              "LastModifiedByRef": {
                "value": "9341458679203214"
              },
              "LastUpdatedTime": "2026-11-10T22:09:04-08:00"
            },
            "CustomField": [],
            "DocNumber": "LSQ8-7129-9A",
            "TxnDate": "2026-11-10",
            "CurrencyRef": {
              "value": "USD",
              "name": "United States Dollar"
            },
            "LinkedTxn": [],
            "Line": [
              {
                "Id": "1",
                "LineNum": 1,
                "Description": "before",
                "Amount": 100.0,
                "DetailType": "SalesItemLineDetail",
                "SalesItemLineDetail": {
                  "ItemRef": {
                    "value": "1",
                    "name": "Services"
                  },
                  "ItemAccountRef": {
                    "value": "1",
                    "name": "Services"
                  },
                  "TaxCodeRef": {
                    "value": "NON"
                  }
                },
                "CustomExtensions": []
              },
              {
                "Id": "2",
                "LineNum": 2,
                "Description": "shipping",
                "Amount": 35.0,
                "DetailType": "SalesItemLineDetail",
                "SalesItemLineDetail": {
                  "ItemRef": {
                    "value": "1",
                    "name": "Services"
                  },
                  "ItemAccountRef": {
                    "value": "1",
                    "name": "Services"
                  },
                  "TaxCodeRef": {
                    "value": "NON"
                  }
                },
                "CustomExtensions": []
              },
              {
                "Amount": 135.0,
                "DetailType": "SubTotalLineDetail",
                "SubTotalLineDetail": {}
              }
            ],
            "TxnTaxDetail": {
              "TotalTax": 0
            },
            "CustomerRef": {
              "value": "1",
              "name": "Amy's Bird Sanctuary"
            },
            "BillAddr": {
              "Id": "2",
              "Line1": "4581 Finch St.",
              "City": "Bayshore",
              "CountrySubDivisionCode": "CA",
              "PostalCode": "94326",
              "Lat": "INVALID",
              "Long": "INVALID"
            },
            "ShipAddr": {
              "Id": "2",
              "Line1": "4581 Finch St.",
              "City": "Bayshore",
              "CountrySubDivisionCode": "CA",
              "PostalCode": "94326",
              "Lat": "INVALID",
              "Long": "INVALID"
            },
            "FreeFormAddress": true,
            "ShipFromAddr": {
              "Id": "118",
              "Line1": "123 Sierra Way",
              "Line2": "San Pablo, CA  87999"
            },
            "DueDate": "2026-12-10",
            "TotalAmt": 135.0,
            "ApplyTaxAfterDiscount": false,
            "PrintStatus": "NeedToPrint",
            "EmailStatus": "NotSet",
            "Balance": 135.0
          },
          "time": "2026-11-10T22:09:04.127-08:00"
        }
        """;

    private const string Voided = """
        {
          "Invoice": {
            "AllowIPNPayment": false,
            "AllowOnlinePayment": true,
            "AllowOnlineCreditCardPayment": true,
            "AllowOnlineACHPayment": true,
            "AllowOnlinePayPalPayment": false,
            "AllowOnlineAffirmPayment": false,
            "domain": "QBO",
            "sparse": false,
            "Id": "162",
            "SyncToken": "2",
            "MetaData": {
              "CreateTime": "2026-11-10T22:09:04-08:00",
              "LastModifiedByRef": {
                "value": "9341458679203214"
              },
              "LastUpdatedTime": "2026-11-10T22:09:11-08:00"
            },
            "CustomField": [],
            "DocNumber": "LSQ8-7129-9A",
            "TxnDate": "2026-11-10",
            "CurrencyRef": {
              "value": "USD",
              "name": "United States Dollar"
            },
            "PrivateNote": "Voided",
            "LinkedTxn": [],
            "Line": [
              {
                "Id": "3",
                "LineNum": 1,
                "Description": "before",
                "Amount": 0,
                "DetailType": "SalesItemLineDetail",
                "SalesItemLineDetail": {
                  "ItemRef": {
                    "value": "1",
                    "name": "Services"
                  },
                  "ItemAccountRef": {
                    "value": "1",
                    "name": "Services"
                  },
                  "TaxCodeRef": {
                    "value": "NON"
                  }
                },
                "CustomExtensions": []
              },
              {
                "Id": "4",
                "LineNum": 2,
                "Description": "shipping, corrected",
                "Amount": 0,
                "DetailType": "SalesItemLineDetail",
                "SalesItemLineDetail": {
                  "ItemRef": {
                    "value": "1",
                    "name": "Services"
                  },
                  "ItemAccountRef": {
                    "value": "1",
                    "name": "Services"
                  },
                  "TaxCodeRef": {
                    "value": "NON"
                  }
                },
                "CustomExtensions": []
              },
              {
                "Amount": 0,
                "DetailType": "SubTotalLineDetail",
                "SubTotalLineDetail": {}
              }
            ],
            "TxnTaxDetail": {
              "TotalTax": 0
            },
            "CustomerRef": {
              "value": "1",
              "name": "Amy's Bird Sanctuary"
            },
            "BillAddr": {
              "Id": "2",
              "Line1": "4581 Finch St.",
              "City": "Bayshore",
              "CountrySubDivisionCode": "CA",
              "PostalCode": "94326",
              "Lat": "INVALID",
              "Long": "INVALID"
            },
            "ShipAddr": {
              "Id": "2",
              "Line1": "4581 Finch St.",
              "City": "Bayshore",
              "CountrySubDivisionCode": "CA",
              "PostalCode": "94326",
              "Lat": "INVALID",
              "Long": "INVALID"
            },
            "FreeFormAddress": true,
            "ShipFromAddr": {
              "Id": "118",
              "Line1": "123 Sierra Way",
              "Line2": "San Pablo, CA  87999"
            },
            "DueDate": "2026-12-10",
            "TotalAmt": 0,
            "ApplyTaxAfterDiscount": false,
            "PrintStatus": "NeedToPrint",
            "EmailStatus": "NotSet",
            "Balance": 0
          },
          "time": "2026-11-10T22:09:10.863-08:00"
        }
        """;

    private const string Lookup = """
        {
          "QueryResponse": {
            "Invoice": [
              {
                "sparse": true,
                "Id": "145",
                "SyncToken": "0",
                "DocNumber": "LSQ8-7129-03",
                "LinkedTxn": [],
                "Line": [],
                "BillAddr": {
                  "Id": "2"
                },
                "ShipAddr": {
                  "Id": "2"
                },
                "ShipFromAddr": {
                  "Id": "96"
                },
                "TotalAmt": 10.0
              }
            ],
            "startPosition": 1,
            "maxResults": 1,
            "totalCount": 1
          },
          "time": "2026-11-10T22:07:59.256-08:00"
        }
        """;

    [Fact]
    public void FromEntity_ReadsIdTokenTotalsAndCustomer()
    {
        var answer = QuickBooksInvoiceAnswer.FromEntity(Created)!;

        answer.Id.Should().Be("162");
        answer.SyncToken.Should().Be("0");
        answer.DocNumber.Should().Be("LSQ8-7129-9A");
        answer.CustomerId.Should().Be("1");
        answer.TotalAmt.Should().Be(135m);
        answer.TotalTax.Should().Be(0m);
        answer.Balance.Should().Be(135m);
        answer.IsVoided.Should().BeFalse();
        answer.HasPayment.Should().BeFalse();
    }

    [Fact]
    public void FromEntity_VoidAnswer_IsVoided()
    {
        var answer = QuickBooksInvoiceAnswer.FromEntity(Voided)!;

        answer.SyncToken.Should().Be("2");
        answer.IsVoided.Should().BeTrue();
        answer.HasPayment.Should().BeFalse();
    }

    [Fact]
    public void HasPayment_WhenTheBalanceIsBelowTheTotal()
    {
        QuickBooksInvoiceAnswer.FromEntity("""{"Invoice":{"Id":"9","SyncToken":"4","TotalAmt":135.00,"Balance":35.00}}""")!
            .HasPayment.Should().BeTrue();
    }

    [Fact]
    public void FirstFromQuery_ReadsTheLookup()
    {
        var answer = QuickBooksInvoiceAnswer.FirstFromQuery(Lookup)!;

        answer.Id.Should().Be("145");
        answer.DocNumber.Should().Be("LSQ8-7129-03");
        answer.TotalAmt.Should().Be(10m);
    }

    [Theory]
    [InlineData("""{"QueryResponse":{}}""")]
    [InlineData("""{"QueryResponse":{"Invoice":[]}}""")]
    [InlineData("")]
    public void FirstFromQuery_NothingFound_IsNull(string json)
    {
        QuickBooksInvoiceAnswer.FirstFromQuery(json).Should().BeNull();
    }

    [Fact]
    public void NumberQuery_EscapesQuotes()
    {
        QuickBooksInvoiceAnswer.NumberQuery(" INV-O'Neil ").Should().Be("select * from Invoice where DocNumber = 'INV-O\\'Neil'");
    }
}
