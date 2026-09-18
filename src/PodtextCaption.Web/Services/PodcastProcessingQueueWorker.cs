using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PodtextCaption.Web.Data;
using PodtextCaption.Web.Models;

namespace PodtextCaption.Web.Services;

public class PodcastProcessingQueueWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PodcastProcessingQueueWorker> _logger;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);

    public PodcastProcessingQueueWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<PodcastProcessingQueueWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Podcast Processing Queue Worker active. Polling queue every {IntervalSeconds} seconds...", PollingInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Heartbeat update to queue_status table
            await UpdateHeartbeatAsync("Ativo");

            try
            {
                await ProcessNextQueuedJobAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Queue Worker: Error checking or processing job queue.");
            }

            try
            {
                await Task.Delay(PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        await UpdateHeartbeatAsync("Inativo");
        _logger.LogInformation("Podcast Processing Queue Worker stopping.");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Podcast Processing Queue Worker performing graceful shutdown...");
        await UpdateHeartbeatAsync("Inativo");
        await base.StopAsync(cancellationToken);
    }

    private async Task UpdateHeartbeatAsync(string status)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            DateTime brasiliaNow = GetBrasiliaTime();

            var record = await db.QueueStatus.FirstOrDefaultAsync(q => q.Id == 1);
            if (record == null)
            {
                record = new QueueStatus { Id = 1, DsStatus = status, DtLastUpdateStatus = brasiliaNow };
                db.QueueStatus.Add(record);
            }
            else
            {
                record.DsStatus = status;
                record.DtLastUpdateStatus = brasiliaNow;
            }
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Queue Worker: Failed to update queue status to {Status}", status);
        }
    }

    public static DateTime GetBrasiliaTime()
    {
        try
        {
            TimeZoneInfo brTz = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, brTz);
        }
        catch
        {
            try
            {
                TimeZoneInfo brTzWin = TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, brTzWin);
            }
            catch
            {
                return DateTime.UtcNow.AddHours(-3);
            }
        }
    }

    private async Task ProcessNextQueuedJobAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jobService = scope.ServiceProvider.GetRequiredService<IPodcastJobService>();

        // Query oldest pending job in the queue
        var job = await db.ProcessingJobs
            .Where(j => j.Status == PodcastStatus.Pending)
            .OrderBy(j => j.StartedAt)
            .FirstOrDefaultAsync(stoppingToken);

        if (job == null)
        {
            return;
        }

        _logger.LogInformation("Queue Worker: Found pending Job {JobId} for Podcast {PodcastId}. Processing...", job.Id, job.PodcastId);

        try
        {
            await jobService.ProcessPodcastPipelineAsync(job.Id, job.PodcastId, job.Language, job.Model, stoppingToken);
            _logger.LogInformation("Queue Worker: Finished pipeline for Job {JobId}", job.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Queue Worker: Execution failed for Job {JobId}", job.Id);
        }
    }
}
