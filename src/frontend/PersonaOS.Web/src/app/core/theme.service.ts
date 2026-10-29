import { Injectable, computed, effect, signal } from '@angular/core';

/** Must match the early script in index.html, which applies the choice before first paint. */
const STORAGE_KEY = 'personaos.theme';

type Scheme = 'light' | 'dark';

/**
 * Light or dark. Until the user picks one the app follows the device; once they pick, the choice
 * sticks on this browser. The choice is an attribute on <html>, which styles.scss reads.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly query = window.matchMedia('(prefers-color-scheme: dark)');
  private readonly systemDark = signal(this.query.matches);
  private readonly choice = signal<Scheme | null>(readChoice());

  readonly dark = computed(() => (this.choice() ?? (this.systemDark() ? 'dark' : 'light')) === 'dark');

  constructor() {
    this.query.addEventListener('change', e => this.systemDark.set(e.matches));

    effect(() => {
      const choice = this.choice();
      const root = document.documentElement;
      if (choice) root.dataset['theme'] = choice;
      else delete root.dataset['theme'];
    });

    // The browser's bar follows the scheme in use, the user's pick included, which the media
    // queries on the meta tags in index.html cannot know.
    effect(() => {
      this.dark();
      const surface = getComputedStyle(document.body).backgroundColor;
      if (!surface) return;
      for (const meta of Array.from(document.querySelectorAll<HTMLMetaElement>('meta[name="theme-color"]'))) {
        meta.content = surface;
      }
    });
  }

  toggle(): void {
    const next: Scheme = this.dark() ? 'light' : 'dark';
    this.choice.set(next);
    try {
      localStorage.setItem(STORAGE_KEY, next);
    } catch {
      // Private windows can refuse storage; the choice still holds for this visit.
    }
  }
}

function readChoice(): Scheme | null {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    return stored === 'light' || stored === 'dark' ? stored : null;
  } catch {
    return null;
  }
}
