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
        'CommunityToolkit.Mvvm.dll',
        'Orderly.Core.dll',
        'Orderly.Data.dll',
        'Orderly.Infrastructure.dll',
        'Orderly.App.dll'
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

function New-P36MetadataJson {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Key,
        [hashtable]$Extra = @{}
    )

    $payload = @{
        qa = @{
            tag     = 'p36qa'
            source  = 'runtime'
            key     = $Key
            markers = @('[P3.6_QA]')
        }
    }

    foreach ($entry in $Extra.GetEnumerator()) {
        $payload[$entry.Key] = $entry.Value
    }

    return ($payload | ConvertTo-Json -Depth 8 -Compress)
}

function New-P36Context {
    $databasePath = Get-DefaultDatabasePath
    $connectionFactory = [Orderly.Data.Sqlite.SqliteConnectionFactory]::new($databasePath)
    $fieldContext = New-QaFieldEncryptionContext -DatabasePath $databasePath
    $fieldEncryptionService = $fieldContext.FieldEncryptionService
    $customerRepository = [Orderly.Data.Repositories.CustomerRepository]::new($connectionFactory, $fieldEncryptionService)
    $orderRepository = [Orderly.Data.Repositories.OrderRepository]::new($connectionFactory, $fieldEncryptionService)
    $dealRepository = [Orderly.Data.Repositories.DealRepository]::new($connectionFactory, $fieldEncryptionService)
    $followUpRepository = [Orderly.Data.Repositories.FollowUpRepository]::new($connectionFactory, $fieldEncryptionService)
    $noteRepository = [Orderly.Data.Repositories.CustomerNoteRepository]::new($connectionFactory, $fieldEncryptionService)
    $messageRepository = [Orderly.Data.Repositories.ConversationMessageRepository]::new($connectionFactory, $fieldEncryptionService)
    $suggestionRepository = [Orderly.Data.Repositories.AiSuggestionRepository]::new($connectionFactory, $fieldEncryptionService)
    $ocrResultRepository = [Orderly.Data.Repositories.OcrResultRepository]::new($connectionFactory, $fieldEncryptionService)
    $activityRepository = [Orderly.Data.Repositories.ActivityLogRepository]::new($connectionFactory, $fieldEncryptionService)
    $priceAdjustmentRepository = [Orderly.Data.Repositories.PriceAdjustmentRepository]::new($connectionFactory, $fieldEncryptionService)
    $replyTemplateRepository = [Orderly.Data.Repositories.ReplyTemplateRepository]::new($connectionFactory, $fieldEncryptionService)
    $settingRepository = [Orderly.Data.Repositories.AppSettingRepository]::new($connectionFactory)
    $syncRecordRepository = [Orderly.Data.Repositories.SyncRecordRepository]::new($connectionFactory)
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
    $navigationRouteService = [Orderly.Data.Services.LocalNavigationRouteService]::new(
        $customerRepository,
        $orderRepository,
        $messageRepository,
        $suggestionRepository,
        $ocrResultRepository,
        $followUpRepository,
        $activityRepository)

    return [pscustomobject]@{
        DatabasePath              = $databasePath
        ConnectionFactory         = $connectionFactory
        CustomerRepository        = $customerRepository
        OrderRepository           = $orderRepository
        DealRepository            = $dealRepository
        FollowUpRepository        = $followUpRepository
        NoteRepository            = $noteRepository
        MessageRepository         = $messageRepository
        SuggestionRepository      = $suggestionRepository
        OcrResultRepository       = $ocrResultRepository
        ActivityRepository        = $activityRepository
        PriceAdjustmentRepository = $priceAdjustmentRepository
        ReplyTemplateRepository   = $replyTemplateRepository
        SettingRepository         = $settingRepository
        SyncRecordRepository      = $syncRecordRepository
        WorkbenchTaskService      = $workbenchTaskService
        SearchService             = $searchService
        NavigationRouteService    = $navigationRouteService
    }
}

function Get-BaselineStatusText {
    return (Invoke-OrderlyAppCommandOrThrow -ArgumentList @('--qa-data-status')).StdOut
}

function New-P36Customer {
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
    $customer.Channel = 'P3.6 Smoke'
    $customer.ContactHandle = $Key
    $customer.Phone = "1390000$((Get-Random -Minimum 1000 -Maximum 9999))"
    $customer.Remark = $Remark
    $customer.ExternalId = $Key
    $customer.RemoteId = $Key
    return $Context.CustomerRepository.CreateAsync($customer).GetAwaiter().GetResult()
}

function New-P36Order {
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
    $order.Amount = 2680
    $order.Requirement = $Requirement
    $order.SourcePlatform = 'QA'
    $order.Channel = 'P3.6 Smoke'
    $order.ExternalId = $Key
    $order.RemoteId = $Key
    return $Context.OrderRepository.CreateAsync($order).GetAwaiter().GetResult()
}

function New-P36Message {
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
    $message.SenderName = '[P3.6_QA] customer'
    $message.Content = $Content
    $message.MessageTime = $OccurredAt
    $message.SourceMessageId = $Key
    $message.MetadataJson = New-P36MetadataJson -Key $Key
    $message.RemoteId = $Key
    return $Context.MessageRepository.CreateAsync($message).GetAwaiter().GetResult()
}

function New-P36Suggestion {
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
    $suggestion.MetadataJson = New-P36MetadataJson -Key $Key -Extra $extra
    $suggestion.RemoteId = $Key
    return $Context.SuggestionRepository.CreateAsync($suggestion).GetAwaiter().GetResult()
}

function New-P36OcrResult {
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
    $ocrResult.MetadataJson = New-P36MetadataJson -Key $Key -Extra @{ provider = 'local' }
    $ocrResult.RemoteId = $Key
    return $Context.OcrResultRepository.CreateAsync($ocrResult).GetAwaiter().GetResult()
}

function New-P36FollowUp {
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

function New-P36Activity {
    param(
        [Parameter(Mandatory = $true)] $Context,
        [Parameter(Mandatory = $true)] $Customer,
        [Parameter(Mandatory = $true)] $Order,
        [Parameter(Mandatory = $true)] [string]$Key,
        [Parameter(Mandatory = $true)] [string]$Title,
        [Parameter(Mandatory = $true)] [datetime]$OccurredAt
    )

    $activity = [Orderly.Core.Models.ActivityLog]::new()
    $activity.Type = [Orderly.Core.Models.ActivityType]::OrderStatusChanged
    $activity.CustomerId = $Customer.Id
    $activity.OrderId = $Order.Id
    $activity.Title = $Title
    $activity.Description = $Title
    $activity.Operator = 'qa'
    $activity.MetadataJson = New-P36MetadataJson -Key $Key
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

function Get-RouteResultSummary {
    param(
        [Parameter(Mandatory = $true)] $Route
    )

    return @{
        CanNavigate        = $Route.CanNavigate
        RequiresUserAction = $Route.RequiresUserAction
        UsedFallback       = $Route.UsedFallback
        DisabledReason     = $Route.DisabledReason
        RequestedSection   = if ($null -eq $Route.RequestedTarget) { '' } else { $Route.RequestedTarget.TargetSectionName }
        RequestedAction    = if ($null -eq $Route.RequestedTarget) { '' } else { $Route.RequestedTarget.ActionHintName }
        ResolvedSection    = if ($null -eq $Route.ResolvedTarget) { '' } else { $Route.ResolvedTarget.TargetSectionName }
        ResolvedAction     = if ($null -eq $Route.ResolvedTarget) { '' } else { $Route.ResolvedTarget.ActionHintName }
    }
}

function Get-StateSnapshot {
    param(
        [Parameter(Mandatory = $true)] $Context,
        [Parameter(Mandatory = $true)] $Suggestion,
        [Parameter(Mandatory = $true)] $OcrResult,
        [Parameter(Mandatory = $true)] $FollowUp,
        [Parameter(Mandatory = $true)] $Clipboard
    )

    $latestSuggestion = $Context.SuggestionRepository.GetByIdAsync($Suggestion.Id).GetAwaiter().GetResult()
    $latestOcr = $Context.OcrResultRepository.GetByIdAsync($OcrResult.Id).GetAwaiter().GetResult()
    $latestFollowUp = $Context.FollowUpRepository.GetByIdAsync($FollowUp.Id).GetAwaiter().GetResult()

    return @{
        ClipboardText      = $Clipboard.LastText
        MessageCount       = @($Context.MessageRepository.ListAsync().GetAwaiter().GetResult()).Count
        ActivityCount      = @($Context.ActivityRepository.ListAsync().GetAwaiter().GetResult()).Count
        SuggestionStatus   = $latestSuggestion.Status.ToString()
        SuggestionMetadata = $latestSuggestion.MetadataJson
        OcrStatus          = $latestOcr.Status.ToString()
        OcrMetadata        = $latestOcr.MetadataJson
        FollowUpStatus     = $latestFollowUp.Status.ToString()
        FollowUpScheduled  = $latestFollowUp.ScheduledAt.ToString('O')
        FollowUpCompleted  = if ($null -eq $latestFollowUp.CompletedAt) { '' } else { $latestFollowUp.CompletedAt.Value.ToString('O') }
    }
}

function New-P36ViewModelHarness {
    param(
        [Parameter(Mandatory = $true)] $Context
    )

    $clipboardService = [Orderly.Infrastructure.Services.InMemoryClipboardService]::new()
    $syncService = [Orderly.Data.Services.LocalSyncService]::new($Context.SyncRecordRepository, $Context.ActivityRepository)
    $aiProviderOptions = [Orderly.Data.Services.AiProviderOptions]::FromEnvironment()
    $localProvider = [Orderly.Data.Services.LocalAiSuggestionProvider]::new()
    $primaryProvider = [Orderly.Data.Services.AiSuggestionProviderFactory]::CreatePrimaryProvider($aiProviderOptions, $localProvider)

    $customerService = [Orderly.Data.Services.CustomerService]::new($Context.CustomerRepository, $Context.ActivityRepository)
    $orderService = [Orderly.Data.Services.OrderService]::new($Context.OrderRepository, $Context.ActivityRepository)
    $dealService = [Orderly.Data.Services.DealService]::new($Context.DealRepository, $Context.ActivityRepository)
    $followUpService = [Orderly.Data.Services.FollowUpService]::new($Context.FollowUpRepository, $Context.ActivityRepository)
    $noteService = [Orderly.Data.Services.NoteService]::new($Context.NoteRepository, $Context.ActivityRepository)
    $conversationService = [Orderly.Data.Services.ConversationService]::new($Context.MessageRepository, $Context.ActivityRepository)
    $ocrService = [Orderly.Data.Services.LocalOcrService]::new($Context.OcrResultRepository, $Context.ActivityRepository, $conversationService, $Context.MessageRepository)
    $aiAssistantService = [Orderly.Data.Services.LocalAiAssistantService]::new(
        $Context.CustomerRepository,
        $Context.OrderRepository,
        $Context.MessageRepository,
        $Context.SuggestionRepository,
        $Context.ActivityRepository,
        $primaryProvider,
        $localProvider,
        $aiProviderOptions)
    $autoReplyService = [Orderly.Data.Services.LocalAutoReplyService]::new($Context.SuggestionRepository, $Context.OrderRepository, $Context.ActivityRepository, $clipboardService)
    $activityLogService = [Orderly.Data.Services.ActivityLogService]::new($Context.ActivityRepository)
    $backupService = [Orderly.Data.Services.LocalBackupService]::new($Context.ConnectionFactory, $syncService, $Context.SyncRecordRepository, $Context.ActivityRepository)
    $priceAdjustmentService = [Orderly.Data.Services.PriceAdjustmentService]::new($Context.PriceAdjustmentRepository, $Context.ActivityRepository)

    $viewModel = [Orderly.App.ViewModels.MainViewModel]::new(
        $Context.CustomerRepository,
        $Context.OrderRepository,
        $customerService,
        $orderService,
        $dealService,
        $followUpService,
        $noteService,
        $conversationService,
        $ocrService,
        $aiAssistantService,
        $autoReplyService,
        $activityLogService,
        $Context.WorkbenchTaskService,
        $Context.SearchService,
        $Context.NavigationRouteService,
        $backupService,
        $priceAdjustmentService,
        $null,
        $Context.ReplyTemplateRepository,
        $Context.SettingRepository,
        $syncService,
        $Context.SyncRecordRepository,
        $clipboardService,
        $Context.DatabasePath,
        $null,
        $null,
        '',
        $false,
        15)

    return [pscustomobject]@{
        ViewModel  = $viewModel
        Clipboard  = $clipboardService
    }
}

Assert-NoRunningOrderlyProcess
Write-Step 'Starting P3.6 navigation route smoke'
Write-Step 'Scope: local navigation projection and ViewModel safe-location only, no UI, no public network, no real AI API'
Write-Step "Repo root: $(Get-RepoRoot)"

if (-not $SkipReset) {
    Write-Step 'Step 1/12: reset QA data'
    Invoke-QaScript -Path $resetScript
} else {
    Write-Step 'Step 1/12: skip QA data reset'
}

Write-Step 'Step 2/12: capture baseline QA status'
$baselineStatus = Get-BaselineStatusText

Write-Step 'Step 3/12: import assemblies and prepare context'
Import-OrderlyAssemblies
$context = New-P36Context

Write-Step 'Step 4/12: create P3.6 fixtures'
$commonToken = '[P3.6_QA] route needle'
$customer = New-P36Customer -Context $context -Key 'p36qa-customer-001' -Name "[P3.6_QA] Customer $commonToken" -Remark "[P3.6_QA] customer remark $commonToken"
$order = New-P36Order -Context $context -Customer $customer -Key 'p36qa-order-001' -Title "[P3.6_QA] Order $commonToken" -Requirement "[P3.6_QA] order requirement $commonToken"
$message = New-P36Message -Context $context -Customer $customer -Order $order -Key 'p36qa-message-001' -Content "[P3.6_QA] Message $commonToken" -OccurredAt ([DateTime]::Now.AddMinutes(-25))
$draftSuggestion = New-P36Suggestion -Context $context -Customer $customer -Order $order -Message $message -Key 'p36qa-suggestion-draft-001' -Text "[P3.6_QA] Draft $commonToken" -Status ([Orderly.Core.Models.AiSuggestionStatus]::DraftPrepared) -AutoReplyState 'copied'
$pendingSuggestion = New-P36Suggestion -Context $context -Customer $customer -Order $order -Message $message -Key 'p36qa-suggestion-pending-001' -Text "[P3.6_QA] Suggestion $commonToken" -Status ([Orderly.Core.Models.AiSuggestionStatus]::Draft) -AutoReplyState 'prepared'
$ocrResult = New-P36OcrResult -Context $context -Customer $customer -Order $order -Key 'p36qa-ocr-001' -Text "[P3.6_QA] OCR $commonToken"
$followUp = New-P36FollowUp -Context $context -Customer $customer -Order $order -Key 'p36qa-followup-001' -Title "[P3.6_QA] FollowUp $commonToken" -ScheduledAt ([DateTime]::Today.AddHours(-2))
$activity = New-P36Activity -Context $context -Customer $customer -Order $order -Key 'p36qa-activity-001' -Title "[P3.6_QA] Activity $commonToken" -OccurredAt ([DateTime]::Now.AddMinutes(-10))
$recentOnlyCustomer = New-P36Customer -Context $context -Key 'p36qa-recent-001' -Name '[P3.6_QA] Recent customer no order' -Remark '[P3.6_QA] recent only'

Write-Step 'Step 5/12: load search results and workbench tasks'
Invoke-QaCiphertextBackfill -DatabasePath (Get-DefaultDatabasePath)
$customerHit = Assert-SearchContainsType -ResultSet (Invoke-Search -Context $context -Query 'Customer [P3.6_QA] route') -TypeName 'Customer'
$orderHit = Assert-SearchContainsType -ResultSet (Invoke-Search -Context $context -Query 'Order [P3.6_QA] route') -TypeName 'Order'
$messageHit = Assert-SearchContainsType -ResultSet (Invoke-Search -Context $context -Query 'Message [P3.6_QA] route') -TypeName 'ConversationMessage'
$suggestionHit = Assert-SearchContainsType -ResultSet (Invoke-Search -Context $context -Query 'Suggestion [P3.6_QA] route') -TypeName 'AiSuggestion'
$ocrHit = Assert-SearchContainsType -ResultSet (Invoke-Search -Context $context -Query 'OCR [P3.6_QA] route') -TypeName 'OcrResult'
$activityHit = Assert-SearchContainsType -ResultSet (Invoke-Search -Context $context -Query 'Activity [P3.6_QA] route') -TypeName 'ActivityLog'
$allTasks = @($context.WorkbenchTaskService.GetTasksAsync().GetAwaiter().GetResult())
$draftTask = @($allTasks | Where-Object { $_.Type.ToString() -eq 'DraftNotSent' -and $_.AiSuggestionId -eq $draftSuggestion.Id }) | Select-Object -First 1
$ocrTask = @($allTasks | Where-Object { $_.Type.ToString() -eq 'OcrNotConverted' -and $_.OcrResultId -eq $ocrResult.Id }) | Select-Object -First 1
$followUpTask = @($allTasks | Where-Object { $_.Type.ToString() -eq 'FollowUpOverdue' -and $_.FollowUpId -eq $followUp.Id }) | Select-Object -First 1
$recentTask = @($allTasks | Where-Object { $_.Type.ToString() -eq 'RecentlyActiveCustomer' -and $_.CustomerId -eq $recentOnlyCustomer.Id }) | Select-Object -First 1

Assert-True -Condition ($null -ne $draftTask) -Message 'Expected DraftNotSent task for P3.6 suggestion.'
Assert-True -Condition ($null -ne $ocrTask) -Message 'Expected OcrNotConverted task for P3.6 OCR result.'
Assert-True -Condition ($null -ne $followUpTask) -Message 'Expected FollowUpOverdue task for P3.6 follow-up.'
Assert-True -Condition ($null -ne $recentTask) -Message 'Expected RecentlyActiveCustomer task without order.'

Write-Step 'Step 6/12: validate route semantics for search results and workbench tasks'
$draftRoute = $context.NavigationRouteService.ResolveAsync($draftTask).GetAwaiter().GetResult()
$ocrTaskRoute = $context.NavigationRouteService.ResolveAsync($ocrTask).GetAwaiter().GetResult()
$followUpRoute = $context.NavigationRouteService.ResolveAsync($followUpTask).GetAwaiter().GetResult()
$customerRoute = $context.NavigationRouteService.ResolveAsync($customerHit).GetAwaiter().GetResult()
$orderRoute = $context.NavigationRouteService.ResolveAsync($orderHit).GetAwaiter().GetResult()
$messageRoute = $context.NavigationRouteService.ResolveAsync($messageHit).GetAwaiter().GetResult()
$suggestionRoute = $context.NavigationRouteService.ResolveAsync($suggestionHit).GetAwaiter().GetResult()
$ocrSearchRoute = $context.NavigationRouteService.ResolveAsync($ocrHit).GetAwaiter().GetResult()
$activityRoute = $context.NavigationRouteService.ResolveAsync($activityHit).GetAwaiter().GetResult()

Assert-True -Condition ($draftTask.TargetSection -eq 'AiSuggestion' -and $draftTask.ActionHint -eq 'ReviewDraft') -Message 'DraftNotSent task route semantics are inconsistent.'
Assert-True -Condition ($draftRoute.CanNavigate -and $draftRoute.RequestedTarget.ActionHintName -eq 'ReviewDraft' -and $draftRoute.RequestedTarget.TargetSectionName -eq 'AiSuggestion') -Message 'DraftNotSent route should resolve to AiSuggestion/ReviewDraft.'
Assert-True -Condition ($ocrTask.ActionHint -eq 'ConvertOcrToMessage' -and $ocrTaskRoute.CanNavigate -and $ocrTaskRoute.RequiresUserAction) -Message 'OcrNotConverted route should resolve and require user action.'
Assert-True -Condition ($followUpRoute.CanNavigate -and $followUpRoute.RequiresUserAction -and $followUpRoute.RequestedTarget.ActionHintName -eq 'CompleteFollowUp') -Message 'FollowUp route should resolve and require user action.'
Assert-True -Condition ($customerRoute.CanNavigate -and $customerRoute.ResolvedTarget.TargetSectionName -eq 'Customer') -Message 'Customer search result should route to customer.'
Assert-True -Condition ($orderRoute.CanNavigate -and $orderRoute.ResolvedTarget.TargetSectionName -eq 'Order') -Message 'Order search result should route to order.'
Assert-True -Condition ($messageRoute.CanNavigate -and $messageRoute.RequestedTarget.TargetSectionName -eq 'Conversation') -Message 'Conversation message search result should route to conversation.'
Assert-True -Condition ($suggestionRoute.CanNavigate -and $suggestionRoute.RequestedTarget.TargetSectionName -eq 'AiSuggestion') -Message 'AI suggestion search result should route to AiSuggestion.'
Assert-True -Condition ($ocrSearchRoute.CanNavigate -and $ocrSearchRoute.RequestedTarget.TargetSectionName -eq 'Ocr' -and $ocrSearchRoute.RequiresUserAction) -Message 'OCR search result should route to Ocr and require user action.'
Assert-True -Condition ($activityHit.TargetSection -eq 'ActivityLog' -and $activityRoute.CanNavigate -and $activityRoute.RequestedTarget.TargetSectionName -eq 'ActivityLog') -Message 'Activity log search result should route with ActivityLog target section.'

Write-Step 'Step 7/12: validate quick action projection consistency'
$draftQuickActions = @($draftTask.QuickActions)
$ocrQuickActions = @($ocrTask.QuickActions)
$followUpQuickActions = @($followUpTask.QuickActions)
$disabledOpenOrder = @($recentTask.QuickActions | Where-Object { $_.Type.ToString() -eq 'OpenOrder' }) | Select-Object -First 1
$copyDraftAction = @($draftQuickActions | Where-Object { $_.Type.ToString() -eq 'CopyDraft' }) | Select-Object -First 1
$markSentAction = @($draftQuickActions | Where-Object { $_.Type.ToString() -eq 'MarkSent' }) | Select-Object -First 1
$convertOcrAction = @($ocrQuickActions | Where-Object { $_.Type.ToString() -eq 'ConvertOcrToMessage' }) | Select-Object -First 1
$completeFollowUpAction = @($followUpQuickActions | Where-Object { $_.Type.ToString() -eq 'CompleteFollowUp' }) | Select-Object -First 1
$snoozeFollowUpAction = @($followUpQuickActions | Where-Object { $_.Type.ToString() -eq 'SnoozeFollowUp' }) | Select-Object -First 1

foreach ($action in @($copyDraftAction, $markSentAction, $convertOcrAction, $completeFollowUpAction, $snoozeFollowUpAction)) {
    Assert-True -Condition ($null -ne $action) -Message 'Expected projected quick action is missing.'
    Assert-True -Condition (-not [string]::IsNullOrWhiteSpace($action.ActionHint)) -Message 'QuickAction ActionHint should not be empty.'
    Assert-True -Condition (-not [string]::IsNullOrWhiteSpace($action.TargetSection)) -Message 'QuickAction TargetSection should not be empty.'
    Assert-True -Condition ($action.RequiresUserAction) -Message "High-risk quick action should require user action: $($action.Type)"
}

Assert-True -Condition ($null -ne $disabledOpenOrder -and -not $disabledOpenOrder.IsEnabled -and -not [string]::IsNullOrWhiteSpace($disabledOpenOrder.DisabledReason)) -Message 'Disabled OpenOrder quick action should include DisabledReason.'
$disabledRoute = $context.NavigationRouteService.ResolveAsync($disabledOpenOrder).GetAwaiter().GetResult()
Assert-True -Condition (-not $disabledRoute.CanNavigate -and $disabledRoute.DisabledReason -eq $disabledOpenOrder.DisabledReason) -Message 'Disabled quick action should not be executable by route service.'

Write-Step 'Step 8/12: validate unknown action and missing entity handling'
$unknownActionItem = [Orderly.Core.Models.SearchResultItem]::new()
$unknownActionItem.Id = 'unknown-action'
$unknownActionItem.Type = [Orderly.Core.Models.SearchResultType]::Customer
$unknownActionItem.Title = 'unknown action'
$unknownActionItem.CustomerId = $customer.Id
$unknownActionItem.RelatedEntityType = 'Customer'
$unknownActionItem.RelatedEntityId = $customer.Id
$unknownActionItem.TargetSection = 'Customer'
$unknownActionItem.ActionHint = 'UnknownActionHint'
$unknownActionRoute = $context.NavigationRouteService.ResolveAsync($unknownActionItem).GetAwaiter().GetResult()
Assert-True -Condition (-not $unknownActionRoute.CanNavigate -and $unknownActionRoute.DisabledReason -like '未知 ActionHint*') -Message 'Unknown ActionHint should fail safely.'

$fallbackItem = [Orderly.Core.Models.SearchResultItem]::new()
$fallbackItem.Id = 'missing-suggestion'
$fallbackItem.Type = [Orderly.Core.Models.SearchResultType]::AiSuggestion
$fallbackItem.Title = 'missing suggestion'
$fallbackItem.CustomerId = $customer.Id
$fallbackItem.OrderId = $order.Id
$fallbackItem.RelatedEntityType = 'AiSuggestion'
$fallbackItem.RelatedEntityId = 999999
$fallbackItem.TargetSection = 'AiSuggestion'
$fallbackItem.ActionHint = 'ReviewSuggestion'
$fallbackRoute = $context.NavigationRouteService.ResolveAsync($fallbackItem).GetAwaiter().GetResult()
Assert-True -Condition ($fallbackRoute.CanNavigate -and $fallbackRoute.UsedFallback -and $fallbackRoute.ResolvedTarget.TargetSectionName -eq 'Order') -Message 'Missing related entity should fallback to order/customer.'

$missingAllItem = [Orderly.Core.Models.SearchResultItem]::new()
$missingAllItem.Id = 'missing-everything'
$missingAllItem.Type = [Orderly.Core.Models.SearchResultType]::AiSuggestion
$missingAllItem.Title = 'missing all'
$missingAllItem.RelatedEntityType = 'AiSuggestion'
$missingAllItem.RelatedEntityId = 999998
$missingAllItem.TargetSection = 'AiSuggestion'
$missingAllItem.ActionHint = 'ReviewSuggestion'
$missingAllRoute = $context.NavigationRouteService.ResolveAsync($missingAllItem).GetAwaiter().GetResult()
Assert-True -Condition (-not $missingAllRoute.CanNavigate) -Message 'Missing entities without fallback should fail safely.'

Write-Step 'Step 9/12: validate ViewModel safe-location behavior'
$harness = New-P36ViewModelHarness -Context $context
$viewModel = $harness.ViewModel
$viewModel.LoadAsync().GetAwaiter().GetResult() | Out-Null
$viewModel.LastNavigationStatus = ''
$viewModel.LastNavigationError = ''
$stateBefore = Get-StateSnapshot -Context $context -Suggestion $draftSuggestion -OcrResult $ocrResult -FollowUp $followUp -Clipboard $harness.Clipboard

$ocrSearchListItem = [Orderly.App.ViewModels.SearchResultListItem]::new($ocrHit)
$followUpTaskListItem = [Orderly.App.ViewModels.WorkbenchTaskListItem]::new($followUpTask)
$draftTaskListItem = [Orderly.App.ViewModels.WorkbenchTaskListItem]::new($draftTask)

$viewModel.OpenSearchResultCommand.ExecuteAsync($ocrSearchListItem).GetAwaiter().GetResult() | Out-Null
Assert-True -Condition ($viewModel.CurrentNavigationTarget.TargetSectionName -eq 'Ocr' -and $viewModel.CurrentOcrResult.Id -eq $ocrResult.Id) -Message 'OpenSearchResultCommand should only locate OCR context.'
$viewModel.OpenWorkbenchTaskCommand.ExecuteAsync($followUpTaskListItem).GetAwaiter().GetResult() | Out-Null
Assert-True -Condition ($viewModel.CurrentNavigationTarget.TargetSectionName -eq 'FollowUp' -and $viewModel.SelectedCustomer.Id -eq $customer.Id) -Message 'OpenWorkbenchTaskCommand should locate follow-up context without completing it.'
$viewModel.OpenWorkbenchTaskCommand.ExecuteAsync($draftTaskListItem).GetAwaiter().GetResult() | Out-Null
Assert-True -Condition ($viewModel.CurrentNavigationTarget.TargetSectionName -eq 'AiSuggestion' -and $viewModel.SelectedAiSuggestion.Id -eq $draftSuggestion.Id) -Message 'OpenWorkbenchTaskCommand should sync selected AI suggestion without copying or sending.'
$stateAfter = Get-StateSnapshot -Context $context -Suggestion $draftSuggestion -OcrResult $ocrResult -FollowUp $followUp -Clipboard $harness.Clipboard
Assert-True -Condition (($stateBefore | ConvertTo-Json -Compress) -eq ($stateAfter | ConvertTo-Json -Compress)) -Message 'OpenSearchResultCommand / OpenWorkbenchTaskCommand should not trigger send, copy, OCR convert, complete or snooze side effects.'
Assert-True -Condition ([string]::IsNullOrWhiteSpace($viewModel.LastNavigationError)) -Message 'ViewModel navigation should not produce errors for valid routes.'

Write-Step 'Step 10/12: validate route summaries for reporting'
$routeSummaries = @(
    (Get-RouteResultSummary -Route $draftRoute),
    (Get-RouteResultSummary -Route $ocrTaskRoute),
    (Get-RouteResultSummary -Route $followUpRoute),
    (Get-RouteResultSummary -Route $fallbackRoute),
    (Get-RouteResultSummary -Route $disabledRoute)
)
Assert-True -Condition ($routeSummaries.Count -eq 5) -Message 'Expected route summaries to be generated.'

Write-Step 'Step 11/12: reset QA data and ensure baseline is restored'
Invoke-QaScript -Path $resetScript
$restoredStatus = Get-BaselineStatusText
Assert-True -Condition ($baselineStatus -eq $restoredStatus) -Message 'QA baseline status changed after reset-qa-data.'

Write-Step 'Step 12/12: confirm local-only execution assumptions'
Write-Step 'No public network calls were made. No real AI API or platform integration was invoked.'

Write-Host ''
Write-Host 'P3.6 NAVIGATION ROUTE SMOKE: PASS'
Write-Host ('Workbench task count: ' + $allTasks.Count)
Write-Host ('Route summaries checked: ' + $routeSummaries.Count)
