# PrivateMessages

Enhanced private messaging plugin for Rust servers using uMod / Oxide.

**Original plugin by:** MisterPixie  
**Enhanced and updated by:** SeesAll

## Overview

PrivateMessages is a lightweight private messaging plugin for Rust that allows players to send direct messages, reply to the last player they messaged, and review recent conversation history.

This enhanced build keeps the original plugin's core behavior intact while improving code quality, performance, moderation usability, and long-term maintainability.

## Credits

This plugin is based on the original **PrivateMessages** plugin created by **MisterPixie**.  
This updated version modernizes and enhances the original implementation while preserving its primary purpose and player-facing functionality.

## What This Enhanced Version Improves

Compared to the original release, this version includes a number of important improvements:

### Performance and efficiency improvements
- Optimized player lookup logic to reduce unnecessary allocations
- Removed repeated lowercase string conversions during player matching
- Replaced slower list-based conversation lookups with dictionary-based storage
- Reduced minor allocation-heavy operations in command handling
- Cached repeated timestamp/epoch usage where appropriate
- Throttled inactivity pruning so the plugin does not scan conversation history on every PM action

### Stability and cleanup improvements
- Cleans stale `/r` reply mappings when players disconnect
- Purges inactive conversation history automatically after a configurable amount of inactivity
- Keeps in-memory history limited and manageable
- Avoids unnecessary config rewrites on every load
- Removes redundant or leftover code paths from the original structure

### Safer player matching
- Improved connected-player lookup behavior
- Detects ambiguous partial-name matches and asks players to be more specific instead of selecting the first matching player
- Helps prevent private messages from being sent to the wrong person

### Moderation and audit improvements
- Adds dedicated daily PM audit logs in:
  `oxide/logs/PrivateMessages/`
- Creates one log file per day, for example:
  `privatemessages_2026-03-09.log`
- Logs both player names and Steam IDs for better moderation and investigations
- Sanitizes logged messages so each PM remains on a single clean line
- Keeps PM audit logs separate from general server console spam

### Logging improvements
- Console logging is disabled by default in this enhanced build
- Dedicated audit-file logging is enabled by default
- Optional log cleanup on new save/wipe via configuration

## Features

- `/pm <player> <message>` — send a private message
- `/r <message>` — reply to the last player involved in a PM conversation
- `/pmhistory <player>` — view recent PM history with a player
- Optional permission-based access
- Optional PM cooldown
- Optional BetterChatMute support
- Optional Ignore plugin support
- Optional UFilter support
- Dedicated daily audit log files
- Automatic cleanup of stale conversation data

## Commands

### `/pm <player> <message>`
Sends a private message to another connected player.

### `/r <message>`
Replies to the last player involved in your most recent private message exchange.

### `/pmhistory <player>`
Displays the recent in-memory PM history between you and the specified player.

> Note: PM history is stored in memory only and is intended for short-term convenience. It is not a permanent archive.

## Permissions

If `UsePermission` is enabled in the configuration, players must have the following permission:

`privatemessages.use`

## Configuration

Example configuration:

```json
{
  "UsePermission": false,
  "EnablePmCooldown": false,
  "PmCooldown": 3,
  "EnablePmHistory": true,
  "EnableConsoleLogging": false,
  "EnableFileAuditLogging": true,
  "HistoryPruneMinutes": 60,
  "ClearAuditLogsOnNewSave": false
}
```

### Configuration options

#### `UsePermission`
If set to `true`, players must have the `privatemessages.use` permission to use PM commands.

#### `EnablePmCooldown`
Enables a cooldown between PM sends.

#### `PmCooldown`
Cooldown in seconds between private messages when PM cooldown is enabled.

#### `EnablePmHistory`
Enables the `/pmhistory` command.

#### `EnableConsoleLogging`
If enabled, PM activity is also written to the server console/log output.

#### `EnableFileAuditLogging`
If enabled, PM activity is written to dedicated daily audit log files under `oxide/logs/PrivateMessages/`.

#### `HistoryPruneMinutes`
How long inactive PM conversations remain in memory before being automatically pruned.

#### `ClearAuditLogsOnNewSave`
If enabled, PM audit logs are cleared when a new save/wipe is detected.

## Audit Logs

This enhanced version stores PM audit logs in a dedicated folder:

`oxide/logs/PrivateMessages/`

Daily files are named like:

`privatemessages_YYYY-MM-DD.log`

Each entry includes:
- timestamp
- sender name
- sender Steam ID
- target name
- target Steam ID
- message content

Example log entry:

```text
[2026-03-09 14:32:18] SenderName (7656119XXXXXXXXXX) -> TargetName (7656119YYYYYYYYYY): hello there
```

## Data handling notes

### PM history
The plugin stores only a small rolling in-memory history for recent conversations. It does **not** retain unlimited PM history in RAM.

### Persistent logging
If audit logging is enabled, PMs are also stored in the dedicated daily log files for moderation and investigation purposes.

## Optional plugin integrations

This plugin can work alongside the following plugins when present:

- **Ignore**
- **UFilter**
- **BetterChatMute**

If those plugins are not installed, PrivateMessages will continue to function normally without them.

## Installation

1. Place `PrivateMessages.cs` in your server's `oxide/plugins/` folder.
2. Load or reload the plugin.
3. Review the generated config in `oxide/config/PrivateMessages.json`.
4. Grant the permission `privatemessages.use` if you enable permission-based usage.

## Recommended update path from older versions

If upgrading from an older edition of this plugin:

1. Back up your existing config file.
2. Replace the old plugin file with the enhanced version.
3. Reload the plugin.
4. Review your config to confirm any newer options are present and set as desired.

## Best practices

- Leave `EnableConsoleLogging` disabled unless you specifically want PMs in the general server logs.
- Use the dedicated audit logs for moderation review.
- Keep `HistoryPruneMinutes` enabled to prevent stale conversation data from lingering in memory unnecessarily.
- Enable `UsePermission` if you want tighter control over who can use private messaging.

## Notes on compatibility

This enhanced version was designed to preserve the original plugin's expected player experience as closely as possible while improving the internal implementation.

Core player-facing behavior such as `/pm`, `/r`, and `/pmhistory` has been retained.

## Suggested GitHub repository info

### Repository name
`PrivateMessages`

### Short description
Enhanced private messaging plugin for Rust with safer player matching, in-memory history cleanup, and dedicated daily audit logs.

### Topics / tags
`rust` `umod` `oxide` `rust-plugin` `private-messages` `chat` `moderation` `audit-logs`

## Suggested release title
`PrivateMessages v1.2.1 - Enhanced Audit Logging and Cleanup Update`

## Suggested release notes

### Added
- Dedicated daily PM audit logs under `oxide/logs/PrivateMessages/`
- Steam ID logging alongside player names
- Automatic inactivity-based conversation pruning
- Optional audit log cleanup on new save/wipe
- Safer ambiguous-name handling for player matching

### Improved
- Better player lookup performance
- Cleaner disconnect cleanup for stale reply/history mappings
- Lower allocation overhead in several command paths
- More efficient conversation history storage
- Better overall maintainability and code hygiene

### Changed
- Console logging disabled by default
- Dedicated file audit logging enabled by default

### Credit
Original plugin by **MisterPixie**  
Enhanced and updated by **SeesAll**
