using System;
using System.Text.Json.Serialization;

namespace RaffleGame.Models;

/// <summary>One ticket purchase. Original gil value survives later price changes.</summary>
[Serializable]
public sealed class RaffleSaleRecord
{
    public string PlayerName { get; set; } = string.Empty;

    // Empty on legacy records: earlier releases recorded the purchaser as PlayerName.
    public string PurchasedBy { get; set; } = string.Empty;

    [JsonIgnore]
    public string BuyerName => string.IsNullOrWhiteSpace(this.PurchasedBy) ? this.PlayerName : this.PurchasedBy;
    [JsonIgnore]
    public bool IsGift => !string.Equals(this.BuyerName, this.PlayerName, StringComparison.OrdinalIgnoreCase);

    public int Tickets { get; set; }

    public int StartTicket { get; set; }

    public int EndTicket { get; set; }

    public long Gil { get; set; }

    public DateTime CreatedAtLocal { get; set; } = DateTime.Now;

    public string TicketRange => this.StartTicket == this.EndTicket
        ? this.StartTicket.ToString()
        : $"{this.StartTicket} - {this.EndTicket}";
}
