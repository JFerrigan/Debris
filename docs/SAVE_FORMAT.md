# Save Format

## Purpose

Debris saves reconstruct deterministic generation plus every authoritative modification. The format must preserve every loose material/fuel cell and detached fragment, avoid serializing render textures, support safe interrupted writes, and remain migratable as content evolves.

## File layout

```text
SaveSlot/
  manifest.json                 schema, world seed, active profile, save revision
  world.json                    player/strategic/station/economy/debt/relationship index
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

## Versioning

`SaveSchemaVersion`, generator revision, field codec version, loose-cell codec version, fragment codec version, material key, and component key are explicit. Any incompatible change requires a migration, retained baseline snapshot, or a visible compatibility/recovery path. The game must not silently regenerate a modified site with a new generator revision.
