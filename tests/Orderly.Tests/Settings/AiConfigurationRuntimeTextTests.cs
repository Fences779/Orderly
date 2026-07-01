using System;
using System.Collections.Generic;
using Orderly.App.ViewModels;
using Orderly.Core.Models;
using Xunit;

namespace Orderly.Tests.Settings;

public sealed class AiConfigurationRuntimeTextTests
{
    [Fact]
    public void Apply_settings_with_saved_default_model_and_no_runtime_model_reports_next_request_uses_saved_model()
    {
        using var env = new TemporaryEnvironmentVariables(new Dictionary<string, string?>
        {
            ["ORDERLY_AI_PROVIDER"] = null,
            ["ORDERLY_AI_MODEL"] = null
        });

        var viewModel = new SettingsViewModel();

        viewModel.ApplySettingsInputsFromPreferences(new AppPreferences
        {
            AiDefaultModel = "gpt-4.1-mini",
            AiTimeoutSeconds = 15
        });

        Assert.Equal(
            "已保存默认模型偏好；若运行时未配置模型，下一次 AI 请求将使用该偏好。",
            viewModel.AiModelPreferenceStatusText);
    }

    [Fact]
    public void Check_configuration_accepts_saved_default_model_for_openai_compatible_provider()
    {
        using var env = new TemporaryEnvironmentVariables(new Dictionary<string, string?>
        {
            ["ORDERLY_AI_PROVIDER"] = "openai-compatible",
            ["ORDERLY_AI_BASE_URL"] = "https://example.com/v1",
            ["ORDERLY_AI_API_KEY"] = "test-key",
            ["ORDERLY_AI_MODEL"] = null
        });

        var viewModel = new SettingsViewModel
        {
            DefaultAiModelInput = "gpt-4.1-mini"
        };

        viewModel.CheckAiConfigurationCommand.Execute(null);

        Assert.Equal(
            "配置检查通过：provider / endpoint / key / model / timeout 已满足最低配置，未执行网络连通性测试。",
            viewModel.AiConnectionCheckStatusText);
        Assert.DoesNotContain("待 provider 接入", viewModel.AiConnectionCheckStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_configuration_for_local_provider_clearly_states_no_network_test()
    {
        using var env = new TemporaryEnvironmentVariables(new Dictionary<string, string?>
        {
            ["ORDERLY_AI_PROVIDER"] = null,
            ["ORDERLY_AI_BASE_URL"] = null,
            ["ORDERLY_AI_API_KEY"] = null,
            ["ORDERLY_AI_MODEL"] = null
        });

        var viewModel = new SettingsViewModel();

        viewModel.CheckAiConfigurationCommand.Execute(null);

        Assert.Equal(
            "配置检查通过：当前 provider=local，仅检查本地配置，未执行网络连通性测试。",
            viewModel.AiConnectionCheckStatusText);
    }

    private sealed class TemporaryEnvironmentVariables : IDisposable
    {
        private readonly Dictionary<string, string?> _originals = new(StringComparer.Ordinal);

        public TemporaryEnvironmentVariables(IReadOnlyDictionary<string, string?> overrides)
        {
            foreach (var item in overrides)
            {
                _originals[item.Key] = Environment.GetEnvironmentVariable(item.Key);
                Environment.SetEnvironmentVariable(item.Key, item.Value);
            }
        }

        public void Dispose()
        {
            foreach (var item in _originals)
            {
                Environment.SetEnvironmentVariable(item.Key, item.Value);
            }
        }
    }
}
