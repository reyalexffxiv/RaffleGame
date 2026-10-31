using System;
using System.Linq;
using RaffleGame.Models;

namespace RaffleGame.Services;

/// <summary>
/// Contains the raffle ticket business logic.
///
/// UI code should call this service for any operation that changes ticket entries,
/// totals, or ticket ranges. This keeps calculations in one place and prevents
/// different windows from drifting out of sync.
/// </summary>
public sealed class RaffleSession
{
    /// <summary>FFXIV /random cannot roll above 999, so the whole raffle is capped here.</summary>
    public const int MaxTotalTickets = 999;

    private readonly Configuration configuration;
    private readonly RaffleStateStorage stateStorage;

    /// <summary>Creates the raffle service from saved configuration and persistent state storage.</summary>
    public RaffleSession(Configuration configuration, RaffleStateStorage stateStorage)
    {
        this.configuration = configuration;
        this.stateStorage = stateStorage;
        this.LastNotice = configuration.ActiveSessionLastNotice ?? string.Empty;
    }

    public string LastNotice { get; private set; } = string.Empty;

    /// <summary>Number of visible purchase rows in the raffle entries table.</summary>
    public int EntryCount => this.configuration.SalesLedger.Count;

    /// <summary>Total tickets currently sold across all entries.</summary>
    public int TotalTickets => (int)Math.Min(MaxTotalTickets,
        this.configuration.SalesLedger.Sum(sale => (long)Math.Max(0, sale.Tickets)));

    public long GrossTicketSalesTotalGil
    {
        get
        {
            decimal total = 0;
            foreach (var sale in this.configuration.SalesLedger)
            {
                total += Math.Max(0L, sale.Gil);
                if (total > long.MaxValue) return long.MaxValue;
            }
            return (long)total;
        }
    }

    public float VenueHostSalesPercent => float.IsFinite(this.configuration.VenueHostSalesPercent)
        ? Math.Clamp(this.configuration.VenueHostSalesPercent, 0f, 100f) : 0f;

    public long VenueHostShareGil => CalculatePercentShare(this.GrossTicketSalesTotalGil, this.VenueHostSalesPercent);

    public long PrizeTicketSalesGil => Math.Max(0, this.GrossTicketSalesTotalGil - this.VenueHostShareGil);

    /// <summary>Full prize pot after base pot, ticket sales, and host share are applied.</summary>
    public bool IsPotOverflow => (decimal)Math.Max(0L, this.configuration.BasePot) + this.PrizeTicketSalesGil > long.MaxValue;
    public long TotalPot => this.IsPotOverflow
        ? long.MaxValue : Math.Max(0L, this.configuration.BasePot) + this.PrizeTicketSalesGil;

    public int RemainingTickets => Math.Max(0, MaxTotalTickets - this.TotalTickets);

    public int? WinningTicket => this.configuration.LastWinningTicket > 0 ? this.configuration.LastWinningTicket : null;
    public string LastWinnerName => this.configuration.LastWinnerName ?? string.Empty;
    public bool HasWinner => this.WinningTicket.HasValue;
    public bool IsDrawing { get; private set; }

    public void SetDrawing(bool drawing) => this.IsDrawing = drawing;

    public bool CompleteDraw(int ticket, RaffleSaleRecord winner)
    {
        if (!this.IsDrawing || this.HasWinner || this.FindTicket(ticket) != winner)
            return false;
        return this.MutateAndPersist(() =>
        {
            this.IsDrawing = false;
            this.configuration.LastWinningTicket = ticket;
            this.configuration.LastWinnerName = winner.PlayerName;
            this.LastNotice = $"Winner: {winner.PlayerName}, ticket {ticket}.";
        });
    }

    public RaffleSaleRecord? LastSale => this.configuration.SalesLedger.LastOrDefault();

    /// <summary>
    /// Starts a fresh raffle by clearing all ticket sales.
    /// </summary>
    public bool Reset() => this.MutateAndPersist(() =>
    {
        this.IsDrawing = false;
        this.configuration.LastWinningTicket = 0;
        this.configuration.LastWinnerName = string.Empty;
        this.configuration.SalesLedger.Clear();
        this.LastNotice = "Raffle reset. Ticket sales cleared.";
    });

    /// <summary>
    /// Adds one purchase to the raffle entries and assigns the next continuous ticket range.
    /// The raffle is hard-capped at 999 tickets because FFXIV /random cannot roll higher.
    /// </summary>
    public bool TryAddTicketSale(string playerName, int tickets, out RaffleSaleRecord? sale, out string error, string? purchasedBy = null)
    {
        sale = null;
        error = string.Empty;

        if (this.IsDrawing || this.HasWinner)
        {
            error = "The draw is in progress or completed. Reset before selling again.";
            this.LastNotice = error;
            return false;
        }

        if (string.IsNullOrWhiteSpace(playerName))
        {
            error = "Select or type a target player first.";
            this.LastNotice = error;
            return false;
        }

        if (tickets <= 0)
        {
            error = "Ticket amount must be at least 1.";
            this.LastNotice = error;
            return false;
        }

        if (this.configuration.MaxTicketsPerPurchase > 0 && tickets > this.configuration.MaxTicketsPerPurchase)
        {
            error = $"Ticket amount cannot exceed {this.configuration.MaxTicketsPerPurchase:N0}.";
            this.LastNotice = error;
            return false;
        }

        var currentTotalTickets = this.TotalTickets;
        var remainingTickets = MaxTotalTickets - currentTotalTickets;

        if (remainingTickets <= 0)
        {
            error = $"Raffle is full. The hard ticket limit is {MaxTotalTickets:N0} because FFXIV /random only supports up to 999.";
            this.LastNotice = error;
            return false;
        }

        if (tickets > remainingTickets)
        {
            error = $"Only {remainingTickets:N0} ticket(s) are available before the raffle reaches the {MaxTotalTickets:N0} ticket limit.";
            this.LastNotice = error;
            return false;
        }

        if (this.configuration.PricePerTicket < 0 ||
            this.configuration.PricePerTicket > long.MaxValue / tickets)
        {
            error = "Ticket price is invalid or exceeds the gil calculation limit.";
            this.LastNotice = error;
            return false;
        }

        var gil = this.configuration.PricePerTicket * tickets;
        var totalGil = 0L;
        foreach (var entry in this.configuration.SalesLedger)
        {
            if (entry.Gil < 0 || entry.Gil > long.MaxValue - totalGil)
            {
                error = "Existing sales contain an invalid gil total.";
                this.LastNotice = error;
                return false;
            }
            totalGil += entry.Gil;
        }
        if (gil > long.MaxValue - totalGil ||
            Math.Max(0L, this.configuration.BasePot) > long.MaxValue - totalGil - gil)
        {
            error = "The raffle pot would exceed the supported gil amount.";
            this.LastNotice = error;
            return false;
        }
        var startTicket = currentTotalTickets + 1;
        var endTicket = startTicket + tickets - 1;
        var cleanPlayerName = playerName.Trim();
        var cleanBuyerName = string.IsNullOrWhiteSpace(purchasedBy) ? cleanPlayerName : purchasedBy.Trim();
        var proposedSale = new RaffleSaleRecord
        {
            PlayerName = cleanPlayerName,
            PurchasedBy = cleanBuyerName,
            Tickets = tickets,
            StartTicket = startTicket,
            EndTicket = endTicket,
            Gil = gil,
            CreatedAtLocal = DateTime.Now
        };

        if (!this.MutateAndPersist(() =>
        {
            this.configuration.SalesLedger.Add(proposedSale);
            this.LastNotice = proposedSale.IsGift
                ? $"{cleanBuyerName} bought tickets {proposedSale.TicketRange} for {cleanPlayerName}. Cost: {gil:N0} gil."
                : $"{cleanPlayerName} received tickets {proposedSale.TicketRange}. Cost: {gil:N0} gil.";
        }))
        {
            sale = null;
            error = "The sale could not be saved. No tickets were issued; recover storage before retrying.";
            return false;
        }
        sale = proposedSale;
        return true;
    }

    /// <summary>Only the newest purchase can be undone without changing issued numbers.</summary>
    public bool RemoveLastSale() => this.RemoveSaleAt(this.configuration.SalesLedger.Count - 1);

    public bool RemoveSaleAt(int index)
    {
        if (this.IsDrawing || this.HasWinner || this.stateStorage.StateLoadFailed || this.stateStorage.StateSaveFailed)
            return false;
        if (index < 0 || index != this.configuration.SalesLedger.Count - 1)
        {
            this.LastNotice = "Only the most recent sale can be deleted. Earlier ticket numbers are permanent.";
            return false;
        }
        var sale = this.configuration.SalesLedger[index];
        return this.MutateAndPersist(() =>
        {
            this.configuration.SalesLedger.RemoveAt(index);
            this.LastNotice = $"Undid {sale.Tickets:N0} ticket(s) for {sale.PlayerName}.";
        });
    }

    public bool ClearSalesLedger()
    {
        if (this.IsDrawing || this.HasWinner || this.stateStorage.StateLoadFailed || this.stateStorage.StateSaveFailed)
            return false;
        return this.MutateAndPersist(() =>
        {
            this.configuration.SalesLedger.Clear();
            this.LastNotice = "Raffle ticket sales cleared.";
        });
    }

    /// <summary>Finds the entry that owns a specific ticket number.</summary>
    public RaffleSaleRecord? FindTicket(int ticketNumber)
    {
        if (ticketNumber <= 0)
            return null;

        return this.configuration.SalesLedger.FirstOrDefault(
            sale => ticketNumber >= sale.StartTicket && ticketNumber <= sale.EndTicket);
    }

    /// <summary>Calculates a rounded gil share for venue/host cuts.</summary>
    private static long CalculatePercentShare(long grossGil, float percent)
    {
        if (grossGil <= 0 || percent <= 0f)
            return 0;

        if (percent >= 100f)
            return grossGil;

        return (long)Math.Round((decimal)grossGil * (decimal)percent / 100m,
            0, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Applies a ledger or winner mutation only if the reliable state file is written.
    /// A failed write rolls back in-memory changes and locks further edits.
    /// </summary>
    private bool MutateAndPersist(Action mutation)
    {
        if (this.stateStorage.StateLoadFailed || this.stateStorage.StateSaveFailed)
            return false;
        var previousSales = this.configuration.SalesLedger.ToList();
        var previousTicket = this.configuration.LastWinningTicket;
        var previousWinner = this.configuration.LastWinnerName;
        var previousNotice = this.LastNotice;
        var previousDrawing = this.IsDrawing;
        var previousTicks = this.configuration.LastStateChangeUtcTicks;
        var previousRunning = this.configuration.ActiveSessionWasRunning;
        var previousSavedNotice = this.configuration.ActiveSessionLastNotice;
        mutation();
        if (this.SaveActiveSessionState()) return true;
        this.configuration.SalesLedger = previousSales;
        this.configuration.LastWinningTicket = previousTicket;
        this.configuration.LastWinnerName = previousWinner;
        this.LastNotice = previousNotice;
        this.IsDrawing = previousDrawing;
        this.configuration.LastStateChangeUtcTicks = previousTicks;
        this.configuration.ActiveSessionWasRunning = previousRunning;
        this.configuration.ActiveSessionLastNotice = previousSavedNotice;
        return false;
    }

    private bool SaveActiveSessionState()
    {
        var previous = this.configuration.LastStateChangeUtcTicks;
        this.configuration.LastStateChangeUtcTicks = Math.Max(DateTime.UtcNow.Ticks,
            previous > 0 && previous < DateTime.MaxValue.Ticks ? previous + 1 : 0);
        // Field retained solely for older saved formats; no Start/Stop state in UI.
        this.configuration.ActiveSessionWasRunning = this.configuration.SalesLedger.Count > 0 && !this.HasWinner;
        this.configuration.ActiveSessionLastNotice = this.LastNotice ?? string.Empty;
        return this.stateStorage.Save(this.configuration);
    }
}
