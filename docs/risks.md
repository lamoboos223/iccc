# Technical risks

## Distributed load generation

One k6 process cannot honestly produce 50K or 100K requests per second. The engine refuses rates above `LoadTesting:SingleGeneratorMaxRps` and records the refusal. A future `ILoadTestEngine` implementation can fan out to several generators. Until that exists, those rates are not executed.

## Telemetry correlation

Prometheus queries are bounded by the execution start and finish. An empty result is stored as a gap. The engine does not substitute zero. The demonstration fixture is tagged `DemonstrationFixture` and the report says so.

## Extrapolation

Horizontal scale-out uses a configurable doubling overhead because linear scaling is not assumed. Past the hard ratio the engine returns insufficient evidence. Latency for an untested instance count or SKU is not claimed, so the optimizer selects only configurations that were measured and met the SLA.

## Generator saturation

k6 `dropped_iterations` marks the run invalid. Reaching the target rate is not sufficient when the SLA fails.

## Smart App Control

Windows Smart App Control on the development machine blocked unsigned build outputs with `0x800711C7`. That is an environment control, not a product defect. Builds and tests need a developer environment that allows locally compiled assemblies.

## Secrets

Scripts and reports must not contain tokens. The guard rejects inline secret assignments. The k6 process receives the token only as an environment variable, and the worker does not write that value into the script file or the audit detail.
