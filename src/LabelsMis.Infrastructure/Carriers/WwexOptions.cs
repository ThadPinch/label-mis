using LabelsMis.Domain.Enums;

namespace LabelsMis.Infrastructure.Carriers;

/// <summary>Worldwide Express's addresses. The staging values are the ones the integration was
/// proven against; the production values come from SpeedShip's public configuration and are first
/// exercised by Test connection on the day production keys arrive (docs/carrier-labels.md). The
/// section exists so tests can point the client at a stub server.</summary>
public class WwexOptions
{
    public const string SectionName = "Wwex";

    public string StagingTokenUrl { get; set; } = "https://auth.staging-wwex.com/oauth/token";
    public string StagingApiBase { get; set; } = "https://speedship.staging-wwex.com/svc/";
    public string StagingAudience { get; set; } = "staging-wwex-apig";

    public string ProductionTokenUrl { get; set; } = "https://auth.wwex.com/oauth/token";
    public string ProductionApiBase { get; set; } = "https://www.speedship.com/svc/";
    public string ProductionAudience { get; set; } = "wwex-apig";

    public int TimeoutSeconds { get; set; } = 30;

    public string TokenUrl(CarrierEnvironment environment) =>
        environment == CarrierEnvironment.Production ? ProductionTokenUrl : StagingTokenUrl;

    public string ApiBase(CarrierEnvironment environment)
    {
        var address = environment == CarrierEnvironment.Production ? ProductionApiBase : StagingApiBase;
        return address.EndsWith('/') ? address : address + "/";
    }

    public string Audience(CarrierEnvironment environment) =>
        environment == CarrierEnvironment.Production ? ProductionAudience : StagingAudience;
}
