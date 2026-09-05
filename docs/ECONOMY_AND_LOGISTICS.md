# Economy, Debt, and Logistics

## Purpose

The economy makes salvage choices legible without turning early play into a single-asteroid jackpot. It supports three viable careers: approved contractor mining, later independent commodity hauling, and higher-risk independent salvage. Money, cargo volume, fuel volume, and company debt are connected pressures rather than separate minigames.

## Local markets

Every station and planet has a local market record: stock, desired stock, buy price, sell price, and a small identity-based modifier. The company landing area buys approved salvage and sells approved supplies/components. Once landed, the player uses its market menu to select cargo to sell; accepted cells are removed from the cargo bay without a required transfer animation. Independent merchants use their own stock and prices. Commodity prices respond to local supply and demand, so hauling goods between locations can be profitable.

Transport is intentionally a mid-game opportunity. A worthwhile run needs a large physical cargo cavity, appropriate containment or handling equipment, enough fuel, and a price spread that exceeds purchase price, fuel, wear, and risk. Early upgrades should normally require several well-chosen salvage trips.

Storage belongs to a location. Cargo sold or stored at one location does not appear at another; paid shipping can move stored material later. Market records, station storage, sales, purchases, and shipments are authoritative, persistent transactions.

## Company debt and freedom

Arcturus starts owing **EE Inc.** for the starter ship. The debt record contains principal, accrued interest, current credit ceiling, approval tier, due-pressure state, and transaction history. The company may approve loans for eligible ship components or supplies within the current ceiling. A loan immediately increases principal; interest accrues against the Frontier Count rather than real-world days, creating steady pressure rather than an instant fail state.

While indebted, Arcturus is an approved contractor. Approved work includes mining operations, basic salvage, and company-issued jobs. Independent commodity hauling, piracy, ancient remains, and alien-ship salvage are unavailable or contractually blocked. The restriction is both a mechanical gate and a source of dialogue tension. Debt pressure can lower/withdraw credit, issue occasional forced contracts, and escalate to clearly announced repossession threats; EE Inc. dialogue and disposition shift with repayment behavior.

Paying the balance in full makes Arcturus a free agent. This unlocks independent contracts and reframes the company from owner-like creditor to a market participant. The exact interest rate, approval tiers, delinquency response, and whether debt can be refinanced are balancing work, not yet fixed.

## Loss and recovery

The company recovery contract prevents permanent character death. If Arcturus is destroyed or stranded beyond repair, recovery returns Arcturus to an available recovery contact (home when operational, otherwise an alternate station) and provides the defined workable recovery-ship path. Carried cargo is forfeited. Unrelated previously deposited material at persistent sites is never removed by recovery. Debt, market state, and all prior world changes remain.

Recovery cost, response fiction, and the exact condition/value of the replacement are open balance decisions. They must be explicit in the pre-loss warning and post-loss ledger. A destroyed home hub can permanently remove its local services, so the economy must preserve access to alternate station/planet markets rather than silently restore the hub.

## Transaction invariants

- A physical cargo cell can be in exactly one place: ship cavity, site, station storage, shipment, or consumed by a completed transaction.
- A sale removes only accepted cargo from the physical cavity and atomically credits the ledger.
- A purchase atomically debits funds/credit and creates stock or cargo at the designated location.
- Debt-funded purchases record the loan before delivering the component; failed delivery rolls back both.
- A recovery event never silently restores forfeited cargo.
- Market prices and stock changes are saved with the location and never regenerated over player transactions.

## Implementation trigger

This replaces the previous “deferred” economy outline. M7 begins a thin version: company buy/sell, home storage, starter debt display, one approved purchase, and transactional tests. Dynamic multi-location markets, shipping, and free-agent contracts follow once the basic loop is verified.

## Implementation checklist

IDs refer to [EXECUTION_PLAN](EXECUTION_PLAN.md); only verified work is checked.

- [ ] C.3 Transactions/services.
- [ ] C.4 Debt/interest/freedom.
- [ ] C.5 Recovery.
- [ ] D.4 Hauling/local markets.
