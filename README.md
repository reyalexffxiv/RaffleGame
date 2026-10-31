# Raffle Game

A lightweight Dalamud raffle-ticket manager for FFXIV venues and events.

## Features

- Sell numbered tickets, including gifts, up to a 999-ticket raffle cap.
- See tickets sold, gross sales, prize pot, and purchase history.
- Use the compact **SELL MAX** button to sell to the selected player and queue a ticket confirmation.
- Customize raffle ads, countdown messages, winner announcements, and ticket tells.
- Draw a winner with FFXIV `/random`; export ticket sales to CSV.

## Commands

`/raffle` or `/rafflegame` opens the plugin. Additional `/rafflegame` commands: `target`, `tell`, `announce`, `draw`, `reset`.

## Installation

Add the [ReyPlugins custom repository](https://raw.githubusercontent.com/reyalexffxiv/ReyPlugins/main/pluginmaster.json) in Dalamud Settings → Experimental → Custom Plugin Repositories, then install **Raffle Game** from the plugin installer. Use the central repository **or** this plugin's individual `pluginmaster.json`, not both.

## Building from source

Requires Windows, .NET 10 SDK and Dalamud API 15 development files. Run `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-DalamudPackage.ps1` from the repository root. The installable ZIP is created in `dist/`. GitHub source ZIPs are not installable plugin packages.

## Data and previous installations

Ticket data is stored locally. Only the most recent sale can be undone without shifting already-issued ticket numbers. Back up configuration and export sales before replacing an older installation.

Raffle Game uses the internal name `RaffleGame`, which is different from the earlier `VenueRaffle` plugin. It attempts a read-only import from standard legacy configuration locations when no new raffle state exists; old files are not deleted. Disable VenueRaffle before running Raffle Game to avoid command conflicts, and verify the imported ticket history before using it live.

Licensed under AGPL-3.0-or-later. See [LICENSE.md](LICENSE.md) and [NOTICE.md](NOTICE.md).
