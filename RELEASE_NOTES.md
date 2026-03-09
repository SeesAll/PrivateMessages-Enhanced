# PrivateMessages v1.2.1 - Enhanced Audit Logging and Cleanup Update

## Added
- Dedicated daily PM audit logs under `oxide/logs/PrivateMessages/`
- Steam ID logging alongside player names
- Automatic inactivity-based conversation pruning
- Optional audit log cleanup on new save/wipe
- Safer ambiguous-name handling for player matching

## Improved
- Better player lookup performance
- Cleaner disconnect cleanup for stale reply/history mappings
- Lower allocation overhead in several command paths
- More efficient conversation history storage
- Better maintainability and code hygiene

## Changed
- Console logging disabled by default
- Dedicated file audit logging enabled by default

## Credit
Original plugin by **MisterPixie**  
Enhanced and updated by **SeesAll**
