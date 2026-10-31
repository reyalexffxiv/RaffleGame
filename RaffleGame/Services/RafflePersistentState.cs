using System.Collections.Generic;
using RaffleGame.Models;

namespace RaffleGame.Services;

/// <summary>
/// JSON payload saved to disk so a raffle survives plugin reloads and game restarts.
/// </summary>
public sealed class RafflePersistentState
{
    public int Version { get; set; } = 1;

    public List<RaffleSaleRecord> SalesLedger { get; set; } = new();

    public long UpdatedUtcTicks { get; set; }
    public int LastWinningTicket { get; set; }
    public string LastWinnerName { get; set; } = string.Empty;
    public bool ActiveSessionWasRunning { get; set; }

    public string ActiveSessionLastNotice { get; set; } = string.Empty;
}
