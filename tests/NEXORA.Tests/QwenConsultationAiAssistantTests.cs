using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NEXORA.Application.Consultation;
using NEXORA.Infrastructure.Consultation;

namespace NEXORA.Tests;

public sealed class QwenConsultationAiAssistantTests
{
    [Fact]
    public async Task InterpretAsync_UsesConfiguredQwenEndpointAndValidatesOutputAgainstAllowedValues()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var handler = new StubHandler(async (request, cancellationToken) =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse("{\"choices\":[{\"message\":{\"content\":\"{\\\"category\\\":\\\"Laptop\\\",\\\"usage_code\\\":\\\"GAMING\\\"}\"},\"finish_reason\":\"stop\"}]}");
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.xkiro.com/v1/") };
        var assistant = new QwenConsultationAiAssistant(
            client,
            Options.Create(new QwenConsultationOptions { ApiKey = "test-key-not-a-secret", Model = "qwen/qwen3-omni-flash:free" }),
            NullLogger<QwenConsultationAiAssistant>.Instance);

        var result = await assistant.InterpretAsync("Laptop chơi game", [new ConsultationNeedOption("GAMING", "Chơi game")]);

        Assert.Equal("Laptop", result?.CategoryName);
        Assert.Equal("GAMING", result?.UsageNeedCode);
        Assert.Equal(HttpMethod.Post, capturedRequest?.Method);
        Assert.Equal("Bearer", capturedRequest?.Headers.Authorization?.Scheme);
        Assert.Equal("test-key-not-a-secret", capturedRequest?.Headers.Authorization?.Parameter);
        Assert.Contains("qwen/qwen3-omni-flash:free", capturedBody, StringComparison.Ordinal);
        Assert.EndsWith("chat/completions", capturedRequest?.RequestUri?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InterpretAsync_OnRateLimitOrMalformedResponse_ReturnsSafeLocalFallback()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(JsonResponse("not-json", HttpStatusCode.TooManyRequests)));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.xkiro.com/v1/") };
        var assistant = new QwenConsultationAiAssistant(client,
            Options.Create(new QwenConsultationOptions { ApiKey = "test-key", Model = "qwen/qwen3-omni-flash:free" }),
            NullLogger<QwenConsultationAiAssistant>.Instance);

        var result = await assistant.InterpretAsync("Laptop", [new ConsultationNeedOption("GAMING", "Chơi game")]);

        Assert.Null(result);
    }

    [Fact]
    public async Task ComposeResponseAsync_UsesBackendEvidenceAndReturnsBoundedPlainText()
    {
        string? capturedBody = null;
        var handler = new StubHandler(async (request, cancellationToken) =>
        {
            capturedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse("{\"choices\":[{\"message\":{\"content\":\"Mình đã lọc các lựa chọn theo nhu cầu của bạn. Hãy xem giá và thông số hiện có trên từng thẻ nhé.\"},\"finish_reason\":\"stop\"}]}");
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.xkiro.com/v1/") };
        var assistant = new QwenConsultationAiAssistant(client,
            Options.Create(new QwenConsultationOptions { ApiKey = "test-key", Model = "qwen/qwen3-omni-flash:free" }),
            NullLogger<QwenConsultationAiAssistant>.Instance);

        var result = await assistant.ComposeResponseAsync("Laptop học tập", CreateClarificationOutcome());

        Assert.Equal("Mình đã lọc các lựa chọn theo nhu cầu của bạn. Hãy xem giá và thông số hiện có trên từng thẻ nhé.", result);
        using var body = JsonDocument.Parse(capturedBody!);
        var messages = body.RootElement.GetProperty("messages");
        Assert.Contains("ClarificationRequired", messages[1].GetProperty("content").GetString(), StringComparison.Ordinal);
        Assert.Contains("Không tự thêm sản phẩm", messages[0].GetProperty("content").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ComposeResponseAsync_StreamsTextDeltasAndReturnsFinalResponse()
    {
        string? capturedBody = null;
        var streamed = new List<string>();
        const string frames = """
            data: {"choices":[{"delta":{"content":"Mình đã tìm "},"finish_reason":null}]}

            data: {"choices":[{"delta":{"content":"lựa chọn phù hợp."},"finish_reason":null}]}

            data: {"choices":[{"delta":{},"finish_reason":"stop"}]}

            data: [DONE]

            """;
        var handler = new StubHandler(async (request, cancellationToken) =>
        {
            capturedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(frames, Encoding.UTF8, "text/event-stream")
            };
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.xkiro.com/v1/") };
        var assistant = new QwenConsultationAiAssistant(client,
            Options.Create(new QwenConsultationOptions { ApiKey = "test-key", Model = "qwen/qwen3-omni-flash:free" }),
            NullLogger<QwenConsultationAiAssistant>.Instance);

        var result = await assistant.ComposeResponseAsync(
            "Laptop học tập",
            CreateClarificationOutcome(),
            (chunk, _) => { streamed.Add(chunk); return ValueTask.CompletedTask; });

        Assert.Equal("Mình đã tìm lựa chọn phù hợp.", result);
        Assert.Equal(["Mình đã tìm ", "lựa chọn phù hợp."], streamed);
        using var body = JsonDocument.Parse(capturedBody!);
        Assert.True(body.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task ComposeResponseAsync_RejectsHtmlAndFallsBack()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(JsonResponse(
            "{\"choices\":[{\"message\":{\"content\":\"<script>alert(1)</script>\"},\"finish_reason\":\"stop\"}]}")));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.xkiro.com/v1/") };
        var assistant = new QwenConsultationAiAssistant(client,
            Options.Create(new QwenConsultationOptions { ApiKey = "test-key", Model = "qwen/qwen3-omni-flash:free" }),
            NullLogger<QwenConsultationAiAssistant>.Instance);

        var result = await assistant.ComposeResponseAsync("Laptop", CreateClarificationOutcome());

        Assert.Null(result);
    }

    private static ConsultationOutcome CreateClarificationOutcome() => new(
        ConsultationOutcomeKind.ClarificationRequired,
        new RequirementProfile(
            "Laptop học tập",
            new RecognizedField(null, null, RecognitionState.Missing),
            new BudgetRequirement(BudgetKind.None, null, null, null, null, RecognitionState.Missing),
            new RecognizedField(null, null, RecognitionState.Missing),
            [], [], [], ["Bạn muốn tìm laptop?"], []),
        [],
        "Cần thêm thông tin.");

    private static HttpResponseMessage JsonResponse(string content, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}
