using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PodtextCaption.Web.Data;
using PodtextCaption.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Ensure required data directories exist using ContentRootPath (IIS & SmarterASP.NET safe)
string baseContentPath = builder.Environment.ContentRootPath;
string dataDir = Path.Combine(baseContentPath, "data");

// Fallback for local dev environment if ../../data exists
string localDataFallback = Path.GetFullPath(Path.Combine(baseContentPath, "../../data"));
if (!Directory.Exists(dataDir) && Directory.Exists(localDataFallback))
{
    dataDir = localDataFallback;
}

string dataDbDir = Path.Combine(dataDir, "database");
string dataAudioDir = Path.Combine(dataDir, "audio");
string dataTranscriptDir = Path.Combine(dataDir, "transcripts");

Directory.CreateDirectory(dataDbDir);
Directory.CreateDirectory(dataAudioDir);
Directory.CreateDirectory(dataTranscriptDir);

// Configure SQLite DbContext with absolute path
string dbFilePath = Path.Combine(dataDbDir, "podtext.db");
string connectionString = $"Data Source={dbFilePath}";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

// Register HttpClient and Application Services
builder.Services.AddHttpClient<ITranscriptionService, TranscriptionService>()
    .RemoveAllLoggers();
builder.Services.AddHttpClient<IAudioDownloadService, AudioDownloadService>();
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
