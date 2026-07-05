using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Data.Services;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    public ObservableCollection<string> AiReplyToneOptions { get; } = new(["简洁", "温和", "专业"]);
    public ObservableCollection<string> AiReplyLengthOptions { get; } = new(["短", "标准", "详细"]);

    [ObservableProperty]
    private bool enableAiAssistantInput;

    [ObservableProperty]
    private bool allowAiOrderContextInput;

    [ObservableProperty]
    private bool allowAiCustomerProfileContextInput;

    [ObservableProperty]
    private string defaultAiModelInput = string.Empty;

    public ObservableCollection<string> AiModelOptions { get; } = new([
        "deepseek-chat",
        "deepseek-reasoner",
        "gpt-4o",
        "gpt-4o-mini",
        "claude-3-5-sonnet",
        "gemini-1.5-pro",
        "gemini-1.5-flash",
        "自定义..."
    ]);

    private string _selectedAiModelOption = "deepseek-chat";
    public string SelectedAiModelOption
    {
        get => _selectedAiModelOption;
        set
        {
            if (SetProperty(ref _selectedAiModelOption, value))
            {
                OnPropertyChanged(nameof(IsCustomAiModelActive));
                if (value != "自定义...")
                {
                    DefaultAiModelInput = value;
                }
                else if (string.IsNullOrEmpty(DefaultAiModelInput) || AiModelOptions.Contains(DefaultAiModelInput))
                {
                    DefaultAiModelInput = "custom-model";
                }
            }
        }
    }

    public bool IsCustomAiModelActive => SelectedAiModelOption == "自定义...";


    [ObservableProperty]
    private int aiTimeoutSecondsInput = 15;

    [ObservableProperty]
    private bool aiAutoRedactBeforeSendInput = true;

    [ObservableProperty]
    private bool aiBlockPhoneInput = true;

    [ObservableProperty]
    private bool aiBlockFullAddressInput = true;

    [ObservableProperty]
    private bool aiBlockPaymentTransactionIdInput = true;

    [ObservableProperty]
    private string aiReplyToneInput = "简洁";

    [ObservableProperty]
    private string aiReplyLengthInput = "标准";

    [ObservableProperty]
    private bool aiAutoGenerateOrderSummaryInput;

    [ObservableProperty]
    private string aiRuntimeProviderText = "local";

    [ObservableProperty]
    private string aiRuntimeModelText = "未配置";

    [ObservableProperty]
    private string aiApiKeyStatusText = "未配置";

    [ObservableProperty]
    private string aiEndpointStatusText = "未配置";

    [ObservableProperty]
    private string aiConnectionCheckStatusText = "未检查";

    [ObservableProperty]
    private string aiModelPreferenceStatusText = "默认模型偏好会在下一次 AI 请求时生效。";

    partial void OnEnableAiAssistantInputChanged(bool value)
    {
        NotifyAiSuggestionCommandStateChanged();
    }

    [RelayCommand]
    private void CheckAiConfiguration()
    {
        if (IsBusy)
        {
            return;
        }

        RefreshAiSettingsRuntimeStatus();
        var options = AiProviderOptions.FromEnvironment();
        var provider = options.RequestedProvider;
        var errors = new List<string>();

        if (options.TimeoutSeconds < MinAiTimeoutSeconds || options.TimeoutSeconds > MaxAiTimeoutSeconds)
        {
            errors.Add($"ORDERLY_AI_TIMEOUT_SECONDS 超出范围（{MinAiTimeoutSeconds}-{MaxAiTimeoutSeconds} 秒）");
        }

        switch (provider)
        {
            case AiProviderOptions.LocalProviderName:
                AiConnectionCheckStatusText = "配置检查通过：当前 provider=local，仅检查本地配置，未执行网络连通性测试。";
                SettingsStatusMessage = "AI 配置检查已完成（仅配置检查）。";
                return;
            case AiProviderOptions.OpenAiCompatibleProviderName:
                if (string.IsNullOrWhiteSpace(options.BaseUrl))
                {
                    errors.Add("ORDERLY_AI_BASE_URL 未配置");
                }

                if (string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    errors.Add("ORDERLY_AI_API_KEY 未配置");
                }

                break;
            case AiProviderOptions.DeepSeekProviderName:
                if (string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    errors.Add("DEEPSEEK_API_KEY 未配置");
                }

                break;
            default:
                errors.Add($"未知 provider：{provider}");
                break;
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            if (string.IsNullOrWhiteSpace(DefaultAiModelInput))
            {
                errors.Add("ORDERLY_AI_MODEL 未配置");
            }
        }

        if (errors.Count == 0)
        {
            AiConnectionCheckStatusText = "配置检查通过：provider / endpoint / key / model / timeout 已满足最低配置，未执行网络连通性测试。";
            SettingsStatusMessage = "AI 配置检查已完成（仅配置检查）。";
            return;
        }

        AiConnectionCheckStatusText = $"配置不完整：{string.Join("；", errors)}。";
        SettingsStatusMessage = "AI 配置检查发现缺项。";
    }

    private void RefreshAiSettingsRuntimeStatus()
    {
        var options = AiProviderOptions.FromEnvironment();
        AiRuntimeProviderText = string.IsNullOrWhiteSpace(options.RequestedProvider) ? "local" : options.RequestedProvider;
        AiRuntimeModelText = string.IsNullOrWhiteSpace(options.Model) ? "未配置" : options.Model;
        AiApiKeyStatusText = string.IsNullOrWhiteSpace(options.ApiKey) ? "未配置" : "已配置";
        AiEndpointStatusText = string.IsNullOrWhiteSpace(options.BaseUrl) ? "未配置" : "已配置";

        AiModelPreferenceStatusText = string.IsNullOrWhiteSpace(DefaultAiModelInput)
            ? "默认模型偏好未设置；若需固定模型可保存偏好。"
            : string.IsNullOrWhiteSpace(options.Model)
                ? "已保存默认模型偏好；若运行时未配置模型，下一次 AI 请求将使用该偏好。"
                : "已保存默认模型偏好；下一次 AI 请求会优先使用设置页模型覆盖运行时模型。";
    }

    partial void OnDefaultAiModelInputChanged(string value)
    {
        if (AiModelOptions.Contains(value))
        {
            SelectedAiModelOption = value;
        }
        else
        {
            SelectedAiModelOption = "自定义...";
        }
    }
}

