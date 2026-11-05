using MonitorAgent.Modules.Commerce.Domain;

namespace MonitorAgent.Tests.Unit;

/// <summary>E5: the order states of a bank transfer that staff verify.</summary>
public class BankTransferOrderTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    private static Order NewOrder() => Order.Place("MA-261105-TEST01", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Monitor Agent", "Standard", 1200m, "EGP", At);

    [Fact]
    public void Submitting_moves_the_order_to_verification_without_paying_it()
    {
        var order = NewOrder();

        Assert.True(order.SubmitBankTransfer("TRF-1", " note ", At).IsSuccess);

        Assert.Equal(OrderStatus.PendingVerification, order.Status);
        Assert.Null(order.PaidAt);
        Assert.Equal("note", order.SubmittedTransfer!.CustomerNote);
        Assert.Empty(order.DomainEvents.OfType<OrderPaid>());
    }

    [Fact]
    public void Approval_pays_once()
    {
        var order = NewOrder();
        order.SubmitBankTransfer("TRF-1", null, At);

        Assert.True(order.ApproveBankTransfer(At).Value);
        Assert.False(order.ApproveBankTransfer(At).Value);

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(Order.BankTransfer, order.PaidVia);
        Assert.Single(order.DomainEvents.OfType<OrderPaid>());
    }

    [Fact]
    public void Approval_needs_a_submitted_transfer()
    {
        Assert.Equal("NO_PAYMENT_TO_VERIFY", NewOrder().ApproveBankTransfer(At).Error!.Code);
    }

    [Fact]
    public void A_card_payment_completing_while_a_transfer_waits_pays_the_order_and_closes_the_transfer()
    {
        var order = NewOrder();
        var card = order.StartPayment("sandbox", At).Value;
        order.CheckoutCreated(card.Id, "chk_1");
        order.SubmitBankTransfer("TRF-1", null, At);

        Assert.True(order.ConfirmPayment(card, 1200m, "EGP", At));

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Null(order.SubmittedTransfer);
        Assert.Equal("NO_PAYMENT_TO_VERIFY", order.RejectBankTransfer("x", At).Error!.Code);
    }

    [Fact]
    public void A_rejected_transfer_reopens_the_order_and_the_same_reference_can_come_back()
    {
        var order = NewOrder();
        order.SubmitBankTransfer("TRF-1", null, At);

        Assert.True(order.RejectBankTransfer("Not found", At).IsSuccess);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        Assert.True(order.SubmitBankTransfer("TRF-1", null, At).IsSuccess);

        Assert.Single(order.Payments);
        Assert.Equal(PaymentStatus.Pending, order.Payments[0].Status);
        Assert.Null(order.Payments[0].FailureReason);
    }
}
