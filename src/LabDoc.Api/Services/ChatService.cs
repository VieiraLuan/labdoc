using System.Diagnostics;
using OpenAI;
using OpenAI.Chat;
using LabDoc.Api.Interfaces;

namespace LabDoc.Api.Services;

public sealed class ChatService : IChatService
{
    private readonly ChatClient _chat;
    private readonly ILogger<ChatService> _logger;
    private readonly string _model;
    private readonly float _temperature;
    private readonly int? _maxOutputTokens;

    public ChatService(OpenAIClient client, IConfiguration configuration, ILogger<ChatService> logger)
    {
        _logger = logger;
        _model = configuration["Llm:ChatModel"]
            ?? throw new InvalidOperationException("Llm:ChatModel is not configured.");

        _temperature = configuration.GetValue("Llm:Temperature", 0.1f);

        var maxTokens = configuration.GetValue("Llm:MaxOutputTokens", 0);
        _maxOutputTokens = maxTokens > 0 ? maxTokens : null;

        _chat = client.GetChatClient(_model);
    }

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
        => SendAsync(systemPrompt, userPrompt, responseFormat: null, ct);

    public Task<string> CompleteJsonAsync(
        string systemPrompt,
        string userPrompt,
        string schemaName,
        string jsonSchema,
        CancellationToken ct = default)
    {
        var format = ChatResponseFormat.CreateJsonSchemaFormat(
            jsonSchemaFormatName: schemaName,
            jsonSchema: BinaryData.FromString(jsonSchema));

        return SendAsync(systemPrompt, userPrompt, format, ct);
    }

    private async Task<string> SendAsync(
        string systemPrompt,
        string userPrompt,
        ChatResponseFormat? responseFormat,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            throw new ArgumentException("The user prompt is empty.", nameof(userPrompt));
        }

        ChatMessage[] messages =
        [
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt)
        ];

        var options = new ChatCompletionOptions { Temperature = _temperature };

        if (responseFormat is not null)
        {
            options.ResponseFormat = responseFormat;
        }

        if (_maxOutputTokens is not null)
        {
            options.MaxOutputTokenCount = _maxOutputTokens;
        }

        var startedAt = Stopwatch.GetTimestamp();
        var result = await _chat.CompleteChatAsync(messages, options, ct);
        var elapsed = Stopwatch.GetElapsedTime(startedAt);

        var completion = result.Value;

        if (completion.FinishReason == ChatFinishReason.Length)
        {
            _logger.LogWarning(
                "The response from {Model} was truncated by the token limit ({Max}).",
                _model, _maxOutputTokens);
        }

        _logger.LogInformation(
            "{Model} replied in {Elapsed:F1}s: {In} input tokens, {Out} output.",
            _model, elapsed.TotalSeconds,
            completion.Usage?.InputTokenCount, completion.Usage?.OutputTokenCount);

        return completion.Content.Count > 0
            ? completion.Content[0].Text
            : string.Empty;
    }
}