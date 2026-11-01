using LabelsMis.Domain.Entities;
using LabelsMis.Domain.Enums;
using LabelsMis.Domain.Invoicing;

namespace LabelsMis.Domain.Tests.Invoicing;

public class QuickBooksMappingRulesTests
{
    private static readonly QuickBooksMapping Map = QuickBooksMapping.Default;

    [Theory]
    [InlineData("SMKB7771", StockType.Substrate, false, "Roll Label")]
    [InlineData("OUTSOURCED", StockType.Substrate, false, "Buyout")]
    [InlineData("outsourced ", StockType.Substrate, false, "Buyout")]
    [InlineData("SHRINK50", StockType.Shrink, false, "Shrink Sleeves")]
    [InlineData("SMKB7771", StockType.Substrate, true, "Buyout")]
    public void ClassFor_FollowsTheSubstrateRule(string code, StockType type, bool outsourced, string expected)
    {
        QuickBooksMappingRules.ClassFor(new QbProductFacts(code, type, outsourced), Map).Should().Be(expected);
    }

    [Theory]
    [InlineData("Die creation", QbLineType.DieCreation, "Die Creation")]
    [InlineData("New diecut tooling", QbLineType.DieCreation, "Die Creation")]
    [InlineData("Rush fee", QbLineType.Rush, "Rush")]
    [InlineData("Artwork / design time (1 hr)", QbLineType.Design, "Design")]
    [InlineData("Proof revisions", QbLineType.Design, "Design")]
    [InlineData("Rush design", QbLineType.Rush, "Rush")]
    [InlineData("Partial pallet handling", QbLineType.Sales, "Sales")]
    [InlineData("", QbLineType.Sales, "Sales")]
    public void MapChargeLine_ClassifiesByKeyword_AndInheritsTheJobClass(string description, QbLineType type, string item)
    {
        var mapped = QuickBooksMappingRules.MapChargeLine(description, "Shrink Sleeves", Map);

        mapped.LineType.Should().Be(type);
        mapped.Item.Should().Be(item);
        mapped.Class.Should().Be("Shrink Sleeves");
    }

    [Fact]
    public void MapProductLine_IsSales_AndOverridesWin()
    {
        var facts = new QbProductFacts("SMKB7771", StockType.Substrate, false);

        QuickBooksMappingRules.MapProductLine(facts, Map).Should().Be(
            new QbLineMapping(QbLineType.Sales, "Sales", "Roll Label", "Product line", "Substrate"));
        var overridden = QuickBooksMappingRules.MapProductLine(facts, Map, " Design ", "Buyout");
        overridden.Item.Should().Be("Design");
        overridden.Class.Should().Be("Buyout");
        overridden.ItemSource.Should().Be("Override");
        overridden.ClassSource.Should().Be("Override");
    }

    [Fact]
    public void JobClass_IsTheFirstProductLines_ElseRollLabel()
    {
        QuickBooksMappingRules.JobClass([
            new QbProductFacts("SHRINK50", StockType.Shrink, false),
            new QbProductFacts("SMKB7771", StockType.Substrate, false)
        ], Map).Should().Be("Shrink Sleeves");
        QuickBooksMappingRules.JobClass([], Map).Should().Be("Roll Label");
    }

    [Fact]
    public void ShippingLine_UsesTheShippingItemAndTheJobClass()
    {
        QuickBooksMappingRules.MapShippingLine("Buyout", Map).Should().Be(
            new QbLineMapping(QbLineType.Shipping, "Shipping", "Buyout", "Shipping", "Job's class"));
    }

    [Fact]
    public void Settings_Update_NormalizesKeywords_AndRequiresItemAndClassNames()
    {
        var settings = QuickBooksSettings.CreateDefault(Guid.NewGuid(), DateTime.UtcNow);
        settings.ToMapping().Should().Be(QuickBooksMapping.Default);

        settings.Update(Map with { RushKeywords = " Rush ;HOT,, expedite ", SalesItem = " Labels " }, Guid.NewGuid(), DateTime.UtcNow);
        settings.RushKeywords.Should().Be("rush, hot, expedite");
        settings.SalesItem.Should().Be("Labels");

        ((Action)(() => settings.Update(Map with { BuyoutClass = " " }, Guid.NewGuid(), DateTime.UtcNow)))
            .Should().Throw<ArgumentException>().WithMessage("Buyout class is required.");
    }
}
