// The tools the program's Query() and Mutation() call. Each one is an endpoint of the sample API
// (samples/OpenUI.Lang.Samples.ChatApi/Weather): arguments become the query string or the body,
// the result is the response body, and an error response fails the call with its message.

async function request(method, path, { query, body } = {}) {
  const params = new URLSearchParams(
    Object.entries(query ?? {}).filter(([, value]) => value !== undefined && value !== null),
  );
  const url = params.size > 0 ? `${path}?${params}` : path;
  const response = await fetch(url, {
    method,
    headers: body ? { "Content-Type": "application/json" } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });
  const payload = await response.json().catch(() => ({}));
  if (!response.ok) {
    throw new Error(payload.error ?? `${method} ${path} failed with ${response.status}`);
  }
  return payload;
}

/**
 * Builds the `toolProvider` for `Renderer`: a map of async functions keyed by tool name.
 * `onCall` hears about every call, so the page can show what the program did.
 */
export function createWeatherTools(onCall) {
  const tool = (name, method, path, toRequest) => async (args) => {
    const call = { tool: name, method, path, args, at: new Date() };
    try {
      const result = await request(method, path, toRequest(args ?? {}));
      onCall({ ...call, ok: true });
      return result;
    } catch (error) {
      onCall({ ...call, ok: false, error: error.message });
      throw error;
    }
  };

  return {
    listLocations: tool("listLocations", "GET", "/api/locations", () => ({})),
    getForecast: tool("getForecast", "GET", "/api/forecast", ({ location, days }) => ({ query: { location, days } })),
    listSprayJobs: tool("listSprayJobs", "GET", "/api/spray-jobs", ({ status }) => ({ query: { status } })),
    scheduleSprayJob: tool("scheduleSprayJob", "POST", "/api/spray-jobs", ({ location, product, date }) => ({
      body: { location, product, date },
    })),
  };
}
