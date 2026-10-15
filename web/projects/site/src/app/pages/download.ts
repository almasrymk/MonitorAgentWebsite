import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { Api, BytesPipe, I18n, Release, TranslatePipe, WhenPipe } from 'shared';
import { useSeo } from '../seo';

type State = { kind: 'loading' } | { kind: 'error' } | { kind: 'ready'; releases: Release[] };

@Component({
  selector: 'site-download',
  imports: [TranslatePipe, WhenPipe, BytesPipe],
  template: `
    <section class="section">
      <div class="container">
        <h1>{{ 'download.title' | t }}</h1>
        <p class="lead">{{ 'download.lead' | t }}</p>

        @switch (state().kind) {
          @case ('loading') {
            <div class="card" aria-busy="true"><div class="skeleton" style="height: 10rem"></div></div>
          }
          @case ('error') {
            <div class="alert" role="alert">{{ 'download.error' | t }}</div>
          }
          @case ('ready') {
            @if (latest(); as release) {
              <article class="card" aria-labelledby="latest-title">
                <p class="eyebrow">{{ 'download.latest' | t }}</p>
                <h2 id="latest-title">{{ 'download.version' | t: { version: release.version } }}</h2>
                <p class="muted">{{ 'download.released' | t: { date: (release.publishedAt | when) } }}</p>
                <div class="table-wrap">
                  <table>
                    <thead>
                      <tr>
                        <th scope="col">{{ 'download.platform' | t }}</th>
                        <th scope="col">{{ 'download.size' | t }}</th>
                        <th scope="col">{{ 'download.checksum' | t }}</th>
                        <th scope="col"><span class="visually-hidden">{{ 'download.file' | t }}</span></th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (pkg of release.packages; track pkg.platform) {
                        <tr>
                          <td>{{ pkg.platform }}</td>
                          <td>{{ pkg.size | bytes }}</td>
                          <td><code class="mono checksum">{{ pkg.sha256 }}</code></td>
                          <td>
                            <a class="btn" [href]="pkg.url" rel="noopener" [attr.aria-label]="'download.downloadFor' | t: { platform: pkg.platform }">{{ 'download.file' | t }}</a>
                          </td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
                @if (release.notes) {
                  <h3>{{ 'download.notes' | t }}</h3>
                  <p class="notes">{{ release.notes }}</p>
                }
                <p class="muted">{{ 'download.requirements' | t }}</p>
              </article>

              @if (earlier().length) {
                <h2 class="section-title">{{ 'download.history' | t }}</h2>
                @for (old of earlier(); track old.id) {
                  <details class="faq">
                    <summary>
                      {{ 'download.version' | t: { version: old.version } }} · {{ old.publishedAt | when }}
                      @if (old.channel !== 'stable') {
                        <span class="badge">{{ 'download.channel' | t: { channel: old.channel } }}</span>
                      }
                    </summary>
                    @if (old.notes) {
                      <p class="notes">{{ old.notes }}</p>
                    }
                  </details>
                }
              }
            } @else {
              <p class="card">{{ 'download.empty' | t }}</p>
            }
          }
        }
      </div>
    </section>
  `,
})
export class DownloadPage implements OnInit {
  private readonly api = inject(Api);
  protected readonly i18n = inject(I18n);
  protected readonly state = signal<State>({ kind: 'loading' });

  private readonly releases = computed(() => {
    const state = this.state();
    return state.kind === 'ready' ? state.releases : [];
  });

  /** The newest stable version: the one the agent is offered too. */
  protected readonly latest = computed(() => this.releases().find((r) => r.channel === 'stable') ?? null);
  protected readonly earlier = computed(() => this.releases().filter((r) => r !== this.latest()));

  constructor() {
    useSeo('download.title', 'download.lead');
  }

  async ngOnInit(): Promise<void> {
    try {
      const releases = await this.api.get<Release[]>('/api/v1/releases');
      const sorted = [...releases].sort((a, b) => (b.publishedAt ?? '').localeCompare(a.publishedAt ?? ''));
      this.state.set({ kind: 'ready', releases: sorted });
    } catch {
      this.state.set({ kind: 'error' });
    }
  }
}
