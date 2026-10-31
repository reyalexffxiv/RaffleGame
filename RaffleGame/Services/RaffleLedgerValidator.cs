using System;
using System.Collections.Generic;
using RaffleGame.Models;

namespace RaffleGame.Services;

/// <summary>Checks persisted ticket ranges without changing the numbers issued to buyers.</summary>
internal static class RaffleLedgerValidator
{
    public static bool IsValid(IReadOnlyList<RaffleSaleRecord>? sales)
    {
        if (sales is null) return false;
        var next = 1;
        decimal total = 0;
        foreach (var sale in sales)
        {
            if (sale is null || string.IsNullOrWhiteSpace(sale.PlayerName) ||
                sale.Tickets < 1 || sale.StartTicket != next ||
                sale.EndTicket != next + sale.Tickets - 1 || sale.Gil < 0)
                return false;
            next += sale.Tickets;
            if (next > RaffleSession.MaxTotalTickets + 1) return false;
            total += sale.Gil;
            if (total > long.MaxValue) return false;
        }
        return true;
    }
}
