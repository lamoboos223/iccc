// Example only. The platform generates scripts from the workload and reads
// ICCC_AUTH_TOKEN from the environment. Do not paste credentials here.
import http from "k6/http";
import { check } from "k6";

export const options = {
  scenarios: {
    payments: {
      executor: "ramping-arrival-rate",
      startRate: 1,
      timeUnit: "1s",
      preAllocatedVUs: 50,
      maxVUs: 200,
      stages: [
        { target: 100, duration: "30s" },
        { target: 100, duration: "60s" },
        { target: 0, duration: "15s" },
      ],
    },
  },
};

export default function () {
  const response = http.get(`${__ENV.ICCC_BASE_URL}/health`);
  check(response, { "status is 200": (item) => item.status === 200 });
}
