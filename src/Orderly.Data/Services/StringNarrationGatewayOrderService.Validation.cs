using System.Text.Json;
using Orderly.Core.Models;

namespace Orderly.Data.Services;

public sealed partial class StringNarrationGatewayOrderService
{
    private static StringNarrationExceptionSampleReplayItem ReplayExceptionSample(StringNarrationExceptionSample sample)
    {
        try
        {
            using var document = JsonDocument.Parse(sample.PayloadJson);
            var payloadRoot = GetPayloadRoot(document.RootElement);
            var summaries = ParseSummaryList(payloadRoot);
            var parsed = summaries.Count > 0
                ? summaries
                : [ParseDetail(payloadRoot)];
            var first = parsed.FirstOrDefault();
            if (first is null)
            {
                return new StringNarrationExceptionSampleReplayItem
                {
                    Name = sample.Name,
                    Success = false,
                    ErrorMessage = "No order payload parsed."
                };
            }

            return new StringNarrationExceptionSampleReplayItem
            {
                Name = sample.Name,
                Success = true,
                ParsedOrderCount = parsed.Count,
                OrderNo = first.OrderNo,
                HasException = first.HasException,
                EffectiveCode = first.Exception.EffectiveCode,
                EffectiveReason = first.Exception.EffectiveReason,
                NormalizedPriority = first.Exception.NormalizedPriority,
                NormalizedResolutionStatus = first.Exception.NormalizedResolutionStatus,
                PrioritySortOrder = first.Exception.PrioritySortOrder,
                ResolutionStatusSortOrder = first.Exception.ResolutionStatusSortOrder,
                SlaDueSortTimestamp = first.Exception.SlaDueSortTimestamp,
                DetectedSortTimestamp = first.ExceptionDetectedSortTimestamp,
                SummaryText = first.ExceptionSummaryText
            };
        }
        catch (JsonException ex)
        {
            return new StringNarrationExceptionSampleReplayItem
            {
                Name = sample.Name,
                Success = false,
                ErrorMessage = $"JSON parse failed: {ex.Message}"
            };
        }
        catch (InvalidOperationException ex)
        {
            return new StringNarrationExceptionSampleReplayItem
            {
                Name = sample.Name,
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private static StringNarrationExceptionSnapshot ParseExceptionSnapshot(
        JsonElement root,
        StringNarrationAddressSnapshot address,
        string fulfillmentStatus,
        string wxShippingSyncStatus,
        string trackingNo,
        StringNarrationProductionOrderSnapshot productionOrder,
        IReadOnlyList<StringNarrationWorkOrderSnapshot> workOrders,
        JsonElement fulfillmentElement,
        JsonElement shippingElement,
        JsonElement paymentElement,
        JsonElement productionElement)
    {
        var primaryExceptionElement = GetFirstObjectFromProperty(root, "exception", "exceptions", "risk", "alert", "alertInfo");
        var candidates = new List<JsonElement>();
        if (primaryExceptionElement.ValueKind == JsonValueKind.Object)
        {
            candidates.Add(primaryExceptionElement);
        }

        if (fulfillmentElement.ValueKind == JsonValueKind.Object)
        {
            candidates.Add(fulfillmentElement);
        }

        if (shippingElement.ValueKind == JsonValueKind.Object)
        {
            candidates.Add(shippingElement);
        }

        if (paymentElement.ValueKind == JsonValueKind.Object)
        {
            candidates.Add(paymentElement);
        }

        if (productionElement.ValueKind == JsonValueKind.Object)
        {
            candidates.Add(productionElement);
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            candidates.Add(root);
        }

        var type = ReadString(primaryExceptionElement, "type", "exceptionType", "riskType", "alertType");
        if (string.IsNullOrWhiteSpace(type))
        {
            type = ReadStringFromCandidates(candidates, "exceptionType", "riskType", "alertType");
        }

        var code = ReadString(primaryExceptionElement, "code", "exceptionCode", "riskCode", "alertCode");
        if (string.IsNullOrWhiteSpace(code))
        {
            code = ReadStringFromCandidates(candidates, "exceptionCode", "riskCode", "alertCode");
        }

        var level = ReadString(primaryExceptionElement, "level", "severity", "exceptionLevel", "riskLevel", "alertLevel");
        if (string.IsNullOrWhiteSpace(level))
        {
            level = ReadStringFromCandidates(candidates, "exceptionLevel", "riskLevel", "alertLevel", "severity");
        }

        var status = ReadString(primaryExceptionElement, "status", "exceptionStatus", "riskStatus", "alertStatus");
        if (string.IsNullOrWhiteSpace(status))
        {
            status = ReadStringFromCandidates(candidates, "exceptionStatus", "riskStatus", "alertStatus");
        }

        var source = ReadString(primaryExceptionElement, "source", "module", "from", "system", "exceptionSource", "riskSource", "alertSource");
        if (string.IsNullOrWhiteSpace(source))
        {
            source = ReadStringFromCandidates(candidates, "exceptionSource", "riskSource", "alertSource");
        }

        var reason = ReadString(primaryExceptionElement, "reason", "message", "description", "detail", "exceptionReason", "riskReason", "alertReason");
        if (string.IsNullOrWhiteSpace(reason))
        {
            reason = ReadStringFromCandidates(candidates, "exceptionReason", "riskReason", "alertReason");
        }

        var suggestedAction = ReadString(primaryExceptionElement, "suggestedAction", "action", "suggestion", "nextAction", "advice");
        if (string.IsNullOrWhiteSpace(suggestedAction))
        {
            suggestedAction = ReadStringFromCandidates(candidates, "exceptionSuggestedAction", "riskSuggestedAction", "alertSuggestedAction", "suggestedAction");
        }

        var adminResolutionRemark = ReadString(primaryExceptionElement, "adminResolutionRemark", "resolutionRemark", "resolution", "resolvedRemark", "remark");
        if (string.IsNullOrWhiteSpace(adminResolutionRemark))
        {
            adminResolutionRemark = ReadStringFromCandidates(candidates, "adminResolutionRemark", "resolutionRemark", "resolvedRemark");
        }

        var owner = ReadString(primaryExceptionElement, "owner", "assignee", "handler", "processor", "exceptionOwner", "riskOwner");
        if (string.IsNullOrWhiteSpace(owner))
        {
            owner = ReadStringFromCandidates(
                candidates,
                "owner",
                "assignee",
                "handler",
                "processor",
                "exceptionOwner",
                "riskOwner");
        }

        var assignee = ReadString(primaryExceptionElement, "assignee", "handler", "processor", "owner", "exceptionAssignee", "riskAssignee");
        if (string.IsNullOrWhiteSpace(assignee))
        {
            assignee = ReadStringFromCandidates(
                candidates,
                "assignee",
                "handler",
                "processor",
                "owner",
                "exceptionAssignee",
                "riskAssignee");
        }

        var priority = ReadString(primaryExceptionElement, "priority", "exceptionPriority", "riskPriority", "severityPriority");
        if (string.IsNullOrWhiteSpace(priority))
        {
            priority = ReadStringFromCandidates(candidates, "priority", "exceptionPriority", "riskPriority", "severityPriority");
        }

        var resolutionStatus = ReadString(primaryExceptionElement, "resolutionStatus", "handleStatus", "processStatus", "exceptionProcessStatus");
        if (string.IsNullOrWhiteSpace(resolutionStatus))
        {
            resolutionStatus = ReadStringFromCandidates(candidates, "resolutionStatus", "handleStatus", "processStatus", "exceptionProcessStatus");
        }

        var resolutionAction = ReadString(primaryExceptionElement, "resolutionAction", "handleAction", "processAction", "fixAction");
        if (string.IsNullOrWhiteSpace(resolutionAction))
        {
            resolutionAction = ReadStringFromCandidates(candidates, "resolutionAction", "handleAction", "processAction", "fixAction");
        }

        var resolvedBy = ReadString(primaryExceptionElement, "resolvedBy", "closedBy", "fixedBy", "handlerId");
        if (string.IsNullOrWhiteSpace(resolvedBy))
        {
            resolvedBy = ReadStringFromCandidates(candidates, "resolvedBy", "closedBy", "fixedBy", "handlerId");
        }

        var slaDueAt = ReadLong(primaryExceptionElement, "slaDueAt", "dueAt", "deadlineAt", "handleDueAt");
        if (slaDueAt <= 0)
        {
            slaDueAt = ReadLongFromCandidates(candidates, "slaDueAt", "dueAt", "deadlineAt", "handleDueAt");
        }

        var lastCheckedAt = ReadLong(primaryExceptionElement, "lastCheckedAt", "checkedAt", "lastReviewAt", "reviewedAt");
        if (lastCheckedAt <= 0)
        {
            lastCheckedAt = ReadLongFromCandidates(candidates, "lastCheckedAt", "checkedAt", "lastReviewAt", "reviewedAt");
        }

        var detectedAt = ReadLong(primaryExceptionElement, "detectedAt", "occurredAt", "raisedAt", "createdAt");
        if (detectedAt <= 0)
        {
            detectedAt = ReadLongFromCandidates(candidates, "exceptionDetectedAt", "riskDetectedAt", "alertDetectedAt", "detectedAt", "occurredAt", "raisedAt");
        }

        var resolvedAt = ReadLong(primaryExceptionElement, "resolvedAt", "closedAt", "resolvedTime");
        if (resolvedAt <= 0)
        {
            resolvedAt = ReadLongFromCandidates(candidates, "exceptionResolvedAt", "riskResolvedAt", "alertResolvedAt", "resolvedAt", "closedAt", "resolvedTime");
        }

        var tags = ReadStringArray(primaryExceptionElement, "tags");
        if (tags.Count == 0)
        {
            tags = ReadStringArray(primaryExceptionElement, "labels");
        }

        if (tags.Count == 0)
        {
            tags = ReadStringArrayFromCandidates(candidates, "exceptionTags", "riskTags", "alertTags", "tags", "labels", "tagList");
        }

        if (tags.Count == 0)
        {
            var singleTag = ReadString(primaryExceptionElement, "tag");
            if (string.IsNullOrWhiteSpace(singleTag))
            {
                singleTag = ReadStringFromCandidates(candidates, "exceptionTag", "riskTag", "alertTag", "tag");
            }

            if (!string.IsNullOrWhiteSpace(singleTag))
            {
                tags = new[] { singleTag };
            }
        }

        var normalizedFulfillmentStatus = StringNarrationFulfillmentStatusCatalog.Normalize(fulfillmentStatus);
        var normalizedShippingSyncStatus = NormalizeValue(wxShippingSyncStatus);
        var requiresReceiverContact = FulfillmentStatesRequiringReceiverContact.Contains(normalizedFulfillmentStatus);
        var requiresTrackingNo = FulfillmentStatesRequiringTrackingNo.Contains(normalizedFulfillmentStatus);
        var hasMissingAddress = requiresReceiverContact && !HasAddressCoreData(address);
        var hasMissingReceiverPhone = requiresReceiverContact && string.IsNullOrWhiteSpace(address.ReceiverPhone);
        var hasMissingTrackingNo = requiresTrackingNo && string.IsNullOrWhiteSpace(trackingNo);
        var hasShippingSyncFailure = ShippingSyncFailureStates.Contains(normalizedShippingSyncStatus);
        var requiresProductionOrder = FulfillmentStatesRequiringProductionOrder.Contains(normalizedFulfillmentStatus);
        var requiresWorkOrders = FulfillmentStatesRequiringWorkOrders.Contains(normalizedFulfillmentStatus);
        var hasProductionOrderMissing = requiresProductionOrder && !productionOrder.HasData;
        var hasWorkOrderMissing = requiresWorkOrders && workOrders.Count == 0;

        var explicitHasException = ReadBoolFromCandidates(candidates, "hasException", "isException");
        var explicitRequiresManualReview = ReadBoolFromCandidates(candidates, "requiresManualReview", "needManualReview", "manualReviewRequired");
        var explicitIsResolved = ReadBoolFromCandidates(candidates, "isResolved", "resolved");
        var normalizedStatus = StringNarrationExceptionFieldCatalog.NormalizeResolutionStatus(status);
        var normalizedResolutionStatus = StringNarrationExceptionFieldCatalog.NormalizeResolutionStatus(resolutionStatus);
        var isResolvedByStatus = string.Equals(normalizedStatus, StringNarrationExceptionFieldCatalog.ResolutionResolved, StringComparison.Ordinal)
            || string.Equals(normalizedResolutionStatus, StringNarrationExceptionFieldCatalog.ResolutionResolved, StringComparison.Ordinal);
        var isResolved = explicitIsResolved
            || resolvedAt > 0
            || isResolvedByStatus;
        var hasExceptionSignal = explicitHasException
            || !string.IsNullOrWhiteSpace(type)
            || !string.IsNullOrWhiteSpace(code)
            || !string.IsNullOrWhiteSpace(level)
            || !string.IsNullOrWhiteSpace(reason)
            || !string.IsNullOrWhiteSpace(priority)
            || !string.IsNullOrWhiteSpace(resolutionStatus)
            || !string.IsNullOrWhiteSpace(resolutionAction)
            || primaryExceptionElement.ValueKind == JsonValueKind.Object
            || hasMissingAddress
            || hasMissingReceiverPhone
            || hasMissingTrackingNo
            || hasShippingSyncFailure
            || hasProductionOrderMissing
            || hasWorkOrderMissing
            || string.Equals(normalizedFulfillmentStatus, StringNarrationFulfillmentStatusCatalog.Exception, StringComparison.OrdinalIgnoreCase);
        var requiresManualReview = explicitRequiresManualReview
            || string.Equals(normalizedFulfillmentStatus, StringNarrationFulfillmentStatusCatalog.Exception, StringComparison.OrdinalIgnoreCase)
            || hasMissingAddress
            || hasMissingReceiverPhone
            || hasMissingTrackingNo
            || hasShippingSyncFailure
            || hasProductionOrderMissing
            || hasWorkOrderMissing;
        if (hasExceptionSignal && !isResolved)
        {
            requiresManualReview = true;
        }

        var inferredCode = StringNarrationExceptionFieldCatalog.InferExceptionCode(
            hasExceptionSignal,
            hasMissingAddress,
            hasMissingReceiverPhone,
            hasMissingTrackingNo,
            hasShippingSyncFailure,
            hasProductionOrderMissing,
            hasWorkOrderMissing,
            normalizedFulfillmentStatus);

        if (string.IsNullOrWhiteSpace(code))
        {
            code = inferredCode;
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            type = string.IsNullOrWhiteSpace(code) ? inferredCode : code;
        }

        if (string.IsNullOrWhiteSpace(reason) && hasExceptionSignal)
        {
            reason = StringNarrationExceptionFieldCatalog.GetExceptionReason(
                string.IsNullOrWhiteSpace(code) ? inferredCode : code);
        }

        var auditLogs = ParseExceptionAuditLogs(root, primaryExceptionElement, fulfillmentElement);

        return new StringNarrationExceptionSnapshot
        {
            Type = type,
            Code = code,
            Level = level,
            Status = status,
            Source = source,
            Reason = reason,
            SuggestedAction = suggestedAction,
            AdminResolutionRemark = adminResolutionRemark,
            Owner = owner,
            Assignee = assignee,
            Priority = priority,
            ResolutionStatus = resolutionStatus,
            ResolutionAction = resolutionAction,
            ResolvedBy = resolvedBy,
            SlaDueAt = slaDueAt,
            LastCheckedAt = lastCheckedAt,
            DetectedAt = detectedAt,
            ResolvedAt = resolvedAt,
            HasException = hasExceptionSignal,
            RequiresManualReview = requiresManualReview,
            IsResolved = isResolved,
            HasMissingAddress = hasMissingAddress,
            HasMissingReceiverPhone = hasMissingReceiverPhone,
            HasMissingTrackingNo = hasMissingTrackingNo,
            HasShippingSyncFailure = hasShippingSyncFailure,
            HasProductionOrderMissing = hasProductionOrderMissing,
            HasWorkOrderMissing = hasWorkOrderMissing,
            AuditLogs = auditLogs,
            Tags = tags,
            Raw = primaryExceptionElement.ValueKind == JsonValueKind.Object ? primaryExceptionElement.Clone() : null
        };
    }

    private static IReadOnlyList<StringNarrationExceptionAuditEntry> ParseExceptionAuditLogs(
        JsonElement root,
        JsonElement exceptionElement,
        JsonElement fulfillmentElement)
    {
        var logsElement = GetFirstArray(
            exceptionElement,
            "auditLogs",
            "resolutionLogs",
            "processLogs",
            "handlingLogs",
            "history");

        if (logsElement.ValueKind != JsonValueKind.Array)
        {
            logsElement = GetFirstArray(
                fulfillmentElement,
                "exceptionAuditLogs",
                "exceptionLogs",
                "resolutionLogs",
                "handlingLogs");
        }

        if (logsElement.ValueKind != JsonValueKind.Array)
        {
            logsElement = GetFirstArray(
                root,
                "exceptionAuditLogs",
                "exceptionLogs",
                "resolutionLogs",
                "handlingLogs");
        }

        if (logsElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var logs = new List<StringNarrationExceptionAuditEntry>();
        foreach (var item in logsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            logs.Add(new StringNarrationExceptionAuditEntry
            {
                Action = ReadString(item, "action", "type", "eventType"),
                FromStatus = ReadString(item, "fromStatus", "previousStatus", "beforeStatus"),
                ToStatus = ReadString(item, "toStatus", "nextStatus", "afterStatus", "resolutionStatus"),
                OperatorId = ReadString(item, "operatorId", "handlerId", "resolvedBy"),
                OperatorOpenid = ReadString(item, "operatorOpenid"),
                Remark = ReadString(item, "remark", "note", "comment", "adminResolutionRemark"),
                ResolutionAction = ReadString(item, "resolutionAction", "handleAction", "processAction"),
                Assignee = ReadString(item, "assignee", "owner", "handler"),
                Priority = ReadString(item, "priority"),
                Source = ReadString(item, "source", "from", "system"),
                At = ReadLong(item, "at", "createdAt", "updatedAt", "actionAt"),
                Raw = item.Clone()
            });
        }

        return logs;
    }
}
