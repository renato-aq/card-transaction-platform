using CardPlatform.Ledger.Application.Holds;
using CardPlatform.Ledger.Domain.Accounts;
using CardPlatform.Ledger.Domain.Common;
using CardPlatform.Ledger.Domain.Holds;
using CardPlatform.Ledger.Domain.Monetary;
using CardPlatform.Ledger.Domain.Transactions;

namespace CardPlatform.Ledger.Application.Tests.Holds;

/// <summary>Capture and void share their setup: a card of 100 with a pending hold of 30.</summary>
public class CaptureAndVoidHoldHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddMinutes(5);
    private static readonly Currency Brl = Currency.Create("BRL").Value;

    private readonly Account _card = Account.Open(AccountType.Card, Brl, Now);
    private readonly Account _holdsAccount = Account.Open(AccountType.Holds, Brl, Now);
    private readonly Account _merchant = Account.Open(AccountType.Merchant, Brl, Now);
    private readonly Account _settlement = Account.Open(AccountType.Settlement, Brl, Now);
    private readonly Account _usdMerchant = Account.Open(AccountType.Merchant, Currency.Create("USD").Value, Now);
    private readonly Hold _hold;
    private readonly FakeLedgerTransactionRepository _transactions = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly CaptureHoldHandler _capture;
    private readonly VoidHoldHandler _void;

    public CaptureAndVoidHoldHandlerTests()
    {
        Money hundred = Money.Create(100m, Brl).Value;
        Assert.True(LedgerTransaction.Create([Entry.Debit(_settlement.Id, hundred), Entry.Credit(_card.Id, hundred)], Now)
            .Value.ApplyTo([_settlement, _card]).IsSuccess);
        _hold = Hold.Place(Guid.CreateVersion7(), _card, _holdsAccount, Money.Create(30m, Brl).Value, Now).Value.Hold;

        FakeAccountRepository accounts = new(_card, _holdsAccount, _merchant, _settlement, _usdMerchant);
        FakeHoldRepository holds = new(_hold);
        FixedTimeProvider time = new(Later);
        _capture = new CaptureHoldHandler(holds, accounts, _transactions, _unitOfWork, time);
        _void = new VoidHoldHandler(holds, accounts, _transactions, _unitOfWork, time);
    }

    [Fact]
    public async Task Capture_moves_the_amount_to_the_merchant_and_saves_once()
    {
        Result<HoldDto> result = await _capture.HandleAsync(new CaptureHold(_hold.Id, _merchant.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(HoldStatus.Captured, result.Value.Status);
        Assert.Equal(Later, result.Value.CompletedAt);
        LedgerTransaction capture = Assert.Single(_transactions.Added);
        Assert.Equal(capture.Id, _hold.CompletionTransactionId);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Equal(70m, _card.Balance.Amount);
        Assert.Equal(0m, _holdsAccount.Balance.Amount);
        Assert.Equal(30m, _merchant.Balance.Amount);
    }

    [Fact]
    public async Task Void_returns_the_amount_to_the_card_and_saves_once()
    {
        Result<HoldDto> result = await _void.HandleAsync(new VoidHold(_hold.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(HoldStatus.Voided, result.Value.Status);
        Assert.Equal(Later, result.Value.CompletedAt);
        Assert.Single(_transactions.Added);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Equal(100m, _card.Balance.Amount);
        Assert.Equal(0m, _holdsAccount.Balance.Amount);
    }

    [Fact]
    public async Task Missing_merchant_account_id_is_rejected_as_invalid()
    {
        AssertRejected(await _capture.HandleAsync(new CaptureHold(_hold.Id, null)), "invalid_account_id", ErrorType.Validation);
        AssertRejected(await _capture.HandleAsync(new CaptureHold(_hold.Id, Guid.Empty)), "invalid_account_id", ErrorType.Validation);
    }

    [Fact]
    public async Task Unknown_hold_is_not_found()
    {
        AssertRejected(
            await _capture.HandleAsync(new CaptureHold(Guid.CreateVersion7(), _merchant.Id)),
            HoldErrors.HoldNotFoundCode,
            ErrorType.NotFound);
        AssertRejected(
            await _void.HandleAsync(new VoidHold(Guid.CreateVersion7())),
            HoldErrors.HoldNotFoundCode,
            ErrorType.NotFound);
    }

    [Fact]
    public async Task Unknown_merchant_account_is_not_found()
    {
        AssertRejected(
            await _capture.HandleAsync(new CaptureHold(_hold.Id, Guid.CreateVersion7())),
            "account_not_found",
            ErrorType.NotFound);
    }

    [Fact]
    public async Task Capture_to_an_account_that_is_not_a_merchant_is_rejected()
    {
        AssertRejected(
            await _capture.HandleAsync(new CaptureHold(_hold.Id, _settlement.Id)),
            Hold.NotAMerchantAccountCode,
            ErrorType.BusinessRule);
    }

    [Fact]
    public async Task Capture_to_a_merchant_in_another_currency_is_rejected()
    {
        AssertRejected(
            await _capture.HandleAsync(new CaptureHold(_hold.Id, _usdMerchant.Id)),
            LedgerTransaction.CurrencyMismatchCode,
            ErrorType.BusinessRule);
    }

    [Fact]
    public async Task Completed_hold_cannot_be_captured_or_voided_again()
    {
        Assert.True((await _void.HandleAsync(new VoidHold(_hold.Id))).IsSuccess);

        Result<HoldDto> capture = await _capture.HandleAsync(new CaptureHold(_hold.Id, _merchant.Id));
        Result<HoldDto> voidAgain = await _void.HandleAsync(new VoidHold(_hold.Id));

        Assert.Equal(Hold.HoldNotPendingCode, capture.Error.Code);
        Assert.Equal(Hold.HoldNotPendingCode, voidAgain.Error.Code);
        Assert.Equal(ErrorType.BusinessRule, voidAgain.Error.Type);
        Assert.Single(_transactions.Added);
        Assert.Equal(1, _unitOfWork.SaveCount);
        Assert.Equal(100m, _card.Balance.Amount);
        Assert.Equal(0m, _merchant.Balance.Amount);
    }

    private void AssertRejected(Result<HoldDto> result, string expectedCode, ErrorType expectedType)
    {
        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
        Assert.Equal(expectedType, result.Error.Type);
        Assert.Empty(_transactions.Added);
        Assert.Equal(0, _unitOfWork.SaveCount);
        Assert.Equal(HoldStatus.Pending, _hold.Status);
        Assert.Equal(70m, _card.Balance.Amount);
        Assert.Equal(30m, _holdsAccount.Balance.Amount);
    }
}
