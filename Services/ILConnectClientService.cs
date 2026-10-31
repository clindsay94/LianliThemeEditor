using System.Net.Http;

namespace ThemeEditorCSharp.Services;

public interface ILConnectClientService
{
    Task<LConnectHttpResult> SendDeviceRequestForJsonAsync(
        HttpClient client,
        string devicePath,
        string type,
        string? body,
        CancellationToken cancellationToken = default);

    Task<LConnectHttpResult> SendServiceRequestForJsonAsync(
        HttpClient client,
        string action,
        string? body,
        CancellationToken cancellationToken = default);

    Task<LConnectHttpResult> SendServiceRequestForJsonAsync(
        HttpClient client,
        string action,
        IReadOnlyDictionary<string, string>? query,
        string? body,
        CancellationToken cancellationToken = default);
}
