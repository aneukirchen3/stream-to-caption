using Microsoft.EntityFrameworkCore;
using PodtextCaption.Web.Models;

namespace PodtextCaption.Web.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Podcast> Podcasts => Set<Podcast>();
    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();
    public DbSet<Speaker> Speakers => Set<Speaker>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Podcast>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).HasMaxLength(300);
            entity.Property(e => e.Status).HasMaxLength(50);
        });

        modelBuilder.Entity<ProcessingJob>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PodcastId).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(50);
        });

        modelBuilder.Entity<Speaker>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PodcastId).IsRequired();
            entity.Property(e => e.SpeakerId).HasMaxLength(50);
            entity.Property(e => e.Label).HasMaxLength(20);
            entity.Property(e => e.Name).HasMaxLength(150);
            entity.Property(e => e.Source).HasMaxLength(50);
            entity.Property(e => e.ColorHex).HasMaxLength(20);
        });
    }
}
