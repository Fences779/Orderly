using System;
using System.Threading;
using System.Windows.Controls;
using Orderly.App.Helpers;
using Xunit;

namespace Orderly.Tests.Ui;

/// <summary>
/// Task 16.2: 置空清空行为单元测试。
/// 验证 <see cref="PasswordBoxBinder"/> 在 ViewModel 字符串被置空时清空控件内容，
/// 且控件→VM 的回写不会触发循环回写（防重入）。
/// <para>
/// <see cref="PasswordBox"/> 是 WPF 控件，必须在 STA 线程上构造与操作；测试项目未引入
/// xunit.stafact，故用 <see cref="Thread"/> 以 <see cref="ApartmentState.STA"/> 运行测试体，
/// 并将断言异常透传回测试线程。
/// </para>
/// </summary>
public sealed class PasswordBoxBinderTests
{
    private static void RunOnSta(Action body)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (captured is not null)
        {
            throw new InvalidOperationException("STA 测试体内发生异常", captured);
        }
    }

    [Fact]
    public void SettingBoundPassword_SyncsControlPassword()
    {
        RunOnSta(() =>
        {
            var box = new PasswordBox();

            PasswordBoxBinder.SetBoundPassword(box, "s3cret!");

            Assert.Equal("s3cret!", box.Password);
        });
    }

    [Fact]
    public void ClearingBoundPasswordToEmpty_ClearsControlPassword()
    {
        RunOnSta(() =>
        {
            var box = new PasswordBox();
            PasswordBoxBinder.SetBoundPassword(box, "s3cret!");
            Assert.Equal("s3cret!", box.Password);

            // VM 字符串被置空（命令成功后重置）→ 控件内容随之清空。
            PasswordBoxBinder.SetBoundPassword(box, string.Empty);

            Assert.Equal(string.Empty, box.Password);
        });
    }

    [Fact]
    public void ClearingBoundPasswordToNull_ClearsControlPassword()
    {
        RunOnSta(() =>
        {
            var box = new PasswordBox();
            PasswordBoxBinder.SetBoundPassword(box, "another-secret");
            Assert.Equal("another-secret", box.Password);

            // null 与 empty 等价处理：控件清空。
            PasswordBoxBinder.SetBoundPassword(box, null!);

            Assert.Equal(string.Empty, box.Password);
        });
    }

    [Fact]
    public void ControlPasswordChange_WritesBackToBoundPassword()
    {
        RunOnSta(() =>
        {
            var box = new PasswordBox();
            // 首次设置非默认值以触发变更回调并挂接 PasswordChanged 订阅。
            PasswordBoxBinder.SetBoundPassword(box, "seed");

            // 模拟用户在控件内输入。
            box.Password = "typed-by-user";

            Assert.Equal("typed-by-user", PasswordBoxBinder.GetBoundPassword(box));
        });
    }

    [Fact]
    public void ControlPasswordChange_DoesNotTriggerCircularWriteback()
    {
        RunOnSta(() =>
        {
            var box = new PasswordBox();
            // 设置非默认值以挂接 PasswordChanged 订阅。
            PasswordBoxBinder.SetBoundPassword(box, "seed");

            // 统计控件回写后 PasswordChanged 的触发次数：防重入应保证用户输入仅触发一次，
            // 控件→VM 回写不会再反向写控件并二次触发 PasswordChanged。
            var changedCount = 0;
            box.PasswordChanged += (_, _) => changedCount++;

            box.Password = "no-loop";

            Assert.Equal(1, changedCount);
            Assert.Equal("no-loop", box.Password);
            Assert.Equal("no-loop", PasswordBoxBinder.GetBoundPassword(box));
        });
    }
}
