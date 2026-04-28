using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public sealed class ConversationMessageListItem
{
    public ConversationMessageListItem(ConversationMessage message)
    {
        Message = message;
    }

    public ConversationMessage Message { get; }
    public int Id => Message.Id;
    public string SenderName => string.IsNullOrWhiteSpace(Message.SenderName) ? "未命名发送方" : Message.SenderName;
    public string Content => Message.Content;
    public string MessageTimeText => Message.MessageTime.ToString("MM-dd HH:mm");
    public string MetaText => $"{GetDirectionLabel(Message.Direction)} · {GetChannelLabel(Message.Channel)}";

    private static string GetDirectionLabel(MessageDirection direction)
    {
        return direction switch
        {
            MessageDirection.Incoming => "客户来消息",
            MessageDirection.Outgoing => "我方发消息",
            MessageDirection.System => "系统记录",
            _ => direction.ToString()
        };
    }

    private static string GetChannelLabel(MessageChannel channel)
    {
        return channel switch
        {
            MessageChannel.Manual => "手工录入",
            MessageChannel.WeChat => "微信",
            MessageChannel.Xianyu => "闲鱼",
            MessageChannel.Other => "其他",
            _ => channel.ToString()
        };
    }
}
