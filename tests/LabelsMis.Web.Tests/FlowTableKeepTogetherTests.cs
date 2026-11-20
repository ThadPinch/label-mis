using FluentAssertions;
using FrontEndSuite.PdfPlatform.Document;
using FrontEndSuite.PdfPlatform.Geometry;
using FrontEndSuite.PdfPlatform.Layout;

namespace LabelsMis.Web.Tests;

/// <summary>The keep-together switch the v2 ticket relies on to hold a job block on one page.</summary>
public class FlowTableKeepTogetherTests
{
    // A 700pt-tall content area: Letter (792) less 46pt top and bottom.
    private const float PageMargin = 46f;

    [Fact]
    public void Add_WhenKeepTogetherTableDoesNotFitTheSpaceLeft_MovesItWholeToTheNextPage()
    {
        // Page 1: 450. The 3 x 100 table has 250 left: kept whole it goes to page 2, where the
        // 450 table no longer fits beside it, giving three pages.
        PageCount(keepMiddleTogether: true).Should().Be(3);
    }

    [Fact]
    public void Add_WhenTableMaySplit_FillsThePageRowByRow()
    {
        // Two of its rows stay on page 1, the third shares page 2 with the 450 table.
        PageCount(keepMiddleTogether: false).Should().Be(2);
    }

    [Fact]
    public void Add_WhenKeepTogetherTableIsTallerThanAPage_StillPaginates()
    {
        using var pdf = PdfDocument.Create();
        var document = new FlowDocument(pdf, PdfRect.Letter, PageMargin, PageMargin, PageMargin, PageMargin);

        document.Add(Rows(height: 100f, count: 9, keepTogether: true));

        pdf.PageCount.Should().Be(2);
    }

    [Fact]
    public void Add_WhenKeepTogetherTableIsFirstOnThePage_DoesNotLeaveABlankPage()
    {
        using var pdf = PdfDocument.Create();
        var document = new FlowDocument(pdf, PdfRect.Letter, PageMargin, PageMargin, PageMargin, PageMargin);

        document.Add(Rows(height: 100f, count: 7, keepTogether: true));

        pdf.PageCount.Should().Be(1);
    }

    [Fact]
    public void Add_WhenHeaderRowStartsANewPage_DrawsItOnce()
    {
        using var pdf = PdfDocument.Create();
        var document = new FlowDocument(pdf, PdfRect.Letter, PageMargin, PageMargin, PageMargin, PageMargin);
        document.Add(Rows(height: 650f, count: 1, keepTogether: false));

        // 50 left: the 300 header moves to page 2 with its 300 body (600). Drawn twice it would
        // take 600 on its own and push the body to a third page.
        var table = new FlowTable([1f]);
        table.AddHeaderCell(new FlowCell { Padding = 0f, PaddingBottom = 300f });
        table.AddCell(new FlowCell { Padding = 0f, PaddingBottom = 300f });
        document.Add(table);

        pdf.PageCount.Should().Be(2);
    }

    private static int PageCount(bool keepMiddleTogether)
    {
        using var pdf = PdfDocument.Create();
        var document = new FlowDocument(pdf, PdfRect.Letter, PageMargin, PageMargin, PageMargin, PageMargin);
        document.Add(Rows(height: 450f, count: 1, keepTogether: false));
        document.Add(Rows(height: 100f, count: 3, keepTogether: keepMiddleTogether));
        document.Add(Rows(height: 450f, count: 1, keepTogether: true));
        return pdf.PageCount;
    }

    private static FlowTable Rows(float height, int count, bool keepTogether)
    {
        var table = new FlowTable([1f]) { KeepTogether = keepTogether };
        for (var i = 0; i < count; i++)
        {
            table.AddCell(new FlowCell { Padding = 0f, PaddingBottom = height });
        }

        return table;
    }
}
