using CommunityToolkit.Mvvm.Input;
using System.Text.Json;
using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanAddConversationMessage))]
    private async Task AddConversationMessageAsync()
    {
        var customer = SelectedCustomer;
        if (customer is null)
        {
            ShowNoSelectionMessage();
            return;
        }

        var content = ConversationMessageInput.Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            StatusMessage = "请输入消息内容";
            return;
        }

        var context = GetConversationContext(customer);

        await ExecuteSaveActionAsync(
            busyMessage: "正在保存沟通记录...",
            successMessage: $"沟通记录已写入{context.ContextLabel}",
            errorTitle: "保存沟通记录失败",
            errorStatusPrefix: "保存沟通记录失败",
            action: async () =>
            {
                await _conversationService.SaveMessageAsync(new ConversationMessage
                {
                    CustomerId = customer.Id,
                    OrderId = context.OrderId,
                    DealId = context.DealId,
                    Direction = MessageDirection.Incoming,
                    Channel = MessageChannel.Manual,
                    SenderName = customer.Name,
                    Content = content,
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        source = "manual-entry",
                        stage = "p2.1"
                    })
                });

                ConversationMessageInput = string.Empty;
                await ReloadSelectedCustomerDetailsAsync(customer);
            });
    }

    private ConversationContext GetConversationContext(Customer customer)
    {
        var order = SelectedOrder;
        if (order is not null && order.CustomerId == customer.Id)
        {
            return new ConversationContext(
                customer.Id,
                order.Id,
                order.DealId ?? SelectedDeal?.Id,
                $"当前订单：{order.Title}");
        }

        return new ConversationContext(
            customer.Id,
            null,
            SelectedDeal?.Id,
            $"当前客户：{customer.Name}");
    }

    private readonly record struct ConversationContext(int CustomerId, int? OrderId, int? DealId, string ContextLabel);
}
