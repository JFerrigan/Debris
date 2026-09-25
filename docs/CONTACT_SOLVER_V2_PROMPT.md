# Contact solver V2 — current implementation handoff

The next assignment is bounded for the user's selected lower-tier Claude model. Use [CLAUDE_V2_0A_PROMPT](CLAUDE_V2_0A_PROMPT.md) as the copy-ready session prompt and [CLAUDE_V2_0A_PLAN](CLAUDE_V2_0A_PLAN.md) as its detailed implementation plan.

Implement **V2-0A only**: exact immutable committed-pre-step archives, all nine legacy capture/replay cases and rollback evidence. Stop after its decision and commit. This replaces the previous prompt that assigned all of V2-0 in one session.

The remaining V2-0 work is V2-0B independent Float64 geometry/tiny direct contact reference, then V2-0C independent converged packed reference and the separate V2 double comparison. Passing V2-0A does not complete the full reference gate or authorize GPU implementation. The [architecture](CONTACT_SOLVER_V2.md) and [full implementation gates](CONTACT_SOLVER_V2_IMPLEMENTATION.md) remain unchanged.

Use one agent, preserve unfinished work, and retain the model/effort explicitly selected by the user. No model SKU, effort change, delegation, numerical simplification or new runtime behavior is implied by this handoff. Damage/fuel and later phases remain deferred.
