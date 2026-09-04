using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PodtextCaption.Web.Data;
using PodtextCaption.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Ensure required data directories exist
string rootDir = Directory.GetCurrentDirectory();
string dataDbDir = Path.GetFullPath(Path.Combine(rootDir, "../../data/database"));
string dataAudioDir = Path.GetFullPath(Path.Combine(rootDir, "../../data/audio"));
string dataTranscriptDir = Path.GetFullPath(Path.Combine(rootDir, "../../data/transcripts"));

Directory.CreateDirectory(dataDbDir);
Directory.CreateDirectory(dataAudioDir);
Directory.CreateDirectory(dataTranscriptDir);

// Configure SQLite DbContext
string connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? $"Data Source={Path.Combine(dataDbDir, "podtext.db")}";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

// Register HttpClient and Application Services
builder.Services.AddHttpClient<IAudioDownloadService, AudioDownloadService>();
builder.Services.AddHttpClient<ITranscriptionService, TranscriptionService>();
builder.Services.AddScoped<IAudioConversionService, AudioConversionService>();
builder.Services.AddScoped<ITranscriptStorageService, TranscriptStorageService>();
builder.Services.AddScoped<IPodcastJobService, PodcastJobService>();
builder.Services.AddScoped<IPodcastService, PodcastService>();

builder.Services.AddControllers();
builder.Services.AddRazorPages();

var app = builder.Build();

// Ensure Database schema is initialized
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

app.MapControllers();
app.MapRazorPages();

app.Run();
