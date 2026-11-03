#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using BettingApp.Data;

namespace BettingApp.Components.Pages;

public partial class AdminTransactions
{
    private List<CashCow> cashCows = new();
    private CashCow newCow = new();
    private string? editingCowId = null;
    private bool showCashCowModal = false;
    private string? cashCowModalError = null;
    private bool isSavingCashCow = false;

    private CashCow? viewingCow = null;
    private List<Transaction> viewingCowTransactions = new();
    private int viewingCowCurrentPage = 1;
    private bool viewingCowHasMore = false;
    private int viewingCowTotalTransactions = 0;
    private const int ViewingCowPageSize = 20;
    private bool isExportingCowLedgerCsv = false;

    private string cowLedgerFilterType = "";
    private string cowLedgerFilterPlatform = "";
    private List<string> cowLedgerAvailableTypes = new() { "Deposit", "Withdrawal", "Transfer" };
    private List<string> cowLedgerAvailablePlatforms = new() { "Vipps", "Revolut", "Bank Transfer" };
    private string cowLedgerSearchTerm = "";
    private List<TransactionPartyFilterItem> cowLedgerSearchResults = new();
    private TransactionPartyFilterItem? cowLedgerSelectedParty;
    private int _cowLedgerSearchVersion = 0;

    private bool HasActiveCowLedgerFilters =>
        !string.IsNullOrEmpty(cowLedgerFilterType) ||
        !string.IsNullOrEmpty(cowLedgerFilterPlatform) ||
        cowLedgerSelectedParty != null;

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
    private bool ccShowNorwayTimezone = false;
    private int _ccPeriodQueryVersion = 0;

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

    private static string FormatCowLedgerTypeOptionLabel(string type) => type switch
    {
        "Deposit" => "Deposits",
        "Withdrawal" => "Withdrawals",
        "Transfer" => "Transfers",
        _ => type
    };

    private void ResetCowLedgerFiltersState()
    {
        ++_cowLedgerSearchVersion;
        cowLedgerFilterType = "";
        cowLedgerFilterPlatform = "";
        cowLedgerSearchTerm = "";
        cowLedgerSearchResults.Clear();
        cowLedgerSelectedParty = null;
    }

    private async Task OpenCowLedger(CashCow cow)
    {
        ResetCowLedgerFiltersState();
        viewingCowCurrentPage = 1;
        await LoadCowTransactions(cow);
    }

    private void CloseCowLedger()
    {
        ResetCowLedgerFiltersState();
        viewingCow = null;
        viewingCowTransactions.Clear();
        viewingCowTotalTransactions = 0;
        viewingCowHasMore = false;
    }

    private async Task ClearAllCowLedgerFilters()
    {
        ResetCowLedgerFiltersState();
        if (viewingCow != null)
        {
            viewingCowCurrentPage = 1;
            await LoadCowTransactions(viewingCow);
        }
    }

    private async Task OnCowLedgerTypeChanged(ChangeEventArgs e)
    {
        cowLedgerFilterType = e.Value?.ToString() ?? "";
        if (viewingCow != null)
        {
            viewingCowCurrentPage = 1;
            await LoadCowTransactions(viewingCow);
        }
    }

    private async Task OnCowLedgerPlatformChanged(ChangeEventArgs e)
    {
        cowLedgerFilterPlatform = e.Value?.ToString() ?? "";
        if (viewingCow != null)
        {
            viewingCowCurrentPage = 1;
            await LoadCowTransactions(viewingCow);
        }
    }

    private async Task PerformCowLedgerPartySearch()
    {
        int version = ++_cowLedgerSearchVersion;

        if (cowLedgerSelectedParty != null && cowLedgerSearchTerm != cowLedgerSelectedParty.PrimaryLabel)
        {
            cowLedgerSelectedParty = null;
            if (viewingCow != null)
            {
                viewingCowCurrentPage = 1;
                await LoadCowTransactions(viewingCow);
            }
        }

        if (string.IsNullOrWhiteSpace(cowLedgerSearchTerm) || cowLedgerSearchTerm.Trim().Length < 2)
        {
            cowLedgerSearchResults.Clear();
            return;
        }

        var results = await SearchTransactionPartiesAsync(cowLedgerSearchTerm, excludeCashCowId: viewingCow?.Id);
        if (version != _cowLedgerSearchVersion) return;
        cowLedgerSearchResults = results;
    }

    private async Task SelectCowLedgerParty(TransactionPartyFilterItem party)
    {
        ++_cowLedgerSearchVersion;
        cowLedgerSelectedParty = party;
        cowLedgerSearchTerm = party.PrimaryLabel;
        cowLedgerSearchResults.Clear();
        if (viewingCow != null)
        {
            viewingCowCurrentPage = 1;
            await LoadCowTransactions(viewingCow);
        }
    }

    private async Task ClearCowLedgerPartySelection()
    {
        ++_cowLedgerSearchVersion;
        bool hadSelection = cowLedgerSelectedParty != null;
        cowLedgerSelectedParty = null;
        cowLedgerSearchTerm = "";
        cowLedgerSearchResults.Clear();

        if (hadSelection && viewingCow != null)
        {
            viewingCowCurrentPage = 1;
            await LoadCowTransactions(viewingCow);
        }
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

    private IQueryable<Transaction> BuildCowLedgerFilteredQuery(ApplicationDbContext context, string cowId)
    {
        var query = context.Transactions
            .AsNoTracking()
            .Where(t => t.SenderId == cowId || t.ReceiverId == cowId);

        var (startUtc, endUtc) = GetCashCowPeriodUtcBounds();
        if (startUtc.HasValue)
        {
            query = query.Where(t => t.Date >= startUtc.Value);
        }
        if (endUtc.HasValue)
        {
            query = query.Where(t => t.Date < endUtc.Value);
        }

        if (!string.IsNullOrEmpty(cowLedgerFilterType))
        {
            query = query.Where(t => t.Type == cowLedgerFilterType);
        }

        if (!string.IsNullOrEmpty(cowLedgerFilterPlatform))
        {
            query = query.Where(t => t.Platform == cowLedgerFilterPlatform);
        }

        if (cowLedgerSelectedParty != null)
        {
            var partyId = cowLedgerSelectedParty.Id;
            query = query.Where(t =>
                (t.SenderId == cowId && t.ReceiverId == partyId) ||
                (t.ReceiverId == cowId && t.SenderId == partyId));
        }

        return query;
    }

    private string FormatCowLedgerCounterpartyName(string? partyType, string? partyId, string? fallbackName)
    {
        string nameDisplay = partyType == "CashCow"
            ? ResolveCashCowName(partyId, fallbackName)
            : (fallbackName ?? "");

        if (!string.IsNullOrEmpty(partyId) && userLookup.TryGetValue(partyId, out var u))
        {
            var fullName = FormatFullName(u.FirstName, u.LastName);
            nameDisplay = string.IsNullOrEmpty(u.DiscordName)
                ? fullName
                : (string.IsNullOrEmpty(fullName) ? u.DiscordName : $"{u.DiscordName} ({fullName})");
        }

        return nameDisplay;
    }

    private async Task ExportCowLedgerCsv()
    {
        if (viewingCow == null || isExportingCowLedgerCsv) return;

        try
        {
            isExportingCowLedgerCsv = true;
            var cow = viewingCow;

            using var context = DbFactory.CreateDbContext();
            var transactions = await BuildCowLedgerFilteredQuery(context, cow.Id)
                .OrderByDescending(t => t.UpdatedAt)
                .ToListAsync();

            var allPartyIds = transactions
                .Select(t => t.SenderId)
                .Concat(transactions.Select(t => t.ReceiverId))
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!)
                .Distinct();
            await PopulateUserLookup(allPartyIds);

            var sb = new StringBuilder();
            sb.AppendLine("Date,Transaction #,Type,Platform,Amount,Direction,Note");

            foreach (var t in transactions)
            {
                bool isIncoming = t.ReceiverId == cow.Id;
                var dateStr = ToCashCowDisplayTime(t.UpdatedAt).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                int signedAmount = isIncoming ? t.AmountNOK : -t.AmountNOK;

                string? partyType = isIncoming ? t.SenderType : t.ReceiverType;
                string? partyId = isIncoming ? t.SenderId : t.ReceiverId;
                string? partyFallback = isIncoming ? t.SenderName : t.ReceiverName;
                string counterpartyName = FormatCowLedgerCounterpartyName(partyType, partyId, partyFallback);
                string ccSuffix = partyType == "CashCow" ? " (CC)" : "";
                string directionText = isIncoming
                    ? $"From {counterpartyName}{ccSuffix}".Trim()
                    : $"To {counterpartyName}{ccSuffix}".Trim();

                string noteText = string.IsNullOrWhiteSpace(t.Note) ? "" : t.Note.Trim();

                sb.AppendLine($"{EscapeCsv(dateStr)},{t.Id.ToString(CultureInfo.InvariantCulture)},{EscapeCsv(t.Type)},{EscapeCsv(t.Platform)},{signedAmount.ToString(CultureInfo.InvariantCulture)},{EscapeCsv(directionText)},{EscapeCsv(noteText)}");
            }

            var cowFullName = FormatFullName(cow.FirstName, cow.LastName);
            var safeName = string.Concat((string.IsNullOrWhiteSpace(cowFullName) ? "CashCow" : cowFullName)
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'))
                .Trim('_');
            while (safeName.Contains("__")) safeName = safeName.Replace("__", "_");
            if (string.IsNullOrEmpty(safeName)) safeName = "CashCow";

            var timestamp = GetCashCowBaseTime().ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);
            var filename = $"Ledger_{safeName}_{timestamp}.csv";

            await JSRuntime.InvokeVoidAsync("downloadCsv", filename, sb.ToString());
        }
        finally
        {
            isExportingCowLedgerCsv = false;
        }
    }

    private static string EscapeCsv(string? field)
    {
        if (string.IsNullOrEmpty(field)) return "";
        field = field.Replace("\"", "\"\"");
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field}\"";
        }
        return field;
    }

    private async Task LoadCowTransactions(CashCow cow)
    {
        viewingCow = cow;
        using var context = DbFactory.CreateDbContext();

        var baseCowQuery = context.Transactions
            .AsNoTracking()
            .Where(t => t.SenderId == cow.Id || t.ReceiverId == cow.Id);

        var distinctMeta = await baseCowQuery
            .Select(t => new { t.Type, t.Platform })
            .Distinct()
            .ToListAsync();

        var types = new List<string> { "Deposit", "Withdrawal", "Transfer" };
        foreach (var t in distinctMeta.Select(x => x.Type).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (!types.Any(existing => string.Equals(existing, t, StringComparison.OrdinalIgnoreCase)))
            {
                types.Add(t);
            }
        }
        if (!string.IsNullOrWhiteSpace(cowLedgerFilterType) && !types.Any(existing => string.Equals(existing, cowLedgerFilterType, StringComparison.OrdinalIgnoreCase)))
        {
            types.Add(cowLedgerFilterType);
        }
        cowLedgerAvailableTypes = types;

        var platforms = new List<string> { "Vipps", "Revolut", "Bank Transfer" };
        foreach (var p in distinctMeta.Select(x => x.Platform).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (!platforms.Any(existing => string.Equals(existing, p, StringComparison.OrdinalIgnoreCase)))
            {
                platforms.Add(p);
            }
        }
        if (!string.IsNullOrWhiteSpace(cowLedgerFilterPlatform) && !platforms.Any(existing => string.Equals(existing, cowLedgerFilterPlatform, StringComparison.OrdinalIgnoreCase)))
        {
            platforms.Add(cowLedgerFilterPlatform);
        }
        cowLedgerAvailablePlatforms = platforms;

        var query = BuildCowLedgerFilteredQuery(context, cow.Id);

        viewingCowTotalTransactions = await query.CountAsync();
        var maxPage = Math.Max(1, (int)Math.Ceiling((double)viewingCowTotalTransactions / ViewingCowPageSize));
        if (viewingCowCurrentPage > maxPage)
        {
            viewingCowCurrentPage = maxPage;
        }
        viewingCowHasMore = viewingCowTotalTransactions > (viewingCowCurrentPage * ViewingCowPageSize);

        viewingCowTransactions = await query
            .OrderByDescending(t => t.UpdatedAt)
            .Skip((viewingCowCurrentPage - 1) * ViewingCowPageSize)
            .Take(ViewingCowPageSize)
            .ToListAsync();

        var additionalUserIds = viewingCowTransactions
            .Select(t => t.SenderId)
            .Concat(viewingCowTransactions.Select(t => t.ReceiverId))
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .Distinct();
        await PopulateUserLookup(additionalUserIds);
    }

    private void OpenAddCashCowModal()
    {
        errorMessage = null;
        cashCowModalError = null;
        editingCowId = null;
        newCow = new CashCow();
        showCashCowModal = true;
    }

    private void EditCashCow(CashCow cow)
    {
        errorMessage = null;
        cashCowModalError = null;
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
        showCashCowModal = true;
    }

    private void CancelEditCashCow()
    {
        if (isSavingCashCow) return;
        errorMessage = null;
        cashCowModalError = null;
        editingCowId = null;
        newCow = new CashCow();
        showCashCowModal = false;
    }

    private void HandleCashCowModalKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape" && !isSavingCashCow)
        {
            CancelEditCashCow();
        }
    }

    private async Task SaveCashCow()
    {
        if (isSavingCashCow) return;

        errorMessage = null;
        cashCowModalError = null;
        successMessage = null;

        newCow.FirstName = (newCow.FirstName ?? "").Trim();
        newCow.LastName = (newCow.LastName ?? "").Trim();
        newCow.OtherPlatformName = (newCow.OtherPlatformName ?? "").Trim();
        newCow.OtherPaymentDetails = (newCow.OtherPaymentDetails ?? "").Trim();

        if (string.IsNullOrWhiteSpace(newCow.FirstName))
        {
            cashCowModalError = "First Name is required.";
            return;
        }

        var validationResult = BettingApp.Services.PaymentValidationHelper.ValidatePaymentMethods(
            newCow.Vipps, newCow.Revolut, newCow.BankTransfer, newCow.OtherPlatformName, newCow.OtherPaymentDetails, requireAtLeastOne: false);

        if (!validationResult.isValid)
        {
            cashCowModalError = validationResult.errorMessage;
            return;
        }

        try
        {
            isSavingCashCow = true;
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
            showCashCowModal = false;
            await LoadTransactions(context);
            _ = ShowSuccess(wasEditing ? "Cash Cow updated successfully." : "Cash Cow added successfully.");
        }
        finally
        {
            isSavingCashCow = false;
        }
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

    private DateTime GetCashCowBaseTime()
    {
        var nowUtc = DateTime.UtcNow;
        if (ccShowNorwayTimezone) return TimeHelpers.GetNorwayTime(nowUtc);
        return hasFetchedOffset ? nowUtc.AddMinutes(-TimezoneOffset) : nowUtc;
    }

    private DateTime GetCashCowUtcFromBaseTime(DateTime baseTime)
    {
        if (ccShowNorwayTimezone)
        {
            return DateTime.SpecifyKind(TimeHelpers.GetUtcFromNorwayTime(baseTime), DateTimeKind.Utc);
        }
        var utc = hasFetchedOffset ? baseTime.AddMinutes(TimezoneOffset) : baseTime;
        return DateTime.SpecifyKind(utc, DateTimeKind.Utc);
    }

    private DateTime ToCashCowDisplayTime(DateTime utcDate)
    {
        if (utcDate == DateTime.MinValue || utcDate == DateTime.MaxValue) return utcDate;
        var normalizedUtc = DateTime.SpecifyKind(utcDate, DateTimeKind.Utc);
        if (ccShowNorwayTimezone) return TimeHelpers.GetNorwayTime(normalizedUtc);
        return ToUserLocal(normalizedUtc);
    }

    private (DateTime? StartUtc, DateTime? EndUtc) GetCashCowPeriodUtcBounds()
    {
        switch (viewMode)
        {
            case ViewMode.AllTime:
                return (null, null);
            case ViewMode.Daily:
            {
                var startBase = currentViewDate.Date;
                var endBase = startBase.AddDays(1);
                return (GetCashCowUtcFromBaseTime(startBase), GetCashCowUtcFromBaseTime(endBase));
            }
            case ViewMode.Weekly:
            {
                var startBase = GetStartOfWeek(currentViewDate);
                var endBase = startBase.AddDays(7);
                return (GetCashCowUtcFromBaseTime(startBase), GetCashCowUtcFromBaseTime(endBase));
            }
            case ViewMode.Monthly:
            {
                var startBase = new DateTime(currentViewDate.Year, currentViewDate.Month, 1);
                var endBase = startBase.AddMonths(1);
                return (GetCashCowUtcFromBaseTime(startBase), GetCashCowUtcFromBaseTime(endBase));
            }
            case ViewMode.Yearly:
            {
                var startBase = new DateTime(currentViewDate.Year, 1, 1);
                var endBase = startBase.AddYears(1);
                return (GetCashCowUtcFromBaseTime(startBase), GetCashCowUtcFromBaseTime(endBase));
            }
            case ViewMode.Custom:
            {
                DateTime? startUtc = ccStartDate.HasValue
                    ? GetCashCowUtcFromBaseTime(ccStartDate.Value.Date)
                    : null;
                DateTime? endUtc = ccEndDate.HasValue
                    ? GetCashCowUtcFromBaseTime(ccEndDate.Value.Date.AddDays(1))
                    : null;
                return (startUtc, endUtc);
            }
            default:
            {
                var startBase = currentViewDate.Date;
                var endBase = startBase.AddDays(1);
                return (GetCashCowUtcFromBaseTime(startBase), GetCashCowUtcFromBaseTime(endBase));
            }
        }
    }

    private async Task OnCashCowTimezoneToggled()
    {
        if (viewMode == ViewMode.AllTime)
        {
            currentViewDate = GetCashCowBaseTime().Date;
        }
        await UpdateCashCowPeriodNet();
    }

    private async Task ClearCashCowFilter()
    {
        ++_ccPeriodQueryVersion;
        ccStartDate = null;
        ccEndDate = null;
        ccPeriodNet = new Dictionary<string, int>();
        if (viewingCow != null)
        {
            viewingCowCurrentPage = 1;
            await LoadCowTransactions(viewingCow);
        }
    }

    private async Task SetViewMode(ViewMode mode)
    {
        viewMode = mode;
        if (mode != ViewMode.Custom)
        {
            await GoToCurrent();
        }
        else
        {
            await UpdateCashCowPeriodNet();
        }
    }

    private async Task GoToCurrent()
    {
        currentViewDate = GetCashCowBaseTime().Date;
        await UpdateCashCowPeriodNet();
    }

    private async Task PreviousPeriod()
    {
        currentViewDate = viewMode switch
        {
            ViewMode.Daily => currentViewDate.AddDays(-1),
            ViewMode.Weekly => currentViewDate.AddDays(-7),
            ViewMode.Monthly => currentViewDate.AddMonths(-1),
            ViewMode.Yearly => currentViewDate.AddYears(-1),
            _ => currentViewDate
        };
        await UpdateCashCowPeriodNet();
    }

    private async Task NextPeriod()
    {
        currentViewDate = viewMode switch
        {
            ViewMode.Daily => currentViewDate.AddDays(1),
            ViewMode.Weekly => currentViewDate.AddDays(7),
            ViewMode.Monthly => currentViewDate.AddMonths(1),
            ViewMode.Yearly => currentViewDate.AddYears(1),
            _ => currentViewDate
        };
        await UpdateCashCowPeriodNet();
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
        if (ccStartDate.HasValue && ccEndDate.HasValue && ccStartDate.Value.Date > ccEndDate.Value.Date)
        {
            (ccStartDate, ccEndDate) = (ccEndDate, ccStartDate);
        }

        if (viewMode == ViewMode.Custom)
        {
            await UpdateCashCowPeriodNet();
        }
    }

    private async Task UpdateCashCowPeriodNetCore(ApplicationDbContext context, int? expectedVersion = null)
    {
        int version = expectedVersion ?? ++_ccPeriodQueryVersion;

        if (viewMode == ViewMode.Custom && ccStartDate == null && ccEndDate == null)
        {
            ccPeriodNet = new Dictionary<string, int>();
            return;
        }

        var query = context.Transactions
            .AsNoTracking()
            .Where(t => t.Status == "Completed" && (t.SenderType == "CashCow" || t.ReceiverType == "CashCow"));

        var (startUtc, endUtc) = GetCashCowPeriodUtcBounds();
        if (startUtc.HasValue)
        {
            query = query.Where(t => t.Date >= startUtc.Value);
        }
        if (endUtc.HasValue)
        {
            query = query.Where(t => t.Date < endUtc.Value);
        }

        var txs = await query
            .Select(t => new { t.SenderType, t.SenderId, t.ReceiverType, t.ReceiverId, t.AmountNOK })
            .ToListAsync();

        if (version != _ccPeriodQueryVersion) return;

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
        if (_isDisposed) return;

        if (viewMode == ViewMode.Custom && ccStartDate == null && ccEndDate == null)
        {
            await ClearCashCowFilter();
            if (!_isDisposed) await InvokeAsync(StateHasChanged);
            return;
        }

        int version = ++_ccPeriodQueryVersion;
        using var context = DbFactory.CreateDbContext();
        await UpdateCashCowPeriodNetCore(context, version);

        if (version != _ccPeriodQueryVersion || _isDisposed) return;

        if (viewingCow != null)
        {
            viewingCowCurrentPage = 1;
            await LoadCowTransactions(viewingCow);
        }

        if (!_isDisposed) await InvokeAsync(StateHasChanged);
    }
}
