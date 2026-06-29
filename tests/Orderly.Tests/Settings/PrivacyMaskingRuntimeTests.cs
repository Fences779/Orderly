using System.Reflection;
using Orderly.App.ViewModels;
using Xunit;

namespace Orderly.Tests.Settings;

public sealed class PrivacyMaskingRuntimeTests
{
    [Fact]
    public void Order_summary_privacy_mask_hides_customer_amount_phone_and_address()
    {
        var text = string.Join(Environment.NewLine, new[]
        {
            "订单：定制手串",
            "客户：张三",
            "金额：¥9,999",
            "状态：已成交",
            "需求：送到上海市浦东新区世纪大道88号",
            "联系方式：13800138000 wx_zhang"
        });

        var masked = InvokeOrderSummaryMask(text, maskPhone: true, maskAddress: true);

        Assert.Contains("客户：[客户已隐藏]", masked);
        Assert.Contains("金额：[金额已隐藏]", masked);
        Assert.Contains("138****8000", masked);
        Assert.Contains("[地址已隐藏]", masked);
        Assert.DoesNotContain("张三", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("¥9,999", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("13800138000", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("上海市浦东新区世纪大道88号", masked, StringComparison.Ordinal);
    }

    private static string InvokeOrderSummaryMask(string text, bool maskPhone, bool maskAddress)
    {
        var method = typeof(MainViewModel).GetMethod(
            "MaskOrderSummaryPrivacy",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return Assert.IsType<string>(method!.Invoke(null, new object[] { text, maskPhone, maskAddress }));
    }
}
