export async function api<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      "X-Dev-Roles": "Engineer",
      "X-Dev-Teams": "11111111-1111-1111-1111-111111111111",
      ...(init?.headers ?? {}),
    },
  });
  if (!response.ok) {
    const detail = (await response.text()).trim();
    throw new Error(detail || `The calculator returned ${response.status}.`);
  }
  const text = await response.text();
  if (!text) return undefined as T;
  const type = response.headers.get("content-type") ?? "";
  return (type.includes("json") ? JSON.parse(text) : text) as T;
}
