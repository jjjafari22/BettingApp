namespace BettingApp.Services;

public static class PaymentValidationHelper
{
    public static (bool isValid, string errorMessage) ValidatePaymentMethods(
        string? vipps, 
        string? revolut, 
        string? bank, 
        string? otherPlatform, 
        string? otherDetails,
        bool requireAtLeastOne = false)
    {
        bool hasVipps = !string.IsNullOrWhiteSpace(vipps);
        bool hasBank = !string.IsNullOrWhiteSpace(bank);
        bool hasRevolut = !string.IsNullOrWhiteSpace(revolut);
        bool hasOtherPlatform = !string.IsNullOrWhiteSpace(otherPlatform);
        bool hasOtherDetails = !string.IsNullOrWhiteSpace(otherDetails);

        if (hasOtherPlatform != hasOtherDetails)
        {
            return (false, "For 'Other Payment Method', you must fill out BOTH the platform name and the receiving info.");
        }

        if (hasVipps && vipps!.Count(char.IsDigit) != 8)
        {
            return (false, "Vipps number must be exactly 8 digits.");
        }

        if (hasBank && bank!.Count(char.IsDigit) != 11)
        {
            return (false, "Bank account number must be exactly 11 digits.");
        }

        if (requireAtLeastOne)
        {
            int methodsCount = (hasVipps ? 1 : 0) + (hasRevolut ? 1 : 0) + (hasBank ? 1 : 0) + (hasOtherPlatform ? 1 : 0);
            if (methodsCount == 0)
            {
                return (false, "Please provide at least one payment method to receive your withdrawal.");
            }
        }

        return (true, "");
    }
}
