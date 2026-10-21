// Applies the user's colour scheme to the page. "system" removes the override, so the stylesheet follows the
// operating system (prefers-color-scheme); "light" and "dark" force a scheme.
export function applyTheme(theme) {
  const root = document.documentElement;
  if (theme === 'light' || theme === 'dark') {
    root.dataset.theme = theme;
  } else {
    delete root.dataset.theme;
  }
}
