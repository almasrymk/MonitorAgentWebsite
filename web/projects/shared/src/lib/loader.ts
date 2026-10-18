import { computed, signal } from '@angular/core';

/** Data a page loads from the API, with the states every page shows: loading, failed (with retry) and ready. */
export class Loader<T> {
  private readonly state = signal<{ status: 'loading' } | { status: 'error'; error: unknown } | { status: 'ready'; value: T }>({
    status: 'loading',
  });

  readonly loading = computed(() => this.state().status === 'loading');
  readonly error = computed(() => {
    const state = this.state();
    return state.status === 'error' ? state.error : null;
  });
  readonly value = computed(() => {
    const state = this.state();
    return state.status === 'ready' ? state.value : undefined;
  });

  constructor(private readonly fetch: () => Promise<T>) {}

  /** Loads (or reloads). With quiet set, the current value stays on screen while it refreshes. */
  async load(quiet = false): Promise<void> {
    if (!quiet) this.state.set({ status: 'loading' });
    try {
      this.state.set({ status: 'ready', value: await this.fetch() });
    } catch (error) {
      this.state.set({ status: 'error', error });
    }
  }

  set(value: T): void {
    this.state.set({ status: 'ready', value });
  }
}
