using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Services;

public sealed partial class QaDataSeeder
{
    public const string QaMarker = QaDataScope.CurrentDisplayMarker;
    public const string QaRuntimeMarker = "[P1_QA_RUNTIME]";
    public const string LegacyQaMarker = "[P1.4.1_QA]";
    public const string LegacyCorruptedQaPrefix = "【P。3——QA";

    private readonly SqliteConnectionFactory _connectionFactory;

    public QaDataSeeder(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public static bool IsRequested(IEnumerable<string>? args = null)
    {
        return args?.Any(IsQaArgument) == true;
    }

    public static bool IsQaMode(IEnumerable<string>? args = null)
    {
        return args?.Any(arg => string.Equals(arg, "--qa-mode", StringComparison.OrdinalIgnoreCase)) == true;
    }

    public async Task<QaSeedResult> SeedIfNeededAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        var now = DateTime.Now;
        var result = new QaSeedResult();
        var customerIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var dealIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var orderIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var messageIds = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var customer in QaCustomers)
        {
            customerIds[customer.Name] = await UpsertCustomerAsync(connection, transaction, customer, now, result, cancellationToken);
        }

        foreach (var deal in QaDeals)
        {
            var customerId = customerIds[deal.CustomerName];
            dealIds[deal.Title] = await UpsertDealAsync(connection, transaction, deal, customerId, now, result, cancellationToken);
        }

        foreach (var order in QaOrders)
        {
            var customerId = customerIds[order.CustomerName];
            int? dealId = string.IsNullOrWhiteSpace(order.DealTitle) ? null : dealIds[order.DealTitle];
            orderIds[order.Title] = await UpsertOrderAsync(connection, transaction, order, customerId, dealId, now, result, cancellationToken);
        }

        foreach (var followUp in QaFollowUps)
        {
            var customerId = customerIds[followUp.CustomerName];
            var orderId = orderIds[followUp.OrderTitle];
            int? dealId = string.IsNullOrWhiteSpace(followUp.DealTitle) ? null : dealIds[followUp.DealTitle];
            await UpsertFollowUpAsync(connection, transaction, followUp, customerId, orderId, dealId, now, result, cancellationToken);
        }

        foreach (var note in QaNotes)
        {
            var customerId = customerIds[note.CustomerName];
            var orderId = orderIds[note.OrderTitle];
            await UpsertNoteAsync(connection, transaction, note, customerId, orderId, now, result, cancellationToken);
        }

        foreach (var adjustment in QaPriceAdjustments)
        {
            var customerId = customerIds[adjustment.CustomerName];
            var orderId = orderIds[adjustment.OrderTitle];
            int? dealId = string.IsNullOrWhiteSpace(adjustment.DealTitle) ? null : dealIds[adjustment.DealTitle];
            await UpsertPriceAdjustmentAsync(connection, transaction, adjustment, customerId, orderId, dealId, now, result, cancellationToken);
        }

        foreach (var activity in QaActivityLogs)
        {
            var customerId = customerIds[activity.CustomerName];
            int? orderId = string.IsNullOrWhiteSpace(activity.OrderTitle) ? null : orderIds[activity.OrderTitle];
            int? dealId = string.IsNullOrWhiteSpace(activity.DealTitle) ? null : dealIds[activity.DealTitle];
            await UpsertActivityLogAsync(connection, transaction, activity, customerId, orderId, dealId, now, result, cancellationToken);
        }

        foreach (var message in QaConversationMessages)
        {
            var customerId = customerIds[message.CustomerName];
            int? orderId = string.IsNullOrWhiteSpace(message.OrderTitle) ? null : orderIds[message.OrderTitle];
            int? dealId = string.IsNullOrWhiteSpace(message.DealTitle) ? null : dealIds[message.DealTitle];
            messageIds[message.RemoteId] = await UpsertConversationMessageAsync(connection, transaction, message, customerId, orderId, dealId, now, result, cancellationToken);
        }

        foreach (var suggestion in QaAiSuggestions)
        {
            var customerId = customerIds[suggestion.CustomerName];
            int? orderId = string.IsNullOrWhiteSpace(suggestion.OrderTitle) ? null : orderIds[suggestion.OrderTitle];
            int? messageId = string.IsNullOrWhiteSpace(suggestion.MessageRemoteId) ? null : messageIds[suggestion.MessageRemoteId];
            await UpsertAiSuggestionAsync(connection, transaction, suggestion, customerId, orderId, messageId, now, result, cancellationToken);
        }

        foreach (var ocrResult in QaOcrResults)
        {
            int? customerId = string.IsNullOrWhiteSpace(ocrResult.CustomerName) ? null : customerIds[ocrResult.CustomerName];
            int? orderId = string.IsNullOrWhiteSpace(ocrResult.OrderTitle) ? null : orderIds[ocrResult.OrderTitle];
            await UpsertOcrResultAsync(connection, transaction, ocrResult, customerId, orderId, now, result, cancellationToken);
        }

        foreach (var syncRecord in QaSyncRecords)
        {
            int entityId = syncRecord.EntityType switch
            {
                "ConversationMessage" => messageIds[syncRecord.EntityRemoteId],
                _ => throw new InvalidOperationException($"Unsupported QA sync entity type: {syncRecord.EntityType}")
            };

            await UpsertSyncRecordAsync(connection, transaction, syncRecord, entityId, now, result, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private static bool IsQaArgument(string arg)
    {
        return string.Equals(arg, "--qa-mode", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--seed-qa-data", StringComparison.OrdinalIgnoreCase);
    }
}
