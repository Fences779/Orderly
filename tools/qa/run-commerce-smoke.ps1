<#
.SYNOPSIS
    Universal commerce smoke (Requirement 12.1-12.3).

.DESCRIPTION
    Drives the ordered universal commerce flow end to end against an isolated QA database,
    exercising the Commerce Service Layer the same way the application does:

        build -> init QA database -> create demo workspace -> create customer ->
        create product -> create inventory item -> record inbound movement ->
        create order + order item -> record payment -> advance fulfillment ->
        complete order (deduct inventory) -> create cash flow ->
        generate dashboard metrics -> generate insight -> pass

    Every step is numbered. On full success the script emits a pass result and exits with
    code 0. On the first failing step it stops, identifies the step, and exits non-zero
    (Req 12.2, 12.3).

    The flow runs against a throwaway plaintext database created under the QA artifact root,
    so it never reads, writes, or relocates real user local data (constraint C-6).

    This script is industry-agnostic and contains no forbidden term.
#>
param(
    [switch]$SkipBuild
)

. (Join-Path $PSScriptRoot 'qa-common.ps1')
Ensure-PowerShellCore -ScriptPath $PSCommandPath -ScriptArguments $args

$script:CurrentStep = 'startup'
$script:TotalSteps = 13

function Invoke-CommerceStep {
    param(
        [Parameter(Mandatory = $true)]
        [int]$Index,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action
    )

    $script:CurrentStep = $Name
    Write-Step ("Step {0}/{1}: {2}" -f $Index, $script:TotalSteps, $Name)
    & $Action
}

function New-IsolatedQaDatabasePath {
    $timestamp = (Get-Date -Format 'yyyyMMdd_HHmmss_fff') + '_' + [guid]::NewGuid().ToString('N').Substring(0, 6)
    $root = Join-Path (Get-QaDatabaseRoot) 'commerce-smoke'
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    return Join-Path $root ("commerce-smoke-$timestamp.db")
}

function Wait-Task {
    param(
        [Parameter(Mandatory = $true)]
        $Task
    )

    return $Task.GetAwaiter().GetResult()
}

Assert-NoRunningOrderlyProcess
Write-Step 'Starting universal commerce smoke'
Write-Step 'Scope: ordered commerce flow over the Commerce Service Layer, isolated QA database, no public network'
Write-Step "Repo root: $(Get-RepoRoot)"

try {
    # --- Step 1: build -------------------------------------------------------
    Invoke-CommerceStep -Index 1 -Name 'build' -Action {
        if ($SkipBuild) {
            Write-Step 'Build skipped by caller (-SkipBuild); reusing existing Debug output'
            return
        }

        Push-Location (Get-RepoRoot)
        try {
            dotnet build Orderly.sln -c Debug
            if (-not $?) {
                throw 'dotnet build failed.'
            }

            $exitCode = if (Test-Path variable:LASTEXITCODE) { $LASTEXITCODE } else { 0 }
            if ($exitCode -ne 0) {
                throw "dotnet build failed with exit code: $exitCode"
            }
        }
        finally {
            Pop-Location
        }
    }

    Import-OrderlyAssembliesForQa

    $databasePath = New-IsolatedQaDatabasePath
    Write-Step "Isolated QA database: $databasePath"
    $connectionFactory = [Orderly.Data.Sqlite.SqliteConnectionFactory]::new($databasePath)

    # Commerce repositories (one per entity), all over the isolated connection factory.
    $workspaceRepository = [Orderly.Data.Commerce.Repositories.BusinessWorkspaceRepository]::new($connectionFactory)
    $customerRepository = [Orderly.Data.Commerce.Repositories.CommerceCustomerRepository]::new($connectionFactory)
    $productRepository = [Orderly.Data.Commerce.Repositories.ProductRepository]::new($connectionFactory)
    $inventoryItemRepository = [Orderly.Data.Commerce.Repositories.InventoryItemRepository]::new($connectionFactory)
    $inventoryMovementRepository = [Orderly.Data.Commerce.Repositories.InventoryMovementRepository]::new($connectionFactory)
    $orderRepository = [Orderly.Data.Commerce.Repositories.CommerceOrderRepository]::new($connectionFactory)
    $orderItemRepository = [Orderly.Data.Commerce.Repositories.OrderItemRepository]::new($connectionFactory)
    $paymentRepository = [Orderly.Data.Commerce.Repositories.PaymentRecordRepository]::new($connectionFactory)
    $cashFlowRepository = [Orderly.Data.Commerce.Repositories.CashFlowEntryRepository]::new($connectionFactory)

    # Commerce Service Layer entry points wired exactly as the application composes them.
    $workspaceService = [Orderly.Data.Commerce.Services.CommerceWorkspaceService]::new($workspaceRepository)
    $productService = [Orderly.Data.Commerce.Services.CommerceProductService]::new($productRepository)
    $inventoryService = [Orderly.Data.Commerce.Services.CommerceInventoryService]::new($inventoryItemRepository, $inventoryMovementRepository)
    $orderService = [Orderly.Data.Commerce.Services.CommerceOrderService]::new(
        $connectionFactory,
        $orderRepository,
        $orderItemRepository,
        $inventoryItemRepository,
        $inventoryMovementRepository,
        $customerRepository)
    $cashFlowService = [Orderly.Data.Commerce.Services.CommerceCashFlowService]::new($cashFlowRepository)
    $dashboardService = [Orderly.Data.Commerce.Services.CommerceDashboardService]::new(
        $orderRepository,
        $cashFlowRepository,
        $inventoryItemRepository,
        $customerRepository)
    $insightService = [Orderly.Data.Commerce.Services.CommerceBusinessInsightService]::new(
        $inventoryService,
        $cashFlowRepository)

    $now = [DateTime]::UtcNow

    # --- Step 2: init QA database (idempotent Commerce schema) ---------------
    Invoke-CommerceStep -Index 2 -Name 'init QA database' -Action {
        $initializer = [Orderly.Data.Sqlite.CommerceSchemaInitializer]::new($connectionFactory)
        Wait-Task $initializer.InitializeAsync([System.Threading.CancellationToken]::None) | Out-Null
    }

    # --- Step 3: create demo workspace ---------------------------------------
    $script:WorkspaceId = [guid]::Empty
    Invoke-CommerceStep -Index 3 -Name 'create demo workspace' -Action {
        $workspace = [Orderly.Core.Commerce.BusinessWorkspace]@{
            Name                = '演示工作区'
            DefaultCurrencyCode = 'CNY'
        }
        $created = Wait-Task $workspaceService.CreateAsync($workspace, [System.Threading.CancellationToken]::None)
        $script:WorkspaceId = $created.Id
        if ($script:WorkspaceId -eq [guid]::Empty) {
            throw 'Workspace creation returned an empty workspace id.'
        }
    }

    # --- Step 4: create customer ---------------------------------------------
    $script:CustomerId = [guid]::Empty
    Invoke-CommerceStep -Index 4 -Name 'create customer' -Action {
        $customer = [Orderly.Core.Commerce.Customer]@{
            WorkspaceId = $script:WorkspaceId
            Name        = '客户 A'
            Phone       = '13800000001'
        }
        $created = Wait-Task $customerRepository.CreateAsync($customer, [System.Threading.CancellationToken]::None)
        $script:CustomerId = $created.Id
    }

    # --- Step 5: create product ----------------------------------------------
    $script:ProductId = [guid]::Empty
    Invoke-CommerceStep -Index 5 -Name 'create product' -Action {
        $product = [Orderly.Core.Commerce.Product]@{
            WorkspaceId  = $script:WorkspaceId
            Name         = '商品 A'
            Code         = 'SP-A'
            ProductType  = [Orderly.Core.Commerce.ProductType]::Physical
            DefaultPrice = [Orderly.Core.Commerce.CommerceMoney]::From([decimal]100.00)
            DefaultCost  = [Orderly.Core.Commerce.CommerceMoney]::From([decimal]60.00)
        }
        $created = Wait-Task $productService.CreateAsync($product, [System.Threading.CancellationToken]::None)
        $script:ProductId = $created.Id
    }

    # --- Step 6: create inventory item (plus a low-stock item for insights) --
    $script:InventoryItemId = [guid]::Empty
    Invoke-CommerceStep -Index 6 -Name 'create inventory item' -Action {
        $item = [Orderly.Core.Commerce.InventoryItem]@{
            WorkspaceId       = $script:WorkspaceId
            Name              = '库存项 A'
            Sku               = 'KC-A'
            ProductId         = $script:ProductId
            QuantityAvailable = [decimal]0
            ReorderThreshold  = [decimal]10
            UnitCost          = [Orderly.Core.Commerce.CommerceMoney]::From([decimal]60.00)
        }
        $created = Wait-Task $inventoryItemRepository.CreateAsync($item, [System.Threading.CancellationToken]::None)
        $script:InventoryItemId = $created.Id

        # A second item kept at/below its reorder threshold guarantees a low-stock insight later.
        $lowStock = [Orderly.Core.Commerce.InventoryItem]@{
            WorkspaceId       = $script:WorkspaceId
            Name              = '库存项 B'
            Sku               = 'KC-B'
            QuantityAvailable = [decimal]3
            ReorderThreshold  = [decimal]5
            UnitCost          = [Orderly.Core.Commerce.CommerceMoney]::From([decimal]30.00)
        }
        Wait-Task $inventoryItemRepository.CreateAsync($lowStock, [System.Threading.CancellationToken]::None) | Out-Null
    }

    # --- Step 7: record inbound movement -------------------------------------
    Invoke-CommerceStep -Index 7 -Name 'record inbound movement' -Action {
        $movement = [Orderly.Core.Commerce.InventoryMovement]@{
            WorkspaceId     = $script:WorkspaceId
            InventoryItemId = $script:InventoryItemId
            MovementType    = [Orderly.Core.Commerce.InventoryMovementType]::Inbound
            Quantity        = [decimal]100
            OccurredAt      = $now.AddDays(-1)
            BusinessKey     = "commerce-smoke:inbound:$($script:InventoryItemId.ToString('N'))"
        }
        $updated = Wait-Task $inventoryService.RecordMovementAsync($movement, [System.Threading.CancellationToken]::None)
        if ($updated.QuantityAvailable -ne [decimal]100) {
            throw "Inbound movement did not apply; expected available 100, got $($updated.QuantityAvailable)."
        }
    }

    # --- Step 8: create order + order item -----------------------------------
    $script:OrderId = [guid]::Empty
    Invoke-CommerceStep -Index 8 -Name 'create order' -Action {
        $order = [Orderly.Core.Commerce.Order]@{
            WorkspaceId = $script:WorkspaceId
            OrderNo     = '订单 001'
            CustomerId  = $script:CustomerId
            OrderedAt   = $now
        }
        $createdOrder = Wait-Task $orderRepository.CreateAsync($order, [System.Threading.CancellationToken]::None)
        $script:OrderId = $createdOrder.Id

        $orderItem = [Orderly.Core.Commerce.OrderItem]@{
            WorkspaceId     = $script:WorkspaceId
            OrderId         = $script:OrderId
            ProductId       = $script:ProductId
            InventoryItemId = $script:InventoryItemId
            Description     = '演示订单明细'
            Quantity        = [decimal]2
            UnitPrice       = [Orderly.Core.Commerce.CommerceMoney]::From([decimal]100.00)
            UnitCost        = [Orderly.Core.Commerce.CommerceMoney]::From([decimal]60.00)
        }
        Wait-Task $orderItemRepository.CreateAsync($orderItem, [System.Threading.CancellationToken]::None) | Out-Null

        # Recompute the order's derived monetary fields from its line (no payments yet).
        $items = [System.Collections.Generic.List[Orderly.Core.Commerce.OrderItem]]::new()
        $items.Add($orderItem)
        $noPayments = [System.Collections.Generic.List[Orderly.Core.Commerce.PaymentRecord]]::new()
        $orderService.RecalculateOrder($createdOrder, $items, $noPayments)
        Wait-Task $orderRepository.UpdateAsync($createdOrder, [System.Threading.CancellationToken]::None) | Out-Null

        if ($createdOrder.Total.Amount -ne [decimal]200.00) {
            throw "Order total recalculation failed; expected 200.00, got $($createdOrder.Total.Amount)."
        }
    }

    # --- Step 9: record payment ----------------------------------------------
    Invoke-CommerceStep -Index 9 -Name 'record payment' -Action {
        $payment = [Orderly.Core.Commerce.PaymentRecord]@{
            WorkspaceId = $script:WorkspaceId
            OrderId     = $script:OrderId
            Amount      = [Orderly.Core.Commerce.CommerceMoney]::From([decimal]200.00)
            PaidAt      = $now
            Method      = '演示收款'
            BusinessKey = "commerce-smoke:payment:$($script:OrderId.ToString('N'))"
        }
        Wait-Task $paymentRepository.CreateAsync($payment, [System.Threading.CancellationToken]::None) | Out-Null

        # Re-derive the order totals now that a payment exists so PaidAmount/Receivable are correct.
        $order = Wait-Task $orderRepository.GetByIdAsync($script:OrderId, [System.Threading.CancellationToken]::None)
        $items = Wait-Task $orderItemRepository.GetAllAsync([System.Threading.CancellationToken]::None)
        $orderItems = [System.Collections.Generic.List[Orderly.Core.Commerce.OrderItem]]::new()
        foreach ($candidate in $items) {
            if ($candidate.OrderId -eq $script:OrderId) {
                $orderItems.Add($candidate)
            }
        }
        $payments = [System.Collections.Generic.List[Orderly.Core.Commerce.PaymentRecord]]::new()
        $payments.Add($payment)
        $orderService.RecalculateOrder($order, $orderItems, $payments)
        Wait-Task $orderRepository.UpdateAsync($order, [System.Threading.CancellationToken]::None) | Out-Null

        if ($order.PaidAmount.Amount -ne [decimal]200.00) {
            throw "Payment not reflected in order; expected paid 200.00, got $($order.PaidAmount.Amount)."
        }
    }

    # --- Step 10: advance fulfillment ----------------------------------------
    Invoke-CommerceStep -Index 10 -Name 'advance fulfillment' -Action {
        $order = Wait-Task $orderRepository.GetByIdAsync($script:OrderId, [System.Threading.CancellationToken]::None)
        $order.FulfillmentStage = [Orderly.Core.Commerce.OrderFulfillmentStage]::Fulfilled
        Wait-Task $orderRepository.UpdateAsync($order, [System.Threading.CancellationToken]::None) | Out-Null

        $reloaded = Wait-Task $orderRepository.GetByIdAsync($script:OrderId, [System.Threading.CancellationToken]::None)
        if ($reloaded.FulfillmentStage -ne [Orderly.Core.Commerce.OrderFulfillmentStage]::Fulfilled) {
            throw "Fulfillment stage did not advance; got $($reloaded.FulfillmentStage)."
        }
    }

    # --- Step 11: complete order (deduct inventory) --------------------------
    Invoke-CommerceStep -Index 11 -Name 'complete order and deduct inventory' -Action {
        $result = Wait-Task $orderService.CompleteOrderAsync($script:OrderId, $now, [System.Threading.CancellationToken]::None)
        if (-not $result.IsCompleted) {
            throw "Order completion was not applied; outcome: $($result.Outcome)."
        }

        $item = Wait-Task $inventoryItemRepository.GetByIdAsync($script:InventoryItemId, [System.Threading.CancellationToken]::None)
        if ($item.QuantityAvailable -ne [decimal]98) {
            throw "Inventory was not deducted; expected available 98, got $($item.QuantityAvailable)."
        }

        $order = Wait-Task $orderRepository.GetByIdAsync($script:OrderId, [System.Threading.CancellationToken]::None)
        if ($order.SalesStage -ne [Orderly.Core.Commerce.OrderSalesStage]::Completed) {
            throw "Sales stage was not advanced to Completed; got $($order.SalesStage)."
        }
    }

    # --- Step 12: create cash flow -------------------------------------------
    Invoke-CommerceStep -Index 12 -Name 'create cash flow' -Action {
        $input = [Orderly.Core.Commerce.Services.CashFlowEntryInput]@{
            WorkspaceId  = $script:WorkspaceId
            Amount       = [Orderly.Core.Commerce.CommerceMoney]::From([decimal]200.00)
            OccurredAt   = $now
            CategoryName = '收入分类 A'
            OrderId      = $script:OrderId
            BusinessKey  = "commerce-smoke:cashflow:$($script:OrderId.ToString('N'))"
        }
        $entry = Wait-Task $cashFlowService.RecordIncomeAsync($input, [System.Threading.CancellationToken]::None)
        if ($entry.Direction -ne [Orderly.Core.Commerce.CashFlowDirection]::Income) {
            throw "Cash-flow entry was not recorded as income; got $($entry.Direction)."
        }
    }

    # --- Step 13a: generate dashboard metrics --------------------------------
    Invoke-CommerceStep -Index 13 -Name 'generate dashboard metrics' -Action {
        $snapshot = Wait-Task $dashboardService.GetSnapshotAsync($now, [System.Threading.CancellationToken]::None)
        if ($null -eq $snapshot) {
            throw 'Dashboard snapshot was null.'
        }
        Write-Step "Dashboard snapshot computed as of $($snapshot.AsOfUtc.ToString('o'))"
    }

    # --- Step 13b: generate insight ------------------------------------------
    Invoke-CommerceStep -Index 13 -Name 'generate insight' -Action {
        $insights = @(Wait-Task $insightService.GenerateInsightsAsync($now, [System.Threading.CancellationToken]::None))
        if ($null -eq $insights -or $insights.Count -lt 1) {
            throw 'Expected at least one generated insight (the low-stock item should raise one).'
        }
        Write-Step "Generated insight count: $($insights.Count)"
    }

    Write-Host ''
    Write-Host 'COMMERCE SMOKE: PASS'
    Write-Host 'All commerce steps completed: build, init QA database, create demo workspace, create customer, create product, create inventory item, record inbound movement, create order, record payment, advance fulfillment, complete order (deduct inventory), create cash flow, generate dashboard, generate insight.'
    exit 0
}
catch {
    Write-Host ''
    Write-Host 'COMMERCE SMOKE: FAILED'
    Write-Host ("Failed step: " + $script:CurrentStep)
    Write-Host ("Reason: " + $_.Exception.Message)
    exit 1
}
