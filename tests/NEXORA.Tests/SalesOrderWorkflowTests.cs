using NEXORA.Domain.Entities;

namespace NEXORA.Tests;

public sealed class SalesOrderWorkflowTests
{
    [Theory]
    [InlineData(OrderWorkflowAction.Confirm, OrderStatuses.Pending, OrderStatuses.Confirmed)]
    [InlineData(OrderWorkflowAction.HandoffToWarehouse, OrderStatuses.Confirmed, OrderStatuses.Preparing)]
    [InlineData(OrderWorkflowAction.MarkShipping, OrderStatuses.Exported, OrderStatuses.Shipping)]
    [InlineData(OrderWorkflowAction.Complete, OrderStatuses.Shipping, OrderStatuses.Completed)]
    public void WorkflowRules_ExposeOnlyApprovedForwardTransitions(
        OrderWorkflowAction action,
        string expectedFrom,
        string expectedTo)
    {
        Assert.True(OrderWorkflowRules.TryGetTransition(action, out var transition));
        Assert.Equal(expectedFrom, transition.FromStatus);
        Assert.Equal(expectedTo, transition.ToStatus);
    }

    [Fact]
    public void WorkflowRules_RejectUndefinedAction()
    {
        Assert.False(OrderWorkflowRules.TryGetTransition((OrderWorkflowAction)int.MaxValue, out _));
    }

    [Theory]
    [InlineData(OrderStatuses.Confirmed, true)]
    [InlineData(OrderStatuses.Preparing, true)]
    [InlineData(OrderStatuses.Pending, false)]
    [InlineData(OrderStatuses.Exported, false)]
    [InlineData(OrderStatuses.Shipping, false)]
    [InlineData(OrderStatuses.Completed, false)]
    [InlineData(OrderStatuses.Cancelled, false)]
    public void CancellationRules_OnlyAllowCustomerRequestBeforeExport(string orderStatus, bool expected)
    {
        Assert.Equal(expected, OrderCancellationRules.CanRequest(orderStatus));
    }

    [Theory]
    [InlineData(OrderStatuses.Preparing, null, true)]
    [InlineData(OrderStatuses.Preparing, OrderCancellationRequestStatuses.Pending, false)]
    [InlineData(OrderStatuses.Preparing, OrderCancellationRequestStatuses.Rejected, true)]
    [InlineData(OrderStatuses.Confirmed, null, false)]
    [InlineData(OrderStatuses.Exported, null, false)]
    public void CancellationRules_ExportRequiresPreparingAndNoPendingRequest(
        string orderStatus,
        string? requestStatus,
        bool expected)
    {
        Assert.Equal(expected, OrderCancellationRules.CanExport(orderStatus, requestStatus));
    }
}
