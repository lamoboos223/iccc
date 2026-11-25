# HTTP API

Development authentication is a header, not a session. Send `X-Dev-Roles` and `X-Dev-Teams`.

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/dashboard` | Service, approval, and calculation counts |
| GET | `/api/services` | Services visible to the caller's teams |
| POST | `/api/services` | Register a service |
| GET | `/api/services/{id}/architecture` | Components, endpoints, and dependencies |
| POST | `/api/services/{id}/dependencies` | Add a dependency |
| POST | `/api/services/{id}/environments` | Add an environment. Production cannot be load-test approved |
| GET | `/api/workload-presets` | 1K, 5K, 10K, 50K, and 100K requests per second |
| POST | `/api/workloads` | Validate and store a workload |
| POST | `/api/load-tests/validate` | Run the safety guard. Does not start a test |
| POST | `/api/load-tests/scripts` | Generate a k6 script |
| POST | `/api/capacity/calculations` | Run formula 2026.1 on a measurement. The body is input, not a recommendation |
| POST | `/api/cost/estimates` | Price quantities from a catalog |
| POST | `/api/scenarios/optimize` | Choose the lowest-cost measured configuration that meets the SLA |
| GET | `/api/catalog` | SKUs and dated prices |
| POST | `/api/requests/{id}/transition` | Move Draft, Submitted, Testing, Analysis, Pending Approval, Approved, or Rejected |
| GET | `/api/requests/{id}/report.md` | Evidence report |
| POST | `/api/assistant/messages` | Plan tools and explain a stored calculation |

OpenAPI is mapped at `/openapi/v1.json` in Development.
