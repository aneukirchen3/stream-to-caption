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

        _logger.LogInformation("Podcast Processing Queue Worker stopping.");
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
