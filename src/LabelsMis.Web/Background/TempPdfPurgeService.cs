using LabelsMis.Domain.Storage;
using LabelsMis.Web.Services.Pdfs;

namespace LabelsMis.Web.Background;

public class PdfStorageOptions
{
    public const string SectionName = "PdfStorage";

    public int TempRetentionDays { get; set; } = 30;
}

/// <summary>
/// Deletes generated PDFs under the tmp/ storage prefix once they are older than the retention
/// window. Safe because every consumer regenerates a missing PDF on demand.
/// </summary>
public class TempPdfPurgeService(
    IServiceProvider serviceProvider,
    Microsoft.Extensions.Options.IOptions<PdfStorageOptions> options,
    ILogger<TempPdfPurgeService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Temp PDF purge failed; retrying in {Interval}", Interval);
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PurgeAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var fileStorage = scope.ServiceProvider.GetRequiredService<IFileStorageClient>();

        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, options.Value.TempRetentionDays));
        var objects = await fileStorage.ListAsync(TempPdfStorage.KeyPrefix, cancellationToken);
        var expired = objects.Where(o => o.LastModifiedUtc < cutoff).ToList();

        foreach (var item in expired)
        {
            await fileStorage.DeleteAsync(item.Key, cancellationToken);
        }

        if (expired.Count > 0)
        {
            logger.LogInformation(
                "Purged {Count} temp PDFs older than {Cutoff:u}", expired.Count, cutoff);
        }
    }
}
