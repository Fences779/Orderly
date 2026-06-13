using System.Collections.Generic;
using System.Threading.Tasks;
using Orderly.Core.Commerce.Connectors;
using Xunit;

namespace Orderly.Tests.Commerce.Connectors;

/// <summary>
/// Unit tests for the reserved, disabled external connectors (Task 14.2).
/// <para>
/// These verify the V1 connector-disabled contract: every reserved connector reports
/// <see cref="ConnectorHealthStatus.Disabled"/> health at startup (Req 8.6) and is never enabled
/// (Req 8.4), and every invocation returns a <see cref="ConnectorOutcome.ConnectorDisabled"/>
/// result while performing no outbound request and leaving local data unchanged (Req 8.5).
/// </para>
/// <para>
/// "No outbound request" is verified structurally: the disabled connectors are inert
/// (<see cref="DisabledConnectorBase"/> holds no network client and performs no I/O), so a call
/// that returns synchronously from an already-completed task with a <c>ConnectorDisabled</c>
/// outcome and an absent payload could not have issued a network request. The tests assert the
/// observable consequences of that contract.
/// </para>
/// </summary>
public sealed class DisabledConnectorTests
{
    private static readonly IReadOnlyList<ConnectorRecord> SampleRecords = new[]
    {
        new ConnectorRecord("key-1", "{\"value\":1}"),
        new ConnectorRecord("key-2", "{\"value\":2}")
    };

    // ---- Startup health (Req 8.6) ----------------------------------------------------------

    [Fact]
    public void Order_connector_reports_disabled_health_at_startup()
    {
        IExternalOrderConnector connector = new DisabledOrderConnector();

        Assert.Equal(ConnectorHealthStatus.Disabled, connector.Health);
        Assert.False(connector.IsEnabled);
        Assert.False(connector.Options.Enabled);
    }

    [Fact]
    public void Inventory_connector_reports_disabled_health_at_startup()
    {
        IExternalInventoryConnector connector = new DisabledInventoryConnector();

        Assert.Equal(ConnectorHealthStatus.Disabled, connector.Health);
        Assert.False(connector.IsEnabled);
        Assert.False(connector.Options.Enabled);
    }

    [Fact]
    public void Disabled_is_the_default_health_status()
    {
        // An uninitialised connector health value reads as Disabled (the enum default, 0).
        Assert.Equal(ConnectorHealthStatus.Disabled, default(ConnectorHealthStatus));
    }

    [Fact]
    public void Connectors_expose_neutral_names()
    {
        Assert.Equal("OrderConnector", new DisabledOrderConnector().Name);
        Assert.Equal("InventoryConnector", new DisabledInventoryConnector().Name);
        Assert.Equal("custom-order", new DisabledOrderConnector("custom-order").Name);
    }

    // ---- Order connector invocation returns ConnectorDisabled (Req 8.5) --------------------

    [Fact]
    public async Task PushOrders_returns_connector_disabled_without_outbound_request()
    {
        var connector = new DisabledOrderConnector();

        ConnectorResult result = await connector.PushOrdersAsync(SampleRecords);

        Assert.Equal(ConnectorOutcome.ConnectorDisabled, result.Outcome);
        Assert.True(result.IsDisabled);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task PullOrders_returns_connector_disabled_with_no_payload()
    {
        var connector = new DisabledOrderConnector();

        ConnectorResult<IReadOnlyList<ConnectorRecord>> result =
            await connector.PullOrdersAsync(new ConnectorQuery());

        Assert.Equal(ConnectorOutcome.ConnectorDisabled, result.Outcome);
        Assert.True(result.IsDisabled);
        // No outbound request occurred, so no records were retrieved.
        Assert.Null(result.Data);
    }

    // ---- Inventory connector invocation returns ConnectorDisabled (Req 8.5) ----------------

    [Fact]
    public async Task PushInventory_returns_connector_disabled_without_outbound_request()
    {
        var connector = new DisabledInventoryConnector();

        ConnectorResult result = await connector.PushInventoryAsync(SampleRecords);

        Assert.Equal(ConnectorOutcome.ConnectorDisabled, result.Outcome);
        Assert.True(result.IsDisabled);
        Assert.NotNull(result.Message);
    }

    [Fact]
    public async Task PullInventory_returns_connector_disabled_with_no_payload()
    {
        var connector = new DisabledInventoryConnector();

        ConnectorResult<IReadOnlyList<ConnectorRecord>> result =
            await connector.PullInventoryAsync(new ConnectorQuery(Filter: "anything", MaxRecords: 50));

        Assert.Equal(ConnectorOutcome.ConnectorDisabled, result.Outcome);
        Assert.True(result.IsDisabled);
        Assert.Null(result.Data);
    }

    // ---- Local data preserved: repeated invocation never alters supplied records (Req 8.5) -

    [Fact]
    public async Task Repeated_invocations_remain_disabled_and_preserve_input_records()
    {
        var connector = new DisabledOrderConnector();
        var records = new List<ConnectorRecord>(SampleRecords);

        for (int i = 0; i < 3; i++)
        {
            ConnectorResult result = await connector.PushOrdersAsync(records);
            Assert.True(result.IsDisabled);
        }

        // The disabled connector performed no work, so the caller's records are untouched.
        Assert.Equal(2, records.Count);
        Assert.Equal("key-1", records[0].RecordKey);
        Assert.Equal("key-2", records[1].RecordKey);
        Assert.Equal(ConnectorHealthStatus.Disabled, connector.Health);
    }
}
