# Dependencies and licenses

| Component | Role | License |
| --- | --- | --- |
| .NET 10 | API and libraries | MIT |
| Entity Framework Core 10 / Npgsql 10 | PostgreSQL persistence | MIT |
| SQLite via Microsoft.EntityFrameworkCore.Sqlite 10 | Local development and tests only | SQLite public domain blessing; native bundle has advisory GHSA-2m69-gcr7-jv3q. PostgreSQL is the server database. |
| Grafana k6 | Load generator, invoked as an external process | AGPL-3.0. The platform does not link k6. Operators install it separately. |
| Prometheus | Telemetry query API, external process | Apache-2.0 |
| OpenTelemetry .NET 1.15 | API traces and OTLP export | Apache-2.0 |
| React, Vite, React Router | Web UI | MIT |
| xUnit | Tests | Apache-2.0 |

EF Core Design brings System.Security.Cryptography.Xml and Pkcs 9.0.0, which have published advisories. Those packages are design-time assets (`PrivateAssets=all`) and are not referenced by the capacity engine.

No price list is compiled into the capacity library. Demonstration prices live in the seed catalog and are valid only for the dates stored on each row.
