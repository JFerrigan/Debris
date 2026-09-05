# Economy, Debt, and Logistics

## Purpose

The economy makes salvage choices legible without turning early play into a single-asteroid jackpot. It supports three viable careers: approved contractor mining, later independent commodity hauling, and higher-risk independent salvage. Money, cargo volume, fuel volume, and company debt are connected pressures rather than separate minigames.

## Local markets

Every station and planet has a local market record: stock, desired stock, buy price, sell price, and a small identity-based modifier. The company landing area buys approved salvage and sells approved supplies/components. Independent merchants use their own stock and prices. Commodity prices respond to local supply and demand, so hauling goods between locations can be profitable.

Transport is intentionally a mid-game opportunity. A worthwhile run needs a large physical cargo cavity, appropriate containment or handling equipment, enough fuel, and a price spread that exceeds purchase price, fuel, wear, and risk. Early upgrades should normally require several well-chosen salvage trips.

Storage belongs to a location. Cargo sold or stored at one location does not appear at another; paid shipping can move stored material later. Market records, station storage, sales, purchases, and shipments are authoritative, persistent transactions.

## Company debt and freedom

Arcturus starts owing the company for the starter ship. The debt record contains principal, accrued interest, current credit ceiling, approval tier, due-pressure state, and transaction history. The company may approve loans for eligible ship components or supplies within the current ceiling. A loan immediately increases principal; interest creates steady pressure rather than an instant fail state.

While indebted, Arcturus is an approved contractor. Approved work includes mining operations, basic salvage, and company-issued jobs. Independent commodity hauling, piracy, ancient remains, and alien-ship salvage are unavailable or contractually blocked. The restriction is both a mechanical gate and a source of dialogue tension.

Paying the balance in full makes Arcturus a free agent. This unlocks independent contracts and reframes the company from owner-like creditor to a market participant. The exact interest rate, approval tiers, delinquency response, and whether debt can be refinanced are balancing work, not yet fixed.

## Loss and recovery

The company recovery contract prevents permanent character death. If Arcturus is destroyed or stranded beyond repair, recovery returns Arcturus to the home hub and provides the defined workable recovery-ship path. All cargo on board or loose at the loss event is forfeited: it is not recovered by the contract and remains at the site if simulation persistence permits. Debt, market state, and all prior world changes remain.

Recovery cost, response fiction, and the exact condition/value of the replacement are open balance decisions. They must be explicit in the pre-loss warning and post-loss ledger.

## Transaction invariants

- A physical cargo cell can be in exactly one place: ship cavity, site, station storage, shipment, or consumed by a completed transaction.
- A sale removes only accepted cargo from the physical cavity and atomically credits the ledger.
- A purchase atomically debits funds/credit and creates stock or cargo at the designated location.
- Debt-funded purchases record the loan before delivering the component; failed delivery rolls back both.
- A recovery event never silently restores forfeited cargo.
- Market prices and stock changes are saved with the location and never regenerated over player transactions.

## Implementation trigger

This replaces the previous “deferred” economy outline. M7 begins a thin version: company buy/sell, home storage, starter debt display, one approved purchase, and transactional tests. Dynamic multi-location markets, shipping, and free-agent contracts follow once the basic loop is verified.
