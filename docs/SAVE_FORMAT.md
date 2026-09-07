# Save Format

## Purpose

Debris saves reconstruct deterministic generation plus every authoritative modification. The format must preserve every loose material/fuel cell and detached fragment, avoid serializing render textures, support safe interrupted writes, and remain migratable as content evolves.

## File layout

```text
SaveSlot/
  manifest.json                 schema, world seed, active profile, save revision
  world.json                    Frontier Count, player/strategic/station/economy/debt/relationship/narrative index
  sites/<SiteId>/site.json      generation metadata and site record index
  sites/<SiteId>/fixed/*.bin    changed fixed-field chunks by ChunkCoord
  sites/<SiteId>/loose/*.bin    lossless spatial loose-cell buckets
  sites/<SiteId>/fragments/*    fragment record + changed local chunks
  journal/                      temporary write-ahead records
  backups/                      last verified manifest/world/site records
```

The exact codec can be binary/packed after profiling. It is not tied to Unity serialization. Asset references serialize stable content keys and versions; never Unity instance IDs or GPU handles.

## Site reconstruction

1. Read world/site metadata and validate schema/content compatibility.
2. Recreate deterministic baseline from world seed, site ID, generator key/revision, and profile.
3. Apply changed fixed-field chunks.
4. Restore component removals/mutable state and topology.
5. Restore every loose-cell bucket and fragment state in spatial order.
6. Upload only streamed active chunks/cells to GPU resources.

The save is correct when a deterministic fixture produces the same fixed-field hash, loose-cell/fragment state hash, and component state after leave/revisit.

## Loose-cell and fragment encoding

Loose cells are partitioned by spatial `ChunkCoord`, not stored in a single global array. Each record retains material, fixed-point position, velocity, flags, and required state. A sleeping chunk can be compressed, but compression/aggregation must be lossless. Fuel spills and cargo spill remain loose-cell records. Fragment records retain transform/velocity, component state, and references to their local material chunks.

## Writing and recovery

Saving creates a journal entry, writes changed files to temporary names, validates hashes/version, then atomically replaces the manifest/index. The prior valid record remains in `backups` until the replacement is verified. A corrupt site payload isolates that site and offers recovery/reporting; it must not silently delete the full world save.

Save operations consume GPU readback snapshots only at explicit save/eviction boundaries. Writes and compression run away from frame-critical work. A site cannot release/reuse GPU resources until its queued changed chunks/cell buckets have committed successfully.

## Cloud and quota strategy

Steam Cloud syncs these local files; it is not the live database. Keep world/index data small and separate from site payloads so unchanged large sites do not re-upload after an unrelated transaction. Before release, use real generated stress saves to set Steam Cloud byte/file quotas, test multi-machine conflicts, and measure upload/exit time. Preserve conflict backups rather than automatically choosing a version that might erase a mined site.

The intended world capacity is at least 100,000 indexed modified sites without a player-visible count limit. The site index is paged/addressable rather than eagerly loading all `site.json` records. Benchmark index metadata and a mixed 100,000-site fixture separately from payload-heavy sites; a large number of untouched procedural contacts must not create one file each.

Temporal encounters are excluded from `sites/`: they are regenerated/expired runtime contacts. An active encounter has a temporary authoritative session snapshot in `world.json`, preserving identity, expiry, inventories and damage on resume. After resolution/expiry retain only durable results (transactions, captured-ship conversion, relationship/discovery/narrative flags, and player/ship state). Atomic component inventories serialize stable item keys/counts/state; physical cargo remains a lossless cargo-cell field.

## Versioning

`SaveSchemaVersion`, generator revision, field codec version, loose-cell codec version, fragment codec version, material key, and component key are explicit. Any incompatible change requires a migration, retained baseline snapshot, or a visible compatibility/recovery path. The game must not silently regenerate a modified site with a new generator revision.

## Implementation checklist

IDs refer to [EXECUTION_PLAN](EXECUTION_PLAN.md); only verified work is checked.

- [ ] B.4 Codecs/migration/interrupted writes.
- [ ] D.GATE Encounter session snapshot.

## Executable checkpoint format (schema 1)

The current vertical slice uses one `salvage.debris` checkpoint plus `.backup` and `.pending`, rather than the final world/delta directory layout above. Header: 32-bit magic `0x44534252`, 32-bit schema, 32-bit uncompressed byte length, 32-byte SHA-256 of the uncompressed body, then Deflate bytes. BinaryWriter fields use little-endian encoding. Cells retain their IEEE-754 position/velocity bits exactly; this prototype does not quantize them to fixed point. A stable material-key table remaps fields, cargo, hull and fragments across catalog ordering changes. Whole ship state is captured into an owned JSON DTO before worker encoding.

The decoder bounds payload sizes and validates dimensions, accounting and occupancy before upload. Schema/generator incompatibility reports an error while retaining source files. There is no older production schema to migrate yet. The final sparse chunk/loose-bucket layout and multi-site manifest remain outstanding under B.4.

The standalone site index is schema 1: 16-byte header (magic, schema, 64-bit count), sorted 40-byte records (32 ASCII ID bytes, 64-bit revision), then SHA-256. Index updates stream the old records into an atomic replacement; lookup seeks directly to records and never opens site payloads. Snapshot and index integration into a world transaction is still pending.

## Schema 2 migration and physical fuel

Schema 2 adds the next unused site-cell identity and a sparse `(cell identity, remaining energy)` table before the ship-enabled flag. Physical cell records remain 32 bytes on GPU; fuel state is consumed only at explicit inventory-transfer boundaries. The CPU tank DTO now stores one grade/residual-energy record per occupied tank cell, preserving partially used cells separately.

The decoder accepts schema 1 and infers its next identity from the maximum saved identity. Legacy tank Low/Standard/Dense counts and BurnRemainder migrate once into exact per-cell energy records. A real schema-1 standalone checkpoint is retained at `Assets/Debris/Persistence/Tests/Fixtures/schema1.debris.bytes`; tests load its 576 cargo cells/250 tank cells and rewrite schema 2 without refilling fuel. Unknown future schemas still fail visibly. Sparse chunk files and world/site transitions remain B.4 work.
