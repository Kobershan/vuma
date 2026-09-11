namespace VumaRetail.Domain.Inventory;

/// <summary>Validation and normalisation for optional company-local stock identity.</summary>
public static class StockTracking
{
    /// <summary>Normalises one tracking string, rejecting oversized values.</summary>
    public static string? Normalise(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string trimmed = value.Trim();
        if (trimmed.Length > 128)
        {
            throw new ArgumentException("A stock tracking value may not exceed 128 characters.", parameterName);
        }

        return trimmed;
    }

    /// <summary>Validates the identity against the signed quantity being recorded.</summary>
    public static (string? BatchReference, DateOnly? ExpiryDate, string? SerialNumber) Validate(
        decimal quantity,
        string? batchReference,
        DateOnly? expiryDate,
        string? serialNumber)
    {
        if (quantity == 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        string? batch = Normalise(batchReference, nameof(batchReference));
        string? serial = Normalise(serialNumber, nameof(serialNumber));
        if (serial is not null && Math.Abs(quantity) != 1m)
        {
            throw new ArgumentException("A serialised movement must have a quantity of exactly one.", nameof(quantity));
        }

        if (expiryDate is not null && batch is null)
        {
            throw new ArgumentException("An expiry date requires a batch reference.", nameof(expiryDate));
        }

        return (batch, expiryDate, serial);
    }
}
