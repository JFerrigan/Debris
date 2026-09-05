# Strategic System

## Scope

Strategic space is the large-scale travel layer. It owns the player’s strategic position, home/other locations, unknown contacts, scanning/discovery, arcade flight state, physical fuel/cargo mass, and explicit transition into a close-up salvage site.

## Coordinate and travel model

Strategic coordinates use double-precision kilometres. The map permits movement indefinitely in any direction; camera origin shifting is presentation-only. The player ship has arcade directional thrust and rotation, with cargo mass affecting acceleration, handling, turning, and fuel consumption. Technology upgrades can improve speed, efficiency, scanner ability, or other travel capability without changing universal material rules.

Fuel is physical stored material available in multiple grades. A higher grade costs more but produces more range per physical tank/cargo volume. A tank breach or deliberate jettison produces persistent loose fuel/material at the current site/location according to the active simulation context; recovery requires the appropriate equipment. Fuel cannot ignite in vacuum because combustion needs oxygen. Running out of fuel is a recoverable consequence, not an automatic game-over rule; cargo jettison can reduce mass and improve the return situation. If the ship is ultimately stranded beyond repair or destroyed, company recovery returns Arcturus to home but forfeits all cargo at the loss site.

## Contacts and scanning

Contacts initially appear as **UNKNOWN OBJECT** records. They have stable IDs, seeded locations, discovery state, and optional scanner signature. Scanner upgrades can reveal site family, material signatures, size, approximate value, danger, and other content progressively. The exact technology tiers and information certainty remain open.

The interface should show home location and increasing return risk before committing the player to an impractical distance. It should help players filter/ping more valuable or advanced targets later rather than forcing manual inspection of every lesser contact. Navigation includes a strategic map, player waypoints and breadcrumb trails, optional autopilot route following, scanner range/signal confidence, and local-hazard presentation. Autopilot follows a player-approved route; it does not erase planning or emergency flight.

## Site transition contract

Selecting/approaching a target creates an `EnterSiteRequest` containing world/site IDs, player ship snapshot, target metadata, and entry context. The strategic scene records the player ship as anchored at that contact and pauses strategic travel/time. `Salvage` loads through a separate scene/loading transition, constructs a `SiteSession`, and restores the same ship in site-local space.

Leaving commits changed site chunks, loose cells/fragments, ship/cargo/component/fuel state, and strategic contact metadata. The return request restores the ship to the same strategic contact. No hidden scene-object reference carries authoritative game state.

## Locations and logistics

Home colony/station is clearly marked and is a fly-in, walkable physical hub: the designated company bay handles unloading, sales, debt, and ship customization, while independent shops and characters occupy the broader station. Other colonies, stations, and planets buy/store materials with local supply, demand, and pricing. Stored material remains at that location unless the player pays for shipment to move it home. Debt restricts indebted contractors to approved work; repaying it unlocks independent hauling and restricted salvage. See `docs/ECONOMY_AND_LOGISTICS.md` and `docs/HOME_STATION_CHARACTERS.md`.

## Verification

The strategic showcase provides fixed world seed, home marker, starter ship, seeded unknown contacts, range/fuel HUD, scanner reveal presets, and entry/exit reproduction. Tests verify stable contact generation, double-coordinate conversion, state transition integrity, fuel/mass calculations, and persistence of strategic location after visiting a site.
