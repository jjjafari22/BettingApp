#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using BettingApp.Data;
using BettingApp.Services;

namespace BettingApp.Components.Pages;

public partial class AdminTransactions
{
    private List<Transaction> pendingWithdrawals = new();

    private int? editingTransactionId = null;
    private int editingTransactionAmount = 0;

    private string editPendingVipps = "";
    private string editPendingRevolut = "";
    private string editPendingBank = "";
    private string editPendingOtherPlatform = "";
    private string editPendingOtherDetails = "";

    private string editPendingVippsBound
    {
        get => editPendingVipps;
        set
        {
            var digits = new string((value ?? "").Where(char.IsDigit).ToArray());
            if (digits.Length > 8) digits = digits.Substring(0, 8);
            editPendingVipps = digits;
        }
    }

    private string editPendingRevolutBound
    {
        get => editPendingRevolut;
        set
        {
            var val = (value ?? "").Trim();
            if (string.IsNullOrEmpty(val) || val == "@") { editPendingRevolut = ""; return; }
            if (!val.StartsWith("@")) val = "@" + val;
            val = "@" + val.Substring(1).Replace("@", "");
            editPendingRevolut = val;
        }
    }

    private string editPendingBankBound
    {
        get => editPendingBank;
        set
        {
            var digits = new string((value ?? "").Where(char.IsDigit).ToArray());
            if (digits.Length > 11) digits = digits.Substring(0, 11);
            if (digits.Length <= 4) editPendingBank = digits;
            else if (digits.Length <= 6) editPendingBank = $"{digits.Substring(0, 4)}.{digits.Substring(4)}";
            else editPendingBank = $"{digits.Substring(0, 4)}.{digits.Substring(4, 2)}.{digits.Substring(6)}";
        }
    }

    private async Task ConfirmWithdraw(Transaction t)
    {
        if (isProcessingAction) return;
        try
        {
            isProcessingAction = true;
            successMessage = null;
            errorMessage = null;
            var displayUser = userLookup.TryGetValue(t.UserId, out var u) && !string.IsNullOrEmpty(u.DiscordName) ? u.DiscordName : t.UserName;

            var remaining = t.AmountNOK - t.FulfilledAmount;
            if (remaining > 0) return;

            var msg = $"This withdrawal has been fully fulfilled. Clear this {t.AmountNOK}kr request for {displayUser}?";

            var confirmed = await DialogService.ConfirmAsync(msg);
            if (!confirmed) return;

            using var context = DbFactory.CreateDbContext();

            var dbTrans = await context.Transactions.FindAsync(t.Id);
            if (dbTrans != null)
            {
                var dbRemaining = dbTrans.AmountNOK - dbTrans.FulfilledAmount;
                if (dbRemaining <= 0)
                {
                    context.Transactions.Remove(dbTrans);
                    context.AuditLogs.Add(new AuditLog { AdminUserName = currentAdminName ?? "Unknown", Action = "Cleared Fulfilled Withdrawal", TargetUserName = t.UserName, Details = $"ID: {t.Id}" });
                    await context.SaveChangesAsync();
                    _ = ShowSuccess("Fulfilled withdrawal request cleared.");
                    await LoadTransactions(context);
                }
            }
        }
        finally
        {
            isProcessingAction = false;
        }
    }

    private void StartEditTransaction(Transaction t)
    {
        editingTransactionId = t.Id;
        editingTransactionAmount = t.AmountNOK;

        editPendingVipps = "";
        editPendingRevolut = "";
        editPendingBank = "";
        editPendingOtherPlatform = "";
        editPendingOtherDetails = "";

        if (!string.IsNullOrEmpty(t.PaymentDetails))
        {
            var parts = t.PaymentDetails.Split(" | ", StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (part.StartsWith("Vipps:")) editPendingVipps = part.Substring(6).Trim();
                else if (part.StartsWith("Revolut:")) editPendingRevolut = part.Substring(8).Trim();
                else if (part.StartsWith("Bank Transfer:")) editPendingBank = part.Substring(14).Trim();
                else
                {
                    var split = part.Split(":", 2);
                    if (split.Length == 2)
                    {
                        editPendingOtherPlatform = split[0].Trim();
                        editPendingOtherDetails = split[1].Trim();
                    }
                    else
                    {
                        editPendingOtherPlatform = "Other";
                        editPendingOtherDetails = part.Trim();
                    }
                }
            }
        }
    }

    private void CancelTransactionEdit()
    {
        editingTransactionId = null;
    }

    private async Task SaveTransactionEdit(Transaction t)
    {
        if (editingTransactionAmount <= 0)
        {
            errorMessage = "Amount must be greater than zero.";
            return;
        }

        var validationResult = BettingApp.Services.PaymentValidationHelper.ValidatePaymentMethods(
            editPendingVipps, editPendingRevolut, editPendingBank, editPendingOtherPlatform, editPendingOtherDetails, requireAtLeastOne: true);

        if (!validationResult.isValid)
        {
            errorMessage = validationResult.errorMessage;
            return;
        }

        var confirmed = await DialogService.ConfirmAsync(
            $"Save changes to pending withdrawal?");
        if (!confirmed) return;

        using var context = DbFactory.CreateDbContext();
        var dbTrans = await context.Transactions.FindAsync(t.Id);
        var user = await context.Users.FindAsync(t.UserId);

        if (dbTrans != null && user != null && dbTrans.Status == "Pending")
        {
            int oldAmount = dbTrans.AmountNOK;
            int difference = oldAmount - editingTransactionAmount;

            if (difference < 0 && user.Balance < Math.Abs(difference))
            {
                var confirmNegative = await DialogService.ConfirmAsync(
                    $"This edit will result in a negative balance ({user.Balance + difference:N0} kr). Proceed anyway?");
                if (!confirmNegative) return;
            }

            user.Balance += difference;
            dbTrans.AmountNOK = editingTransactionAmount;

            var newDetailsList = new List<string>();
            if (!string.IsNullOrWhiteSpace(editPendingVipps)) newDetailsList.Add($"Vipps: {editPendingVipps.Trim()}");
            if (!string.IsNullOrWhiteSpace(editPendingRevolut)) newDetailsList.Add($"Revolut: {editPendingRevolut.Trim()}");
            if (!string.IsNullOrWhiteSpace(editPendingBank)) newDetailsList.Add($"Bank Transfer: {editPendingBank.Trim()}");
            if (!string.IsNullOrWhiteSpace(editPendingOtherPlatform) && !string.IsNullOrWhiteSpace(editPendingOtherDetails))
                newDetailsList.Add($"{editPendingOtherPlatform.Trim()}: {editPendingOtherDetails.Trim()}");

            string newDetailsStr = string.Join(" | ", newDetailsList);

            string oldDetails = dbTrans.PaymentDetails ?? "";
            dbTrans.PaymentDetails = newDetailsStr;

            dbTrans.UpdatedAt = DateTime.UtcNow;

            context.AuditLogs.Add(new AuditLog
            {
                AdminUserName = currentAdminName ?? "Unknown",
                Action = "Edited Pending Withdrawal",
                TargetUserName = t.UserName,
                Details = $"ID: {t.Id}, OldAmt: {oldAmount}, NewAmt: {editingTransactionAmount}, OldDet: {oldDetails}, NewDet: {newDetailsStr}"
            });

            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                using var reloadContext = DbFactory.CreateDbContext();
                await LoadTransactions(reloadContext);
                errorMessage = "Concurrency conflict: This user or withdrawal was modified concurrently. Please try again.";
                return;
            }
            UiNotifier.NotifyUser(t.UserId, "Withdrawal amount updated");
            UiNotifier.NotifyAdmin("Withdrawal amount updated");

            _ = ShowSuccess($"Withdrawal amount updated to {editingTransactionAmount} kr.");
            editingTransactionId = null;
            await LoadTransactions(context);
        }
    }

    private async Task RejectWithdraw(Transaction t)
    {
        if (isProcessingAction) return;
        try
        {
            isProcessingAction = true;
            var confirmed = await DialogService.ConfirmAsync("Reject this withdrawal request?");
            if (!confirmed) return;

            using var context = DbFactory.CreateDbContext();
            var dbTrans = await context.Transactions.FindAsync(t.Id);
            var user = await context.Users.FindAsync(t.UserId);
            if (dbTrans != null && user != null && dbTrans.Status == "Pending")
            {
                dbTrans.Status = "Rejected";
                dbTrans.UpdatedAt = DateTime.UtcNow;

                var remainingToRefund = dbTrans.AmountNOK - dbTrans.FulfilledAmount;
                user.Balance += (int)remainingToRefund;
                context.AuditLogs.Add(new AuditLog { AdminUserName = currentAdminName ?? "Unknown", Action = "Rejected Withdrawal", TargetUserName = t.UserName, Details = $"Amount: {remainingToRefund} (Original: {t.AmountNOK}), ID: {t.Id}" });
                await context.SaveChangesAsync();

                UiNotifier.NotifyUser(t.UserId, "Withdrawal rejected");
                UiNotifier.NotifyAdmin("Withdrawal rejected");
                if (!string.IsNullOrEmpty(user.DiscordUserId))
                {
                    await DiscordService.NotifyUserWithdrawalAsync(user.DiscordUserId, t, "Rejected");
                }
                _ = ShowSuccess("Withdrawal rejected.");
                await LoadTransactions(context);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            using var reloadContext = DbFactory.CreateDbContext();
            await LoadTransactions(reloadContext);
            errorMessage = "Concurrency conflict: This user or withdrawal was modified concurrently. Please try again.";
        }
        finally
        {
            isProcessingAction = false;
        }
    }
}
