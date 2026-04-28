using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface IOcrService
{
    Task<OcrResult> CreateOcrTaskAsync(OcrResult result, CancellationToken cancellationToken = default);
    Task<OcrResult?> CompleteOcrTaskAsync(int id, string extractedText, CancellationToken cancellationToken = default);
    Task<OcrResult?> FailOcrTaskAsync(int id, string errorMessage, CancellationToken cancellationToken = default);
    Task<ConversationMessage> ConvertToConversationMessageAsync(int ocrResultId, string senderName, int? dealId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OcrResult>> ListByCustomerAsync(int customerId, CancellationToken cancellationToken = default);
}
