# Contact solver V2 — coupled GPU architecture

Design revision: 2026-09-24. **Planned, not implemented or performance-qualified.** This document is the numerical and data contract for B.3R.R1 V2. [Implementation and acceptance](CONTACT_SOLVER_V2_IMPLEMENTATION.md) specifies the work batches and stop conditions. [CONTACT_PHYSICS](CONTACT_PHYSICS.md) specifies product invariants and integration boundaries. If an older document prescribes mass-split Jacobi, three fixed legacy iteration profiles, or a separate rigid solver, this revision supersedes that prescription.

## 1. Decision, scope and evidence

Replace the current contact solver with a **matrix-free semismooth Newton solve of the complete 2D contact system**, using restarted GMRES for its linear search directions, small contact-manifold preconditioners, and bounded residual-based line searches. Grains, ships, fragments and anchored terrain participate in the same contact operator. Velocity and geometric position correction remain separate solves. Each square face contact has up to two actual clipped points; tied SAT axes are not treated as two independent corner constraints.

The purpose is to transmit the effect of a contact through the coupled system without relying exclusively on repeated independent local corrections. This is an algorithm replacement, not a cosmetic refactor. Preserve the GPU matter representation, material identities, explicit mass/mobility policy, ordered transactional ownership, and rendering access. Replace contact construction, solve scratch, convergence control and the old grain/rigid scheduling split.

Evidence supports replacing the current method, but does not prove the replacement will meet 8 ms. The [checkpoint](evidence/B3R-physics-viability.md) records all legacy 4/2, 8/4 and 12/6 profiles failing packed correctness before throughput. The scalar 1.8 experiment and subsequent 4x/8x experiments changed relaxation or repetition; the [8x patch](evidence/B3R-coupled-8x-solver.patch) did not introduce a new coupling algorithm. Arithmetic parity with that implementation is not a convergence proof. The current code also collapses a face patch to a representative point and retains position lambda while recomputing its direction. These are specific formulation risks to isolate, not already established causes.

Two approaches were considered:

| Approach | Useful property | Decisive limitation for this project | Decision |
|---|---|---|---|
| Block projected Gauss–Seidel, with contacts colored into independent batches | Established local contact solving; two-point blocks improve flat support; updated state propagates within a sweep | Hundreds of contacts share the finite ship endpoint. Correct edge coloring needs at least that endpoint's contact degree in sequential batches; treating the ship as static to avoid this is forbidden. A special hull solver would introduce another coupling design. | Retain as an offline comparison on small cases; do not implement as the production path in this batch. |
| Global contact residual with a matrix-free Newton/Krylov direction | Includes all finite endpoints in every operator application; a heavily contacted hull needs a segmented reduction, not a color per contact | More numerical machinery, global reductions, possible rank deficiency and significant dispatch cost | Selected, with explicit convergence, dispatch and performance gates before adoption. |

Do not add a third production solver during implementation. A failed gate produces a bounded redesign decision with its evidence. It does not authorize silently changing friction, adding compliance, making grains smaller, accepting overlap or replacing dynamic cargo with an aggregate rigid mass.

## 2. Authority and retained product rules

One material cell remains a unit square with independent center, velocity, angle and spin. Its mass is material density and its inertia about its center is mass/6. Cargo classification is metadata; it does not add cargo mass to the hull or change the grain's dynamics. A body has finite positive inverse mass/inertia unless its persistent mobility policy explicitly anchors it.

CPU owns content, hull topology, machinery, stable IDs, material definitions, tank inventories and commands. GPU owns every active endpoint's motion, contact solving, convergence decisions, geometric validation and step commit. CPU may perform offline diagnostic contact solves in Editor tests, and command-time topology reconstruction after a fence. Neither is a runtime contact fallback.

No per-cell GameObjects/Rigidbodies, deleted overflow matter, hidden overlap, hard-stop velocity reset, automatic anchoring or authoritative rendering workaround. An ordinary sleeping flag must not alter mass. Initial V2 qualification keeps every grain mechanically active, including zero-velocity exterior grains. Sleeping/active-island optimization requires its own later equivalence and wake tests.

The existing gameplay path remains the default until explicit cutover gates pass. A single experimental solver selector chooses legacy proof or V2 at construction; no tick may mix their states. Do not build a general pluggable physics framework.

## 3. Coordinates and state

Use cell units, seconds and material-mass units. Fixed frame dt is 1/60 s. Float32 is the production GPU arithmetic; independent reference arithmetic is Float64. Stable world/site positions retain an integer page origin plus local float coordinates. The R1 benchmark keeps the existing fixed page origin and [-512,512) grain-center bounds so its workload is unchanged.

Store substep movement as local deltas from the substep start. Evaluate local contact arms before adding large site offsets; add the accumulated delta to the committed center only at publication. This reduces cancellation. Large-world origin shifting is a fenced translation of all endpoint and collider coordinates plus page metadata, with no velocity/angle change; implement it only in the later streaming phase. A large body whose center is outside the grain page remains a valid collider if its boundary proxies intersect the page.

Retain the 48-byte public grain record and 32-byte body state/parameter records. Grain identities and body persistent IDs are independent of temporary endpoint indices. Body endpoints remain after the allocated grain region so append/remove transactions do not move every body index. In R1, capacity is 8,192 grains, one ship, 16 fragments and one anchored terrain endpoint: at most 8,210 endpoints. The solver uses the same operator for every endpoint class.

Authoritative state contains poses, velocities, identities/materials, mobility, mass/inertia and topology revision. Solver manifolds, impulses, Krylov vectors, active branches, convergence counters and warm-start keys are derived. They never become the only copy of authoritative matter.

## 4. Geometry and broad phase

### 4.1 Contact manifold contract

For two convex square/rectangle primitives, use SAT only to select the reference separating face. Choose the maximum separation axis, orient it consistently from endpoint A to B, identify the incident face, and clip that edge against the reference face's two side planes. Retain each distinct clipped endpoint whose normal gap is within the query margin. A point contact produces one point; a face interval produces two. Do not replace that interval with its midpoint. Deduplicate points within 1e-5 cell.

Each point stores both local surface anchors, the common world-space midpoint used for impulse arms, normal, signed gap and stable feature key. Negative gap means penetration. Normal/tangent Jacobians use one common midpoint for the two endpoint arms so equal/opposite impulses do not create an artificial net torque. For normal motion, shifting either surface anchor along the normal to this midpoint does not alter its normal angular Jacobian. The tangential Jacobian uses the common midpoint deliberately.

Tie rule: retain a previous reference face if it is still within 1e-6 cell of the maximum separating axis and its feature remains valid; otherwise choose the maximum axis with a stable endpoint/axis tie break. Never force a materially worse axis for persistence. A diagonal corner tie represents competing descriptions of one convex contact, not two perpendicular constraints that block sliding. Distinct exterior faces of a concave hull corner remain distinct physical features.

Keep the existing cached rectangular hull/terrain boundary patches as R1 collision primitives. Their occupied union must match the effective material field. Boundary construction must exclude internal faces and duplicate coplanar support from adjacent patches; validate exposed-face ownership against the shape mask when building the cache. A selected primitive face that lies inside the same rigid body's material union is not an exterior contact. Canonical proof fixtures keep their exact existing rectangle unions. Do not approximate a cavity with its enclosing solid rectangle.

At topology-cache construction, intersect each primitive face with the exterior boundary of its own material union. Split partially exposed faces into stable intervals and assign each coplanar shared interval one owner. Narrow phase clips against these exposed intervals; an internal winning SAT face requires an exterior-feature query, not automatic acceptance of the primitive overlap. The independent validator also tests occupied-union containment: a grain wholly inside solid material must fault even if no exposed face was found nearby. Seam, concave-corner, partial-face and deep-containment tests are required before accepting this cache. The 4,096-proxy capacity applies after splitting; overflow rejects the topology proposal.

Contact keys include ordered stable endpoint identities, both shape revisions, reference/incident feature IDs and clipped endpoint provenance. A slot index alone is never a persistent contact key. Opening/closing a door, cutting a cell, changing mass/COM, replacing a boundary cache or relocating a page invalidates affected cached contacts before the next solve.

### 4.2 Candidate construction and coverage

Retain four-cell bins and a 256x256 page for R1. Build swept AABBs using translational motion, angular radius and the .25-cell correction envelope. Bin queries enumerate the full overlapped integer range of the AABB, not an assumed fixed 3x3 stencil. Stable-sort each resulting pair by endpoint/feature identity and deduplicate before narrow phase. Integer counting atomics are allowed. Floating-point reaction atomics are not.

Replace an all-grains-by-all-boundaries scan with a spatial index of boundary proxy AABBs. Short proxies spanning at most 64 bins use sorted bin-reference spans. Longer proxies use one separate sorted global list, capped at 64 entries, and receive an AABB rejection before SAT. All affected collision primitives are retained; exhausting either index reports a capacity fault. The query sees short spans plus the global list and deduplicates their keys. Rigid/rigid pairs enter through the same broad phase, excluding same-body primitives and two explicitly anchored endpoints.

At each substep, gather using the start state and swept bounds. Refresh narrow-phase manifolds at the predicted pose and after each accepted nonlinear position update. The pair list may be reused while all endpoints remain inside their gather envelopes. If an update leaves an envelope, reject the tick with `SearchEnvelope`; do not silently continue with incomplete candidates. After the final solve, an independent validation query repeats broad-phase overlap detection from final poses and computes actual geometry, so a missing constraint cannot produce a false pass.

R1 pair capacity is 524,288 unique candidate pairs, maximum 64 incident candidate primitive pairs per grain, 65,536 active manifolds, 131,072 contact points and 262,144 velocity rows (normal+tangent). A manifold contains at most two points. Rigid pair limits are 64 points per rigid endpoint pair and 4,096 rigid/rigid points total within the common pools. These are hard validated limits, not pruning targets. Increasing one requires revising the buffer budget and rerunning its capacity tests.

## 5. Common contact operator

Let `u` concatenate `(vx, vy, spin)` for all dynamic endpoints. Let `W` be block diagonal with `(inverseMass, inverseMass, inverseInertia)`. Anchored endpoints contribute a zero block. A normal row for common point p and normal n from A to B is:

```text
J_i u = dot(n, vB - vA) + cross(rB,n)*spinB - cross(rA,n)*spinA
rA = p - centerA; rB = p - centerB
A = J W J^T
u(lambda) = uFree + W J^T lambda
```

Tangential rows use `t = (-ny,nx)` and the same arms. J contains all grain/grain, grain/rigid, grain/terrain and rigid/rigid rows. Remove endpoint-degree factors from the physical effective mass. The diagonal is the true `A_ii`; endpoint degree affects scheduling only.

Never form the dense matrix A. Apply it as:

1. Each endpoint gathers `J^T vector` from a sorted CSR adjacency span.
2. Multiply that endpoint's three summed components by W.
3. Each contact row reads its two endpoint results and evaluates J.

For long endpoint spans, first reduce fixed 128-entry segments into partials, then reduce the endpoint's partials in a fixed tree. This bounds workgroup work for a heavily contacted hull. It includes the finite hull response in the same product as the grains. No outer loop alternates a frozen ship with moving grains. No dense contact clique is stored for that hull.

Use a fixed stable reduction order. GPU results need tolerance-based reproducibility, not cross-GPU bit identity. Identity ordering, candidate ordering, feature tie breaks and fault reporting must nevertheless be deterministic for a given input and backend.

## 6. Velocity contact law

After external force integration, hold substep geometry fixed while solving velocity. Restitution is zero. Coulomb coefficient is .3 for the benchmark and zero for analytical frictionless cases. No springs, contact softness, normal compliance, rolling resistance or automatic velocity damping are introduced.

For gap g and substep duration h, set `b_n = -max(g,0)/h`. A separated speculative contact may close its remaining gap; a touching/penetrating contact permits no approaching normal velocity. Friction is enabled only when `g <= .0001`; a separated speculative contact has tangent impulse zero.

The target system is:

```text
w_n = J_n u(lambda) - b_n
lambda_n >= 0; w_n >= 0; lambda_n*w_n = 0
lambda_t in [-mu*lambda_n, +mu*lambda_n]
lambda_t = clamp(lambda_t - r_t*(J_t u(lambda)), -mu*lambda_n, +mu*lambda_n)
```

The tangential condition gives zero slip when sticking and opposing, saturated friction when sliding. This is the non-associated Coulomb law: do not substitute minimization over a combined friction cone that introduces normal dilation during sliding.

### 6.1 Scaling and residual

For each row let `D_i = A_ii`. Two-anchored rows are discarded before this point. Nonfinite or nonpositive D for a retained row is a formulation fault, not something to hide with an arbitrary mass floor. Use scaled impulse `x_i = sqrt(D_i)*lambda_i` and scaled operator `B = D^-1/2 A D^-1/2`. B has unit diagonal in valid rows. For velocity, `c = D^-1/2*(J*uFree - b)` with b zero for tangents; `w = B*x + c`.

For each point define `gamma = mu*sqrt(D_t/D_n)` and `a = gamma*max(x_n,0)`. The complete scaled residual is:

```text
F_n(x) = x_n - max(0, x_n - w_n)
F_t(x) = x_t - clamp(x_t - w_t, -a, +a)
```

When friction is disabled, keep the tangent slot with `F_t=x_t` and zero its physical impulse; this preserves a fixed two-row stride. A solution satisfies the original contact law. Normal and tangent convergence are checked separately. Report `max_i(abs(sqrt(D_i)*F_i))` in cell/s, including spin's surface contribution, and the maximum direct violation of normal and friction conditions. Production velocity tolerance is 1e-5 cell/s. Passing a scaled vector norm alone cannot conceal a row above that physical tolerance.

### 6.2 Generalized derivative

With geometry and branch selection frozen at x, apply H to direction z using a single Bz product:

| Residual branch | `(H*z)_i` |
|---|---|
| Normal: `x_n-w_n > 0` | `(B*z)_n` |
| Normal: otherwise | `z_n` |
| Tangent: strictly inside clamp interval | `(B*z)_t` |
| Tangent: above interval, `x_n>0` | `z_t - gamma*z_n` |
| Tangent: below interval, `x_n>0` | `z_t + gamma*z_n` |
| Collapsed interval or friction disabled | `z_t` |

At an exact normal tie select the active branch only if the unscaled normal velocity violates its bound; otherwise select the inactive branch. At a tangent tie select the saturated branch. Use the same rules in GPU and reference code. Friction coupling makes H generally nonsymmetric; standard CG/CR must not be used for this specified residual.

## 7. Newton, GMRES and bounded globalization

Use right-preconditioned restarted GMRES(16) for `H*deltaX = -F`. Build one local preconditioner for each manifold, using its exact up-to-4x4 H submatrix, including both points and normal/tangent coupling. Use pivoted LU. Add a preconditioner-only diagonal shift of 1e-3; reject pivots below 1e-6 after scaling and use the diagonal of that shifted block as its preconditioner if necessary. Count this event. This shift is not added to the contact residual, physical mass or accepted solution.

The preconditioner is fixed during one GMRES call. Store 17 Arnoldi basis vectors; compute right-preconditioned combinations from the fixed block factors instead of storing a second basis. Use two-pass classical Gram–Schmidt with all existing basis dot products reduced together, then Givens rotations on the small Hessenberg matrix. This avoids one dispatch chain per earlier basis vector. Verify orthogonality in diagnostic runs. Stop the linear solve at relative true residual .1 or the remaining Krylov budget. Check the actual linear residual before using a reported convergence flag; a small Hessenberg estimate alone is insufficient.

Rank deficiency is expected in packed contacts. Identical rows and internal faces must first be removed geometrically. Remaining redundant support can still give nonunique lambda while resulting endpoint motion is unique. If GMRES breaks down or cannot produce a useful direction, restart the search equation with `H + eta*I`, using eta=1e-5 and then 1e-3, consuming the same per-Newton Krylov budget. This regularizes the search direction only; line search and final acceptance always evaluate the unshifted F. Exhaustion is a `LinearSolve`/`Convergence` fault. Do not introduce permanent physical compliance to make a singular solve pass.

For each direction, test alpha in `1, 1/2, 1/4, ..., 1/128`. Candidate x may be outside unilateral bounds during a search, because F defines those bounds through its projections. Reconstruct endpoint motion from the total candidate impulse and the immutable free state; do not accumulate partially rejected candidate updates into endpoint buffers. Accept the first finite trial satisfying `0.5*||Ftrial||² <= (1-1e-4*alpha)*0.5*||F||²`, or satisfying all final physical residual tolerances. The residual is scaled as above for the merit calculation; the physical maximum remains an independent final test.

Once a velocity solution meets tolerance, project its normal impulses to nonnegative values and tangents into their physical friction intervals, reconstruct motion and recompute the true residual. Publish only if this projected state still meets every tolerance; otherwise continue within the remaining Newton budget or fault. Apply the corresponding nonnegative projection and residual recheck to position multipliers before using a pose correction. This removes tiny negative accepted impulses without hiding their physical effect.

If all eight trials fail, use the next unused eta value within the remaining budget and retry the direction. If no budget remains, fault. Do not advance an unconverged step merely because its last iterate looks visually acceptable. Record branch changes, true residual history, damping, restart counts, accepted alpha and the first stable offending contact key.

### 7.1 Warm starting

Cache only converged velocity impulses from accepted substeps. Match full contact keys, normal agreement `dot >= .98`, local-anchor displacement <= .01 cell and current gap <= .005 cell. Drop unmatched rows; initialize them to zero. Reset on any change in h, mass/COM, mobility, shape revision or page origin. Transform a retained world impulse into the current normal/tangent directions and project it into the current unilateral/friction bounds. Never infer warm-start validity from slot reuse.

Within a tick, cache changes are provisional. Publish the final cache only with the successful whole tick. Failure discards all provisional cache writes. Cache cold starts must also pass every physical case; warm starts are an optimization, not a correctness condition. Saves omit the cache, and restoration must converge cold within the declared profile limits.

## 8. Position correction as a fresh coupled problem

Predict centers and angles with the accepted substep velocities. Recompute manifolds from predicted geometry. Position correction changes poses only and never writes physical velocity or spin. Target slop is .002 cell for grain/grain and .0001 cell for every contact involving a rigid body/terrain. Final hard validation is stricter than the old interim solver: <= .01 grain/grain and <= .001 grain/solid and rigid/solid. The legacy comparator retains its historical .002 grain/solid fault threshold and reports its .001 crossing separately.

For one frozen geometric linearization, solve for a **fresh** nonnegative correction multiplier p:

```text
deltaQ = W*J_n^T*p
y = J_n*deltaQ + g + slop
p >= 0; y >= 0; p*y = 0
```

This is the KKT system of the minimum mass-weighted displacement problem with linearized nonpenetration constraints. Use the same scaled normal residual/Newton/GMRES implementation with `c = D^-1/2*(g+slop)`, no tangent rows, and initial p=0. The position residual tolerance is 1e-5 cell. There is no degree-based denominator. Accumulation inside this fixed linear system is mathematically meaningful; carrying its p to newly rotated normals or another geometric linearization is prohibited.

After the linearized solve converges, evaluate the proposed pose update against actual geometry. Test the same eight alpha values. Accept the first trial that decreases the sum of squared positive violations `max(0,-gap-slop)` by the same Armijo fraction or puts every actual constraint within its target plus 2e-5 cell. Preserve already satisfied solid constraints below the .001 hard bound. Also re-query final candidates so newly encountered contacts contribute to this merit. A new contact inside the gather envelope is added and forces relinearization; it is never ignored because it was absent from the previous manifold list. Reset p after every accepted pose update.

Stop when actual geometry satisfies target+2e-5, or the profile's geometry-refresh budget is exhausted. At exhaustion, accept only if all hard geometric bounds and position-system residual checks pass; report `targetReached=false` independently so a hard-bound pass does not masquerade as full cleanup. The unforced residual-grain requirement remains <= .002 after 120 unforced ticks. Any correction outside the .25-cell surface-displacement envelope faults. With a .125-cell motion allowance, total substep surface travel must remain <= .375.

Measure mass-weighted COM change from correction, total linear/angular momentum before and after the complete substep, and kinetic energy. Equal/opposite impulses guarantee the intended exchange for the fixed Jacobian; finite geometric correction can still affect orbital angular momentum, so final conservation checks remain necessary.

## 9. Time stepping and profiles

Keep fixed dt=1/60 and select 4–16 substeps from the same speed rule: `max(4, ceil(2*maximumSurfaceSpeed*dt/.25))`, with only the existing 1e-5 rounding allowance at integral boundaries. Include linear speed, spin times bounding radius and the maximum submitted external acceleration, including suction on grains. Exceeding 16 faults without clamping. Before prediction also validate that contact-modified motion fits the selected per-substep motion envelope. If it does not, report `SubstepBound`; do not tunnel or silently choose extra iterations.

The old 4/2, 8/4 and 12/6 remain immutable **legacy comparison profiles**. Their historical evidence is reused; rerun only when a shared geometry/runner change requires a new comparison. They do not name V2 iteration counts.

V2 has three explicit qualification profiles. Tolerances, geometry, mass, friction and all acceptance limits are identical across them:

| Profile | Velocity Newton updates/substep | Krylov products per Newton update, including restarts/retries | Position geometry updates/substep | Position Newton updates per geometry update | Line-search trials/direction |
|---|---:|---:|---:|---:|---:|
| C1 | 4 | 16 | 2 | 4 | 8 |
| C2 | 6 | 24 | 3 | 4 | 8 |
| C3 | 8 | 32 | 4 | 4 | 8 |

Position uses the same per-Newton Krylov cap as velocity. All loops may terminate early on GPU convergence. The Krylov cap counts Arnoldi products, true linear-residual verification and every regularized retry; reserve a product for verification before taking another Arnoldi step. Separately budget at most three B products per Newton update: one base-residual evaluation, one B*deltaX for all line-search trials, and one final projected-state verification. For frozen geometry, all eight trials use wTrial=w+alpha*(B*deltaX); they require no additional B products. A rejected direction and regularized retry must charge its additional B*deltaX against the remaining Krylov cap. Report Krylov and other residual products separately and their sum. No uncounted residual product is permitted. Startup/cold solve and every substep obey the same caps. No case-specific iteration tuning, force scaling or extra hidden substeps is allowed. Record actual and maximum updates/products/dispatches for each tick.

These are initial implementation budgets, not promised performant settings. C3's worst case is expensive. The scheduling gate in the implementation plan must measure both empty masked schedules and active products before the full benchmark. If no profile meets cost and correctness together, V2 fails its adoption gate; changing these counts requires a new named design revision and recorded reason.

## 10. GPU execution and ownership

`CoupledContactSolver` owns a fixed set of buffers through its session owner. Build immutable command schedules once per profile/capacity/topology generation. Per tick, upload one small command record with tick ID, external force/torque/suction, topology generation and output slot. Avoid rebuilding thousands of C# dispatch commands each frame. Never use synchronous readback to steer Newton, GMRES, line search or convergence.

GPU control buffers hold substep/iteration counts, convergence masks, chosen alpha, true residuals, first fault and indirect dispatch arguments. A completed or faulted stage writes zero work into its later indirect arguments. A masked no-op does not count as accepted physical work. Driver/GPU processing of those masked commands still counts toward frame and submission cost; early exits must not be described as free.

Each operator product uses bounded stages: endpoint segment gather, endpoint partial reduction and W scaling, row evaluation, batched scalar reduction, vector update. Fuse row evaluation with branch/residual work where it preserves dependencies. Use 128-thread workgroups for scans/reductions initially and 64 for per-point geometry; changing sizes for a measured backend does not change mathematics. Allocate no more than eight writable resources per kernel until the pinned Metal compiler/layout smoke test proves the actual binding layout. Split immutable metadata from read/write vector arenas, and give every live arena slice explicit byte offsets and lifetime.

All stages remain on one ordered graphics queue through Unity command buffers. No async-compute overlap or GPU spin-lock/global-barrier trick is part of V2. The command schedule is one concrete Unity implementation; introducing a native compute scheduler is a separate design revision if measured dispatch overhead prevents qualification. The optional native timing bridge in the measurement plan does not own physical simulation.

Tick publication order:

```text
Read committed generation -> initialize working endpoints and provisional cache
For each selected substep:
  apply forces -> validate speed -> gather pairs -> build manifolds/CSR
  coupled velocity solve -> validate velocity residual -> predict
  coupled position solve with manifold refresh -> independent final validation
  retain provisional substep state only if all checks pass
Reduce conservation, counts, envelope and fault facts
If successful: commit endpoints, grains and cache; then classified cargo facts
Publish completion record keyed by tick and generation
```

One failing thread cannot suppress other threads' diagnostic maxima. Latch stage activity before validation and reduce over all participating rows. No grain, body, cache or cargo classification from a failed working tick becomes committed. The FIFO gameplay owner retains its existing provisional fuel/topology discipline; failed ticks and later provisional operations cannot consume material or energy.

## 11. Buffer layouts and budget

All offsets and strides require C#/HLSL layout assertions. Public state layouts remain compatible with current renderer snapshots. New internal records:

| Record | Stride | Fields/order |
|---|---:|---|
| CandidateKey | 16 B | uint endpointA, endpointB, primitiveA, primitiveB; stable order supplied by remap table |
| Manifold | 32 B | uint4 endpoints/firstPoint/pointCount; uint4 stable-key hash, shape-generation references and flags; full equality uses point keys, never hash alone |
| ContactPoint | 96 B | uint4 manifoldIndex/featureA/featureB/flags; float4 localAnchorA/localAnchorB; float4 normal/gap/targetNormalVelocity; float4 commonArmA/commonArmB; float4 Dnormal/Dtangent/friction/zero; uint4 stable point-key words |
| EndpointAdjacency | 8 B | uint pointIndex, endpointSide; both normal and tangent contribution derived together |
| CachePoint | 64 B | uint4 stable endpoint-key words; uint4 feature/revision key words; float4 normalImpulse/tangentImpulse/normalXY; float4 localAnchorA/localAnchorB |
| SolveControl | 256 B minimum | versioned typed counters/flags/residuals; named offsets shared by C#/HLSL; no anonymous reused diagnostic words |

Stable endpoint-key words refer to a session table of full persistent identities plus generation, never a truncated hash with no collision check. The table survives slot reuse only through a generation change. Hash acceleration must confirm full key equality.

Planned upper allocation allowances at 8,192 grains (MiB means 1,048,576 bytes). These are design ceilings, not measured use:

| Pool | Allowance | Derivation/purpose |
|---|---:|---|
| Authoritative/working endpoints, grains, parameters | 2 MiB | public state and two motion images |
| Candidate keys and sort ping-pong | 16 MiB | 2 * 524,288 * 16 B |
| Manifolds | 2 MiB | 65,536 * 32 B |
| Point geometry | 12 MiB | 131,072 * 96 B |
| CSR, adjacency sorting and segmented-reduction metadata | 8 MiB | two endpoint references per point plus sort scratch/offsets |
| Grain bins, scans and active row lists | 4 MiB | fixed page and bounded scan scratch |
| Boundary proxies and their spatial references | 3 MiB | 4,096 primitives; 262,144 short-proxy bin references; long list |
| Krylov basis and vector workspace | 29 MiB | 17 basis + 12 work vectors * 262,144 floats; position aliases this arena |
| Manifold block factors and pivots | 5 MiB | 65,536 * 80 B |
| Committed/provisional contact caches | 16 MiB | 2 * 131,072 * 64 B |
| Control, indirect args, reductions and compact completion rings | 3 MiB | includes actual schedule-control allocation |
| Fixture terrain/render buffers | 2 MiB | existing 16-chunk sparse platform fixture fits this allowance |
| Timing sample rings | 1 MiB | managed/native GPU-visible counters if supported |
| Reserved fenced-topology scratch | 8 MiB | explicit upper allowance; full duplicate solver arenas are forbidden |
| **Total planned allowances** | **111 MiB** | **17 MiB headroom under the unchanged 128 MiB gate** |

All GraphicsBuffers, RenderTexture payloads used by this benchmark, native timing buffers and staging/scratch buffers count, including inactive reserved capacity. Do not count only occupied elements. Publish exact allocation arithmetic in the source manifest and measured evidence. Command-buffer/driver memory that cannot be enumerated is reported separately as unavailable plus observed process memory; it is not silently claimed inside this explicit-buffer total.

The layout avoids a second full contact row array and any dense contact matrix. Refactor the old ~80 MiB solver allocation out of the live V2 instance; keeping both solvers allocated concurrently would invalidate the budget. Comparative runs instantiate one at a time. Later topology publication must drain and reconfigure one arena with bounded scratch, rather than allocate a second 111 MiB solver. Resource allocation failure leaves the old authoritative snapshot recoverable and the session visibly stopped.

## 12. Limits, scaling and portability

The first earned active budget remains 8,192 rotating square grains, one ship, 16 fragments, 16 terrain chunks and the declared boundary/contact capacities. A world may contain more matter through lossless storage and explicit activation. That does not authorize freezing a loose object that can still contact active matter. Saturated admission leaves the source material in place; activation must reserve capacity before publishing a live interacting region.

Larger worlds and ships scale through page-local coordinates, indexed exposed-boundary proxies, compact active rows and bounded per-endpoint reductions. Do not claim a 1,000x1,000 ship is supported merely because its dimensions fit an integer type; it must fit measured active-boundary, material-field, contact and streaming budgets. No full-site scan is permitted in the contact loop. The 10,000-grain exploration and 100,000-site storage test remain separate later gates.

Metal/M4 Pro is the first measured backend. HLSL kernels retain a portable Unity compute path; GPU float64, vendor-specific floating atomics and warp-size assumptions are prohibited. Windows/Linux correctness, compiler layouts and timing need separate execution before platform claims. The publication/rollback contract is shared across backends even when the timing collector differs.

## 13. Research basis and limits of inference

- Macklin et al., [Non-Smooth Newton Methods for Deformable Multi-Body Dynamics](https://arxiv.org/abs/1907.04587), establishes a global nonsmooth contact/friction approach and a GPU Krylov implementation. V2's explicit 2D projection residual, nonsymmetric GMRES backend, finite caps and strict geometric rejection are project design choices. The paper does not certify these settings or Metal performance.
- Catto's [Solver2D](https://box2d.org/posts/2024/02/solver2d/) discusses contact blocks, substeps, position correction, warm starts and precision. It motivates checking the formulation and preserving two-point support; it does not establish the cause of this project's packed failure.
- Catto's [SIMD Matters](https://box2d.org/posts/2024/08/simd-matters/) explains independent contact coloring. The high-degree dynamic-hull bottleneck above follows from that independence rule and this project's contact graph.
- [PETSc GMRES documentation](https://petsc.org/release/manualpages/KSP/KSPGMRES/) is a reference for restarted GMRES and orthogonalization terminology. No PETSc runtime dependency is introduced.

Do not claim convergence, conservation, throughput or architecture viability from a design citation. Only the implementation gates and matching measured fixture earn those claims.
