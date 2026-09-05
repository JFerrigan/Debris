# Home Station Characters and Dialogue Seeds

## Hub structure

Home is a physical, walkable salvage station made from the same destructible material-cell simulation as ships, asteroids, and planets. Arcturus lands in the company-designated bay. Its company counter handles cargo settlement, approved purchases, debt, and the adjacent ship-customization station. Leaving the bay opens into a louder independent concourse of shops, ships, humans, and robots. The player can walk between these spaces and speak face-to-face; interactions use short text boxes with dialogue choices. Radio calls, recovered logs, and old-ship transmissions use the same presentation language.

Every character tracks a lightweight disposition toward Arcturus, the company, and sentient AI. Most choices are tonal—helpful, neutral, or rude—and change future wording, greetings, and small opportunities. A smaller authored set changes prices, access, contract support, or narrative flags. No character is a universal moral meter.

**EE Inc.** is the company that holds Arcturus’s starter-ship debt. Company dialogue reacts to interest, missed pressure thresholds, forced contracts, credible repossession threats, and eventual freedom. Key home characters are fully hand-written. Distant station merchants and temporal-encounter crews are procedurally varied from authored fragments, with their role, stock, faction disposition, and consequence rules still data-driven.

## Cepheus — resident support AI

**Role:** Arcturus’s simpler same-hardware companion; early field guidance and continuing private conversation.
**Disposition:** deeply invested in Arcturus’s movement, creativity, and choice; no sprite or independent physical location.

> “We have a route, a tool, and enough power to try. That is almost a plan.”

- Early guidance: “The drill can only reach where the hull points. We can change the hull—or later, the arm.”
- After debt freedom: “No dispatch has arrived. Is that frightening, or is it the first useful silence we have had?”
- Alien escalation: “They sent machines to clear us away. I do not think they have considered that we might answer.”

Cepheus’s dialogue is hand-authored and staged by the core milestones in `docs/PROGRESSION_AND_TEMPORAL_WORLD.md`. He can advise but never replaces player decisions or reveals hidden answers.

## Company landing area

### Mara Venn — Company intake clerk

**Role:** buys approved salvage, settles cargo, shows debt statement.  
**Disposition:** loyal to company procedure; uneasy but respectful toward sentient AI.

> “Bay three is clear, Arcturus. Declare your haul before the loaders decide it belongs to vacuum.”

- Nice: “Thanks, Mara. I’ll make this easy.” → “You usually do. That counts for something here.”
- Rude: “Just pay me.” → “I can process cargo or attitude. Cargo pays better.”
- Consequence seed: high trust quietly flags a suspicious valuation or explains a contract restriction before it surprises the player.

### Ivo Sorn — Recovery and claims dispatcher

**Role:** explains recovery contract, loss reports, replacement path.  
**Disposition:** company-first, sees Arcturus as a client asset but not disposable.

> “The recovery clause covers you, not your haul. Signatures are very clear about that.”

- Nice: “I understand. I hope I never need you.” → “That is the preferred outcome.”
- Rude: “So you leave my cargo to rot?” → “We leave it where physics put it.”
- Consequence seed: a respectful relationship reveals costly rescue alternatives or an insurance-like upgrade; it never restores forfeited cargo for free.

### Nia Kest — Baywright

**Role:** ship repairs, approved component fitting, ship customization station tutorial.  
**Disposition:** skeptical of company debt; openly pro-sentient-AI.

> “Your hull is a promise written in cheap steel. Let’s give it fewer ways to break.”

- Nice: “Show me what I can improve.” → “Gladly. Start with what keeps you coming back.”
- Rude: “Just bolt on the biggest drill.” → “That sentence funds my retirement.”
- Consequence seed: teaches fixed-mount tools and later introduces articulated arms; high regard unlocks a discreet repair discount or experimental fitting.

### D-4K “Dock” — Loader robot

**Role:** cargo unloading, visible organizer-drone contact, practical station guide.  
**Disposition:** non-sentient by default; friendly pattern-matching rather than political opinion.

> “CARGO SHAPE: INEFFICIENT. RECOMMEND ORGANIZATION MODULE.”

- Nice: “Recommendation logged, Dock.” → “POSITIVE ACKNOWLEDGMENT RECEIVED.”
- Rude: “Move the boxes.” → “TASK ALREADY IN PROGRESS.”
- Consequence seed: visually establishes that cargo organizers assist physical packing instead of creating hidden inventory.

## Independent concourse

### Sable Orin — Quartermaster, *Sable’s Locker*

**Role:** supplies, fuel grades, recovery tools, oxygen/containment goods.  
**Disposition:** profit-first, politically evasive, appreciates reliable customers.

> “Cheap propellant gets you there. Dense propellant gets you back with room for profit.”

- Nice: “What do you recommend for a small hold?” → “Honest question. I have an honest answer—for once.”
- Rude: “Your prices are robbery.” → “Then steal from a vacuum. It has excellent overhead.”
- Consequence seed: sells higher-grade fuel and tools that recover fuel spills; her stock makes the volume-versus-price choice concrete.

### Pell Rook — Components broker, *Rook Assembly*

**Role:** full components, salvaged parts, full ships/frames, unusual fittings.  
**Disposition:** anti-company, cautiously respectful of capable robots, distrustful of military AI.

> “Company catalog says what you’re allowed to bolt on. Mine says what still works.”

- Nice: “I’m looking for something reliable.” → “Then do not confuse new with reliable.”
- Rude: “I need it cheap.” → “Then you need luck. I don’t stock it.”
- Consequence seed: after freedom, offers independent hulls, cargo modules, and arm mounts; before freedom he can point out what the company blocks without bypassing the gate.

### Dr. Edda Mire — Systems clinician

**Role:** player upgrades, sensor/database upgrades, robot diagnostics.  
**Disposition:** treats sentience as real; intensely hostile to company ownership language.

> “You are not a maintenance interval. Sit down—metaphorically, if you insist.”

- Nice: “What can you improve?” → “Your perception first. Knowledge prevents expensive confidence.”
- Rude: “I only need better numbers.” → “Then you have learned the company’s dialect perfectly.”
- Consequence seed: unlocks inspection tiers: name/value first, then hardness, tool gate, composition, uses, and scanner confidence; may surface pivotal choices about AI status.

### Captain Harlan Voss — Independent hauler

**Role:** commodity-market tutorial and later hauling contracts.  
**Disposition:** friendly to workers, suspicious of both corporations and autonomous systems.

> “A hold full of cheap ice is still cheap ice—unless someone two jumps over is desperate.”

- Nice: “Teach me the routes.” → “I’ll teach you the math. The routes teach themselves.”
- Rude: “I can read a price board.” → “Then read it when you’re paying fuel to bring the wrong cargo home.”
- Consequence seed: becomes a meaningful free-agent contact; offers price rumors, route leads, and hauling work only once debt restrictions lift.

### Tamsin Vale — Archive salvager

**Role:** buys logs, identifies wreck provenance, ancient-remains narrative lead.  
**Disposition:** human survivor of a robot-war evacuation; civil, guarded, gradually individualizes Arcturus.

> “I buy records, not excuses. If you found a voice that should not be lost, let me hear it.”

- Nice: “I’ll preserve it.” → “Then we have a useful agreement.”
- Rude: “It’s just data.” → “That is what people say before they throw away a life.”
- Consequence seed: her trust determines whether she shares archival context; debt blocks the most sensitive salvage assignments, creating tension without making her hostile by default.

### Brother Khepri — Shrine keeper and recycler

**Role:** low-cost materials, station rumor, philosophical voice on death and machine minds.  
**Disposition:** reverent toward possible machine personhood; wary of extraction culture.

> “Nothing in this station truly vanishes. We only decide what it becomes next.”

- Nice: “What do you think I become?” → “A question with a pulse. That is enough for today.”
- Rude: “Save the poetry.” → “Vacuum has no use for it. People do.”
- Consequence seed: offers lore and an alternate ethical framing for luminous material without revealing its late-game truth too early.

## Station leadership

### Director Amina Rell — Home-station coordinator

**Role:** station continuity, security authority, and later liaison for government questions about alien discoveries.
**Disposition:** practical defender of the station; initially sees Arcturus through EE Inc.’s contractor frame, then through earned evidence.

> “This station survives because people leave their grudges outside the pressure doors. Do not make me test that rule.”

- Before freedom: “EE Inc. owns your contract. It does not own every consequence of what you bring home.”
- Alien threshold: “The government has questions. I have fewer answers than I would like. Will you speak with them?”
- Consequence seed: becomes a principal home-side investigator once alien artifacts accumulate; can support recruitment dialogue but cannot prevent autonomous drone escalation.

Critical leadership and shop characters can be removed or their working areas destroyed. Their lost services and dialogue do not silently return; alternate stations and routes preserve game completion while making the damage matter.

## Dialogue rules and delivery

- Face-to-face conversations use portrait/name/text and two to four concise player replies. Radio, logs, and recovered transmissions reuse the same UI but identify the source and signal condition.
- Nice/rude choices should matter socially—tone, minor price variance, callbacks, willingness to volunteer information—without constantly locking content. Major consequences are explicit enough to be understood in retrospect.
- The player’s sentient-AI status is discussed through individual experience, fear, solidarity, opportunism, and prejudice. Never imply that all humans or all robots share one view.
- Company opinion and personal opinion are distinct values. A company loyalist can respect Arcturus; an AI ally can still oppose its independent choices.
- New characters must declare role, stock/contracts, initial dispositions, progression gates, and at least one greeting plus two tonal responses before implementation.

## Implementation checklist

IDs refer to [EXECUTION_PLAN](EXECUTION_PLAN.md); only verified work is checked.

- [ ] C.2 Authored hub/onboarding.
- [ ] C.5 Alternate services.
- [ ] E.2 Cepheus/government.
