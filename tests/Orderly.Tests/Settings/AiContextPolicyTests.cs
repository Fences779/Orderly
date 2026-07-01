using System;
using System.Collections.Generic;
using System.Reflection;
using Orderly.Core.Models;
using Orderly.Data.Services;
using Xunit;

namespace Orderly.Tests.Settings;

public sealed class AiContextPolicyTests
{
    [Fact]
    public void Customer_profile_toggle_hides_customer_fields_but_keeps_recent_conversation_context()
    {
        var request = BuildRequest();
        var preferences = new AppPreferences
        {
            AiAllowCustomerProfileContext = false,
            AiAllowOrderContext = true
        };

        var result = InvokeApplyContextPolicy(request, preferences);

        Assert.Equal(string.Empty, result.CustomerName);
        Assert.Equal(string.Empty, result.CustomerNickname);
        Assert.Equal(string.Empty, result.CustomerRemark);
        Assert.Equal(request.FocusMessage, result.FocusMessage);
        Assert.Equal(request.RecentMessages.Count, result.RecentMessages.Count);
        Assert.Equal(request.RecentMessages[0].Content, result.RecentMessages[0].Content);
    }

    [Fact]
    public void Order_context_toggle_hides_order_fields_but_keeps_recent_conversation_context()
    {
        var request = BuildRequest();
        var preferences = new AppPreferences
        {
            AiAllowCustomerProfileContext = true,
            AiAllowOrderContext = false
        };

        var result = InvokeApplyContextPolicy(request, preferences);

        Assert.Equal(string.Empty, result.OrderTitle);
        Assert.Equal(string.Empty, result.OrderBudgetText);
        Assert.Equal(string.Empty, result.OrderStatusText);
        Assert.Equal(string.Empty, result.OrderRemark);
        Assert.Equal(request.FocusMessage, result.FocusMessage);
        Assert.Equal(request.RecentMessages.Count, result.RecentMessages.Count);
        Assert.Equal(request.RecentMessages[1].Content, result.RecentMessages[1].Content);
    }

    private static AiSuggestionRequest BuildRequest()
    {
        return new AiSuggestionRequest
        {
            CustomerId = 1,
            OrderId = 2,
            MessageId = 3,
            CustomerName = "张三",
            CustomerNickname = "vip-zhang",
            CustomerRemark = "老客户，偏好暖色包装",
            OrderTitle = "定制手串",
            OrderBudgetText = "¥999",
            OrderStatusText = "待报价",
            OrderRemark = "尽快出稿",
            FocusMessage = "可以先给我看看大概效果吗？",
            RecentMessages = new List<AiSuggestionContextMessage>
            {
                new()
                {
                    RoleLabel = "客户",
                    SenderName = "张三",
                    Content = "可以先给我看看大概效果吗？",
                    MessageTime = new DateTime(2026, 6, 29, 10, 0, 0, DateTimeKind.Utc)
                },
                new()
                {
                    RoleLabel = "我",
                    SenderName = "店主",
                    Content = "可以的，我先确认一下尺寸和预算。",
                    MessageTime = new DateTime(2026, 6, 29, 10, 2, 0, DateTimeKind.Utc)
                }
            }
        };
    }

    private static AiSuggestionRequest InvokeApplyContextPolicy(AiSuggestionRequest request, AppPreferences preferences)
    {
        var method = typeof(LocalAiAssistantService).GetMethod(
            "ApplyContextPolicy",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var args = new object[] { request, preferences, false };
        var result = method!.Invoke(null, args);

        return Assert.IsType<AiSuggestionRequest>(result);
    }
}
