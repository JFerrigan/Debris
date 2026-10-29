# Four-step V2 tick integration — source only

User scope: implement four ordered stages and commit after each; do not launch Unity, compile, build or run tests. Source inspection is not runtime acceptance. Legacy remains default.

## Step 1

Accepted projected velocity output now updates transaction working endpoints; external force/torque integration; cold impulses; per-substep geometry rebasing; graph/finiteness rejection before motion application.

Next: Implement bounded prediction and surface-speed checks (step 2).

No compilation, tests, Unity, build or player run. All physical, resource and performance gates remain open.

## Step 2

GPU selects 4–16 substeps from surface speed and full-frame external acceleration; residual and integration share the selected GPU clock; prediction rejects nonfinite or greater-than-.125-cell surface motion; inactive fixed slots do not advance transaction stages.

Next: Connect position correction to predicted working poses (step 3).

No compilation, tests, Unity, build or player run. All physical, resource and performance gates remain open.
