using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Orderly.Core.Models;
using Orderly.Core.Services;

namespace Orderly.App.ViewModels;

/// <summary>
/// 「我的页」头像命令与占位逻辑接线（任务 14.4，design §4.5 / §6.2，Req 6.3~6.6）。
///
/// <para>命令清单：更换头像、移除头像。<see cref="ChangeAvatarCommand"/> 经可注入的文件选择委托
/// <see cref="PickAvatarFile"/>（默认使用 <see cref="Microsoft.Win32.OpenFileDialog"/> 图片过滤，便于测试）取得源图片路径 →
/// 经 <see cref="IAvatarStorageService.SaveAvatarAsync"/> 按 <see cref="AvatarConstraints"/> 校验 / 剥离 EXIF /
/// 缩放 / 受保护写入得到 <see cref="AvatarReference"/> → 经 <see cref="IAppSettingRepository"/> 持久化
/// <see cref="AppPreferences.AvatarReference"/> → 即时刷新 <see cref="AvatarImageSource"/>（Req 6.3）。</para>
///
/// <para>拒绝路径（Req 6.6）：捕获 <see cref="AvatarValidationException"/> 后<b>保留原头像</b>（不更新引用、
/// 不改 <see cref="AvatarImageSource"/>），就地提示「图片无效或过大」（<see cref="AvatarStatus"/> + Toast 警告）。</para>
///
/// <para><see cref="LoadAvatarFromPreferencesAsync"/> 在启动 / 刷新时从 <see cref="AppPreferences.AvatarReference"/>
/// 经 <see cref="IAvatarStorageService.ResolveAvatarPath"/> 加载头像（null 引用 → null，由视图渲染占位，Req 6.4 / 6.5）。</para>
///
/// <para>UI 线程与文件占用：<see cref="BitmapImage"/> 以 <see cref="BitmapCacheOption.OnLoad"/> 一次性载入并
/// <c>Freeze</c>，载入后不再持有源文件句柄，避免后续覆盖 / 删除时的文件占用。</para>
/// </summary>
public partial class MeProfileViewModel
{
    /// <summary>
    /// 头像操作就地状态文案（成功 / 拒绝 / 失败，Req 6.6）。拒绝时置「图片无效或过大」。
    /// </summary>
    [ObservableProperty]
    private string avatarStatus = string.Empty;

    /// <summary>
    /// 头像源图片选择委托（可注入，便于测试）。返回所选图片路径；返回 <c>null</c> 或空白表示用户取消。
    /// 未接线（<c>null</c>）时回退到 <see cref="DefaultPickAvatarFile"/>（<see cref="Microsoft.Win32.OpenFileDialog"/> 图片过滤）。
    /// </summary>
    public Func<string?>? PickAvatarFile { get; set; }

    /// <summary>
    /// 默认占位首字（Req 6.5）：从 <see cref="CurrentAccountDisplayName"/> 取首字——中文取首个汉字、
    /// 拉丁取首字母并大写；显示名为空时回退通用占位字符。供任务 18.4 视图绑定渲染「渐变底色 + 首字」占位。
    /// </summary>
    public string AvatarPlaceholderInitial => ComputePlaceholderInitial(CurrentAccountDisplayName);

    private bool CanChangeAvatar() => _avatarService is not null && !IsBusy;

    private bool CanRemoveAvatar() => _avatarService is not null && !IsBusy;

    /// <summary>
    /// 更换头像（Req 6.3 / 6.6）：选图 → 校验 / 处理 / 持久化 → 即时刷新；拒绝时保留原头像并就地提示。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanChangeAvatar))]
    private async Task ChangeAvatarAsync()
    {
        if (_avatarService is null || IsBusy)
        {
            return;
        }

        var sourcePath = (PickAvatarFile ?? DefaultPickAvatarFile).Invoke();
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            // 用户取消选择：保持原头像与状态不变。
            return;
        }

        try
        {
            IsBusy = true;

            // 经存储服务校验（格式 / 大小 / 可解码）→ 剥离 EXIF → 缩放 → 受保护写入，得相对引用键。
            var reference = await _avatarService.SaveAvatarAsync(CurrentAccountId, sourcePath, CancellationToken.None);

            // 持久化引用键到偏好（Req 6.3）：经现有 KV upsert 通道，不引入 schema 迁移。
            await PersistAvatarReferenceAsync(reference.RelativeKey, CancellationToken.None);

            // 即时刷新显示（Req 6.3）：从受保护子目录解析绝对路径并加载位图。
            AvatarImageSource = LoadFrozenBitmap(_avatarService.ResolveAvatarPath(reference));
            AvatarStatus = "头像已更新";
            _toast?.Show("头像已更新", ToastSeverity.Success);
        }
        catch (AvatarValidationException)
        {
            // Req 6.6：拒绝时保留原头像（不更新引用、不改 AvatarImageSource），就地提示。
            AvatarStatus = "图片无效或过大";
            _toast?.Show("图片无效或过大", ToastSeverity.Warning);
        }
        catch (Exception ex)
        {
            // 其它意外失败同样保留原头像，给出就地反馈（不泄露内部异常细节给 Toast 主体）。
            AvatarStatus = $"更换头像失败：{ex.Message}";
            _toast?.Show("更换头像失败，请稍后重试", ToastSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 移除头像（Req 6.4 / 6.5）：删除已存头像文件 → 清空偏好引用 → 置 <see cref="AvatarImageSource"/> 为 null
    /// （由视图回退渲染默认占位）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveAvatar))]
    private async Task RemoveAvatarAsync()
    {
        if (_avatarService is null || IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            await _avatarService.RemoveAvatarAsync(CurrentAccountId, CancellationToken.None);
            await PersistAvatarReferenceAsync(null, CancellationToken.None);

            AvatarImageSource = null;
            AvatarStatus = "已恢复默认头像";
            _toast?.Show("已恢复默认头像", ToastSeverity.Success);
        }
        catch (Exception ex)
        {
            AvatarStatus = $"移除头像失败：{ex.Message}";
            _toast?.Show("移除头像失败，请稍后重试", ToastSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 启动 / 刷新时从 <see cref="AppPreferences.AvatarReference"/> 加载头像（Req 6.4 / 6.5）。
    /// 无引用（null / 空白）→ <see cref="AvatarImageSource"/> 置 null，由视图渲染默认占位。
    /// </summary>
    public async Task LoadAvatarFromPreferencesAsync(CancellationToken cancellationToken = default)
    {
        if (_settingRepository is null || _avatarService is null)
        {
            AvatarImageSource = null;
            return;
        }

        try
        {
            var preferences = await _settingRepository.GetPreferencesAsync(cancellationToken);
            var key = preferences.AvatarReference;
            if (string.IsNullOrWhiteSpace(key))
            {
                AvatarImageSource = null;
                return;
            }

            AvatarImageSource = LoadFrozenBitmap(_avatarService.ResolveAvatarPath(new AvatarReference(key)));
        }
        catch (Exception)
        {
            // 加载失败回退占位，不抛断调用方（头像为非关键展示资源）。
            AvatarImageSource = null;
        }
    }

    // ── 内部帮助 ──

    /// <summary>读取当前偏好、覆盖 <see cref="AppPreferences.AvatarReference"/> 后持久化（最小作用域，仅改头像引用）。</summary>
    private async Task PersistAvatarReferenceAsync(string? relativeKey, CancellationToken cancellationToken)
    {
        if (_settingRepository is null)
        {
            return;
        }

        var preferences = await _settingRepository.GetPreferencesAsync(cancellationToken);
        preferences.AvatarReference = relativeKey;
        await _settingRepository.SavePreferencesAsync(preferences, cancellationToken);
    }

    /// <summary>
    /// 以 <see cref="BitmapCacheOption.OnLoad"/> 一次性载入位图并 <c>Freeze</c>（载入后释放源文件句柄，
    /// 避免覆盖 / 删除时文件占用；冻结后可跨线程安全用作 <see cref="ImageSource"/>）。路径为 null 或文件不存在 → null。
    /// </summary>
    private static ImageSource? LoadFrozenBitmap(string? absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(absolutePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>默认源图片选择器：<see cref="OpenFileDialog"/> 限定图片格式（与 <see cref="AvatarConstraints"/> 一致）。</summary>
    private static string? DefaultPickAvatarFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择头像图片",
            Filter = "图片文件 (*.jpg;*.jpeg;*.png;*.webp)|*.jpg;*.jpeg;*.png;*.webp",
            CheckFileExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    /// <summary>
    /// 计算占位首字（Req 6.5）：取显示名首个文本元素（正确处理代理对 / 组合字符）；
    /// 拉丁字母大写，中文等其它脚本原样返回单字；空显示名回退通用占位字符 <c>·</c>。
    /// </summary>
    private static string ComputePlaceholderInitial(string? displayName)
    {
        var name = displayName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return "·";
        }

        var enumerator = StringInfo.GetTextElementEnumerator(name);
        var firstElement = enumerator.MoveNext() ? (string)enumerator.Current : name.Substring(0, 1);

        // 拉丁取首字母大写；中文（及其它非拉丁）ToUpperInvariant 为无操作，原样返回首个字符。
        return firstElement.ToUpperInvariant();
    }

    /// <summary>显示名变化时同步刷新占位首字（供视图绑定）。</summary>
    partial void OnCurrentAccountDisplayNameChanged(string value)
        => OnPropertyChanged(nameof(AvatarPlaceholderInitial));
}
