import { FormEvent, useEffect, useRef, useState } from "react";
import { api } from "./api";

const serviceId = "44444444-4444-4444-4444-444444444444";

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

type Calculation = {
  formulaVersion: string;
  status: string;
  approvableAsValidated: boolean;
  lines: Line[];
  limitations: string[];
  assumptions: string[];
};

type FormState = {
  targetRps: string;
  instances: string;
  vcpu: string;
  memory: string;
  memoryUnit: "MB" | "GB";
  storageGb: string;
  sustainedRps: string;
  durationSeconds: string;
  cpuPercent: string;
  memoryPercent: string;
  errorPercent: string;
  errorLimitPercent: string;
  p95Ms: string;
  p95LimitMs: string;
  generatorSaturated: boolean;
};

const initial: FormState = {
  targetRps: "10000",
  instances: "1",
  vcpu: "1",
  memory: "1024",
  memoryUnit: "MB",
  storageGb: "1",
  sustainedRps: "",
  durationSeconds: "60",
  cpuPercent: "",
  memoryPercent: "",
  errorPercent: "0",
  errorLimitPercent: "1",
  p95Ms: "",
  p95LimitMs: "",
  generatorSaturated: false,
};

export function App() {
  const [form, setForm] = useState<FormState>(initial);
  const [error, setError] = useState("");
  const [pending, setPending] = useState(false);
  const [result, setResult] = useState<Calculation | null>(null);
  const feedback = useRef<HTMLDivElement>(null);

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((current) => ({ ...current, [key]: value }));
  }

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError("");
    setResult(null);

    const targetRps = number(form.targetRps);
    const instances = number(form.instances);
    const vcpu = number(form.vcpu);
    const memoryAmount = number(form.memory);
    const storageGb = number(form.storageGb);
    const sustainedRps = number(form.sustainedRps);
    const durationSeconds = number(form.durationSeconds);
    const cpuPercent = number(form.cpuPercent);
    const memoryPercent = number(form.memoryPercent);
    const errorPercent = number(form.errorPercent);
    const errorLimitPercent = number(form.errorLimitPercent);
    const p95Ms = optionalNumber(form.p95Ms);
    const p95LimitMs = optionalNumber(form.p95LimitMs);
    const memoryGb = form.memoryUnit === "MB" ? memoryAmount / 1024 : memoryAmount;

    const problem = validate({
      targetRps,
      instances,
      vcpu,
      memoryGb,
      storageGb,
      sustainedRps,
      durationSeconds,
      cpuPercent,
      memoryPercent,
      errorPercent,
      errorLimitPercent,
      p95Ms,
      p95LimitMs,
    });
    if (problem) {
      setError(problem);
      return;
    }

    setPending(true);
    feedback.current?.scrollIntoView({ behavior: "smooth", block: "start" });
    try {
      const payload = await api<{ result: Calculation }>("/api/capacity/calculations", {
        method: "POST",
        body: JSON.stringify({
          serviceId,
          input: {
            workload: { metric: "RequestsPerSecond", targetValue: targetRps },
            sla: {
              maxErrorRate: errorLimitPercent / 100,
              maxP95LatencyMs: p95LimitMs,
            },
            observation: {
              achievedRps: sustainedRps,
              errorRate: errorPercent / 100,
              p95LatencyMs: p95Ms,
              generatorSaturated: form.generatorSaturated,
              sustainDuration: clock(durationSeconds),
              observedAtUtc: new Date().toISOString(),
            },
            application: {
              instanceCount: instances,
              vcpuPerInstance: vcpu,
              memoryGbPerInstance: memoryGb,
              cpuUtilization: cpuPercent / 100,
              memoryUtilization: memoryPercent / 100,
            },
          },
        }),
      });
      setResult(payload.result);
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "The calculation could not be completed.");
    } finally {
      setPending(false);
    }
  }

  useEffect(() => {
    if (error || result) {
      feedback.current?.scrollIntoView({ behavior: "smooth", block: "start" });
    }
  }, [error, result]);

  const memoryGb = form.memoryUnit === "MB" ? number(form.memory) / 1024 : number(form.memory);

  return (
    <main>
      <h1>Size from a load test</h1>
      <p className="note">
        Enter the machine you tested and the rate that machine held. The result is how many copies of that machine the target needs.
      </p>
      <div ref={feedback}>
        {error && <p className="banner">{error}</p>}
        {result && (
          <Result
            result={result}
            machine={{
              instances: number(form.instances),
              vcpu: number(form.vcpu),
              memoryGb,
              storageGb: number(form.storageGb),
            }}
          />
        )}
      </div>
      <form onSubmit={onSubmit}>
        <fieldset>
          <legend>Target</legend>
          <label>
            Requests per second
            <input value={form.targetRps} inputMode="decimal" onChange={(event) => set("targetRps", event.target.value)} />
          </label>
        </fieldset>
        <fieldset>
          <legend>Machine used in the test</legend>
          <div className="fields">
            <label>
              Instances
              <input value={form.instances} inputMode="numeric" onChange={(event) => set("instances", event.target.value)} />
            </label>
            <label>
              vCPU per instance
              <input value={form.vcpu} inputMode="decimal" onChange={(event) => set("vcpu", event.target.value)} />
            </label>
            <label>
              Memory
              <span className="split">
                <input value={form.memory} inputMode="decimal" onChange={(event) => set("memory", event.target.value)} />
                <select value={form.memoryUnit} onChange={(event) => set("memoryUnit", event.target.value as FormState["memoryUnit"])}>
                  <option value="MB">MB</option>
                  <option value="GB">GB</option>
                </select>
              </span>
            </label>
            <label>
              Storage (GB)
              <input value={form.storageGb} inputMode="decimal" onChange={(event) => set("storageGb", event.target.value)} />
            </label>
          </div>
        </fieldset>
        <fieldset>
          <legend>What the test sustained</legend>
          <div className="fields">
            <label>
              Requests per second
              <input value={form.sustainedRps} inputMode="decimal" onChange={(event) => set("sustainedRps", event.target.value)} />
            </label>
            <label>
              Held for (seconds)
              <input value={form.durationSeconds} inputMode="numeric" onChange={(event) => set("durationSeconds", event.target.value)} />
            </label>
            <label>
              CPU use (%)
              <input value={form.cpuPercent} inputMode="decimal" onChange={(event) => set("cpuPercent", event.target.value)} />
            </label>
            <label>
              Memory use (%)
              <input value={form.memoryPercent} inputMode="decimal" onChange={(event) => set("memoryPercent", event.target.value)} />
            </label>
          </div>
          <details>
            <summary>Latency and errors</summary>
            <div className="fields">
              <label>
                Errors observed (%)
                <input value={form.errorPercent} inputMode="decimal" onChange={(event) => set("errorPercent", event.target.value)} />
              </label>
              <label>
                Error limit (%)
                <input value={form.errorLimitPercent} inputMode="decimal" onChange={(event) => set("errorLimitPercent", event.target.value)} />
              </label>
              <label>
                p95 latency (ms)
                <input value={form.p95Ms} inputMode="decimal" onChange={(event) => set("p95Ms", event.target.value)} />
              </label>
              <label>
                p95 limit (ms)
                <input value={form.p95LimitMs} inputMode="decimal" onChange={(event) => set("p95LimitMs", event.target.value)} />
              </label>
            </div>
            <label className="check">
              <input
                type="checkbox"
                checked={form.generatorSaturated}
                onChange={(event) => set("generatorSaturated", event.target.checked)}
              />
              The load generator was maxed out
            </label>
          </details>
        </fieldset>
        <button type="submit" disabled={pending}>{pending ? "Calculating" : "Calculate"}</button>
      </form>
    </main>
  );
}

function Result({
  result,
  machine,
}: {
  result: Calculation;
  machine: { instances: number; vcpu: number; memoryGb: number; storageGb: number };
}) {
  const serving = metric(result.lines, "serving-instances");
  const spare = metric(result.lines, "availability-spare-instances");
  const total = metric(result.lines, "total-instances");
  const vcpu = metric(result.lines, "serving-vcpu");
  const memory = metric(result.lines, "serving-memory-gb");
  const machineLabel = `${trim(machine.vcpu)} vCPU, ${trim(machine.memoryGb)} GB memory, ${trim(machine.storageGb)} GB storage`;

  return (
    <section className="result">
      <h2>Result</h2>
      {serving?.value != null && total?.value != null ? (
        <p className="answer">
          {trim(total.value)} machines like the tested one ({machineLabel}). {trim(serving.value)} serve traffic
          {spare?.value ? ` and ${trim(spare.value)} is spare` : ""}.
        </p>
      ) : (
        <p className="answer">No machine count was produced from this test.</p>
      )}
      <p>
        <span className={`badge ${serving?.evidence ?? "Insufficient"}`}>{serving?.evidence ?? result.status}</span>
        {" "}
        {serving?.value == null
          ? "There is no count to approve."
          : result.approvableAsValidated
            ? "This count can be approved as validated."
            : "Approval as a validated result needs a test at the target rate."}
      </p>
      {serving?.limitation && <p>{serving.limitation}</p>}
      <p>
        {vcpu?.value != null && memory?.value != null
          ? `Serving resources: ${trim(vcpu.value)} vCPU, ${trim(memory.value)} GB memory. `
          : ""}
        Storage stays {trim(machine.storageGb)} GB on each machine.
      </p>
      {result.limitations.length > 0 && (
        <ul>
          {result.limitations.map((item) => <li key={item}>{item}</li>)}
        </ul>
      )}
      {result.assumptions.length > 0 && (
        <ul className="assumptions">
          {result.assumptions.map((item) => <li key={item}>{item}</li>)}
        </ul>
      )}
      <p className="note">Formula {result.formulaVersion}. Status {result.status}.</p>
    </section>
  );
}

function metric(lines: Line[], name: string) {
  return lines.find((line) => line.component === "application" && line.metric === name);
}

function number(value: string) {
  return Number(value);
}

function optionalNumber(value: string) {
  const trimmed = value.trim();
  if (!trimmed) return null;
  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
}

function clock(seconds: number) {
  const whole = Math.floor(seconds);
  const hh = Math.floor(whole / 3600);
  const mm = Math.floor((whole % 3600) / 60);
  const ss = whole % 60;
  return [hh, mm, ss].map((part) => String(part).padStart(2, "0")).join(":");
}

function trim(value: number) {
  return Number(value.toFixed(2)).toString();
}

function validate(input: {
  targetRps: number;
  instances: number;
  vcpu: number;
  memoryGb: number;
  storageGb: number;
  sustainedRps: number;
  durationSeconds: number;
  cpuPercent: number;
  memoryPercent: number;
  errorPercent: number;
  errorLimitPercent: number;
  p95Ms: number | null;
  p95LimitMs: number | null;
}) {
  if (!Number.isFinite(input.targetRps) || input.targetRps <= 0) return "Enter the target requests per second.";
  if (!Number.isInteger(input.instances) || input.instances < 1) return "Enter how many instances were in the test.";
  if (!Number.isFinite(input.vcpu) || input.vcpu <= 0) return "Enter the vCPU on each tested instance.";
  if (!Number.isFinite(input.memoryGb) || input.memoryGb <= 0) return "Enter the memory on each tested instance.";
  if (!Number.isFinite(input.storageGb) || input.storageGb < 0) return "Enter the storage on each tested instance.";
  if (!Number.isFinite(input.sustainedRps) || input.sustainedRps <= 0) return "Enter the requests per second the test sustained.";
  if (!Number.isFinite(input.durationSeconds) || input.durationSeconds < 60) return "The test has to have held that rate for at least 60 seconds.";
  if (!Number.isFinite(input.cpuPercent) || input.cpuPercent < 0 || input.cpuPercent > 100) return "Enter CPU use from 0 to 100 percent.";
  if (!Number.isFinite(input.memoryPercent) || input.memoryPercent < 0 || input.memoryPercent > 100) return "Enter memory use from 0 to 100 percent.";
  if (!Number.isFinite(input.errorPercent) || input.errorPercent < 0 || input.errorPercent > 100) return "Enter observed errors from 0 to 100 percent.";
  if (!Number.isFinite(input.errorLimitPercent) || input.errorLimitPercent < 0 || input.errorLimitPercent > 100) return "Enter the error limit from 0 to 100 percent.";
  if (input.p95LimitMs != null && input.p95Ms == null) return "Enter the p95 latency measured during the test.";
  return "";
}
