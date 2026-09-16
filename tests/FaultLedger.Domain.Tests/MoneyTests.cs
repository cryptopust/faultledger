using System.Globalization;
using FaultLedger.Domain;

namespace FaultLedger.Domain.Tests;

public sealed class MoneyTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("-10.999")]
    [InlineData("10.999")]
    [InlineData("0.00000001")]
    [InlineData("99999999999999999999.99999999")]
    [InlineData("-99999999999999999999.99999999")]
    public void ValidMoney_IsCreatedExactly(string text)
    {
        decimal amount = Money.ParseAmount(text);
        var money = new Money(amount, "USD");
        Assert.Equal(decimal.Parse(text, CultureInfo.InvariantCulture), money.Amount);
        Assert.Equal(decimal.GetBits(amount), decimal.GetBits(money.Amount));
    }

    [Theory]
    [InlineData("usd", "USD")]
    [InlineData("Usd", "USD")]
    [InlineData("eur", "EUR")]
    [InlineData("inr", "INR")]
    [InlineData("zzz", "ZZZ")]
    public void Currency_IsNormalizedToUppercaseInvariant(string currency, string expected)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal(expected, new Money(1, currency).Currency);
            Assert.Equal(10.999m, Money.ParseAmount("10.999"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("US")]
    [InlineData("USDD")]
    [InlineData("12A")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" USD")]
    [InlineData("USＤ")]
    [InlineData("İNR")]
    public void InvalidCurrencyShape_IsRejected(string currency) =>
        Assert.Throws<ArgumentException>(() => new Money(1, currency));

    [Fact]
    public void MoneyEquality_RequiresSameAmountAndCurrency()
    {
        var money = new Money(1.00m, "usd");
        Assert.Equal(new Money(1m, "USD"), money);
        Assert.Equal(new Money(1m, "USD").GetHashCode(), money.GetHashCode());
        Assert.NotEqual(new Money(2m, "USD"), money);
        Assert.NotEqual(new Money(1m, "EUR"), money);
    }

    [Fact]
    public void DifferentCurrencyArithmetic_IsRejected()
    {
        var usd = new Money(2m, "USD");
        var eur = new Money(1m, "EUR");
        Assert.Throws<ArgumentException>(() => usd.Add(eur));
        Assert.Throws<ArgumentException>(() => usd.Subtract(eur));
    }

    [Fact]
    public void SameCurrencyArithmetic_IsExactAndMayProduceNegativeMoney()
    {
        var money = new Money(10.999m, "USD");
        Assert.Equal(new Money(11m, "USD"), money.Add(new Money(0.001m, "USD")));
        Assert.Equal(new Money(-0.001m, "USD"), money.Subtract(new Money(11m, "USD")));
    }

    [Fact]
    public void ExcessivePrecision_IsRejectedRatherThanRounded()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(1.000000001m, "USD"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(1.000000000m, "USD"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(100000000000000000000m, "USD"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(decimal.MaxValue, "USD"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Money(decimal.MinValue, "USD"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Money(Money.MaximumAmount, "USD").Add(new Money(0.00000001m, "USD")));
    }

    [Theory]
    [InlineData("1.000000001")]
    [InlineData("1.000000000")]
    [InlineData("1.23456789012345678901234567899")]
    [InlineData("100000000000000000000")]
    [InlineData("1e2")]
    [InlineData("1,25")]
    [InlineData(" 1")]
    [InlineData("+1")]
    [InlineData("NaN")]
    [InlineData("1.")]
    [InlineData(".1")]
    [InlineData("--1")]
    public void UnsupportedAmountText_IsRejectedBeforeDecimalConversion(string text) =>
        Assert.Throws<ArgumentException>(() => Money.ParseAmount(text));
}
