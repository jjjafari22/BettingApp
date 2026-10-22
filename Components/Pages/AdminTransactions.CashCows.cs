#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using BettingApp.Data;

namespace BettingApp.Components.Pages;

public partial class AdminTransactions
{
    private List<CashCow> cashCows = new();
    private CashCow newCow = new();
    private string? editingCowId = null;

    private CashCow? viewingCow = null;
    private List<Transaction> viewingCowTransactions = new();
    private int viewingCowCurrentPage = 1;
    private bool viewingCowHasMore = false;
    private const int ViewingCowPageSize = 20;

    private DateTime? ccStartDate;
    private DateTime? ccEndDate;
    private Dictionary<string, int> ccPeriodNet = new();
    private Dictionary<string, DateTime?> ccLastUpdated = new();
    private string ccSortColumn = "LastUpdated";
    private bool ccSortAscending = false;

    private enum ViewMode { AllTime, Daily, Weekly, Monthly, Yearly, Custom }
    private ViewMode viewMode = ViewMode.AllTime;
    private DateTime currentViewDate = DateTime.UtcNow.Date;
    private bool showInternalAccounts = false;

    private string newCowVippsBound
    {
        get => newCow.Vipps;
        set
        {
            var digits = new string((value ?? "").Where(char.IsDigit).ToArray());
            if (digits.Length > 8) digits = digits.Substring(0, 8);
            newCow.Vipps = digits;
        }
    }

    private string newCowRevolutBound
    {
        get => newCow.Revolut;
        set
        {
            var val = (value ?? "").Trim();
            if (string.IsNullOrEmpty(val) || val == "@") { newCow.Revolut = ""; return; }
            if (!val.StartsWith("@")) val = "@" + val;
            val = "@" + val.Substring(1).Replace("@", "");
            newCow.Revolut = val;
        }
    }

    private string newCowBankTransferBound
    {
        get => newCow.BankTransfer;
        set
        {
            var digits = new string((value ?? "").Where(char.IsDigit).ToArray());
            if (digits.Length > 11) digits = digits.Substring(0, 11);
            if (digits.Length <= 4) newCow.BankTransfer = digits;
            else if (digits.Length <= 6) newCow.BankTransfer = $"{digits.Substring(0, 4)}.{digits.Substring(4)}";
            else newCow.BankTransfer = $"{digits.Substring(0, 4)}.{digits.Substring(4, 2)}.{digits.Substring(6)}";
        }
    }

    private async Task OpenCowLedger(CashCow cow)
    {
        viewingCowCurrentPage = 1;
        await LoadCowTransactions(cow);
    }

    private async Task NextCowPage()
    {
        viewingCowCurrentPage++;
        await LoadCowTransactions(viewingCow!);
    }

    private async Task PrevCowPage()
    {
        if (viewingCowCurrentPage > 1)
        {
            viewingCowCurrentPage--;
            await LoadCowTransactions(viewingCow!);
        }
    }

    private async Task LoadCowTransactions(CashCow cow)
    {
        viewingCow = cow;
        using var context = DbFactory.CreateDbContext();

        var query = context.Transactions.Where(t => t.SenderId == cow.Id || t.ReceiverId == cow.Id);

        if (viewMode != ViewMode.AllTime)
        {
            DateTime startBase, endBase;
            switch (viewMode)
            {
                case ViewMode.Daily:
                    startBase = currentViewDate.Date;
                    endBase = startBase.AddDays(1);
                    break;
                case ViewMode.Weekly:
                    startBase = GetStartOfWeek(currentViewDate);
                    endBase = startBase.AddDays(7);
                    break;
                case ViewMode.Monthly:
                    startBase = new DateTime(currentViewDate.Year, currentViewDate.Month, 1);
                    endBase = startBase.AddMonths(1);
                    break;
                case ViewMode.Yearly:
                    startBase = new DateTime(currentViewDate.Year, 1, 1);
                    endBase = startBase.AddYears(1);
                    break;
                case ViewMode.Custom:
                    startBase = ccStartDate?.Date ?? currentViewDate.Date;
                    endBase = (ccEndDate?.Date ?? currentViewDate.Date).AddDays(1);
                    break;
                default:
                    startBase = currentViewDate.Date;
                    endBase = startBase.AddDays(1);
                    break;
            }

            bool isFiltering = viewMode != ViewMode.Custom || ccStartDate.HasValue || ccEndDate.HasValue;
            if (isFiltering)
            {
                var startUtc = DateTime.SpecifyKind(startBase.AddMinutes(TimezoneOffset), DateTimeKind.Utc);
                var endUtc = DateTime.SpecifyKind(endBase.AddMinutes(TimezoneOffset), DateTimeKind.Utc);
                query = query.Where(t => t.Date >= startUtc && t.Date < endUtc);
            }
        }

        var total = await query.CountAsync();
        viewingCowHasMore = total > (viewingCowCurrentPage * ViewingCowPageSize);

        viewingCowTransactions = await query
            .OrderByDescending(t => t.UpdatedAt)
            .Skip((viewingCowCurrentPage - 1) * ViewingCowPageSize)
            .Take(ViewingCowPageSize)
            .ToListAsync();

        var additionalUserIds = viewingCowTransactions.Select(t => t.SenderId).Concat(viewingCowTransactions.Select(t => t.ReceiverId)).Distinct();
        await PopulateUserLookup(additionalUserIds!);
    }

    private void EditCashCow(CashCow cow)
    {
        errorMessage = null;
        editingCowId = cow.Id;
        newCow = new CashCow
        {
            Id = cow.Id,
            FirstName = cow.FirstName?.Trim() ?? "",
            LastName = cow.LastName?.Trim() ?? "",
            Vipps = cow.Vipps,
            Revolut = cow.Revolut,
            BankTransfer = cow.BankTransfer,
            OtherPlatformName = cow.OtherPlatformName,
            OtherPaymentDetails = cow.OtherPaymentDetails,
            Balance = cow.Balance,
            CreatedAt = cow.CreatedAt
        };
    }

    private void CancelEditCashCow()
    {
        errorMessage = null;
        editingCowId = null;
        newCow = new CashCow();
    }

    private async Task SaveCashCow()
    {
        errorMessage = null;
        successMessage = null;

        newCow.FirstName = (newCow.FirstName ?? "").Trim();
        newCow.LastName = (newCow.LastName ?? "").Trim();
        newCow.OtherPlatformName = (newCow.OtherPlatformName ?? "").Trim();
        newCow.OtherPaymentDetails = (newCow.OtherPaymentDetails ?? "").Trim();

        if (string.IsNullOrWhiteSpace(newCow.FirstName))
        {
            errorMessage = "First Name is required.";
            return;
        }

        var validationResult = BettingApp.Services.PaymentValidationHelper.ValidatePaymentMethods(
            newCow.Vipps, newCow.Revolut, newCow.BankTransfer, newCow.OtherPlatformName, newCow.OtherPaymentDetails, requireAtLeastOne: false);

        if (!validationResult.isValid)
        {
            errorMessage = validationResult.errorMessage;
            return;
        }

        using var context = DbFactory.CreateDbContext();
        if (editingCowId == null)
        {
            context.CashCows.Add(newCow);
        }
        else
        {
            var dbCow = await context.CashCows.FindAsync(editingCowId);
            if (dbCow != null)
            {
                dbCow.FirstName = newCow.FirstName;
                dbCow.LastName = newCow.LastName;
                dbCow.Vipps = newCow.Vipps;
                dbCow.Revolut = newCow.Revolut;
                dbCow.BankTransfer = newCow.BankTransfer;
                dbCow.OtherPlatformName = newCow.OtherPlatformName;
                dbCow.OtherPaymentDetails = newCow.OtherPaymentDetails;
            }
        }
        await context.SaveChangesAsync();

        bool wasEditing = editingCowId != null;
        editingCowId = null;
        newCow = new CashCow();
        await LoadTransactions(context);
        _ = ShowSuccess(wasEditing ? "Cash Cow updated successfully." : "Cash Cow added successfully.");
    }

    private void SortCashCows(string column)
    {
        if (ccSortColumn == column)
        {
            ccSortAscending = !ccSortAscending;
        }
        else
        {
            ccSortColumn = column;
            ccSortAscending = false;
        }
    }

    private string GetSortIcon(string column)
    {
        if (ccSortColumn != column) return "";
        return ccSortAscending ? "↑" : "↓";
    }

    private int GetCashCowPeriodNet(CashCow cow)
    {
        bool isFiltering = viewMode != ViewMode.Custom || ccStartDate.HasValue || ccEndDate.HasValue;
        return isFiltering
            ? (ccPeriodNet.TryGetValue(cow.Id, out var n) ? n : 0)
            : (int)cow.Balance;
    }

    private IEnumerable<CashCow> GetSortedCashCows()
    {
        var query = cashCows.AsEnumerable();

        if (!showInternalAccounts)
        {
            query = query.Where(c => !(string.Equals(c.FirstName?.Trim(), "MISC", StringComparison.OrdinalIgnoreCase) &&
                                       string.Equals(c.LastName?.Trim(), "CASTLE", StringComparison.OrdinalIgnoreCase)));
        }

        IOrderedEnumerable<CashCow> ordered;
        if (ccSortColumn == "LifetimeBalance")
        {
            ordered = ccSortAscending ? query.OrderBy(c => c.Balance) : query.OrderByDescending(c => c.Balance);
        }
        else if (ccSortColumn == "PeriodNet")
        {
            ordered = ccSortAscending ? query.OrderBy(GetCashCowPeriodNet) : query.OrderByDescending(GetCashCowPeriodNet);
        }
        else
        {
            // LastUpdated (default)
            ordered = ccSortAscending
                ? query.OrderBy(c => ccLastUpdated.TryGetValue(c.Id, out var d) ? (d ?? DateTime.MinValue) : DateTime.MinValue)
                : query.OrderByDescending(c => ccLastUpdated.TryGetValue(c.Id, out var d) ? (d ?? DateTime.MinValue) : DateTime.MinValue);
        }
        return ordered.ThenBy(c => FormatFullName(c.FirstName, c.LastName), StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Id);
    }

    private async Task ClearCashCowFilter()
    {
        ccStartDate = null;
        ccEndDate = null;
        ccPeriodNet = new Dictionary<string, int>();
        if (viewingCow != null)
        {
            viewingCowCurrentPage = 1;
            await LoadCowTransactions(viewingCow);
        }
    }

    private void SetViewMode(ViewMode mode)
    {
        viewMode = mode;
        if (mode != ViewMode.Custom)
        {
            _ = GoToCurrent();
        }
        else
        {
            _ = UpdateCashCowPeriodNet();
        }
    }

    private async Task GoToCurrent()
    {
        currentViewDate = DateTime.UtcNow.AddMinutes(-TimezoneOffset).Date;
        await UpdateCashCowPeriodNet();
    }

    private void PreviousPeriod()
    {
        currentViewDate = viewMode switch
        {
            ViewMode.Daily => currentViewDate.AddDays(-1),
            ViewMode.Weekly => currentViewDate.AddDays(-7),
            ViewMode.Monthly => currentViewDate.AddMonths(-1),
            ViewMode.Yearly => currentViewDate.AddYears(-1),
            _ => currentViewDate
        };
        _ = UpdateCashCowPeriodNet();
    }

    private void NextPeriod()
    {
        currentViewDate = viewMode switch
        {
            ViewMode.Daily => currentViewDate.AddDays(1),
            ViewMode.Weekly => currentViewDate.AddDays(7),
            ViewMode.Monthly => currentViewDate.AddMonths(1),
            ViewMode.Yearly => currentViewDate.AddYears(1),
            _ => currentViewDate
        };
        _ = UpdateCashCowPeriodNet();
    }

    private string GetPeriodDisplayString()
    {
        return viewMode switch
        {
            ViewMode.Daily => currentViewDate.ToString("dd MMM yyyy"),
            ViewMode.Weekly =>
                GetStartOfWeek(currentViewDate).ToString("dd MMM") + " - " +
                GetStartOfWeek(currentViewDate).AddDays(6).ToString("dd MMM yyyy"),
            ViewMode.Monthly => currentViewDate.ToString("MMMM yyyy"),
            ViewMode.Yearly => currentViewDate.ToString("yyyy"),
            _ => ""
        };
    }

    private DateTime GetStartOfWeek(DateTime dt)
    {
        int diff = (7 + (dt.DayOfWeek - DayOfWeek.Monday)) % 7;
        return dt.Date.AddDays(-1 * diff);
    }

    private async Task OnCustomDateChanged()
    {
        if (viewMode == ViewMode.Custom)
        {
            await UpdateCashCowPeriodNet();
        }
    }

    private async Task UpdateCashCowPeriodNetCore(ApplicationDbContext context)
    {
        if (viewMode == ViewMode.Custom && ccStartDate == null && ccEndDate == null)
        {
            ccPeriodNet = new Dictionary<string, int>();
            return;
        }

        var query = context.Transactions
            .AsNoTracking()
            .Where(t => t.Status == "Completed" && (t.SenderType == "CashCow" || t.ReceiverType == "CashCow"));

        if (viewMode != ViewMode.AllTime)
        {
            DateTime startBase, endBase;
            switch (viewMode)
            {
                case ViewMode.Daily:
                    startBase = currentViewDate.Date;
                    endBase = startBase.AddDays(1);
                    break;
                case ViewMode.Weekly:
                    startBase = GetStartOfWeek(currentViewDate);
                    endBase = startBase.AddDays(7);
                    break;
                case ViewMode.Monthly:
                    startBase = new DateTime(currentViewDate.Year, currentViewDate.Month, 1);
                    endBase = startBase.AddMonths(1);
                    break;
                case ViewMode.Yearly:
                    startBase = new DateTime(currentViewDate.Year, 1, 1);
                    endBase = startBase.AddYears(1);
                    break;
                case ViewMode.Custom:
                    startBase = ccStartDate?.Date ?? currentViewDate.Date;
                    endBase = (ccEndDate?.Date ?? currentViewDate.Date).AddDays(1);
                    break;
                default:
                    startBase = currentViewDate.Date;
                    endBase = startBase.AddDays(1);
                    break;
            }

            var startUtc = DateTime.SpecifyKind(startBase.AddMinutes(TimezoneOffset), DateTimeKind.Utc);
            var endUtc = DateTime.SpecifyKind(endBase.AddMinutes(TimezoneOffset), DateTimeKind.Utc);
            query = query.Where(t => t.Date >= startUtc && t.Date < endUtc);
        }

        var txs = await query
            .Select(t => new { t.SenderType, t.SenderId, t.ReceiverType, t.ReceiverId, t.AmountNOK })
            .ToListAsync();

        var nextPeriodNet = new Dictionary<string, int>();
        foreach (var cow in cashCows)
        {
            var ccId = cow.Id;
            var sent = txs.Where(t => t.SenderType == "CashCow" && string.Equals(t.SenderId, ccId, StringComparison.OrdinalIgnoreCase)).Sum(t => t.AmountNOK);
            var received = txs.Where(t => t.ReceiverType == "CashCow" && string.Equals(t.ReceiverId, ccId, StringComparison.OrdinalIgnoreCase)).Sum(t => t.AmountNOK);
            nextPeriodNet[ccId] = received - sent;
        }
        ccPeriodNet = nextPeriodNet;
    }

    private async Task UpdateCashCowPeriodNet()
    {
        if (viewMode == ViewMode.Custom && ccStartDate == null && ccEndDate == null)
        {
            await ClearCashCowFilter();
            await InvokeAsync(StateHasChanged);
            return;
        }

        using var context = DbFactory.CreateDbContext();
        await UpdateCashCowPeriodNetCore(context);

        if (viewingCow != null)
        {
            viewingCowCurrentPage = 1;
            await LoadCowTransactions(viewingCow);
        }

        await InvokeAsync(StateHasChanged);
    }
}
