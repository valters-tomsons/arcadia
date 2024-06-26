using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arcadia.Hosting;

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
            _logger.LogInformation("CDN host is disabled, content will not be served.");
            return Task.CompletedTask;
        }

        _absoluteRootPath = Path.GetFullPath(_settings.ContentRoot);

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
            _logger.LogError(e, "CDN host failed to start: {}", e.Message);
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
        var contentType = GetContentType(filePath);

        if (filePath is not null && contentType is not null)
        {
            byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
            context.Response.ContentType = contentType;
            context.Response.ContentLength64 = fileBytes.Length;
            await context.Response.OutputStream.WriteAsync(fileBytes);
        }
        else
        {
            _logger.LogTrace("Returning 404 for request: {reqPath}", requestPath);
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        }

        context.Response.Close();
    }

    private string? GetFullPath(string requestPath)
    {
        var filePath = Path.GetFullPath(Path.Combine(_absoluteRootPath, requestPath));
        var isValid = filePath.StartsWith(_absoluteRootPath, StringComparison.OrdinalIgnoreCase);
        return isValid ? filePath : null;
    }

    private static string? GetContentType(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".xml" => "application/xml",
            _ => null
        };
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _httpListener?.Stop();
        return Task.CompletedTask;
    }
}