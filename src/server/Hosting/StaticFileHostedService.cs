using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcadia.Hosting;

/// <summary>
/// easo.ea.com
/// </summary>
public class StaticFileHostedService(IOptions<FileServerSettings> settings, ILogger<StaticFileHostedService> logger) : IHostedService
{
    private readonly ILogger<StaticFileHostedService> _logger = logger;
    private readonly FileServerSettings _settings = settings.Value;
    private readonly HttpListener _httpListener = new();

    private string _absoluteRootPath = string.Empty;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_settings.EnableCdn)
        {
            return Task.CompletedTask;
        }

        _absoluteRootPath = Path.GetFullPath(_settings.ContentRoot);
        if(!_absoluteRootPath.EndsWith('/'))
        {
            _absoluteRootPath += '/';
        }

        try
        {
            const string prefix = "http://*:80/";
            _httpListener.Prefixes.Add(prefix);
            _httpListener.Start();

            Task.Run(() => StartListening(cancellationToken), cancellationToken);

            _logger.LogInformation("File server '{prefix}' listening at root path: {path}", prefix, _absoluteRootPath);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "CDN host failed to start: {Message}", e.Message);
        }

        return Task.CompletedTask;
    }

    private async Task StartListening(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var context = await _httpListener.GetContextAsync();
            _ = Task.Run(() => HandleRequestAsync(context), cancellationToken);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var requestPath = context.Request?.Url?.AbsolutePath.TrimStart('/');
        if (string.IsNullOrWhiteSpace(requestPath))
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            context.Response.Close();
            return;
        }

        var filePath = GetFullPath(requestPath);
        if (filePath is not null)
        {
            _logger.LogTrace("Serving content of '{filePath}'", filePath);

            byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
            context.Response.ContentLength64 = fileBytes.Length;
            await context.Response.OutputStream.WriteAsync(fileBytes);
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        }

        _logger.LogInformation("Returning HTTP {statusCode} for '{reqPath}'", context.Response.StatusCode, requestPath);
        context.Response.Close();
    }

    private string? GetFullPath(string requestPath)
    {
        var filePath = Path.GetFullPath(Path.Combine(_absoluteRootPath, requestPath));

        var pathIsBounded = filePath.StartsWith(_absoluteRootPath, StringComparison.OrdinalIgnoreCase);
        var pathIsFile = File.Exists(filePath) && !Directory.Exists(filePath);
        
        return pathIsBounded && pathIsFile ? filePath : null;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _httpListener?.Stop();
        return Task.CompletedTask;
    }
}