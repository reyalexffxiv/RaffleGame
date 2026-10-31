using System;
using System.IO;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace RaffleGame.Services;

/// <summary>
/// Saves and loads the live raffle ledger through Dalamud's reliable file storage service.
///
/// This gives the raffle state its own JSON file while using Dalamud's atomic/reliable
/// storage layer, including the backup copy that IReliableFileStorage maintains.
/// </summary>
public sealed class RaffleStateStorage
{
    private const string StateFileName = "raffle-state.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public bool StateLoadFailed { get; private set; }
    public bool StateSaveFailed { get; private set; }
    private readonly IReliableFileStorage reliableFileStorage;
    private readonly IPluginLog log;
    private readonly string stateFilePath;

    /// <summary>Builds the storage path under Dalamud's plugin configuration directory.</summary>
    public RaffleStateStorage(
        IDalamudPluginInterface pluginInterface,
        IReliableFileStorage reliableFileStorage,
        IPluginLog log)
    {
        this.reliableFileStorage = reliableFileStorage;
        this.log = log;
        Directory.CreateDirectory(pluginInterface.ConfigDirectory.FullName);
        this.stateFilePath = Path.Combine(pluginInterface.ConfigDirectory.FullName, StateFileName);
    }

    /// <summary>Loads the latest saved raffle state into the provided configuration object.</summary>
    public void LoadInto(Configuration configuration)
    {
        if (!this.reliableFileStorage.Exists(this.stateFilePath))
        {
            if (configuration.SalesLedger.Count > 0 &&
                !RaffleLedgerValidator.IsValid(configuration.SalesLedger))
            {
                this.StateLoadFailed = true;
                this.log.Error("Configuration ledger failed validation. Refusing to overwrite old data.");
                return;
            }
            // First run after upgrade: preserve the old config-based ledger by writing it
            // to the separate reliable state file.
            if (configuration.SalesLedger.Count > 0 || configuration.ActiveSessionWasRunning || !string.IsNullOrWhiteSpace(configuration.ActiveSessionLastNotice))
            {
                this.log.Information("Migrating legacy RaffleGame state from plugin configuration to IReliableFileStorage.");
                this.Save(configuration);
            }

            return;
        }

        try
        {
            var contents = this.reliableFileStorage
                .ReadAllTextAsync(this.stateFilePath)
                .GetAwaiter()
                .GetResult();

            var state = JsonSerializer.Deserialize<RafflePersistentState>(contents, JsonOptions)
                ?? throw new InvalidDataException("RaffleGame state file was empty or invalid.");

            if (!RaffleLedgerValidator.IsValid(state.SalesLedger))
                throw new InvalidDataException("Saved ticket ledger failed validation; original file preserved.");
            // If a previous state write failed after Dalamud saved its configuration,
            // prefer the newer validated configuration rather than losing the sale.
            if (configuration.LastStateChangeUtcTicks > state.UpdatedUtcTicks)
            {
                if (!RaffleLedgerValidator.IsValid(configuration.SalesLedger))
                    throw new InvalidDataException("Newer configuration ledger failed validation.");
                this.log.Warning("Recovering more recent raffle ledger from plugin configuration.");
                this.Save(configuration);
                return;
            }
            configuration.SalesLedger = state.SalesLedger;
            configuration.LastStateChangeUtcTicks = state.UpdatedUtcTicks;
            configuration.LastWinningTicket = state.LastWinningTicket;
            configuration.LastWinnerName = state.LastWinnerName ?? string.Empty;
            configuration.ActiveSessionWasRunning = state.ActiveSessionWasRunning;
            configuration.ActiveSessionLastNotice = state.ActiveSessionLastNotice ?? string.Empty;

            this.log.Information("Loaded RaffleGame state from IReliableFileStorage: {Count} sale record(s).", configuration.SalesLedger.Count);
        }
        catch (FileNotFoundException)
        {
            this.log.Information("No RaffleGame reliable state file found yet.");
        }
        catch (Exception ex)
        {
            this.StateLoadFailed = true;
            this.log.Error(ex, "RaffleGame saved state failed to load. Writes disabled; original file preserved.");
        }
    }

    /// <summary>Persists the raffle state through Dalamud's reliable file storage API.</summary>
    public bool Save(Configuration configuration)
    {
        if (this.StateLoadFailed || this.StateSaveFailed)
        {
            this.log.Warning("RaffleGame refused to overwrite unavailable raffle state.");
            return false;
        }
        try
        {
            var state = new RafflePersistentState
            {
                SalesLedger = configuration.SalesLedger,
                UpdatedUtcTicks = configuration.LastStateChangeUtcTicks,
                LastWinningTicket = configuration.LastWinningTicket,
                LastWinnerName = configuration.LastWinnerName,
                ActiveSessionWasRunning = configuration.ActiveSessionWasRunning,
                ActiveSessionLastNotice = configuration.ActiveSessionLastNotice ?? string.Empty,
            };

            var json = JsonSerializer.Serialize(state, JsonOptions);
            this.reliableFileStorage
                .WriteAllTextAsync(this.stateFilePath, json)
                .GetAwaiter()
                .GetResult();
            // The reliable file is authoritative. The plugin config is a secondary
            // settings/legacy recovery snapshot, not a prerequisite for an issued ticket.
            try { configuration.Save(); }
            catch (Exception ex) { this.log.Warning(ex, "RaffleGame state saved, but config mirror failed."); }
            return true;
        }
        catch (Exception ex)
        {
            this.StateSaveFailed = true;
            this.log.Error(ex, "RaffleGame state save failed; edits locked until restart/recovery.");
            return false;
        }
    }
}
