import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';

const serviceId = '44444444-4444-4444-4444-444444444444';

interface Line {
  component: string;
  metric: string;
  value: number | null;
  unit: string;
  evidence: string;
  limitation?: string | null;
}

interface Calculation {
  formulaVersion: string;
  status: string;
  approvableAsValidated: boolean;
  lines: Line[];
  limitations: string[];
  assumptions: string[];
}

interface ResourceRow {
  name: string;
  quantity: string;
}

interface FormState {
  targetRps: string;
  instances: string;
  vcpu: string;
  memory: string;
  memoryUnit: 'MB' | 'GB';
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
}

@Component({
  selector: 'app-root',
  imports: [FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  private readonly http = inject(HttpClient);
  private readonly feedback = viewChild<ElementRef<HTMLElement>>('feedback');

  protected readonly pending = signal(false);
  protected readonly error = signal('');
  protected readonly result = signal<Calculation | null>(null);
  protected readonly form: FormState = {
    targetRps: '10000',
    instances: '1',
    vcpu: '1',
    memory: '1024',
    memoryUnit: 'MB',
    storageGb: '1',
    sustainedRps: '',
    durationSeconds: '60',
    cpuPercent: '',
    memoryPercent: '',
    errorPercent: '0',
    errorLimitPercent: '1',
    p95Ms: '',
    p95LimitMs: '',
    generatorSaturated: false,
  };

  protected get memoryGb(): number {
    const amount = Number(this.form.memory);
    return this.form.memoryUnit === 'MB' ? amount / 1024 : amount;
  }

  protected resources(): ResourceRow[] {
    const total = this.quantity('total-instances');
    const storageEach = Number(this.form.storageGb);
    return [
      { name: 'Serving instances', quantity: this.quantity('serving-instances') },
      { name: 'Spare instances', quantity: this.quantity('availability-spare-instances') },
      { name: 'Total instances', quantity: total },
      { name: 'vCPU', quantity: this.quantity('serving-vcpu') },
      { name: 'Memory (GB)', quantity: this.quantity('serving-memory-gb') },
      { name: 'Storage (GB)', quantity: total == null || !Number.isFinite(storageEach) ? null : total * storageEach },
    ].map((row) => ({ name: row.name, quantity: row.quantity == null ? '—' : this.trim(row.quantity) }));
  }

  private quantity(metric: string): number | null {
    const value = this.result()?.lines.find((item) => item.component === 'application' && item.metric === metric)?.value;
    return value == null ? null : value;
  }

  protected calculate(): void {
    this.error.set('');
    this.result.set(null);
    const targetRps = Number(this.form.targetRps);
    const instances = Number(this.form.instances);
    const vcpu = Number(this.form.vcpu);
    const storageGb = Number(this.form.storageGb);
    const sustainedRps = Number(this.form.sustainedRps);
    const durationSeconds = Number(this.form.durationSeconds);
    const cpuPercent = Number(this.form.cpuPercent);
    const memoryPercent = Number(this.form.memoryPercent);
    const errorPercent = Number(this.form.errorPercent);
    const errorLimitPercent = Number(this.form.errorLimitPercent);
    const p95Ms = optionalNumber(this.form.p95Ms);
    const p95LimitMs = optionalNumber(this.form.p95LimitMs);
    const problem = validate({
      targetRps,
      instances,
      vcpu,
      memoryGb: this.memoryGb,
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
      this.error.set(problem);
      this.scrollToFeedback();
      return;
    }

    this.pending.set(true);
    this.scrollToFeedback();
    this.http
      .post<{ result: Calculation }>('/api/capacity/calculations', {
        serviceId,
        input: {
          workload: { metric: 'RequestsPerSecond', targetValue: targetRps },
          sla: {
            maxErrorRate: errorLimitPercent / 100,
            maxP95LatencyMs: p95LimitMs,
          },
          observation: {
            achievedRps: sustainedRps,
            errorRate: errorPercent / 100,
            p95LatencyMs: p95Ms,
            generatorSaturated: this.form.generatorSaturated,
            sustainDuration: clock(durationSeconds),
            observedAtUtc: new Date().toISOString(),
          },
          application: {
            instanceCount: instances,
            vcpuPerInstance: vcpu,
            memoryGbPerInstance: this.memoryGb,
            cpuUtilization: cpuPercent / 100,
            memoryUtilization: memoryPercent / 100,
          },
        },
      }, {
        headers: new HttpHeaders({
          'X-Dev-Roles': 'Engineer',
          'X-Dev-Teams': '11111111-1111-1111-1111-111111111111',
        }),
      })
      .subscribe({
        next: (payload) => {
          this.pending.set(false);
          if (!payload?.result?.lines) {
            this.error.set('The calculator returned a response this page could not read.');
          } else {
            this.result.set(payload.result);
          }
          this.scrollToFeedback();
        },
        error: (reason: unknown) => {
          this.pending.set(false);
          this.error.set(message(reason));
          this.scrollToFeedback();
        },
      });
  }

  private trim(value: number): string {
    return Number(value.toFixed(2)).toString();
  }

  private scrollToFeedback(): void {
    queueMicrotask(() => this.feedback()?.nativeElement.scrollIntoView({ behavior: 'smooth', block: 'nearest' }));
  }
}

function optionalNumber(value: string): number | null {
  const trimmed = value.trim();
  if (!trimmed) return null;
  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
}

function clock(seconds: number): string {
  const whole = Math.floor(seconds);
  const hh = Math.floor(whole / 3600);
  const mm = Math.floor((whole % 3600) / 60);
  const ss = whole % 60;
  return [hh, mm, ss].map((part) => String(part).padStart(2, '0')).join(':');
}

function message(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const detail = typeof error.error === 'string' ? error.error.trim() : '';
    return detail || `The calculator returned ${error.status}.`;
  }
  return 'The calculation could not be completed.';
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
}): string {
  if (!Number.isFinite(input.targetRps) || input.targetRps <= 0) return 'Enter the target requests per second.';
  if (!Number.isInteger(input.instances) || input.instances < 1) return 'Enter how many instances were in the test.';
  if (!Number.isFinite(input.vcpu) || input.vcpu <= 0) return 'Enter the vCPU on each tested instance.';
  if (!Number.isFinite(input.memoryGb) || input.memoryGb <= 0) return 'Enter the memory on each tested instance.';
  if (!Number.isFinite(input.storageGb) || input.storageGb < 0) return 'Enter the storage on each tested instance.';
  if (!Number.isFinite(input.sustainedRps) || input.sustainedRps <= 0) return 'Enter the requests per second the test sustained.';
  if (!Number.isFinite(input.durationSeconds) || input.durationSeconds < 60) return 'The test has to have held that rate for at least 60 seconds.';
  if (!Number.isFinite(input.cpuPercent) || input.cpuPercent < 0 || input.cpuPercent > 100) return 'Enter CPU use from 0 to 100 percent.';
  if (!Number.isFinite(input.memoryPercent) || input.memoryPercent < 0 || input.memoryPercent > 100) return 'Enter memory use from 0 to 100 percent.';
  if (!Number.isFinite(input.errorPercent) || input.errorPercent < 0 || input.errorPercent > 100) return 'Enter observed errors from 0 to 100 percent.';
  if (!Number.isFinite(input.errorLimitPercent) || input.errorLimitPercent < 0 || input.errorLimitPercent > 100) return 'Enter the error limit from 0 to 100 percent.';
  if (input.p95LimitMs != null && input.p95Ms == null) return 'Enter the p95 latency measured during the test.';
  return '';
}
