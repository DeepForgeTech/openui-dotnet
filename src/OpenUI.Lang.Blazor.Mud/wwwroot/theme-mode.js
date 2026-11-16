// What ThemeModeState needs from the browser. A browser can refuse localStorage outright, so it is always behind a try.

export function start(key, owner) {
  const system = matchMedia("(prefers-color-scheme: dark)");

  // For as long as the page lasts, which a circuit doesn't outlive. Once it has ended there is nobody to tell.
  system.addEventListener("change", (event) => owner.invokeMethodAsync("SystemChangedAsync", event.matches).catch(() => {}));

  // Whatever else is stored is somebody else's value, which could be any size, and goes no further than here.
  let mode = null;
  try {
    const stored = localStorage.getItem(key);
    mode = stored === "light" || stored === "dark" || stored === "system" ? stored : null;
  } catch {
    // Treated as nothing stored.
  }

  return { mode, systemIsDark: system.matches };
}

export function write(key, mode) {
  try {
    localStorage.setItem(key, mode);
  } catch {
    // The choice lasts until a reload instead.
  }
}

export function paint(isDark, background) {
  const root = document.documentElement.style;
  root.colorScheme = isDark ? "dark" : "light";
  root.backgroundColor = background;
}
