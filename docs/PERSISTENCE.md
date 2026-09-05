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

- [ ] B.4 Atomic lossless saves/recovery.
- [ ] B.5 100,000-site index.
- [ ] D.GATE Active encounter resume/expiry.
