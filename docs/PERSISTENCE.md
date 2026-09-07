# Persistence Design

## Principle

Persistent world generation is deterministic; player-caused change is sparse. Save authoritative model data, never render textures, shaders, or transient visual particles.

## Save layout

`WorldSave` contains schema version, world seed, Frontier Count, player body/equipment/misc-inventory state, strategic contacts, discovered metadata, station inventory/economy/debt data, relationship/narrative flags, and an index of `SiteRecord`s. A `SiteRecord` contains `SiteId`, generator key/revision/seed, last visit state, and references to changed chunk payloads, removed/changed components, atmosphere records, and lossless loose-cell/detached-fragment records. Ship cargo persists as an occupied cargo-cell field (position + material key/state), not as a capacity-independent resource count. Atomic component inventory (fuel tanks, misc storage) is serialized by stable item key/count/state, separately from physical cargo cells.

A changed chunk payload stores chunk coordinate, state format version, compressed material/flag/state bytes, and integrity hash. It is only created when a generated chunk differs from its deterministic baseline. Site content is reconstructed by generation first, then applying ordered deltas. A material/content key—not a fragile Unity asset instance ID—is serialized.

## Lifecycle

1. Entering a site generates/loads chunks in the required streaming region.
2. Modifications mark affected chunks dirty on GPU and in the site record index.
3. Eviction or explicit save asynchronously reads dirty chunk fields, validates them, compresses on a worker, and atomically replaces that chunk payload.
4. Leaving a site commits site metadata and a bounded persistent-debris snapshot, then disposes GPU resources only after pending saves complete.
5. Returning reconstructs generated state plus deltas exactly.

Save writes use a temporary file plus atomic replace. A previous valid save is retained until the new write is verified. Corrupt payloads are isolated per site; the load UI can report recovery rather than silently discarding the whole world.

Temporal encounter records are short-lived runtime state, not permanent `SiteRecord`s. On resolution or expiry, retain only durable outcomes. During an active encounter, save its complete authoritative session (including identity, expiry, inventory and damage) for exact resume. Durable outcomes include: transactions, actor/ship capture conversion, player/ship damage, relationship changes, discoveries, and narrative flags. Never create a silent permanent-site delta merely because an encounter was visited.

## Versioning and limits

Every root record, site generator, chunk format, material key, component key, and content definition has a version/key. Generator revision changes require a migration, compatible regeneration, or retained baseline snapshot for affected sites. Every authoritative loose cell and detached fragment persists. Sleeping chunks may use compressed or aggregate encodings only when the encoding is lossless for material identity, cell count, position/velocity state, and reactivation; insignificant dust may be visual-only only if it never represented an authoritative material cell.

There is no gameplay cap on discovered or modified sites. Design capacity is at least **100,000 indexed site records** in one world. This is an engineering target, not permission to retain unbounded GPU memory: only active site data is loaded, unmodified contacts remain compact procedural metadata, and modified-site payloads are individually addressed/on-demand. Stress fixtures must measure 100,000-site index size, open/save time, and representative large-delta storage. If a platform storage constraint is reached, surface an actionable save-management warning; never silently prune world history. The first slice records actual payload sizes in `docs/PERFORMANCE.md` rather than assuming compression ratios.

## Implementation checklist

IDs refer to [EXECUTION_PLAN](EXECUTION_PLAN.md); only verified work is checked.

- [x] B.4 Atomic lossless saves/recovery.
- [x] B.5 100,000-site index (index-only fixture; full B.5 remains open).
- [ ] D.GATE Active encounter resume/expiry.

## Implemented checkpoint — 2026-09-07

`SalvageSaveCodec` schema 1 stores an exact compressed active-site checkpoint: material-key table, generator identity/revision/seed, all fixed/damage chunks, cell identity/position/velocity/flags, ship hull/pose, blueprint, unit health/support, fragment records and finite tank state. `AtomicSalvageStore` verifies SHA-256, flushes a pending file, atomically replaces the primary and retains a verified backup. Interrupted candidates never replace committed data. Corrupt primaries recover the previous generation with an explicit UI message; unsupported schemas/generators are rejected without rewriting the original.

F5/F9 save and restore the playable site under `Application.persistentDataPath/Saves/salvage.debris`. Simulation pauses across the snapshot/transaction boundary; encoding and disk I/O run on a worker. Load constructs a replacement session before disposing the current session. Action assets now own flight turn, suction, door and save/load bindings.

`SiteIndex` independently proves 100,000 modified-site identities with sorted fixed-size records, streamed merges, checksum/backup recovery and binary-search lookup. No site payloads are loaded by lookup. It is not yet wired to a world/site transition manager.

Verified: 27/27 EditMode tests, exact disk/GPU restoration with cargo and partial terrain damage, catalog reordering, interrupted replacement, corrupt-primary recovery, future-schema rejection and the 100,000-site fixture. B.4 remains open for dirty-only per-chunk files, multi-site leave/revisit integration. B.5 remains open for large ships and active-region streaming. Full fragment physics is not established by serializing fragment records.

Schema-1 → schema-2 migration and physical fuel state are now verified with a retained prior standalone save. The next unused cell identity survives pumping, spilling, pool compaction and save/load. B.4 remains unchecked for sparse dirty-only files and actual multi-site leave/revisit transitions.

## B.4 sparse world and site lifecycle — 2026-09-07

The playable session now uses `WorldStore`, `SparseSiteStore`, `SpatialCellCodec` and `SiteTransit`. F5 publishes a sparse world checkpoint; F9 restores it; T travels between two salvage sites. Startup resumes an existing world. R restores a saved world instead of resetting its mined terrain. A legacy `salvage.debris` remains importable with F9 when no world exists; the next save creates the world without rewriting the source.

Changed fixed/damage chunks and spatial loose-cell buckets are compressed, hashed and individually addressed. Unchanged baseline chunks are regenerated and verified by a saved baseline hash. Unchanged blobs are reused. Material keys remap both generated baseline and saved deltas before validation. Ship/cargo/fragment/fuel records retain exact floating-point state and stable identities.

Departure separates ship-local cargo and attached machinery from world-space deposits and detached machinery. The inactive record has no player hull, tank contents or cargo. Arrival combines the current portable ship with the destination's deposits, validates a clear physical berth and debris capacity, and allocates future cells from the monotonic world identity sequence. A damaged tank must finish its lossless spill before departure. Detached tank outlets follow the fragment's actual pose. The destination runtime is prepared before committing both site records and the active-world pointer. Failed arrival or interrupted publication retains the original committed world.

A sorted index generation and all immutable site dependencies are written/verified before atomic root replacement. Revision checks and a writer lease reject stale/concurrent writes. The previous verified root remains available. Damage to an inactive site is isolated to that visit; damage to active dependencies can recover the previous complete root. Unsupported schema/generator revisions fail without silently regenerating modified sites.

Verified: 38/38 tests, legacy-world import, four dirty chunks with spatial fuel/cell state, unchanged-blob reuse, A→B→save/load→A with no duplicates, exact fragment/cell/GPU restoration, interrupted publication, stale writes, content reordering, baseline mismatch and corrupt-record recovery. A Mac player performs the same two-site travel/resume/revisit loop. B.4 is checked for bounded active-site persistence. B.5 still owns large-region paging, incremental GPU readback, full-size ship/fragment masks, mixed 100,000-site payload stress and unreachable-generation cleanup. Explicit saves currently snapshot the entire bounded active page; only differences reach disk. Inactive sites sleep exactly rather than simulating elapsed absence.

## B.5 save-growth checkpoint

`CollectUnreferenced` now marks every indexed site record and blob reachable from both the current manifest and its verified recovery manifest, then deletes only unreachable files owned by the store. It validates the complete dependency graph before deletion. A missing, damaged or unsupported live dependency defers the entire collection; user notes and every current/recoverable site are retained. Collection runs on the save worker after publication, and maintenance failure cannot change the committed gameplay outcome. This bounds obsolete-generation growth without limiting the number of modified sites. The large mixed-world runtime cost remains to be measured before treating collection after every save as a final scheduling policy.

The 38-test suite verifies collection after interrupted travel, both roots remaining loadable, unrelated-file retention, and no deletion when an inactive record is damaged. This supersedes the earlier temporary retention of unreachable candidates. B.5 remains unchecked for large ship/fragment fields, active-region streaming and mixed 100,000-site payload execution.
