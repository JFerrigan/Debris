# B.3R V2-0A exact pre-step archive and replay

V2-0A only, 2026-09-25 UTC. This Editor-only batch captures the unchanged legacy comparator immediately before a rejected `Step`, then reconstructs a fresh comparator from the closed binary and executes the archived command once. The input physical bits, source rollback and local replay rollback matched in all nine cases. It does not establish contact feasibility, V2 convergence, throughput or player behavior. At this V2-0A checkpoint, V2-0B and V2-0C remained open.

## Source and archive identity

- Starting HEAD `f0c73f7320e5ebad89a0011d9f1da49f7c6a451d`; design checkpoint `d2eb84c1917fc1ead21023d54aa9b1fd90738815`. Initial tracked patch is `/private/tmp/debris-v2-0a-initial-f0c73f7.patch`; unrelated dirty and untracked work was preserved.
- Protected dirty source SHA-256: `ParallelGrains.compute` `d41c1a466fa103566f39e28508116b1609d0b9776737a024fd2d4469c7986f0c`; `ParallelGrainSolver.cs` `3798fe07644d4fcea4693f4bcdaac3a21aca25830213375478375b1e883711aa`; `ParallelGameplaySession.cs` `859d1dc9ba04722f8aac625e5c6f0dd821553bf083def2437b20bb5e2d1eb9a2`. These remained byte-identical.
- Fixture SHA-256: `ProofFixtures.cs` `8d9558846b5bd043d826fa078dd4d067468c7bd46ace967b33b49e0d9c423168`; `ProofDiagnosticFixtures.cs` `a95fbefffe4117588572ac0a95f6f08a2c47fce7ec07a4c22e38221218792cc2`; `ProofViabilityFixture.cs` `2f0a7a65a475b413ae2ebc39a01a7f7fc30d0e49546cdc04f86ca1c3f9ea72b4`; `ProofRecords.cs` `a5949985714c386a784dbb2fadc613b1de9fcf8c3cf86d17b1f82a19fd6d060b`.
- Canonical archive run: `Logs/b3r-v2-0a/20260925T005106Z-f0c73f7-3c17c6d83c514f76b0db7db4c9f06ce1/`. Each row names a directory under `inputs/` and has `input.bin` plus `manifest.json`; the matching JSON under `outputs/` retains both full diagnostic arrays, maxima, source/local tick mapping and rollback facts. The table hash is the SHA-256 of each `input.bin`. Generated binary inputs remain in ignored `Logs` and are not committed. The manifest publishes after the binary is closed and hashed; an absent manifest makes an archive unusable.
- Format `B3R_PRESTEP` v1 stores explicit little-endian scalar sections: grain 48 bytes, committed endpoint 32, original body parameter 32, boundary 32, original mass 4, and pre-step diagnostics 4. It retains active count, allocated capacity, body endpoint start, constructor options, original default-mass path, command and fixed 1/60 dt. Current fixtures have deterministic fixture ordinals, not production persistent body IDs. Page origin is zero; legacy cache kind is `none`. Combined fixture source revision is 1; the completed player workload/report revision is 2.

## Exact capture and replay

| Family | Profile | Source attempt / committed | Source fault | Fresh attempt / committed / fault | Source / replay bitwise rollback | Input SHA-256 |
|---|---|---:|---|---|---|---|
| shared | 4/2 | 40 / 39 | SolidPenetration | 1 / 0 / SolidPenetration | yes / yes | `ffb1527d47c1ae9ee9857fcb86941899c8bf66ff5264ab9bbf2da9820f6ab92e` |
| shared | 8/4 | 54 / 53 | SolidPenetration | 1 / 0 / SolidPenetration | yes / yes | `02c901cba69570be66875a355b1e490f5b396e95786d2df6f4ea838f6811085f` |
| shared | 12/6 | 55 / 54 | SolidPenetration | 1 / 0 / SolidPenetration | yes / yes | `977cfd2b0695cec9b648101d284eef7733468dcd5c2ea232f69184c4298d6ef5` |
| resting | 4/2 | 3 / 2 | SolidPenetration | 1 / 0 / SolidPenetration | yes / yes | `0f65aadc2beaa4810f4674d4ca88014e9310b442393cfe07875fb28f31539e77` |
| resting | 8/4 | 4 / 3 | SolidPenetration | 1 / 0 / SolidPenetration | yes / yes | `b668fc5a7d39b561ae4175aed0a2dd4ff25823c7ccdf84743010fca8ebc52816` |
| resting | 12/6 | 5 / 4 | SolidPenetration | 1 / 0 / SolidPenetration | yes / yes | `806eee6ce1035212cd975e37b7978c06066a4f92608a8865fe8e465e03e58c66` |
| combined | 4/2 | 1 / 0 | SolidPenetration | 1 / 0 / SolidPenetration | yes / yes | `2bd4e6a806c146baab3b7513459d83b5cf53175ae38f18c054bd978345293b01` |
| combined | 8/4 | 1 / 0 | SolidPenetration | 1 / 0 / SolidPenetration | yes / yes | `13677b70b524007eee2252df9caab4c7d6caaed691e665719b24449d2f9e6ca4` |
| combined | 12/6 | 2 / 1 | SolidPenetration | 1 / 0 / SolidPenetration | yes / yes | `2e5f40eff028a6a89763de8546e8916e5487083ec9f71aad477e24cfe1665549` |

Both source and fresh replay reported the same grain/solid maxima in every row; the per-case output JSON retains their exact values and all 16 diagnostic words. The combined source attempts/commits exactly match the prior player prefix (1/0, 1/0, 2/1). Shared and resting attempt counts also match the earlier canonical table; old post-force traces are supplemental, never labeled exact inputs. `SubstepStart` occurs after force preparation, so reversing it would be approximate.

## Historical archive inventory

The three inspected `/private/tmp` directories existed. The following SHA-256 inventory covers every surviving trace and sidecar. The `b3r-diagnostic-tests/packed-shared-4-2` trace is the known overwritten prototype, **not** the older original; the separate original survives under `b3r-current-diagnostic-tests`. The older original at its former diagnostic path is missing and was not recreated. The `b3r-overrelax-tests` entries are prototype outputs. The complete machine-local inventory was also saved to `/private/tmp/debris-v2-0a-historical-hashes.txt` before editing.

```text
29f796548e5b4261f3436f98d077e182fe2b84c43c65f7e6b06ff023f0a34e89  /private/tmp/b3r-diagnostic-tests/packed-shared-4-2.b3rt.gz
875738c46fcf934e640613b0bf4495dca26d95ea2d932d0feb495b0717377ab2  /private/tmp/b3r-diagnostic-tests/packed-shared-12-6.b3rt.gz
c3d39e564a6a1a4e4d16dd95cc92a26564498356ba9753de62c617888e4c1f04  /private/tmp/b3r-diagnostic-tests/packed-resting-12-6.b3rt.gz
17c912b4db6f24f88b39577ad154fe368dc9cc9f126b8f544c8b7bfee8ad6e6f  /private/tmp/b3r-diagnostic-tests/packed-shared-4-2.b3rt.gz.wall.csv
209c5c65a25455e9619c335236f3295c5bfef9334f3165d8fc4882d218eb7977  /private/tmp/b3r-diagnostic-tests/packed-shared-4-2.summary.csv
33e83dd825b31c742f56ea40931ab72993a0583278f48132b52455ea4950067b  /private/tmp/b3r-diagnostic-tests/packed-resting-4-2.summary.csv
266d7a8a7534ec8b8704678cb595bf4aadc2feb8aad57e7f5368939ad3aa9f85  /private/tmp/b3r-diagnostic-tests/packed-resting-4-2.b3rt.gz
7fc21db386b20791dd5999bdd48cbd7130f76125a875cd2fc92fc7c754dd72d8  /private/tmp/b3r-diagnostic-tests/packed-shared-8-4.b3rt.gz.wall.csv
670996dc12585a628d33131bace659994ac472e59f60d25361a7c1894f02de63  /private/tmp/b3r-diagnostic-tests/packed-resting-8-4.b3rt.gz.wall.csv
3df612b0eaa50483a0fa5ebac01e5e5283f3a77d17498323a027359aa16f7822  /private/tmp/b3r-diagnostic-tests/packed-resting-8-4.b3rt.gz
889c385f109dbc1fee55b6b9ade05b7f33c60d034c01f1c26795743e636298ee  /private/tmp/b3r-diagnostic-tests/packed-shared-12-6.summary.csv
76057a4415c7d71421ef4c6d6faa811c013e0e2e4c08f9d165f6e8cac848442b  /private/tmp/b3r-diagnostic-tests/packed-resting-12-6.b3rt.gz.wall.csv
099a3cdb2867501fc0e9d9176532c8d4ad9eb86eb591fb12f7c56253ce4fa98d  /private/tmp/b3r-diagnostic-tests/packed-shared-8-4.summary.csv
b7d25b2c88db40cae455fd9aa2f3f55aa07a6359bdbe9b56845a14d993a2bee6  /private/tmp/b3r-diagnostic-tests/packed-resting-12-6.summary.csv
3c9f33b87b83ffbe44f1d6a6f9ec0860597247ede420cb7f363eec879421450b  /private/tmp/b3r-diagnostic-tests/packed-resting-8-4.summary.csv
29cd58b86dbf0e77939475df347a4124477da2c7802343d03cf7682ca9b3aced  /private/tmp/b3r-diagnostic-tests/packed-shared-8-4.b3rt.gz
57a59217e339dfda4aabd222ddb4d04e81a62eaec2be8cc50f3590054a449947  /private/tmp/b3r-diagnostic-tests/packed-resting-4-2.b3rt.gz.wall.csv
b645adc5295c89d312394e1dcd81f8eb695f8ef43debcf239b7fc0b1ba18fb4a  /private/tmp/b3r-diagnostic-tests/packed-shared-12-6.b3rt.gz.wall.csv
6f046778ecc4189c8a0b789d81689c7ffdf36d7c641ab1eaf55af51883d14c75  /private/tmp/b3r-current-diagnostic-tests/packed-shared-4-2.b3rt.gz
a8b02d2b0c890007ae2ec4c06f8460bd537c11eb9a46bb04a9afde661051d5ea  /private/tmp/b3r-current-diagnostic-tests/packed-shared-12-6.b3rt.gz
804a44797a83b36247f890a0d4b57685894714ce6a5b0ee0be51982f712b98f3  /private/tmp/b3r-current-diagnostic-tests/packed-resting-12-6.b3rt.gz
e8c10e4b175882f86dd3e206a99fa7c2a6097adca5ce73b8b292a7c221079b16  /private/tmp/b3r-current-diagnostic-tests/packed-shared-4-2.b3rt.gz.wall.csv
77ebea740c77c4a1d51af85e92416d4a37ef756f13a33c60ca07a93805a1c246  /private/tmp/b3r-current-diagnostic-tests/packed-shared-4-2.summary.csv
33e83dd825b31c742f56ea40931ab72993a0583278f48132b52455ea4950067b  /private/tmp/b3r-current-diagnostic-tests/packed-resting-4-2.summary.csv
b5e577dd7344435c9e868cb8c39aa565c1103d0640ecf7ee6cb203e5dfcb0fc0  /private/tmp/b3r-current-diagnostic-tests/packed-resting-4-2.b3rt.gz
f46ebd7692c70f91b023466d92f44e8625249bc41e3a99b72c4e4444bc8c6ddc  /private/tmp/b3r-current-diagnostic-tests/packed-shared-8-4.b3rt.gz.wall.csv
165290d17862bc12701cf6e81bf6f4a28f30cee56245103449e8d9bd41d1b639  /private/tmp/b3r-current-diagnostic-tests/packed-resting-8-4.b3rt.gz.wall.csv
acd3c898f9aea920385a0d03f6b6895ab5f8a08aa7a50e28bd983dde93180733  /private/tmp/b3r-current-diagnostic-tests/packed-resting-8-4.b3rt.gz
1c345aa2fcc600572a9eba51c959f37ab3cb3beef4773de4c09f715b01324bde  /private/tmp/b3r-current-diagnostic-tests/packed-shared-12-6.summary.csv
76057a4415c7d71421ef4c6d6faa811c013e0e2e4c08f9d165f6e8cac848442b  /private/tmp/b3r-current-diagnostic-tests/packed-resting-12-6.b3rt.gz.wall.csv
ef9b33b4dc2fdb2de0c002bacad0a7cbb78d0097423e73f58d7480ed1580ab77  /private/tmp/b3r-current-diagnostic-tests/packed-shared-8-4.summary.csv
b7d25b2c88db40cae455fd9aa2f3f55aa07a6359bdbe9b56845a14d993a2bee6  /private/tmp/b3r-current-diagnostic-tests/packed-resting-12-6.summary.csv
3c9f33b87b83ffbe44f1d6a6f9ec0860597247ede420cb7f363eec879421450b  /private/tmp/b3r-current-diagnostic-tests/packed-resting-8-4.summary.csv
53b94e4f8330bc7989ff932a913265eef5281682282d5f46f0d95ef8902f3ba6  /private/tmp/b3r-current-diagnostic-tests/packed-shared-8-4.b3rt.gz
57a59217e339dfda4aabd222ddb4d04e81a62eaec2be8cc50f3590054a449947  /private/tmp/b3r-current-diagnostic-tests/packed-resting-4-2.b3rt.gz.wall.csv
4cb83c8ca5a6b16aadadf06496de85cdc2bea9dcbcdf8985d570ad6066c6e273  /private/tmp/b3r-current-diagnostic-tests/packed-shared-12-6.b3rt.gz.wall.csv
a962bdeaf2756e6b66a26ff5f97cfe680a154de61b56b4d37a839cc9912e06ab  /private/tmp/b3r-overrelax-tests/packed-shared-4-2.b3rt.gz
a17ebeb7d93110c5c12bd9f84c3d30295516ac9cd5b78dd7464a9b9eb794819f  /private/tmp/b3r-overrelax-tests/packed-shared-12-6.b3rt.gz
12c9f5d3bef854f54eb37df4fcb8725b657ea655f894a9478ad4da2f994568c7  /private/tmp/b3r-overrelax-tests/packed-resting-12-6.b3rt.gz
5f05acf698de82bbe7a7f3466e31d783b67bf03db00e41cc2357246a9d5f4f4d  /private/tmp/b3r-overrelax-tests/packed-shared-4-2.b3rt.gz.wall.csv
754dfe3129870c7bf45dd93800ed699a99e32a10cec83c9e4f46dae3edff3573  /private/tmp/b3r-overrelax-tests/packed-shared-4-2.summary.csv
3b98f647d6b42895fd2513f63c4469b2a36611aedd9d1f444339fad382a2c756  /private/tmp/b3r-overrelax-tests/packed-resting-4-2.summary.csv
263f9974c9e6be27b4be3965cd8b3b7b425126d678c6217a65d6c93a26458292  /private/tmp/b3r-overrelax-tests/packed-resting-4-2.b3rt.gz
0df4af537765bd89880d79f1a75d3684425a47c0c99c975945323d20e8f62961  /private/tmp/b3r-overrelax-tests/packed-shared-8-4.b3rt.gz.wall.csv
ab16e45bf43061a1b29dfc89db6c7ae292d8ed5a15feae8cdd18c9b4d2cb4178  /private/tmp/b3r-overrelax-tests/packed-resting-8-4.b3rt.gz.wall.csv
c3ed17c2bb7f8e71ee34d0c5508b07756eca20e45681085f85cc020c91a1df44  /private/tmp/b3r-overrelax-tests/packed-resting-8-4.b3rt.gz
00b7dc506bca4da0ccef5bbba0d66791a470fa12fb607b994f5b6d6e3ea6c163  /private/tmp/b3r-overrelax-tests/packed-shared-12-6.summary.csv
8b8a79b0a219a06a8a0fdadb328b1d619fe7fae2e7b5d20f4639d8e0aca9af29  /private/tmp/b3r-overrelax-tests/packed-resting-12-6.b3rt.gz.wall.csv
ee9cd3ac535f5b3aa1b5baf4046708ccf0ba824d04defaa2e63dbe9d7120e3cd  /private/tmp/b3r-overrelax-tests/packed-shared-8-4.summary.csv
70c189e682aedde27fba64ef912c96f101754a3f67ba089412672a3e2259f0ae  /private/tmp/b3r-overrelax-tests/packed-resting-12-6.summary.csv
93ba9975936dccc6820a0e69de80fef487e3ae955349c7696cb8970e08bf3e54  /private/tmp/b3r-overrelax-tests/packed-resting-8-4.summary.csv
9d66550495b24c1b91c7aab234b6b28b10bef5e188d74b2249cb8448ff89fbb5  /private/tmp/b3r-overrelax-tests/packed-shared-8-4.b3rt.gz
0808536d81b24b6879b8096b35ae72abb64cf407e655ccfb3aef3813239e6772  /private/tmp/b3r-overrelax-tests/packed-resting-4-2.b3rt.gz.wall.csv
7d26df781319eac59723e819de90b07372af53c2a457b5f6ed30a2a3ceb09d26  /private/tmp/b3r-overrelax-tests/packed-shared-12-6.b3rt.gz.wall.csv
```

## Validation and limits

- Archive codec: `Logs/v2-0a-archive-final.xml`, 4/4 passed. Roundtrip includes signed zero, reserved words, capacity versus active endpoint indexing, malformed format/hash rejection and duplicate-write preservation.
- Ordinary GPU accepted/rejected archive replay: `Logs/v2-0a-ordinary-20260925-1.xml`, 1/1 test passed, covering both branches.
- Explicit nine-case matrix: `Logs/v2-0a-matrix-final.xml`, 1/1 named test passed; nine archives and reports present. All nine faulted on `SolidPenetration`, with bitwise committed rollback.
- Existing diagnostic export-path coverage: `Logs/v2-0a-diagnostic-20260925-1.xml`, 1/1 passed; exports used a new unique `/private/tmp/b3r-current-diagnostic-tests-*` directory.
- Failed attempts: first two Unity invocations found a `LooseCell` namespace compile collision; the third compiled but `-quit` yielded no test XML. The corrected filter without `-quit` executed 4/4. After tightening reservation, body identity metadata and diagnostics reports, the final codec and matrix checks were rerun once. No Mac build or player was run for this Editor-only batch.
- Unresolved: V2-0B independent Float64 geometry/tiny direct reference and V2-0C converged packed reference remain open. R1/B.GATE stay open. Physics GPU, total GPU, frame and CPU p95 remain unmeasured; damage/fuel stays paused. Client-reported usage: unavailable.

## V2-0B — independent Float64 geometry and tiny direct reference

Scoped Editor-only result, completed 2026-10-02 UTC. Starting HEAD on `main` was `27e92d2979d2363b02ab4db2e11c57c857e0b71e`. Implementation began 2026-09-25 02:27:50 UTC and the session paused before documentation; the wall interval through this handoff includes that seven-day interruption and is **not** active implementation time. Unity 6000.3.11f1 on the existing Mac/Metal project ran the tests. No runtime source, package, archive, GPU solver, build or player changed. Unrelated dirty and untracked workspace work was preserved.

### Source identity and immutable input

The three protected, already-dirty files stayed byte-identical to the values recorded **before editing**: `ParallelGrains.compute` SHA-256 `d41c1a466fa103566f39e28508116b1609d0b9776737a024fd2d4469c7986f0c`, `ParallelGrainSolver.cs` `3798fe07644d4fcea4693f4bcdaac3a21aca25830213375478375b1e883711aa`, `ParallelGameplaySession.cs` `859d1dc9ba04722f8aac625e5c6f0dd821553bf083def2437b20bb5e2d1eb9a2`. Final Editor source SHA-256: `CoupledReferenceGeometry.cs` `15d12a4182c0392fcc95427d2a39743cfcab5aedeb0d611b7c9459db8b648638`; `CoupledContactReference.cs` `3e3235c5e799c6f4e0a85636c4eed1047c7d128a017a4a9feb71376fe098d892`; `CoupledReferenceGeometryTests.cs` `637c87c6c99381dd30d420ab03168b808de668e19d21cb319e7cb5333036d91c`; `CoupledContactReferenceTests.cs` `3c076d233a940d093ed91db2c41ea1cc19d214208a5c0e11be05242e392b72d0`.

`DArchive.Read` calls `CoupledReplayArchive.Read(caseDirectory, out manifest)` and checks each of the nine exact `input.bin` SHA-256 hashes in the V2-0A table above. It converts archive Float32 coordinates/motion/mass and patch data once into double records, retains 2,500 versus 8,192 allocated grain capacity, active count, body endpoint start and patch body indices. The nine inputs were read only. There was no trajectory recapture, GPU replay or archived-state geometry acceptance claim; no archived endpoint/feature geometry observation was used as an oracle.

### Geometry and analytical checks

Explicit double transformed rectangle vertices, SAT maximum-separation face selection, previous-face tie within 1e-6 cell, incident edge clipping and 1e-5-cell point deduplication produced one or two physical points with A→B normal, negative penetrating gap, local anchors and a common midpoint. Analytic fixtures covered separation, touching, 0.2-cell penetration, flat two-point support, one-point corner, 45-degree rotation, endpoint reversal, translation, diagonal reference tie and interval collapse. One tied convex diagonal contact yields one point/normal. Rectangle-union tests rejected an internal adjacent-patch seam, merged duplicate coplanar support, retained a one-cell partially exposed face interval and both distinct concave-corner faces. A rotated/transformed union frame preserved the two-point support. Exact polygon/grid partition coverage independently detected a square deep inside occupied material and preserved a ring cavity, including rotated squares.

The tiny matrix explicitly assembles `J`, `W` and `A=J W Jᵀ` in double precision. Tests checked its symmetric positive-semidefinite 2-row form, analytical diagonal `1/2+1/5+(.25²)(1/3+1/7)`, endpoint gather/apply equivalence, equal/opposite linear impulse and orbital-plus-spin torque balance. Endpoint degree never enters effective mass. One/two-point direct solves enumerate up to 4/16 open, sticking and signed-sliding state combinations; frictionless global normal solves enumerate up to 256 sets for eight points. All use restitution zero, Coulomb .3 or explicit zero, `b_n=-max(g,0)/h`, and friction only for `g<=.0001`. Rank-revealing one-sided Jacobi SVD has at most 100 sweeps and relative singular threshold `1e-12*sigma_max`. Failure to converge or find a certified candidate is reported inconclusive; the solver does not infer infeasibility. It selects the valid minimum-norm pseudoinverse candidate with stable enumeration tie order. Direct original-equation normal/friction/slip and equation residuals must each be <=1e-8 in their physical units.

Analytic expected endpoint motion passed for equal masses, 10,000:1 impact (shared speed `100000/10001`), anchored counterpart, off-center spin, centered/off-center flat loading, sticking, both sliding signs, friction zero, speculative closure at `-.6` cell/s, and tangential motion with zero normal load/zero invented lift. Duplicate-feature keys are distinct only for distinct clipped points. Rank-deficient duplicate flat support yielded nonunique impulses but unique zero post-contact normal motion. The global frictionless three-body chain and fresh nonnegative, mass-weighted frozen-position correction matched independent multipliers `(2,1)` and `(.6,.4)` respectively; eight independent contacts enumerated all 256 active sets. No nonlinear packed-position solve was attempted.

From the final contact XML: 10,000:1 case rank 2, residual 0, 4 candidates; two-point flat support rank 4, residual `1.609823385706477e-15`, 16 candidates; duplicate support rank 1, residual 0, 4 candidates; three-body chain velocity rank 2, residual `4.4408920985006262e-16`, fresh position rank 2, residual `5.5511151231257827e-17`; eight-point case rank 8, residual 0, 256 candidates. All are below the specified `1e-8` Float64 reference bound.

### Verification, failed attempts and limits

Final pinned-editor commands used `-batchmode -projectPath "$PWD" -runTests -testPlatform EditMode -testFilter Debris.Simulation.Tests.CoupledReferenceGeometryTests -testResults "$PWD/Logs/v2-0b-geometry-concave.xml" -logFile "$PWD/Logs/v2-0b-geometry-concave.log"` and the same command with filter `Debris.Simulation.Tests.CoupledContactReferenceTests`, XML/log stems `v2-0b-contact-final`. Both processes exited 0; actual XML counts were geometry **5/5** and contact/archive **9/9**. No `-quit` was passed. The contact diagnostic rerun followed test-only output additions after a passing 9/9 result. The geometry final reruns followed a rotated-union test, a stable clipped-endpoint key correction and preservation of the full unsigned patch feature ID. The contact final rerun checked that feature-ID conversion change. The last geometry test edit asserted both concave-corner contact normals and its 5/5 rerun passed. Earlier passing counts were 5/5 and 9/9.

Earlier attempts: the first compile stopped on two C# multiple-`var` declarations; the corrected comma-separated filter selected zero tests and was replaced by actual fully qualified fixture filters. Initial geometry 3/5 exposed a floating-point touching margin and two mistaken fixture expectations; initial contact 4/8 exposed incorrect analytical expectations for archive allocation, tangent inertia and support spin, plus an actual friction-disabled speculative-branch defect. The latter was fixed in the reference equations, not by changing physical tolerance. A subsequent geometry 4/5 revealed the previous-face tie test requested the Y face while expecting X, corrected to request the X face. Contact then passed 8/8. The later union contact extension first produced a 4/5 geometry result because a corner fixture also touched other exterior faces; its assertion was narrowed to the analytically selected exposed right face. No physical bound was enlarged. Source did not change while a final verification was running. No broad suite, Mac build, player, GPU replay, packed reference, or timing run was made; client-reported usage is unavailable.

A remaining design ambiguity for later full-system validation is a square wider than a thin solid strip and centered across it: e.g. square `[-.5,.5]²` and occupied strip `[-.25,.25] × [-1,1]`. Both opposing exterior faces can be clipped in the local union query. This batch does not declare which push-out side a full nonlinear geometric correction should choose or treat opposing normals as independent feasible correction constraints. That question belongs to V2-0C geometry refresh/packed comparison and does not affect the listed one-sided fixtures. The full nine archived packed systems are not required to pass V2 bounds here and remain unresolved. V2-0C, full V2-0, R1 and B.GATE stay open; V2 throughput and all p95 values remain unmeasured.
