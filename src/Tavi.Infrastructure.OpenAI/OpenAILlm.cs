using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;
using Tavi.Application;
using Tavi.Application.LanguageModel;

namespace Tavi.Infrastructure.OpenAI
{
    public enum ClientType
    {
        Chat,
        Response
    }

    public class OpenAILlm : ILanguageModel
    {
        private OpenAILlmConfig _config = new();
        private ApiKeyCredential _credential;
        private ResponsesClient _responsesClient;
        private ChatClient _chatClient;

        private static CancellationTokenSource _forceCancellationTokenSource = new();

        public static void ForceCancel()
        {
            _forceCancellationTokenSource?.Cancel();
        }

        public void Init(object config)
        {
            _config = (OpenAILlmConfig)config;
            try
            {
                if (_config.ClientType == ClientType.Response)
                {
                    ResponsesClientOptions options = new ResponsesClientOptions();
                    options.Endpoint = _config.Uri;
                    _credential = new ApiKeyCredential(_config.APIKey);
                    _responsesClient = new ResponsesClient(_credential, options);
                }
                else if (_config.ClientType == ClientType.Chat)
                {
                    OpenAIClientOptions options = new OpenAIClientOptions();
                    options.Endpoint = _config.Uri;
                    _credential = new ApiKeyCredential(_config.APIKey);
                    _chatClient = new ChatClient(_config.Model, _credential, options);
                }
            }
            catch (Exception e)
            {
                throw new LanguageModelException("LLM初始化异常：" + e.Message + "\n" + e.StackTrace);
            }
        }

        public async Task<string> GenerateAsync(Message message, CancellationToken cancellationToken = default)
        {
            using CancellationTokenSource timeoutSource =
                new(TimeSpan.FromSeconds(30));
            _forceCancellationTokenSource = new();
            CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token,
                _forceCancellationTokenSource.Token, cancellationToken);
            try
            {
                if (_config.ClientType == ClientType.Response)
                {
                    CreateResponseOptions options = CreateResponseOptions(message);
                    options.InputItems.Add(ResponseItem.CreateSystemMessageItem(message.SystemPrompt ?? ""));
                    options.InputItems.Add(ResponseItem.CreateSystemMessageItem(message.JsonOutputConstraint ?? ""));
                    options.InputItems.Add(ResponseItem.CreateUserMessageItem(message.UserContext ?? ""));

                    for (int round = 0; round < _config.MaxRound; round++)
                    {
                        ResponseResult response = await _responsesClient.CreateResponseAsync(options, linked.Token);
                        List<FunctionCallResponseItem> functionCalls =
                            response.OutputItems.OfType<FunctionCallResponseItem>().ToList();

                        if (functionCalls.Count == 0)
                        {
                            string result = response.GetOutputText();

                            if (!IsJsonOutputValid(result, message.JsonOutputConstraint))
                            {
                                options = CreateResponseOptions(message, response.Id);
                                options.InputItems.Add(ResponseItem.CreateUserMessageItem(
                                    "上一次回复不是合法的 JSON，请严格按照 JSON 输出约束重新生成。"
                                ));
                                round--;
                                continue;
                            }

                            message.Result = result;
                            return message.Result;
                        }

                        options = CreateResponseOptions(message, response.Id);

                        foreach (FunctionCallResponseItem item in functionCalls)
                        {
                            ITool? tool = message.Tools.Find(candidate =>
                                candidate.name == item.FunctionName
                            );

                            if (tool is null)
                            {
                                throw new LanguageModelException(
                                    $"模型请求了未注册的工具：{item.FunctionName}"
                                );
                            }

                            string result = await tool.Execute(
                                item.FunctionArguments,
                                linked.Token
                            );

                            options.InputItems.Add(
                                ResponseItem.CreateFunctionCallOutputItem(
                                    item.CallId,
                                    result
                                )
                            );
                        }
                    }

                    throw new LanguageModelException(
                        $"工具调用超过最大轮数 {_config.MaxRound}。"
                    );
                }
                else if (_config.ClientType == ClientType.Chat)
                {
                    ChatCompletion? completion = null;

                    var options = new ChatCompletionOptions();

                    foreach (ITool tool in message.Tools)
                    {
                        options.Tools.Add(
                            ChatTool.CreateFunctionTool(
                                functionName: tool.name,
                                functionDescription: tool.description,
                                functionParameters: tool.parameterData
                            )
                        );
                    }

                    var chatMessages = new List<ChatMessage>
                    {
                        new SystemChatMessage(message.SystemPrompt ?? ""),
                        new SystemChatMessage(message.JsonOutputConstraint ?? ""),
                        new UserChatMessage(message.UserContext ?? "")
                    };

                    for (int round = 0; round < _config.MaxRound; round++)
                    {
                        completion = await _chatClient.CompleteChatAsync(
                            chatMessages,
                            options,
                            linked.Token
                        );

                        if (completion.FinishReason == ChatFinishReason.Stop)
                        {
                            chatMessages.Add(new AssistantChatMessage(completion));

                            string result = completion.Content.Count > 0
                                ? completion.Content[0].Text
                                : string.Empty;

                            if (!IsJsonOutputValid(result, message.JsonOutputConstraint))
                            {
                                chatMessages.Add(new UserChatMessage(
                                    "上一次回复不是合法的 JSON，请严格按照 JSON 输出约束重新生成。"
                                ));
                                round--;
                                continue;
                            }

                            message.Result = result;
                            return message.Result;
                        }

                        if (completion.FinishReason == ChatFinishReason.Length)
                        {
                            throw new LanguageModelException(
                                "LLM 调用超过上下文或输出长度限制！"
                            );
                        }

                        if (completion.FinishReason ==
                            ChatFinishReason.ContentFilter)
                        {
                            throw new LanguageModelException(
                                "LLM 调用触发内容屏蔽！"
                            );
                        }

                        if (completion.FinishReason ==
                            ChatFinishReason.ToolCalls)
                        {
                            // 必须先记录模型发起工具调用的 assistant 消息。
                            chatMessages.Add(
                                new AssistantChatMessage(completion)
                            );

                            foreach (ChatToolCall item in completion.ToolCalls)
                            {
                                ITool? tool = message.Tools.Find(candidate =>
                                    candidate.name == item.FunctionName
                                );

                                if (tool is null)
                                {
                                    throw new LanguageModelException(
                                        $"模型请求了未注册的工具：" +
                                        $"{item.FunctionName}"
                                    );
                                }

                                string result = await tool.Execute(
                                    item.FunctionArguments,
                                    linked.Token
                                );

                                chatMessages.Add(
                                    new ToolChatMessage(item.Id, result)
                                );
                            }

                            // 下一轮把 assistant tool call 和工具结果一起发回。
                            continue;
                        }

                        if (completion.FinishReason ==
                            ChatFinishReason.FunctionCall)
                        {
                            TaviCore.logger.LogWarning(
                                "模型使用了过时的 FunctionCall。"
                            );

                            chatMessages.Add(
                                new AssistantChatMessage(completion)
                            );

                            ITool? tool = message.Tools.Find(candidate =>
                                candidate.name ==
                                completion.FunctionCall.FunctionName
                            );

                            if (tool is null)
                            {
                                throw new LanguageModelException(
                                    $"模型请求了未注册的函数：" +
                                    $"{completion.FunctionCall.FunctionName}"
                                );
                            }

                            string result = await tool.Execute(
                                completion.FunctionCall.FunctionArguments,
                                linked.Token
                            );

                            chatMessages.Add(
                                new FunctionChatMessage(
                                    completion.FunctionCall.FunctionName,
                                    result
                                )
                            );

                            continue;
                        }

                        throw new LanguageModelException(
                            $"OpenAI SDK 返回异常的 ChatFinishReason：" +
                            $"{completion.FinishReason}"
                        );
                    }

                    throw new LanguageModelException(
                        $"工具调用超过最大轮数 {_config.MaxRound}。"
                    );
                }
                else
                {
                    throw new Exception();
                }
            }
            catch (OperationCanceledException e) when (_forceCancellationTokenSource.Token.IsCancellationRequested)
            {
                throw new LanguageModelException("已强制取消");
            }
            catch (Exception e)
            {
                throw new LanguageModelException("LLM生成异常：" + e.Message + "\n" + e.StackTrace);
            }
            finally
            {
                _forceCancellationTokenSource?.Dispose();
                _forceCancellationTokenSource = null;
            }
        }

        private CreateResponseOptions CreateResponseOptions(
            Message message,
            string? previousResponseId = null
        )
        {
            var options = new CreateResponseOptions
            {
                Model = _config.Model
            };

            if (previousResponseId is not null)
            {
                options.PreviousResponseId = previousResponseId;
            }

            foreach (ITool tool in message.Tools)
            {
                options.Tools.Add(
                    ResponseTool.CreateFunctionTool(
                        functionName: tool.name,
                        functionParameters: tool.parameterData,
                        strictModeEnabled: true,
                        functionDescription: tool.description
                    )
                );
            }

            return options;
        }

        private static bool IsJsonOutputValid(
            string output,
            string? jsonOutputConstraint
        )
        {
            if (string.IsNullOrWhiteSpace(jsonOutputConstraint))
            {
                return true;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(output);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
