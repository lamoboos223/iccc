import { FormEvent, useEffect, useState } from "react";
import { NavLink, Route, Routes } from "react-router-dom";
import { api } from "./api";

const serviceId = "44444444-4444-4444-4444-444444444444";
const requestId = "99999999-9999-9999-9999-999999999999";

type Line = {
  component: string;
  metric: string;
  value: number | null;
  unit: string;
  evidence: string;
  confidence: number;
  formulaId: string;
  limitation?: string | null;
};

const links = [
  ["/", "Dashboard"],
  ["/services", "Service registry"],
  ["/workloads", "Workload designer"],
  ["/load-tests", "Load tests"],
  ["/analysis", "Performance analysis"],
  ["/calculator", "Infrastructure calculator"],
  ["/scenarios", "Scenario comparison"],
  ["/assistant", "AI assistant"],
  ["/catalog", "Infrastructure catalog"],
  ["/approvals", "Approval center"],
  ["/reports", "Reports"],
];

export function App() {
  return (
    <div className="shell">
      <nav>
        <strong>Certification</strong>
        {links.map(([to, label]) => (
          <NavLink key={to} to={to} end={to === "/"}>
            {label}
          </NavLink>
        ))}
      </nav>
      <main>
        <Routes>
          <Route path="/" element={<Dashboard />} />
          <Route path="/services" element={<Services />} />
          <Route path="/workloads" element={<Workloads />} />
          <Route path="/load-tests" element={<LoadTests />} />
          <Route path="/analysis" element={<Analysis />} />
          <Route path="/calculator" element={<Calculator />} />
          <Route path="/scenarios" element={<Scenarios />} />
          <Route path="/assistant" element={<Assistant />} />
          <Route path="/catalog" element={<Catalog />} />
          <Route path="/approvals" element={<Approvals />} />
          <Route path="/reports" element={<Reports />} />
        </Routes>
      </main>
    </div>
  );
}

function Evidence({ kind }: { kind: string }) {
  return <span className={`badge ${kind}`}>{kind}</span>;
}

function Lines({ lines }: { lines: Line[] }) {
  return (
    <table>
      <thead>
        <tr>
          <th>Component</th>
          <th>Metric</th>
          <th>Value</th>
          <th>Evidence</th>
          <th>Formula</th>
        </tr>
      </thead>
      <tbody>
        {lines.map((line) => (
          <tr key={`${line.component}-${line.metric}`}>
            <td>{line.component}</td>
            <td>{line.metric}</td>
            <td>{line.value ?? "—"} {line.unit}</td>
            <td><Evidence kind={line.evidence} /> {line.limitation}</td>
            <td>{line.formulaId}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function Dashboard() {
  const [data, setData] = useState<Record<string, number> | null>(null);
  const [error, setError] = useState("");
  useEffect(() => {
    api<Record<string, number>>("/api/dashboard").then(setData).catch((reason: Error) => setError(reason.message));
  }, []);
  return (
    <>
      <h1>Dashboard</h1>
      <p className="note">Counts come from the platform database. A calculation that is not validated is counted separately from one the engine marked approvable.</p>
      {error && <p>{error}</p>}
      {data && (
        <div className="grid">
          {Object.entries(data).map(([key, value]) => (
            <div className="stat" key={key}><span>{value}</span>{key}</div>
          ))}
        </div>
      )}
    </>
  );
}

function Services() {
  const [rows, setRows] = useState<Array<{ id: string; name: string; runtime: string; deploymentModel: string }>>([]);
  const [graph, setGraph] = useState<string>("");
  useEffect(() => {
    api<typeof rows>("/api/services").then(setRows).catch(() => setRows([]));
    api<unknown>(`/api/services/${serviceId}/architecture`).then((value) => setGraph(JSON.stringify(value, null, 2))).catch(() => setGraph(""));
  }, []);
  return (
    <>
      <h1>Service registry</h1>
      <p className="note">The seeded Payments API is a demonstration fixture. Its dependencies are SQL Server and Redis.</p>
      <table>
        <thead><tr><th>Name</th><th>Runtime</th><th>Deployment</th></tr></thead>
        <tbody>{rows.map((row) => <tr key={row.id}><td>{row.name}</td><td>{row.runtime}</td><td>{row.deploymentModel}</td></tr>)}</tbody>
      </table>
      <h2>Architecture</h2>
      <pre>{graph}</pre>
    </>
  );
}

function Workloads() {
  const [presets, setPresets] = useState<Array<{ name: string; targetValue: number; note: string }>>([]);
  useEffect(() => { api<typeof presets>("/api/workload-presets").then(setPresets).catch(() => setPresets([])); }, []);
  return (
    <>
      <h1>Workload designer</h1>
      <p className="banner">Requests per second and concurrent users are different measures. Presets below are requests per second. The sizing engine refuses to turn a user count into an instance count.</p>
      <table>
        <thead><tr><th>Preset</th><th>Requests per second</th><th>Note</th></tr></thead>
        <tbody>{presets.map((preset) => <tr key={preset.name}><td>{preset.name}</td><td>{preset.targetValue}</td><td>{preset.note}</td></tr>)}</tbody>
      </table>
    </>
  );
}

function LoadTests() {
  const [result, setResult] = useState("");
  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const body = {
      environmentKind: form.get("kind"),
      loadTestAllowed: true,
      targetUrl: form.get("url"),
      allowlistHosts: ["payments-test.internal"],
      requestsPerSecond: Number(form.get("rps")),
      durationSeconds: 120,
      maxRequestsPerSecond: 20000,
      maxDurationSeconds: 1800,
      highLoadAuthorizationThreshold: 1000,
      explicitlyAuthorized: form.get("authorized") === "on",
    };
    const response = await fetch("/api/load-tests/validate", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
    setResult(await response.text());
  }
  return (
    <>
      <h1>Load tests</h1>
      <p className="note">Validation runs before any generator starts. Production, unknown hosts, and high rates without authorization are rejected.</p>
      <form onSubmit={onSubmit}>
        <label>Environment <select name="kind"><option>Test</option><option>Production</option></select></label>
        <label>Target URL <input name="url" defaultValue="https://payments-test.internal/api" /></label>
        <label>Requests per second <input name="rps" type="number" defaultValue={10000} /></label>
        <label><input name="authorized" type="checkbox" /> Explicitly authorized</label>
        <button type="submit">Validate</button>
      </form>
      <pre>{result}</pre>
    </>
  );
}

function Analysis() {
  const [lines, setLines] = useState<Line[]>([]);
  const [status, setStatus] = useState("");
  useEffect(() => {
    api<{ result: { status: string; lines: Line[] } }>("/api/capacity/calculations/88888888-8888-8888-8888-888888888888")
      .then((payload) => { setStatus(payload.result.status); setLines(payload.result.lines); })
      .catch(() => setStatus("Calculation not available"));
  }, []);
  return (
    <>
      <h1>Performance analysis</h1>
      <p className="banner">Seeded status: {status}. Values keep the evidence class returned by formula 2026.1.</p>
      <Lines lines={lines} />
    </>
  );
}

function Calculator() {
  return (
    <>
      <h1>Infrastructure calculator</h1>
      <p className="note">The calculator on the server is the capacity library. This screen shows the stored result for the seeded 10K rps target against a 2.5K rps measurement. That gap is extrapolation, not a measured fleet size.</p>
      <Analysis />
    </>
  );
}

function Scenarios() {
  const [text, setText] = useState("Run a comparison to see measured and untested rows.");
  async function compare() {
    const payload = await api<{ result: { explanation: string; selected: { name: string } | null } }>("/api/scenarios/optimize", {
      method: "POST",
      body: JSON.stringify({
        serviceId,
        request: {
          targetRps: 10000,
          maxP95LatencyMs: 200,
          measuredRpsPerInstance: 1250,
          measuredP95Ms: 95,
          measuredSku: "vm-4c8g",
          measuredVcpu: 4,
          measuredMemoryGb: 8,
          testedInstanceCount: 2,
          candidates: [
            { name: "measured-small", sku: "vm-4c8g", instanceCount: 2, monthlyCostPerInstance: 180, hasDirectMeasurement: true, measuredAggregateRps: 2500, measuredP95Ms: 95, measuredErrorRate: 0.002, vcpu: 4, memoryGb: 8 },
            { name: "cheaper-untested", sku: "vm-2c4g", instanceCount: 4, monthlyCostPerInstance: 90, vcpu: 2, memoryGb: 4 },
          ],
        },
      }),
    });
    setText(`${payload.result.explanation}\nSelected: ${payload.result.selected?.name ?? "none"}. The cheaper untested SKU is not treated as SLA-compliant.`);
  }
  return (
    <>
      <h1>Scenario comparison</h1>
      <button type="button" onClick={compare}>Compare seeded candidates</button>
      <pre>{text}</pre>
    </>
  );
}

function Assistant() {
  const [text, setText] = useState("");
  async function ask(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const payload = await api<{ message: string; tools: string[]; rule: string }>("/api/assistant/messages", {
      method: "POST",
      body: JSON.stringify({ serviceId, message: form.get("message"), authorizeTests: false }),
    });
    setText(`${payload.rule}\n\nTools: ${payload.tools.join(", ")}\n\n${payload.message}`);
  }
  return (
    <>
      <h1>AI assistant</h1>
      <form onSubmit={ask}>
        <textarea name="message" rows={4} defaultValue="Estimate the required infrastructure for 100K RPS. I think we need 128 vCPU." />
        <button type="submit">Ask</button>
      </form>
      <pre>{text}</pre>
    </>
  );
}

function Catalog() {
  const [rows, setRows] = useState<Array<{ sku: string; name: string; vcpu: number; memoryGb: number; catalogVersion: string; prices: Array<{ component: string; unitPrice: number; currency?: string }> }>>([]);
  useEffect(() => { api<typeof rows>("/api/catalog").then(setRows).catch(() => setRows([])); }, []);
  return (
    <>
      <h1>Infrastructure catalog</h1>
      <p className="note">Prices are catalog rows with validity dates. The capacity engine does not contain them.</p>
      <table>
        <thead><tr><th>SKU</th><th>Spec</th><th>Version</th><th>Prices</th></tr></thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.sku}>
              <td>{row.sku}</td>
              <td>{row.vcpu} vCPU / {row.memoryGb} GB</td>
              <td>{row.catalogVersion}</td>
              <td>{row.prices.map((price) => `${price.component} ${price.unitPrice}`).join(", ")}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}

function Approvals() {
  const [rows, setRows] = useState<Array<{ id: string; title: string; status: string }>>([]);
  const [message, setMessage] = useState("");
  function reload() { api<typeof rows>("/api/requests").then(setRows).catch(() => setRows([])); }
  useEffect(reload, []);
  async function decide(risk: boolean) {
    localStorage.setItem("iccc-role", "Approver");
    try {
      await api(`/api/requests/${requestId}/transition`, {
        method: "POST",
        body: JSON.stringify({ to: "Approved", riskAccepted: risk, note: risk ? "Risk accepted for extrapolation." : "Approve as validated." }),
      });
      setMessage(risk ? "Approved with an explicit risk acceptance. This is not a validated measurement." : "Approved.");
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Rejected");
    }
    reload();
  }
  return (
    <>
      <h1>Approval center</h1>
      <p className="banner">The seeded 10K rps recommendation is extrapolated. Approving it as validated is rejected. Approving it requires accepting that risk.</p>
      <button type="button" onClick={() => decide(false)}>Approve as validated</button>{" "}
      <button type="button" onClick={() => decide(true)}>Accept extrapolation risk</button>
      <pre>{message}</pre>
      <table>
        <thead><tr><th>Title</th><th>Status</th></tr></thead>
        <tbody>{rows.map((row) => <tr key={row.id}><td>{row.title}</td><td>{row.status}</td></tr>)}</tbody>
      </table>
    </>
  );
}

function Reports() {
  const [markdown, setMarkdown] = useState("");
  useEffect(() => { api<string>(`/api/requests/${requestId}/report.md`).then(setMarkdown).catch(() => setMarkdown("Report unavailable.")); }, []);
  return (
    <>
      <h1>Reports</h1>
      <p><a href={`/api/requests/${requestId}/report.csv`}>Download CSV</a></p>
      <pre>{markdown}</pre>
    </>
  );
}
