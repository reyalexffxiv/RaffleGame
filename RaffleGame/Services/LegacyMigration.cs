using System;
using System.IO;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace RaffleGame.Services;

/// <summary>One-way, non-destructive import of the previous VenueRaffle settings and ledger.</summary>
internal static class LegacyMigration
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static Configuration? TryImport(IDalamudPluginInterface pluginInterface, IPluginLog log,
        out string notice)
    {
        notice = string.Empty;
        // Dalamud uses separate PluginConfigs folders/files per InternalName. Look only at
        // well-known sibling paths; never overwrite or delete the previous installation.
        var directory = pluginInterface.ConfigDirectory.FullName;
        var parent = Directory.GetParent(directory)?.FullName;
        if (parent is null) return null;
        var legacyConfig = Path.Combine(parent, "VenueRaffle.json");
        var legacyLedger = Path.Combine(parent, "VenueRaffle", "raffle-state.json");
        if (!File.Exists(legacyConfig) && !File.Exists(legacyLedger)) return null;
        try
        {
            var imported = File.Exists(legacyConfig)
                ? JsonSerializer.Deserialize<Configuration>(File.ReadAllText(legacyConfig), JsonOptions)
                : new Configuration();
            if (imported is null) return null;
            if (File.Exists(legacyLedger))
            {
                var previous = JsonSerializer.Deserialize<RafflePersistentState>(File.ReadAllText(legacyLedger), JsonOptions);
                if (previous?.SalesLedger is not null)
                {
                    imported.SalesLedger = previous.SalesLedger;
                    imported.ActiveSessionWasRunning = previous.ActiveSessionWasRunning;
                    imported.ActiveSessionLastNotice = previous.ActiveSessionLastNotice;
                }
            }
            if (!RaffleLedgerValidator.IsValid(imported.SalesLedger))
                throw new InvalidDataException("Legacy raffle ledger failed validation.");
            notice = $"Imported {imported.SalesLedger.Count} VenueRaffle sale(s). Original files were left untouched.";
            log.Information(notice);
            return imported;
        }
        catch (Exception ex)
        {
            notice = "Legacy VenueRaffle settings could not be imported. Original files were left untouched.";
            log.Error(ex, notice);
            return null;
        }
    }
}
