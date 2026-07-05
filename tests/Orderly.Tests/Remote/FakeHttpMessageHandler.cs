using System.Net;

namespace Orderly.Tests.Remote;

/// <summary>
/// 用于 Remote 服务单元测试的 HttpMessageHandler 替身：按谓词匹配请求并返回预设响应。
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly List<(Func<HttpRequestMessage, bool> Match, Func<HttpRequestMessage, HttpResponseMessage> Respond)> _handlers = new();

    public IReadOnlyList<HttpRequestMessage> CapturedRequests => _captured;
    private readonly List<HttpRequestMessage> _captured = new();

    public FakeHttpMessageHandler When(Func<HttpRequestMessage, bool> match, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _handlers.Add((match, respond));
        return this;
    }

    public FakeHttpMessageHandler When(HttpMethod method, string pathContains, HttpStatusCode statusCode, string content = "")
    {
        return When(
            req => req.Method == method && req.RequestUri?.ToString().Contains(pathContains, StringComparison.OrdinalIgnoreCase) == true,
            _ => new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
            });
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _captured.Add(request);
        foreach (var (match, respond) in _handlers)
        {
            if (match(request))
            {
                return Task.FromResult(respond(request));
            }
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($"No fake handler matched {request.Method} {request.RequestUri}")
        });
    }
}
