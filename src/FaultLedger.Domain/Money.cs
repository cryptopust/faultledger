using System.Globalization;

namespace FaultLedger.Domain;

public sealed record Money
{
    public const int Precision = 28;
    public const int Scale = 8;
    public const decimal MaximumAmount = 99999999999999999999.99999999m;

    public Money(decimal amount, string currency)
    {
        ArgumentNullException.ThrowIfNull(currency);
        if (currency.Length != 3 || currency.Any(character => !char.IsAsciiLetter(character)))
        {
            throw new ArgumentException("Currency must contain exactly three ASCII letters.", nameof(currency));
        }

        if (((decimal.GetBits(amount)[3] >> 16) & 255) > Scale ||
            amount > MaximumAmount || amount < -MaximumAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount supports at most 20 integer and 8 fractional digits; rounding is prohibited.");
        }

        Amount = amount;
        Currency = currency.ToUpperInvariant();
    }

    public decimal Amount { get; }
    public string Currency { get; }

    public static decimal ParseAmount(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ReadOnlySpan<char> digits = text.AsSpan();
        if (digits.Length > 0 && digits[0] == '-')
        {
            digits = digits[1..];
        }

        int point = digits.IndexOf('.');
        ReadOnlySpan<char> integer = point < 0 ? digits : digits[..point];
        ReadOnlySpan<char> fraction = point < 0 ? [] : digits[(point + 1)..];
        if (integer.Length is < 1 or > 20 || fraction.Length > Scale ||
            (point >= 0 && fraction.IsEmpty) || !AllAsciiDigits(integer) ||
            !AllAsciiDigits(fraction) ||
            !decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal amount))
        {
            throw new ArgumentException("Amount must be an exact fixed-point number with at most 20 integer and 8 fractional digits.", nameof(text));
        }

        return amount;
    }

    public Money Add(Money other)
    {
        RequireSameCurrency(other);
        return new Money(checked(Amount + other.Amount), Currency);
    }

    public Money Subtract(Money other)
    {
        RequireSameCurrency(other);
        return new Money(checked(Amount - other.Amount), Currency);
    }

    private void RequireSameCurrency(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Currency != other.Currency)
        {
            throw new ArgumentException("Money operations require the same currency.", nameof(other));
        }
    }

    private static bool AllAsciiDigits(ReadOnlySpan<char> characters)
    {
        foreach (char character in characters)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
