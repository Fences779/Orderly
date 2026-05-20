param(
    [switch]$SkipReset
)

. (Join-Path $PSScriptRoot 'qa-common.ps1')
Ensure-PowerShellCore -ScriptPath $PSCommandPath -ScriptArguments $args

$resetScript = Join-Path $PSScriptRoot 'reset-qa-data.ps1'

function Invoke-QaScript {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [string[]]$ArgumentList = @()
    )

    & $Path @ArgumentList

    if (-not $?) {
        throw "$([System.IO.Path]::GetFileName($Path)) failed."
    }

    $exitCode = if (Test-Path variable:LASTEXITCODE) { $LASTEXITCODE } else { 0 }
    if ($exitCode -ne 0) {
        throw "$([System.IO.Path]::GetFileName($Path)) failed with exit code: $exitCode"
    }
}

function Import-OrderlyAssemblies {
    $binRoot = Join-RepoPath @('src', 'Orderly.App', 'bin', 'Debug', 'net8.0-windows')
    $nativeRuntimePath = Join-Path $binRoot 'runtimes\\win-x64\\native'
    if (Test-Path -LiteralPath $nativeRuntimePath) {
        $env:PATH = "$nativeRuntimePath;$binRoot;$env:PATH"
        if (-not ('QaNativeLoader' -as [type])) {
            Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class QaNativeLoader
{
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool SetDllDirectory(string lpPathName);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibrary(string lpFileName);
}
"@
        }

        [QaNativeLoader]::SetDllDirectory($nativeRuntimePath) | Out-Null
        $nativeLibrary = Join-Path $nativeRuntimePath 'e_sqlite3.dll'
        if ([QaNativeLoader]::LoadLibrary($nativeLibrary) -eq [IntPtr]::Zero) {
            throw "Failed to preload native SQLite library: $nativeLibrary"
        }
    }

    $assemblyNames = @(
        'SQLitePCLRaw.core.dll',
        'SQLitePCLRaw.provider.e_sqlite3.dll',
        'SQLitePCLRaw.batteries_v2.dll',
        'Microsoft.Data.Sqlite.dll',
        'Orderly.Core.dll',
        'Orderly.Data.dll',
        'Orderly.Infrastructure.dll'
    )

    foreach ($assemblyName in $assemblyNames) {
        $assemblyPath = Join-Path $binRoot $assemblyName
        if (-not (Test-Path -LiteralPath $assemblyPath)) {
            throw "Missing QA dependency assembly: $assemblyPath"
        }

        [System.Reflection.Assembly]::LoadFrom($assemblyPath) | Out-Null
    }

    [SQLitePCL.Batteries_V2]::Init()
}

function New-QaMetadataJson {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Key,
        [hashtable]$Extra = @{}
    )

    $payload = @{
        qa = @{
            tag     = 'p35qa'
            source  = 'runtime'
            key     = $Key
            markers = @('[P1.3_QA]', '[P3.5_QA]')
        }
    }

    foreach ($entry in $Extra.GetEnumerator()) {
        $payload[$entry.Key] = $entry.Value
    }

    return ($payload | ConvertTo-Json -Depth 8 -Compress)
}

function New-P35Context {
    $databasePath = Get-DefaultDatabasePath
    $connectionFactory = [Orderly.Data.Sqlite.SqliteConnectionFactory]::new($databasePath)
    $fieldContext = New-QaFieldEncryptionContext -DatabasePath $databasePath
    $fieldEncryptionService = $fieldContext.FieldEncryptionService
    $customerRepository = [Orderly.Data.Repositories.CustomerRepository]::new($connectionFactory, $fieldEncryptionService)
    $orderRepository = [Orderly.Data.Repositories.OrderRepository]::new($connectionFactory, $fieldEncryptionService)
    $dealRepository = [Orderly.Data.Repositories.DealRepository]::new($connectionFactory, $fieldEncryptionService)
    $followUpRepository = [Orderly.Data.Repositories.FollowUpRepository]::new($connectionFactory, $fieldEncryptionService)
    $messageRepository = [Orderly.Data.Repositories.ConversationMessageRepository]::new($connectionFactory, $fieldEncryptionService)
    $suggestionRepository = [Orderly.Data.Repositories.AiSuggestionRepository]::new($connectionFactory, $fieldEncryptionService)
    $ocrResultRepository = [Orderly.Data.Repositories.OcrResultRepository]::new($connectionFactory, $fieldEncryptionService)
    $activityRepository = [Orderly.Data.Repositories.ActivityLogRepository]::new($connectionFactory, $fieldEncryptionService)
    $priceAdjustmentRepository = [Orderly.Data.Repositories.PriceAdjustmentRepository]::new($connectionFactory, $fieldEncryptionService)
    $workbenchTaskService = [Orderly.Data.Services.LocalWorkbenchTaskService]::new(
        $customerRepository,
        $orderRepository,
        $dealRepository,
        $followUpRepository,
        $messageRepository,
        $suggestionRepository,
        $ocrResultRepository,
        $activityRepository,
        $priceAdjustmentRepository)
    $searchService = [Orderly.Data.Services.LocalGlobalSearchService]::new(
        $customerRepository,
        $orderRepository,
        $dealRepository,
        $followUpRepository,
        $messageRepository,
        $suggestionRepository,
        $ocrResultRepository,
        $activityRepository,
        $priceAdjustmentRepository)

    return [pscustomobject]@{
        CustomerRepository        = $customerRepository
        OrderRepository           = $orderRepository
        DealRepository            = $dealRepository
        FollowUpRepository        = $followUpRepository
        MessageRepository         = $messageRepository
        SuggestionRepository      = $suggestionRepository
        OcrResultRepository       = $ocrResultRepository
        ActivityRepository        = $activityRepository
        PriceAdjustmentRepository = $priceAdjustmentRepository
        WorkbenchTaskService      = $workbenchTaskService
        SearchService             = $searchService
    }
}

function Get-BaselineStatusText {
    return (Invoke-OrderlyAppCommandOrThrow -ArgumentList @('--qa-data-status')).StdOut
}

function New-P35Customer {
    param(
        [Parameter(Mandatory = $true)] $Context,
        [Parameter(Mandatory = $true)] [string]$Key,
        [Parameter(Mandatory = $true)] [string]$Name,
        [Parameter(Mandatory = $true)] [string]$Remark
    )

    $customer = [Orderly.Core.Models.Customer]::new()
    $customer.Name = $Name
    $customer.Status = [Orderly.Core.Models.CustomerStatus]::Active
    $customer.Priority = [Orderly.Core.Models.CustomerPriority]::Normal
    $customer.SourcePlatform = 'QA'
    $customer.Channel = 'P3.5 Smoke'
    $customer.ContactHandle = $Key
    $customer.Phone = "1380000$((Get-Random -Minimum 1000 -Maximum 9999))"
    $customer.Remark = $Remark
    $customer.ExternalId = $Key
    $customer.RemoteId = $Key
    return $Context.CustomerRepository.CreateAsync($customer).GetAwaiter().GetResult()
}

function New-P35Order {
    param(
        [Parameter(Mandatory = $true)] $Context,
        [Parameter(Mandatory = $true)] $Customer,
        [Parameter(Mandatory = $true)] [string]$Key,
        [Parameter(Mandatory = $true)] [string]$Title,
        [Parameter(Mandatory = $true)] [string]$Requirement
    )

    $order = [Orderly.Core.Models.MerchantOrder]::new()
    $order.CustomerId = $Customer.Id
    $order.Title = $Title
    $order.Status = [Orderly.Core.Models.OrderStatus]::PendingCommunication
    $order.Amount = 1888
    $order.Requirement = $Requirement
    $order.SourcePlatform = 'QA'
    $order.Channel = 'P3.5 Smoke'
    $order.ExternalId = $Key
    $order.RemoteId = $Key
    return $Context.OrderRepository.CreateAsync($order).GetAwaiter().GetResult()
}

function New-P35Message {
    param(
        [Parameter(Mandatory = $true)] $Context,
        [Parameter(Mandatory = $true)] $Customer,
        [Parameter(Mandatory = $true)] $Order,
        [Parameter(Mandatory = $true)] [string]$Key,
        [Parameter(Mandatory = $true)] [string]$Content,
        [Parameter(Mandatory = $true)] [datetime]$OccurredAt
    )

    $message = [Orderly.Core.Models.ConversationMessage]::new()
    $message.CustomerId = $Customer.Id
    $message.OrderId = $Order.Id
    $message.Direction = [Orderly.Core.Models.MessageDirection]::Incoming
    $message.Channel = [Orderly.Core.Models.MessageChannel]::Manual
    $message.SenderName = '[P3.5_QA] customer'
    $message.Content = $Content
    $message.MessageTime = $OccurredAt
    $message.SourceMessageId = $Key
    $message.MetadataJson = New-QaMetadataJson -Key $Key
    $message.RemoteId = $Key
    return $Context.MessageRepository.CreateAsync($message).GetAwaiter().GetResult()
}

function New-P35Suggestion {
    param(
        [Parameter(Mandatory = $true)] $Context,
        [Parameter(Mandatory = $true)] $Customer,
        [Parameter(Mandatory = $true)] $Order,
        [Parameter(Mandatory = $true)] $Message,
        [Parameter(Mandatory = $true)] [string]$Key,
        [Parameter(Mandatory = $true)] [string]$Text,
        [Parameter(Mandatory = $true)] [Orderly.Core.Models.AiSuggestionStatus]$Status,
        [string]$AutoReplyState = 'prepared'
    )

    $extra = @{
        autoReply = @{
            mode      = 'local-draft'
            state     = $AutoReplyState
            localOnly = $true
        }
    }

    $suggestion = [Orderly.Core.Models.AiSuggestion]::new()
    $suggestion.CustomerId = $Customer.Id
    $suggestion.OrderId = $Order.Id
    $suggestion.MessageId = $Message.Id
    $suggestion.SuggestionText = $Text
    $suggestion.Reason = $Text
    $suggestion.Status = $Status
    $suggestion.MetadataJson = New-QaMetadataJson -Key $Key -Extra $extra
    $suggestion.RemoteId = $Key
    return $Context.SuggestionRepository.CreateAsync($suggestion).GetAwaiter().GetResult()
}

function New-P35OcrResult {
    param(
        [Parameter(Mandatory = $true)] $Context,
        [Parameter(Mandatory = $true)] $Customer,
        [Parameter(Mandatory = $true)] $Order,
        [Parameter(Mandatory = $true)] [string]$Key,
        [Parameter(Mandatory = $true)] [string]$Text
    )

    $ocrResult = [Orderly.Core.Models.OcrResult]::new()
    $ocrResult.CustomerId = $Customer.Id
    $ocrResult.OrderId = $Order.Id
    $ocrResult.SourcePath = "D:\\qa\\$Key.png"
    $ocrResult.SourceName = "$Key.png"
    $ocrResult.ExtractedText = $Text
    $ocrResult.Status = [Orderly.Core.Models.OcrStatus]::Completed
    $ocrResult.MetadataJson = New-QaMetadataJson -Key $Key -Extra @{ provider = 'local' }
    $ocrResult.RemoteId = $Key
    return $Context.OcrResultRepository.CreateAsync($ocrResult).GetAwaiter().GetResult()
}

function New-P35FollowUp {
    param(
        [Parameter(Mandatory = $true)] $Context,
        [Parameter(Mandatory = $true)] $Customer,
        [Parameter(Mandatory = $true)] $Order,
        [Parameter(Mandatory = $true)] [string]$Key,
        [Parameter(Mandatory = $true)] [string]$Title,
        [Parameter(Mandatory = $true)] [datetime]$ScheduledAt
    )

    $followUp = [Orderly.Core.Models.FollowUp]::new()
    $followUp.CustomerId = $Customer.Id
    $followUp.OrderId = $Order.Id
    $followUp.Title = $Title
    $followUp.Content = $Title
    $followUp.Status = [Orderly.Core.Models.FollowUpStatus]::Pending
    $followUp.ScheduledAt = $ScheduledAt
    $followUp.RemoteId = $Key
    return $Context.FollowUpRepository.CreateAsync($followUp).GetAwaiter().GetResult()
}

function New-P35Activity {
    param(
        [Parameter(Mandatory = $true)] $Context,
        [Parameter(Mandatory = $true)] $Customer,
        $Order,
        [Parameter(Mandatory = $true)] [string]$Key,
        [Parameter(Mandatory = $true)] [string]$Title,
        [Parameter(Mandatory = $true)] [datetime]$OccurredAt,
        [Orderly.Core.Models.ActivityType]$Type = [Orderly.Core.Models.ActivityType]::CustomerUpdated
    )

    $activity = [Orderly.Core.Models.ActivityLog]::new()
    $activity.Type = $Type
    $activity.CustomerId = $Customer.Id
    $activity.OrderId = if ($null -eq $Order) { $null } else { $Order.Id }
    $activity.Title = $Title
    $activity.Description = $Title
    $activity.Operator = 'qa'
    $activity.MetadataJson = New-QaMetadataJson -Key $Key
    $activity.CreatedAt = $OccurredAt
    $activity.UpdatedAt = $OccurredAt
    $activity.RemoteId = $Key
    return $Context.ActivityRepository.CreateAsync($activity).GetAwaiter().GetResult()
}

function Invoke-Search {
    param(
        [Parameter(Mandatory = $true)] $Context,
        [Parameter(Mandatory = $true)] [AllowEmptyString()] [string]$Query,
        [int]$Limit = 50
    )

    $request = [Orderly.Core.Models.SearchRequest]::new()
    $request.Query = $Query
    $request.Limit = $Limit
    return $Context.SearchService.SearchAsync($request).GetAwaiter().GetResult()
}

function Assert-True {
    param(
        [Parameter(Mandatory = $true)] [bool]$Condition,
        [Parameter(Mandatory = $true)] [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-SearchContainsType {
    param(
        [Parameter(Mandatory = $true)] $ResultSet,
        [Parameter(Mandatory = $true)] [string]$TypeName
    )

    $matched = @($ResultSet.Items | Where-Object { $_.Type.ToString() -eq $TypeName })
    if ($matched.Count -eq 0) {
        throw "Expected search result type not found: $TypeName"
    }

    return $matched[0]
}

function Get-RepositoryCountSnapshot {
    param(
        [Parameter(Mandatory = $true)] $Context
    )

    return @{
        Messages    = @($Context.MessageRepository.ListAsync().GetAwaiter().GetResult()).Count
        Suggestions = @($Context.SuggestionRepository.ListAsync().GetAwaiter().GetResult()).Count
        OcrResults  = @($Context.OcrResultRepository.ListAsync().GetAwaiter().GetResult()).Count
        FollowUps   = @($Context.FollowUpRepository.ListAsync().GetAwaiter().GetResult()).Count
        Activities  = @($Context.ActivityRepository.ListAsync().GetAwaiter().GetResult()).Count
    }
}

Assert-NoRunningOrderlyProcess
Write-Step 'Starting P3.5 search and action smoke'
Write-Step 'Scope: local search/workbench projection only, no UI, no public network, no real AI API'
Write-Step "Repo root: $(Get-RepoRoot)"

if (-not $SkipReset) {
    Write-Step 'Step 1/10: reset QA data'
    Invoke-QaScript -Path $resetScript
} else {
    Write-Step 'Step 1/10: skip QA data reset'
}

Write-Step 'Step 2/10: capture baseline QA status'
$baselineStatus = Get-BaselineStatusText

Write-Step 'Step 3/10: import assemblies and prepare search context'
Import-OrderlyAssemblies
$context = New-P35Context

Write-Step 'Step 4/10: create P3.5 search fixtures'
$commonToken = '[P3.5_QA] common needle'
$customer = New-P35Customer -Context $context -Key 'p35qa-customer-001' -Name "[P3.5_QA] Customer needle $commonToken" -Remark "[P3.5_QA] customer remark $commonToken"
$order = New-P35Order -Context $context -Customer $customer -Key 'p35qa-order-001' -Title "[P3.5_QA] Order needle $commonToken" -Requirement "[P3.5_QA] order requirement $commonToken"
$message = New-P35Message -Context $context -Customer $customer -Order $order -Key 'p35qa-message-001' -Content "[P3.5_QA] Message needle $commonToken" -OccurredAt ([DateTime]::Now.AddMinutes(-30))
$suggestion = New-P35Suggestion -Context $context -Customer $customer -Order $order -Message $message -Key 'p35qa-suggestion-001' -Text "[P3.5_QA] Suggestion needle $commonToken" -Status ([Orderly.Core.Models.AiSuggestionStatus]::DraftPrepared)
$ocrResult = New-P35OcrResult -Context $context -Customer $customer -Order $order -Key 'p35qa-ocr-001' -Text "[P3.5_QA] OCR needle $commonToken"
$followUp = New-P35FollowUp -Context $context -Customer $customer -Order $order -Key 'p35qa-followup-001' -Title "[P3.5_QA] FollowUp needle $commonToken" -ScheduledAt ([DateTime]::Today.AddHours(11))
$activity = New-P35Activity -Context $context -Customer $customer -Order $order -Key 'p35qa-activity-001' -Title "[P3.5_QA] Activity needle $commonToken" -OccurredAt ([DateTime]::Now.AddMinutes(-20))
Invoke-QaCiphertextBackfill -DatabasePath (Get-DefaultDatabasePath)
$recentOnlyCustomer = New-P35Customer -Context $context -Key 'p35qa-recent-001' -Name '[P3.5_QA] Recent only customer' -Remark '[P3.5_QA] recent only'
$null = New-P35Activity -Context $context -Customer $recentOnlyCustomer -Order $null -Key 'p35qa-recent-activity-001' -Title '[P3.5_QA] recent only activity' -OccurredAt ([DateTime]::Now.AddMinutes(-5))

Write-Step 'Step 5/10: validate empty and short search queries'
$emptyResult = Invoke-Search -Context $context -Query ''
Assert-True -Condition ($emptyResult.TotalCount -eq 0 -and $emptyResult.Items.Count -eq 0) -Message 'Empty query should return empty search result set.'
$shortResult = Invoke-Search -Context $context -Query 'a'
Assert-True -Condition ($shortResult.TotalCount -eq 0 -and $shortResult.Items.Count -eq 0) -Message 'Query shorter than 2 chars should return empty search result set.'

Write-Step 'Step 6/10: validate entity coverage and result fields'
$customerSearch = Invoke-Search -Context $context -Query 'Customer needle'
$customerHit = Assert-SearchContainsType -ResultSet $customerSearch -TypeName 'Customer'
Assert-True -Condition ($customerHit.TargetSection -eq 'Customer' -and $customerHit.ActionHint -eq 'OpenCustomer') -Message 'Customer search result should include target section and action hint.'

$orderSearch = Invoke-Search -Context $context -Query 'Order needle'
$orderHit = Assert-SearchContainsType -ResultSet $orderSearch -TypeName 'Order'
Assert-True -Condition ($orderHit.TargetSection -eq 'Order' -and $orderHit.ActionHint -eq 'OpenOrder') -Message 'Order search result should include target section and action hint.'

$messageSearch = Invoke-Search -Context $context -Query 'Message needle'
$messageHit = Assert-SearchContainsType -ResultSet $messageSearch -TypeName 'ConversationMessage'
Assert-True -Condition ($messageHit.TargetSection -eq 'Conversation' -and -not [string]::IsNullOrWhiteSpace($messageHit.ActionHint)) -Message 'Conversation message search result should include target section and action hint.'

$suggestionSearch = Invoke-Search -Context $context -Query 'Suggestion needle'
$suggestionHit = Assert-SearchContainsType -ResultSet $suggestionSearch -TypeName 'AiSuggestion'
Assert-True -Condition ($suggestionHit.TargetSection -eq 'AiSuggestion' -and -not [string]::IsNullOrWhiteSpace($suggestionHit.ActionHint)) -Message 'AI suggestion search result should include target section and action hint.'

$ocrSearch = Invoke-Search -Context $context -Query 'OCR needle'
$ocrHit = Assert-SearchContainsType -ResultSet $ocrSearch -TypeName 'OcrResult'
Assert-True -Condition ($ocrHit.TargetSection -eq 'Ocr' -and -not [string]::IsNullOrWhiteSpace($ocrHit.ActionHint)) -Message 'OCR search result should include target section and action hint.'

$followUpSearch = Invoke-Search -Context $context -Query 'FollowUp needle'
$followUpHit = Assert-SearchContainsType -ResultSet $followUpSearch -TypeName 'FollowUp'
Assert-True -Condition ($followUpHit.TargetSection -eq 'FollowUp' -and -not [string]::IsNullOrWhiteSpace($followUpHit.ActionHint)) -Message 'FollowUp search result should include target section and action hint.'

$activitySearch = Invoke-Search -Context $context -Query 'Activity needle'
$activityHit = Assert-SearchContainsType -ResultSet $activitySearch -TypeName 'ActivityLog'
Assert-True -Condition (-not [string]::IsNullOrWhiteSpace($activityHit.TargetSection) -and -not [string]::IsNullOrWhiteSpace($activityHit.ActionHint)) -Message 'Activity log search result should include target section and action hint.'

Write-Step 'Step 7/10: validate unified search coverage and stable ordering'
$commonSearch1 = Invoke-Search -Context $context -Query 'common needle'
$commonSearch2 = Invoke-Search -Context $context -Query 'common needle'
foreach ($typeName in @('Customer', 'Order', 'ConversationMessage', 'AiSuggestion', 'OcrResult', 'FollowUp', 'ActivityLog')) {
    $null = Assert-SearchContainsType -ResultSet $commonSearch1 -TypeName $typeName
}

$firstIds = @($commonSearch1.Items | Select-Object -ExpandProperty Id)
$secondIds = @($commonSearch2.Items | Select-Object -ExpandProperty Id)
Assert-True -Condition (($firstIds -join '|') -eq ($secondIds -join '|')) -Message 'Search result ordering should be stable across repeated reads.'

Write-Step 'Step 8/10: validate workbench task filter and query overloads'
$allTasks = @($context.WorkbenchTaskService.GetTasksAsync().GetAwaiter().GetResult())
$draftTask = @($allTasks | Where-Object { $_.Type.ToString() -eq 'DraftNotSent' -and $_.AiSuggestionId -eq $suggestion.Id }) | Select-Object -First 1
Assert-True -Condition ($null -ne $draftTask) -Message 'Expected DraftNotSent workbench task for P3.5 suggestion.'
$recentTask = @($allTasks | Where-Object { $_.Type.ToString() -eq 'RecentlyActiveCustomer' -and $_.CustomerId -eq $recentOnlyCustomer.Id }) | Select-Object -First 1
Assert-True -Condition ($null -ne $recentTask) -Message 'Expected RecentlyActiveCustomer task for recent-only customer.'

$draftFilter = [Orderly.Core.Models.WorkbenchTaskFilter]::new()
$draftFilter.TaskType = [Orderly.Core.Models.WorkbenchTaskType]::DraftNotSent
$draftOnlyTasks = @($context.WorkbenchTaskService.GetTasksAsync($draftFilter).GetAwaiter().GetResult())
Assert-True -Condition ($draftOnlyTasks.Count -gt 0 -and @($draftOnlyTasks | Where-Object { $_.Type.ToString() -ne 'DraftNotSent' }).Count -eq 0) -Message 'Workbench task filter should support TaskType filtering.'

$hideRecentFilter = [Orderly.Core.Models.WorkbenchTaskFilter]::new()
$hideRecentFilter.IncludeRecentlyActive = $false
$withoutRecentTasks = @($context.WorkbenchTaskService.GetTasksAsync($hideRecentFilter).GetAwaiter().GetResult())
Assert-True -Condition (@($withoutRecentTasks | Where-Object { $_.Type.ToString() -eq 'RecentlyActiveCustomer' }).Count -eq 0) -Message 'Workbench task filter should exclude RecentlyActiveCustomer when requested.'

$queryModel = [Orderly.Core.Models.WorkbenchTaskQuery]::new()
$queryModel.Filter = $draftFilter
$queryModel.Limit = 1
$limitedDraftTasks = @($context.WorkbenchTaskService.GetTasksAsync($queryModel).GetAwaiter().GetResult())
Assert-True -Condition ($limitedDraftTasks.Count -eq 1 -and $limitedDraftTasks[0].Type.ToString() -eq 'DraftNotSent') -Message 'Workbench task query overload should support filter + limit.'

Write-Step 'Step 9/10: validate quick actions are projected without side effects'
$countBefore = Get-RepositoryCountSnapshot -Context $context
$draftActions = @($draftTask.QuickActions | Select-Object -ExpandProperty Type | ForEach-Object { $_.ToString() })
Assert-True -Condition ($draftActions -contains 'ReviewDraft' -and $draftActions -contains 'CopyDraft' -and $draftActions -contains 'MarkSent') -Message 'DraftNotSent quick actions are incomplete.'

$ocrTask = @($allTasks | Where-Object { $_.Type.ToString() -eq 'OcrNotConverted' -and $_.OcrResultId -eq $ocrResult.Id }) | Select-Object -First 1
Assert-True -Condition ($null -ne $ocrTask) -Message 'Expected OcrNotConverted task for P3.5 OCR result.'
$ocrActions = @($ocrTask.QuickActions | Select-Object -ExpandProperty Type | ForEach-Object { $_.ToString() })
Assert-True -Condition ($ocrActions -contains 'ConvertOcrToMessage') -Message 'OcrNotConverted quick action is missing.'

$followUpTask = @($allTasks | Where-Object { $_.FollowUpId -eq $followUp.Id }) | Select-Object -First 1
Assert-True -Condition ($null -ne $followUpTask) -Message 'Expected follow-up task for P3.5 follow-up.'
$followUpActions = @($followUpTask.QuickActions | Select-Object -ExpandProperty Type | ForEach-Object { $_.ToString() })
Assert-True -Condition ($followUpActions -contains 'CompleteFollowUp' -and $followUpActions -contains 'SnoozeFollowUp') -Message 'FollowUp quick actions are incomplete.'

$replyTask = @($allTasks | Where-Object { $_.Type.ToString() -eq 'ReplyNeeded' -and $_.MessageId -eq $message.Id }) | Select-Object -First 1
Assert-True -Condition ($null -ne $replyTask) -Message 'Expected ReplyNeeded task for P3.5 message.'
$replyActions = @($replyTask.QuickActions | Select-Object -ExpandProperty Type | ForEach-Object { $_.ToString() })
Assert-True -Condition ($replyActions -contains 'ReplyToCustomer') -Message 'ReplyNeeded quick action is missing.'

$countAfter = Get-RepositoryCountSnapshot -Context $context
Assert-True -Condition (($countBefore | ConvertTo-Json -Compress) -eq ($countAfter | ConvertTo-Json -Compress)) -Message 'Generating quick actions should not mutate repository counts.'

Write-Step 'Step 10/10: reset QA data and ensure baseline is restored'
Invoke-QaScript -Path $resetScript
$restoredStatus = Get-BaselineStatusText
Assert-True -Condition ($baselineStatus -eq $restoredStatus) -Message 'QA baseline status changed after reset-qa-data.'

Write-Host ''
Write-Host 'P3.5 SEARCH/ACTION SMOKE: PASS'
Write-Host ('Search result count for common needle: ' + $commonSearch1.TotalCount)
Write-Host ('Workbench task count: ' + $allTasks.Count)
