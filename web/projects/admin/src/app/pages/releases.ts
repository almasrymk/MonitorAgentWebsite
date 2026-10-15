import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, BytesPipe, errorText, I18n, Loader, LoadError, Release, Skeleton, StatusBadge, TranslatePipe, WhenPipe } from 'shared';

/** The manifest is uploaded byte for byte: the signature covers these exact bytes, so nothing may re-encode them. */
async function toBase64(file: File): Promise<string> {
  const bytes = new Uint8Array(await file.arrayBuffer());
  let binary = '';
  for (let i = 0; i < bytes.length; i += 0x8000) binary += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
  return btoa(binary);
}

@Component({
  selector: 'admin-releases',
  imports: [FormsModule, TranslatePipe, WhenPipe, BytesPipe, LoadError, Skeleton, StatusBadge],
  template: `
    <h1>{{ 'releases.title' | t }}</h1>
    <div class="stack">
      <section class="card" aria-labelledby="add-title">
        <h2 id="add-title">{{ 'releases.add' | t }}</h2>
        <p class="muted">{{ 'releases.addHelp' | t }}</p>
        <form (ngSubmit)="add()">
          <div class="row">
            <div class="field" style="flex: 2; min-width: 220px">
              <label for="manifest">{{ 'releases.manifest' | t }}</label>
              <input id="manifest" name="manifest" type="file" accept=".json,application/json" required (change)="manifest = $any($event.target).files?.[0] ?? null" />
            </div>
            <div class="field" style="flex: 2; min-width: 220px">
              <label for="signature">{{ 'releases.signature' | t }}</label>
              <input id="signature" name="signature" type="file" accept=".sig,text/plain" required (change)="signature = $any($event.target).files?.[0] ?? null" />
            </div>
            <div class="field" style="flex: 1; min-width: 140px">
              <label for="channel">{{ 'releases.channel' | t }}</label>
              <input id="channel" name="channel" required pattern="[a-z][a-z0-9\\-]*" maxlength="20" aria-describedby="channel-help" [(ngModel)]="channel" />
              <p id="channel-help" class="muted">{{ 'releases.channelHelp' | t }}</p>
            </div>
          </div>
          <button class="btn" type="submit" [disabled]="busy()">{{ 'releases.upload' | t }}</button>
        </form>
      </section>

      @if (message()) {
        <p class="alert" [class.ok]="!failed()" [attr.role]="failed() ? 'alert' : 'status'">{{ message() }}</p>
      }

      @if (releases.loading()) {
        <ma-skeleton />
      } @else if (releases.error()) {
        <ma-load-error [error]="releases.error()" (retry)="releases.load()" />
      } @else if (releases.value(); as value) {
        @if (value.length === 0) {
          <p class="card muted">{{ 'releases.none' | t }}</p>
        } @else {
          <div class="card table-wrap">
            <table>
              <thead>
                <tr>
                  <th scope="col">{{ 'releases.version' | t }}</th>
                  <th scope="col">{{ 'releases.channel' | t }}</th>
                  <th scope="col">{{ 'releases.status' | t }}</th>
                  <th scope="col">{{ 'releases.packages' | t }}</th>
                  <th scope="col">{{ 'releases.added' | t }}</th>
                  <th scope="col">{{ 'releases.published' | t }}</th>
                  <th scope="col"><span class="visually-hidden">{{ 'orders.actions' | t }}</span></th>
                </tr>
              </thead>
              <tbody>
                @for (release of value; track release.id) {
                  <tr>
                    <td class="mono">{{ release.version }}</td>
                    <td>{{ release.channel }}</td>
                    <td><ma-status [value]="release.status" prefix="releases.status." /></td>
                    <td>
                      @for (pkg of release.packages; track pkg.platform) {
                        <div>{{ pkg.platform }} · {{ pkg.size | bytes }}</div>
                      }
                    </td>
                    <td>{{ release.createdAt | when }}</td>
                    <td>{{ release.publishedAt | when }}</td>
                    <td>
                      @if (release.status === 'Draft') {
                        <button class="btn" type="button" (click)="publish(release)" [disabled]="busy()">{{ 'releases.publish' | t }}</button>
                      } @else if (release.status === 'Published') {
                        <button class="btn secondary" type="button" (click)="withdraw(release)" [disabled]="busy()">{{ 'releases.withdraw' | t }}</button>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      }
    </div>
  `,
})
export class ReleasesPage implements OnInit {
  private readonly api = inject(Api);
  private readonly i18n = inject(I18n);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly failed = signal(false);
  protected manifest: File | null = null;
  protected signature: File | null = null;
  protected channel = 'stable';

  protected readonly releases = new Loader(() => this.api.get<Release[]>('/api/v1/admin/releases'));

  ngOnInit(): void {
    void this.releases.load();
  }

  /** Adds a draft. The API checks the publisher signature and the manifest rules before keeping anything. */
  protected add(): Promise<void> {
    const manifest = this.manifest;
    const signature = this.signature;
    if (!manifest || !signature) return Promise.resolve();
    return this.act(async () => {
      const release = await this.api.post<Release>('/api/v1/admin/releases', {
        channel: this.channel,
        manifestBase64: await toBase64(manifest),
        signature: (await signature.text()).trim(),
      });
      this.message.set(this.i18n.t('releases.draftAdded', { version: release.version }));
    });
  }

  protected publish(release: Release): Promise<void> {
    if (!confirm(this.i18n.t('releases.publishConfirm', { version: release.version, channel: release.channel }))) return Promise.resolve();
    return this.act(async () => {
      await this.api.post(`/api/v1/admin/releases/${release.id}/publish`);
      this.message.set(this.i18n.t('releases.publishedNow', { version: release.version }));
    });
  }

  protected withdraw(release: Release): Promise<void> {
    if (!confirm(this.i18n.t('releases.withdrawConfirm', { version: release.version }))) return Promise.resolve();
    return this.act(async () => {
      await this.api.post(`/api/v1/admin/releases/${release.id}/withdraw`);
      this.message.set(this.i18n.t('releases.withdrawn', { version: release.version }));
    });
  }

  private async act(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.message.set('');
    this.failed.set(false);
    try {
      await action();
    } catch (error) {
      this.failed.set(true);
      this.message.set(errorText(this.i18n, error));
    } finally {
      this.busy.set(false);
      await this.releases.load(true);
    }
  }
}
