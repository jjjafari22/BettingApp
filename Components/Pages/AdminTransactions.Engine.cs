#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using BettingApp.Data;
using BettingApp.Services;

namespace BettingApp.Components.Pages;

public partial class AdminTransactions
{
    private Transaction newTrans = new() { Type = "Deposit", Platform = "Vipps", Date = DateTime.UtcNow, Status = "Completed" };
    private int? adminAddAmount;
    private string otherPlatformName = "";
    private bool isPaymentDetailsAutoFilled = false;

    private string addTransSenderType = "User";
    private string addTransSenderId = "";
    private ApplicationUser? selectedSenderUser = null;
    private string senderSearchTerm = "";
    private List<ApplicationUser> senderSearchResults = new();

    private string addTransReceiverType = "User";
    private string addTransReceiverId = "";
    private ApplicationUser? selectedReceiverUser = null;
    private string receiverSearchTerm = "";
    private List<ApplicationUser> receiverSearchResults = new();

    private string searchTerm = "";
    private List<ApplicationUser> searchResults = new();
    private ApplicationUser? selectedUser;

    private string newTransPaymentDetailsBound
    {
        get => newTrans.PaymentDetails ?? "";
        set
        {
            if (newTrans.Platform == "Vipps")
            {
                var digits = new string((value ?? "").Where(char.IsDigit).ToArray());
                if (digits.Length > 8) digits = digits.Substring(0, 8);
                newTrans.PaymentDetails = digits;
            }
            else if (newTrans.Platform == "Revolut")
            {
                var val = (value ?? "").Trim();
                if (string.IsNullOrEmpty(val) || val == "@") { newTrans.PaymentDetails = ""; return; }
                if (!val.StartsWith("@")) val = "@" + val;
                val = "@" + val.Substring(1).Replace("@", "");
                newTrans.PaymentDetails = val;
            }
            else if (newTrans.Platform == "Bank Transfer")
            {
                var digits = new string((value ?? "").Where(char.IsDigit).ToArray());
                if (digits.Length > 11) digits = digits.Substring(0, 11);
                if (digits.Length <= 4) newTrans.PaymentDetails = digits;
                else if (digits.Length <= 6) newTrans.PaymentDetails = $"{digits.Substring(0, 4)}.{digits.Substring(4)}";
                else newTrans.PaymentDetails = $"{digits.Substring(0, 4)}.{digits.Substring(4, 2)}.{digits.Substring(6)}";
            }
            else
            {
                newTrans.PaymentDetails = value ?? "";
            }
        }
    }

    private void OnPlatformChanged(ChangeEventArgs e)
    {
        var val = e.Value?.ToString() ?? "";
        newTrans.Platform = val;
        AutoFillPaymentDetails();
    }

    private void AutoFillPaymentDetails()
    {
        string autoFilled = "";

        if (addTransReceiverType == "CashCow" && !string.IsNullOrEmpty(addTransReceiverId))
        {
            var cow = cashCows.FirstOrDefault(c => c.Id == addTransReceiverId);
            if (cow != null)
            {
                if (newTrans.Platform == "Vipps") autoFilled = cow.Vipps;
                else if (newTrans.Platform == "Revolut") autoFilled = cow.Revolut;
                else if (newTrans.Platform == "Bank Transfer") autoFilled = cow.BankTransfer;
                else if (newTrans.Platform == "Other")
                {
                    otherPlatformName = cow.OtherPlatformName;
                    autoFilled = cow.OtherPaymentDetails;
                }
            }
        }
        else if (addTransReceiverType == "User" && selectedReceiverUser != null)
        {
            var pending = pendingWithdrawals.FirstOrDefault(p => p.UserId == selectedReceiverUser.Id);
            if (pending != null && !string.IsNullOrEmpty(pending.PaymentDetails))
            {
                var parts = pending.PaymentDetails.Split(" | ", StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    if (newTrans.Platform == "Other")
                    {
                        if (!part.StartsWith("Vipps:") && !part.StartsWith("Revolut:") && !part.StartsWith("Bank Transfer:"))
                        {
                            var split = part.Split(":", 2);
                            if (split.Length == 2)
                            {
                                otherPlatformName = split[0].Trim();
                                autoFilled = split[1].Trim();
                            }
                            else
                            {
                                otherPlatformName = "Other";
                                autoFilled = part.Trim();
                            }
                            break;
                        }
                    }
                    else if (part.StartsWith(newTrans.Platform + ":"))
                    {
                        autoFilled = part.Substring(newTrans.Platform.Length + 1).Trim();
                        break;
                    }
                }
            }
        }

        newTrans.PaymentDetails = autoFilled ?? "";
        isPaymentDetailsAutoFilled = !string.IsNullOrEmpty(newTrans.PaymentDetails);
        if (newTrans.Platform == "Other" && string.IsNullOrEmpty(newTrans.PaymentDetails))
        {
            otherPlatformName = "";
        }
    }

    private async Task SearchSender()
    {
        if (string.IsNullOrWhiteSpace(senderSearchTerm) || senderSearchTerm.Length < 2)
        {
            senderSearchResults.Clear();
            return;
        }
        var searchLower = senderSearchTerm.ToLower();
        using var context = DbFactory.CreateDbContext();
        senderSearchResults = await context.Users
            .Where(u => (u.DiscordUsername != null && u.DiscordUsername.ToLower().Contains(searchLower)) || (u.UserName != null && u.UserName.ToLower().Contains(searchLower)))
            .OrderBy(u => u.DiscordUsername)
            .Take(10)
            .AsNoTracking()
            .ToListAsync();
    }

    private async Task SearchReceiver()
    {
        if (string.IsNullOrWhiteSpace(receiverSearchTerm) || receiverSearchTerm.Length < 2)
        {
            receiverSearchResults.Clear();
            return;
        }
        var searchLower = receiverSearchTerm.ToLower();
        using var context = DbFactory.CreateDbContext();
        receiverSearchResults = await context.Users
            .Where(u => (u.DiscordUsername != null && u.DiscordUsername.ToLower().Contains(searchLower)) || (u.UserName != null && u.UserName.ToLower().Contains(searchLower)))
            .OrderBy(u => u.DiscordUsername)
            .Take(10)
            .AsNoTracking()
            .ToListAsync();
    }

    private async Task AddTransaction()
    {
        if (isSavingTransaction) return;
        Task? minOverlayTask = null;
        bool isSuccess = false;
        try
        {
            isSavingTransaction = true;
            StateHasChanged();
            await Task.Delay(1);
            minOverlayTask = UiExecutionHelper.StartDelay();

            successMessage = null;
            errorMessage = null;

            if (string.IsNullOrEmpty(newTrans.Platform) || !adminAddAmount.HasValue || adminAddAmount.Value <= 0)
            {
                errorMessage = "Please enter valid amount and platform.";
                return;
            }
            if (newTrans.Type != "Free Bet" && addTransSenderType == "User" && string.IsNullOrEmpty(addTransSenderId)) { errorMessage = "Select Sender User"; return; }
            if (addTransReceiverType == "User" && string.IsNullOrEmpty(addTransReceiverId)) { errorMessage = "Select Receiver User"; return; }

            if (newTrans.Type != "Free Bet" && addTransSenderType == addTransReceiverType && addTransSenderId == addTransReceiverId)
            {
                errorMessage = "Sender and Receiver cannot be the exact same.";
                return;
            }

            if (newTrans.Type != "Free Bet")
            {
                string? vipps = newTrans.Platform == "Vipps" ? newTrans.PaymentDetails : null;
                string? revolut = newTrans.Platform == "Revolut" ? newTrans.PaymentDetails : null;
                string? bank = newTrans.Platform == "Bank Transfer" ? newTrans.PaymentDetails : null;
                string? otherPlatform = newTrans.Platform == "Other" ? otherPlatformName : null;
                string? otherDetails = newTrans.Platform == "Other" ? newTrans.PaymentDetails : null;

                var validationResult = BettingApp.Services.PaymentValidationHelper.ValidatePaymentMethods(
                    vipps, revolut, bank, otherPlatform, otherDetails, requireAtLeastOne: true);

                if (!validationResult.isValid)
                {
                    errorMessage = validationResult.errorMessage;
                    return;
                }
            }

            using var context = DbFactory.CreateDbContext();

            decimal amount = adminAddAmount.Value;

            if (newTrans.Type != "Free Bet" && newTrans.Platform == "Other")
            {
                newTrans.Platform = otherPlatformName;
            }
            else if (newTrans.Type == "Free Bet")
            {
                newTrans.Platform = "Free Bet";
            }

            // 1. Resolve Sender
            ApplicationUser? senderUser = null;
            CashCow? senderCC = null;
            if (addTransSenderType == "User") senderUser = await context.Users.FindAsync(addTransSenderId);
            else if (addTransSenderType == "CashCow") senderCC = await context.CashCows.FindAsync(addTransSenderId);

            // 2. Resolve Receiver
            ApplicationUser? receiverUser = null;
            CashCow? receiverCC = null;
            if (addTransReceiverType == "User") receiverUser = await context.Users.FindAsync(addTransReceiverId);
            else if (addTransReceiverType == "CashCow") receiverCC = await context.CashCows.FindAsync(addTransReceiverId);

            newTrans.AmountNOK = (int)amount;
            newTrans.Date = DateTime.UtcNow;
            newTrans.UpdatedAt = DateTime.UtcNow;
            newTrans.Status = "Completed";

            newTrans.SenderType = addTransSenderType;
            newTrans.SenderId = addTransSenderId;
            newTrans.SenderName = senderUser != null ? senderUser.FirstName + " " + senderUser.LastName : (senderCC != null ? senderCC.FirstName + " " + senderCC.LastName : "Unknown");

            newTrans.ReceiverType = addTransReceiverType;
            newTrans.ReceiverId = addTransReceiverId;
            newTrans.ReceiverName = receiverUser != null ? receiverUser.FirstName + " " + receiverUser.LastName : (receiverCC != null ? receiverCC.FirstName + " " + receiverCC.LastName : "Unknown");

            // Helper to create a base transaction row with optional Note stamped
            Transaction CreateBaseTx() => new Transaction
            {
                AmountNOK = (int)amount,
                Date = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Status = "Completed",
                Platform = newTrans.Platform,
                PaymentDetails = newTrans.PaymentDetails,
                Note = string.IsNullOrWhiteSpace(newTrans.Note) ? null : newTrans.Note.Trim(),
                SenderType = addTransSenderType,
                SenderId = addTransSenderId,
                SenderName = senderUser != null ? $"{senderUser.FirstName} {senderUser.LastName}" : (senderCC != null ? $"{senderCC.FirstName} {senderCC.LastName}" : "Unknown"),
                ReceiverType = addTransReceiverType,
                ReceiverId = addTransReceiverId,
                ReceiverName = receiverUser != null ? $"{receiverUser.FirstName} {receiverUser.LastName}" : (receiverCC != null ? $"{receiverCC.FirstName} {receiverCC.LastName}" : "Unknown")
            };

            var transactionsToAdd = new List<Transaction>();

            // ==========================================
            // THE AUTOMATED ROUTING ENGINE
            // ==========================================

            if (newTrans.Type == "Free Bet")
            {
                if (receiverUser != null)
                {
                    receiverUser.FreeBetBalance += amount;
                    var tx = CreateBaseTx();
                    tx.Type = "Free Bet";
                    tx.UserId = receiverUser.Id;
                    tx.UserName = receiverUser.UserName ?? "";
                    transactionsToAdd.Add(tx);
                }
            }
            else
            {
                int amountAlreadyDeducted = 0;
                // Intercept pending withdrawals to dynamically reduce their requested amount
                if (addTransReceiverType == "User" && receiverUser != null)
                {
                    var pendingW = await context.Transactions
                        .Where(t => t.UserId == receiverUser.Id && t.Type == "Withdrawal" && t.Status == "Pending")
                        .OrderBy(t => t.Date)
                        .FirstOrDefaultAsync();

                    if (pendingW != null)
                    {
                        // The pending request's original amount was ALREADY deducted from the user's balance.
                        decimal remainingToFulfill = pendingW.AmountNOK - pendingW.FulfilledAmount;
                        decimal pendingAmount = remainingToFulfill > 0 ? remainingToFulfill : 0;
                        amountAlreadyDeducted = (int)Math.Min(pendingAmount, (decimal)amount);

                        // Increment the FulfilledAmount instead of destructively modifying AmountNOK.
                        // This preserves the Initial Requested Amount for admin visibility.
                        pendingW.FulfilledAmount += (decimal)amount;
                    }
                }

                // 1. Process Sender (Gives physical cash => Gets digital balance / Deducts CC)
                if (addTransSenderType == "User" && senderUser != null)
                {
                    senderUser.Balance += amount;
                    var txSender = CreateBaseTx();
                    txSender.Type = "Deposit";
                    txSender.UserId = senderUser.Id;
                    txSender.UserName = senderUser.UserName ?? "";
                    transactionsToAdd.Add(txSender);
                }
                else if (addTransSenderType == "CashCow" && senderCC != null)
                {
                    senderCC.Balance -= amount;
                }

                // 2. Process Receiver (Gets physical cash => Loses digital balance / Adds CC)
                if (addTransReceiverType == "User" && receiverUser != null)
                {
                    receiverUser.Balance -= (amount - amountAlreadyDeducted);
                    var txReceiver = CreateBaseTx();
                    txReceiver.Type = "Withdrawal";
                    txReceiver.UserId = receiverUser.Id;
                    txReceiver.UserName = receiverUser.UserName ?? "";
                    transactionsToAdd.Add(txReceiver);
                }
                else if (addTransReceiverType == "CashCow" && receiverCC != null)
                {
                    receiverCC.Balance += amount;
                }

                if (addTransSenderType == "CashCow" && addTransReceiverType == "CashCow")
                {
                    var txTransfer = CreateBaseTx();
                    txTransfer.Type = "Transfer";
                    txTransfer.UserId = "";
                    txTransfer.UserName = "System";
                    transactionsToAdd.Add(txTransfer);
                }
            }

            context.Transactions.AddRange(transactionsToAdd);
            var auditAction = string.Join(" & ", transactionsToAdd.Select(t => t.Type).Distinct());
            context.AuditLogs.Add(new AuditLog
            {
                AdminUserName = currentAdminName ?? "Unknown",
                Action = $"Added {auditAction}",
                TargetUserName = "Multiple",
                Details = $"Amount: {amount}, From: {addTransSenderType}, To: {addTransReceiverType}" +
                          (!string.IsNullOrWhiteSpace(newTrans.Note) ? $", Note: {newTrans.Note.Trim()}" : "")
            });
            await context.SaveChangesAsync();

            foreach (var tx in transactionsToAdd)
            {
                if (tx.Type == "Withdrawal")
                {
                    var user = await context.Users.FindAsync(tx.UserId);
                    if (user != null && !string.IsNullOrEmpty(user.DiscordUserId))
                    {
                        await DiscordService.NotifyUserWithdrawalAsync(user.DiscordUserId, tx, "Completed");
                    }
                    UiNotifier.NotifyUser(tx.UserId, "Withdrawal processed");
                    UiNotifier.NotifyAdmin("Withdrawal processed");
                }
                else if (tx.Type == "Deposit")
                {
                    var user = await context.Users.FindAsync(tx.UserId);
                    if (user != null && !string.IsNullOrEmpty(user.DiscordUserId))
                    {
                        await DiscordService.NotifyUserDepositAsync(user.DiscordUserId, tx.AmountNOK);
                    }
                    UiNotifier.NotifyUser(tx.UserId, "Deposit processed");
                    UiNotifier.NotifyAdmin("Deposit processed");
                }
            }

            _ = ShowSuccess("Transaction added & balances dynamically routed successfully!");
            ClearForm();

            await LoadTransactions(context);
            isSuccess = true;
        }
        finally
        {
            await UiExecutionHelper.AwaitMinDurationAsync(minOverlayTask, isSuccess);
            isSavingTransaction = false;
            StateHasChanged();
        }
    }

    private void ClearForm()
    {
        var keepType = newTrans.Type;
        newTrans = new Transaction { Type = keepType, Platform = "Vipps", Date = DateTime.UtcNow, Status = "Completed" };
        adminAddAmount = null;

        addTransSenderType = "User";
        addTransSenderId = "";
        selectedSenderUser = null;
        senderSearchTerm = "";

        addTransReceiverType = "User";
        addTransReceiverId = "";
        selectedReceiverUser = null;
        receiverSearchTerm = "";

        otherPlatformName = "";
        isPaymentDetailsAutoFilled = false;
    }

    private async Task PerformSearchUsers()
    {
        if (string.IsNullOrWhiteSpace(searchTerm) || searchTerm.Length < 2)
        {
            searchResults.Clear();
            return;
        }

        using var context = DbFactory.CreateDbContext();
        searchResults = await context.Users
            .Where(u => u.DiscordUsername != null && u.DiscordUsername.Contains(searchTerm) || (u.UserName != null && u.UserName.Contains(searchTerm)))
            .OrderBy(u => u.DiscordUsername)
            .Take(5)
            .AsNoTracking()
            .ToListAsync();
    }

    private void SelectUser(ApplicationUser user)
    {
        selectedUser = user;
        searchTerm = !string.IsNullOrEmpty(user.DiscordUsername) ? user.DiscordUsername : (user.UserName ?? "");
        searchResults.Clear();

        newTrans.UserId = selectedUser.Id;
        newTrans.UserName = selectedUser.UserName ?? "Unknown";
        errorMessage = null;
    }

    private void ClearSelection()
    {
        selectedUser = null;
        searchTerm = "";
        searchResults.Clear();
        newTrans.UserId = "";
    }
}
