# Architecture

The system is a modular monolith. Projects reference each other only in the direction below.

```text
backend/src/Domain
backend/src/Capacity          (no project references)
backend/src/Costing        -> Capacity
backend/src/Application    -> Domain, Capacity, Costing
backend/src/Infrastructure -> Application, Domain
backend/src/Api            -> Application, Infrastructure, Domain, Capacity, Costing
```

`Iccc.Capacity` calculates quantities from measurements. `Iccc.Costing` prices quantities from a catalog and compares configurations. Neither project references ASP.NET Core, Entity Framework, or an LLM client.

## Evidence

Every capacity line carries one of:

| Evidence | Meaning |
| --- | --- |
| Measured | Observed in the test |
| Calculated | Derived from measured inputs by formula version 2026.1, inside the tested operating point |
| Extrapolated | The target or the instance count goes beyond what was tested, including linear CPU relief and untested SKUs |
| Assumption | Policy input such as headroom, N+1 spare capacity, retention, or an availability target |
| Insufficient | The engine refused to produce a number |

`ApprovableAsValidated` is true only when the serving instance count is calculated from a measurement that met the SLA, stayed within the utilization caps, and did not exceed the tested rate. An approver can still accept an extrapolated result explicitly. Invalid measurements and insufficient evidence cannot be approved.

The engine never increases throughput because CPU was under the cap. Spare headroom is reported as unused, not as extra requests per second. Instance size stays on the tested vCPU and memory. A larger SKU is not inferred.

## Formula version 2026.1

Serving instances:

```text
ratedPerInstance = (sustainableRps / testedInstances) * utilizationDerate
derate = min(1, cpuCap / observedCpu, memoryCap / observedMemory)
serving = ceil(targetRps / ratedPerInstance * overhead(doublings) * headroom)
overhead = (1 + overheadPerDoubling) ^ log2(raw / testedInstances)   when raw > tested
total = serving + spare
spare = 0 | 1 (N+1) | serving (active-active)
```

If `target / sustainable` is above the hard extrapolation ratio (default 5), serving instances are omitted. A saturated generator returns `InvalidMeasurement` and no instance count. An SLA miss without a lower compliant rate returns `InsufficientEvidence`.

Database, cache, broker, worker, and network lines use the same evidence rules. Cache memory uses the measured working set, not the request rate. A dependency past its cap blocks application scale-out.

## Load tests

`LoadTestGuard` rejects production, hosts outside the environment allowlist, cloud metadata addresses, rates or durations above the environment ceiling, high rates without explicit authorization, and scripts that contain inline secrets. k6 scripts read `ICCC_AUTH_TOKEN` from the environment. The k6 engine refuses a rate above the single-generator ceiling instead of pretending one machine produced it. Dropped k6 iterations mark the generator as saturated.

## Assistant

`AssistantPlanner` chooses tools. It does not calculate. When a stored calculation exists, `RecommendationNarrator` writes the numbers from that result. `NarrativeGuard` removes sentences whose numbers are not in the engine output.

## Persistence

`PlatformDbContext` stores services, workloads, executions, telemetry snapshots, catalog prices, immutable calculation input and result JSON, scenarios, approval history, assistant messages, and audit entries. Calculation JSON keeps the formula version and the inputs that produced it. Catalog prices have validity dates. The development host creates the schema on startup until a migration is added; `Migrate()` is used once migrations exist.

## What is intentionally unfinished

OIDC is represented by `Auth:Mode`. Development headers work locally. Production with development auth fails startup. A real OIDC authority is not wired yet. Distributed k6, a live Prometheus scrape during a run, and PDF bytes are not complete. Reports are Markdown, CSV, and HTML. The background worker that shells out to k6 is off unless `LoadTesting:Worker` is true.
