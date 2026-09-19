using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PodtextCaption.Web.Services;

public interface IFtpStorageService
{
    Task UploadFileAsync(string localFilePath, string relativeRemotePath, CancellationToken cancellationToken = default);
    Task<bool> VerifyFileExistsAsync(string relativeRemotePath, CancellationToken cancellationToken = default);
}

public class FtpStorageService : IFtpStorageService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<FtpStorageService> _logger;

    public FtpStorageService(IConfiguration configuration, ILogger<FtpStorageService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task UploadFileAsync(string localFilePath, string relativeRemotePath, CancellationToken cancellationToken = default)
    {
        string? server = _configuration["Ftp:Server"];
        string? user = _configuration["Ftp:User"];
        string? password = _configuration["Ftp:Password"];
        string remoteBasePath = _configuration["Ftp:RemoteBasePath"] ?? "/StreamToCaption/data";

        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(user) || password == null)
        {
            _logger.LogWarning("FTP upload skipped: FTP server, user, or password not configured.");
            return;
        }

        if (!File.Exists(localFilePath))
        {
            _logger.LogWarning("FTP upload skipped: Local file does not exist at {Path}", localFilePath);
            return;
        }

        string cleanRemotePath = relativeRemotePath.TrimStart('/');
        string fullRemoteUri = $"ftp://{server}{remoteBasePath}/{cleanRemotePath}";

        _logger.LogInformation("FTP: Starting upload of {LocalPath} to {RemoteUri}", localFilePath, fullRemoteUri);

        try
        {
            // Ensure remote directory structure exists
            await EnsureRemoteDirectoryExistsAsync(server, user, password, remoteBasePath, cleanRemotePath, cancellationToken);

            var request = (FtpWebRequest)WebRequest.Create(fullRemoteUri);
            request.Method = WebRequestMethods.Ftp.UploadFile;
            request.Credentials = new NetworkCredential(user, password);
            request.UseBinary = true;
            request.UsePassive = true;
            request.KeepAlive = false;

            using (var fileStream = File.OpenRead(localFilePath))
            using (var requestStream = await request.GetRequestStreamAsync())
            {
                await fileStream.CopyToAsync(requestStream, cancellationToken);
            }

            using (var response = (FtpWebResponse)await request.GetResponseAsync())
            {
                _logger.LogInformation("FTP: Successfully uploaded {LocalPath} to {RemoteUri}. Response: {Status}", localFilePath, fullRemoteUri, response.StatusDescription?.Trim());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FTP: Exception uploading file {LocalPath} to {RemoteUri}", localFilePath, fullRemoteUri);
        }
    }

    private async Task EnsureRemoteDirectoryExistsAsync(string server, string user, string password, string remoteBasePath, string relativePath, CancellationToken cancellationToken)
    {
        string? dirPath = Path.GetDirectoryName(relativePath)?.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(dirPath)) return;

        string[] segments = dirPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string currentPath = remoteBasePath;

        foreach (var seg in segments)
        {
            currentPath = $"{currentPath.TrimEnd('/')}/{seg}";
            string mkdirUri = $"ftp://{server}{currentPath}";

            try
            {
                var req = (FtpWebRequest)WebRequest.Create(mkdirUri);
                req.Method = WebRequestMethods.Ftp.MakeDirectory;
                req.Credentials = new NetworkCredential(user, password);
                req.UsePassive = true;
                req.KeepAlive = false;

                using var resp = (FtpWebResponse)await req.GetResponseAsync();
            }
            catch (WebException ex)
            {
                // Directory may already exist (FTP status 550), which is expected
                _logger.LogDebug(ex, "FTP mkdir response for {Uri}", mkdirUri);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "FTP mkdir attempt failed for {Uri}", mkdirUri);
            }
        }
    }

    public async Task<bool> VerifyFileExistsAsync(string relativeRemotePath, CancellationToken cancellationToken = default)
    {
        string? server = _configuration["Ftp:Server"];
        string? user = _configuration["Ftp:User"];
        string? password = _configuration["Ftp:Password"];
        string remoteBasePath = _configuration["Ftp:RemoteBasePath"] ?? "/StreamToCaption/data";

        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(user))
        {
            _logger.LogWarning("FTP verify skipped: FTP server or user not configured.");
            return false;
        }

        string cleanRemotePath = relativeRemotePath.TrimStart('/');
        string fullRemoteUri = $"ftp://{server}{remoteBasePath}/{cleanRemotePath}";

        try
        {
            var request = (FtpWebRequest)WebRequest.Create(fullRemoteUri);
            request.Method = WebRequestMethods.Ftp.GetFileSize;
            request.Credentials = new NetworkCredential(user, password);
            request.UseBinary = true;
            request.UsePassive = true;
            request.KeepAlive = false;

            using var response = (FtpWebResponse)await request.GetResponseAsync();
            return response.ContentLength >= 0;
        }
        catch (WebException ex)
        {
            if (ex.Response is FtpWebResponse ftpResp)
            {
                _logger.LogWarning("FTP verify check failed for {Uri}: {StatusCode} - {Description}", fullRemoteUri, ftpResp.StatusCode, ftpResp.StatusDescription?.Trim());
            }
            else
            {
                _logger.LogWarning(ex, "FTP verify check failed with WebException for {Uri}", fullRemoteUri);
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FTP verify check failed with Exception for {Uri}", fullRemoteUri);
            return false;
        }
    }
}
