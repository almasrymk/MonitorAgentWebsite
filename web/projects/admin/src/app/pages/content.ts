import { Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import {
  Api, errorText, I18n, Loader, LoadError, PageContent, PageDetail, PageSummary, PageVersion, RichText, Session, Skeleton, StatusBadge,
  TranslatePipe, WhenPipe,
} from 'shared';

const emptyContent = (): PageContent => ({ titleAr: '', titleEn: '', descriptionAr: '', descriptionEn: '', bodyAr: '', bodyEn: '' });

/** What the signed-in staff member may do with content. Admins may do both, the API stops them approving their own edit. */
function contentRoles() {
  const session = inject(Session);
  const has = (role: string) => computed(() => !!session.me()?.roles.some((r) => r === role || r === 'Admin'));
  return { editor: has('ContentEditor'), reviewer: has('ContentReviewer') };
}

@Component({
  selector: 'admin-content',
  imports: [FormsModule, RouterLink, TranslatePipe, WhenPipe, LoadError, Skeleton, StatusBadge],
  template: `
    <div class="page-head">
      <h1>{{ 'content.title' | t }}</h1>
    </div>
    <p class="muted">{{ 'content.lead' | t }}</p>
    @if (pages.loading()) {
      <ma-skeleton />
    } @else if (pages.error()) {
      <ma-load-error [error]="pages.error()" (retry)="pages.load()" />
    } @else if (pages.value(); as value) {
      @if (value.length === 0) {
        <p class="card muted">{{ 'common.none' | t }}</p>
      } @else {
        <div class="card table-wrap">
          <table>
            <thead>
              <tr>
                <th scope="col">{{ 'content.page' | t }}</th>
                <th scope="col">{{ 'content.address' | t }}</th>
                <th scope="col">{{ 'content.live' | t }}</th>
                <th scope="col">{{ 'content.working' | t }}</th>
                <th scope="col">{{ 'support.updated' | t }}</th>
              </tr>
            </thead>
            <tbody>
              @for (page of value; track page.id) {
                <tr>
                  <td><a [routerLink]="['/content', page.id]">{{ i18n.lang() === 'ar' ? page.titleAr : page.titleEn }}</a></td>
                  <td class="mono">{{ page.slug }}</td>
                  <td>{{ page.publishedVersion ? ('content.versionN' | t: { n: page.publishedVersion }) : '—' }}</td>
                  <td>
                    @if (page.openStatus) {
                      <ma-status [value]="page.openStatus" prefix="content.status." />
                    } @else {
                      —
                    }
                  </td>
                  <td>{{ page.updatedAt | when: true }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    }

    @if (roles.editor()) {
      <form class="card stack" style="margin-top: 1.5rem" (ngSubmit)="create()">
        <h2>{{ 'content.new' | t }}</h2>
        <div>
          <label for="new-slug">{{ 'content.address' | t }}</label>
          <input id="new-slug" name="slug" required maxlength="60" pattern="[a-z0-9]+(-[a-z0-9]+)*" dir="ltr" [(ngModel)]="slug"
            aria-describedby="new-slug-hint" />
          <p id="new-slug-hint" class="muted">{{ 'content.slugHint' | t }}</p>
        </div>
        <div>
          <label for="new-title-ar">{{ 'content.titleAr' | t }}</label>
          <input id="new-title-ar" name="titleAr" required maxlength="150" dir="rtl" [(ngModel)]="titleAr" />
        </div>
        <div>
          <label for="new-title-en">{{ 'content.titleEn' | t }}</label>
          <input id="new-title-en" name="titleEn" required maxlength="150" dir="ltr" [(ngModel)]="titleEn" />
        </div>
        @if (error()) {
          <p class="alert" role="alert">{{ error() }}</p>
        }
        <div><button class="btn" type="submit" [disabled]="busy()">{{ 'content.create' | t }}</button></div>
      </form>
    }
  `,
})
export class ContentPagesPage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  protected readonly i18n = inject(I18n);
  protected readonly roles = contentRoles();
  protected readonly pages = new Loader(() => this.api.get<PageSummary[]>('/api/v1/admin/content/pages'));
  protected slug = '';
  protected titleAr = '';
  protected titleEn = '';
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  constructor() {
    void this.pages.load();
  }

  /** Starts the page with its titles; the body is written on the page's own screen. */
  protected async create(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      const content = { ...emptyContent(), titleAr: this.titleAr, titleEn: this.titleEn };
      const page = await this.api.post<PageDetail>('/api/v1/admin/content/pages', { slug: this.slug.trim(), content });
      await this.router.navigate(['/content', page.id]);
    } catch (error) {
      this.error.set(errorText(this.i18n, error));
    } finally {
      this.busy.set(false);
    }
  }
}

@Component({
  selector: 'admin-content-page',
  imports: [FormsModule, RouterLink, TranslatePipe, WhenPipe, LoadError, Skeleton, StatusBadge, RichText],
  template: `
    <p><a routerLink="/content">← {{ 'content.title' | t }}</a></p>
    @if (page.loading()) {
      <ma-skeleton />
    } @else if (page.error()) {
      <ma-load-error [error]="page.error()" (retry)="page.load()" />
    } @else if (page.value(); as value) {
      <div class="page-head">
        <h1>{{ i18n.lang() === 'ar' ? current().titleAr : current().titleEn }}</h1>
        @if (open(); as o) {
          <ma-status [value]="o.status" prefix="content.status." />
        }
      </div>
      <p class="muted">
        <span class="mono" dir="ltr">/{{ i18n.lang() }}/pages/{{ value.slug }}</span> ·
        {{ value.publishedVersion ? ('content.liveVersion' | t: { n: value.publishedVersion }) : ('content.notLive' | t) }}
      </p>

      @if (message()) {
        <p class="alert" [class.ok]="!failed()" [attr.role]="failed() ? 'alert' : 'status'">{{ message() }}</p>
      }

      @if (open()?.status === 'InReview') {
        <section class="card stack" aria-labelledby="review-title">
          <h2 id="review-title">{{ 'content.inReview' | t: { n: open()!.number, name: open()!.editedBy } }}</h2>
          <div class="row">
            @if (roles.reviewer()) {
              <button class="btn" type="button" [disabled]="busy()" (click)="step('approve')">{{ 'content.approve' | t }}</button>
            }
            @if (roles.editor()) {
              <button class="btn secondary" type="button" [disabled]="busy()" (click)="step('withdraw')">{{ 'content.withdraw' | t }}</button>
            }
          </div>
          @if (roles.reviewer()) {
            <form class="stack" (ngSubmit)="reject()">
              <div>
                <label for="reject-note">{{ 'content.rejectNote' | t }}</label>
                <textarea id="reject-note" name="note" rows="3" maxlength="1000" required [(ngModel)]="note"></textarea>
              </div>
              <div><button class="btn secondary" type="submit" [disabled]="busy() || !note.trim()">{{ 'content.reject' | t }}</button></div>
            </form>
          }
        </section>
        <section class="card" aria-labelledby="preview-title">
          <h2 id="preview-title">{{ 'content.preview' | t }}</h2>
          <div class="grid-2">
            <div lang="ar" dir="rtl">
              <h3>{{ open()!.titleAr }}</h3>
              <p class="muted">{{ open()!.descriptionAr }}</p>
              <ma-rich-text [text]="open()!.bodyAr" />
            </div>
            <div lang="en" dir="ltr">
              <h3>{{ open()!.titleEn }}</h3>
              <p class="muted">{{ open()!.descriptionEn }}</p>
              <ma-rich-text [text]="open()!.bodyEn" />
            </div>
          </div>
        </section>
      } @else if (roles.editor()) {
        <form class="card stack" (ngSubmit)="save(false)">
          <h2>{{ open() ? ('content.editDraft' | t: { n: open()!.number }) : ('content.startDraft' | t) }}</h2>
          @if (open()?.reviewNote; as reviewNote) {
            <p class="alert" role="status">{{ 'content.sentBack' | t: { name: open()!.reviewedBy ?? '—', note: reviewNote } }}</p>
          }
          <div class="grid-2">
            <div class="stack" lang="ar" dir="rtl">
              <div>
                <label for="title-ar">{{ 'content.titleAr' | t }}</label>
                <input id="title-ar" name="titleAr" required maxlength="150" [(ngModel)]="draft.titleAr" />
              </div>
              <div>
                <label for="description-ar">{{ 'content.descriptionAr' | t }}</label>
                <input id="description-ar" name="descriptionAr" maxlength="300" [(ngModel)]="draft.descriptionAr" />
              </div>
              <div>
                <label for="body-ar">{{ 'content.bodyAr' | t }}</label>
                <textarea id="body-ar" name="bodyAr" rows="14" required maxlength="20000" [(ngModel)]="draft.bodyAr"></textarea>
              </div>
            </div>
            <div class="stack" lang="en" dir="ltr">
              <div>
                <label for="title-en">{{ 'content.titleEn' | t }}</label>
                <input id="title-en" name="titleEn" required maxlength="150" [(ngModel)]="draft.titleEn" />
              </div>
              <div>
                <label for="description-en">{{ 'content.descriptionEn' | t }}</label>
                <input id="description-en" name="descriptionEn" maxlength="300" [(ngModel)]="draft.descriptionEn" />
              </div>
              <div>
                <label for="body-en">{{ 'content.bodyEn' | t }}</label>
                <textarea id="body-en" name="bodyEn" rows="14" required maxlength="20000" [(ngModel)]="draft.bodyEn"></textarea>
              </div>
            </div>
          </div>
          <p class="muted">{{ 'content.formatHint' | t }}</p>
          <div class="row">
            <button class="btn secondary" type="submit" [disabled]="busy()">{{ 'content.saveDraft' | t }}</button>
            <button class="btn" type="button" [disabled]="busy()" (click)="save(true)">{{ 'content.submit' | t }}</button>
          </div>
          <details>
            <summary>{{ 'content.preview' | t }}</summary>
            <div class="grid-2">
              <div lang="ar" dir="rtl"><ma-rich-text [text]="draft.bodyAr" /></div>
              <div lang="en" dir="ltr"><ma-rich-text [text]="draft.bodyEn" /></div>
            </div>
          </details>
        </form>
      } @else {
        <p class="card muted">{{ 'content.nothingToReview' | t }}</p>
      }

      <h2 class="section-gap" id="history-title">{{ 'content.history' | t }}</h2>
      <!-- Focusable so keyboard users can scroll the table sideways on a phone. -->
      <div class="card table-wrap" tabindex="0" role="region" aria-labelledby="history-title">
        <table>
          <thead>
            <tr>
              <th scope="col">{{ 'content.version' | t }}</th>
              <th scope="col">{{ 'support.statusLabel' | t }}</th>
              <th scope="col">{{ 'content.editedBy' | t }}</th>
              <th scope="col">{{ 'content.reviewedBy' | t }}</th>
              <th scope="col"><span class="visually-hidden">{{ 'content.restore' | t }}</span></th>
            </tr>
          </thead>
          <tbody>
            @for (v of value.versions; track v.number) {
              <tr>
                <td>{{ v.number }}</td>
                <td><ma-status [value]="v.status" prefix="content.status." /></td>
                <td>{{ v.editedBy }} · {{ v.editedAt | when: true }}</td>
                <td>{{ v.reviewedBy ?? '—' }}@if (v.reviewNote) { · <span class="muted">{{ v.reviewNote }}</span> }</td>
                <td>
                  @if (roles.editor() && v !== open() && open()?.status !== 'InReview') {
                    <button class="btn secondary" type="button" [disabled]="busy()" (click)="restore(v)"
                      [attr.aria-label]="'content.restoreN' | t: { n: v.number }">{{ 'content.restore' | t }}</button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class ContentPageEditor {
  readonly id = input.required<string>();
  private readonly api = inject(Api);
  protected readonly i18n = inject(I18n);
  protected readonly roles = contentRoles();
  protected readonly page = new Loader(() => this.api.get<PageDetail>(`/api/v1/admin/content/pages/${this.id()}`));
  /** The version being worked on, if any. */
  protected readonly open = computed(() => this.page.value()?.versions.find((v) => v.status === 'Draft' || v.status === 'InReview') ?? null);
  /** What the page says now: the open version, else the live one, else the newest. */
  protected readonly current = computed(() => {
    const versions = this.page.value()?.versions ?? [];
    return this.open() ?? versions.find((v) => v.status === 'Published') ?? versions[0];
  });
  protected draft: PageContent = emptyContent();
  protected note = '';
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly failed = signal(false);

  constructor() {
    effect(() => {
      this.id();
      untracked(() => void this.page.load());
    });
    // The form starts from what the page says now, and again after every step.
    effect(() => {
      const version = this.current();
      untracked(() => (this.draft = version ? pick(version) : emptyContent()));
    });
  }

  protected save(thenSubmit: boolean): Promise<void> {
    return this.run(async () => {
      let page = await this.api.put<PageDetail>(`/api/v1/admin/content/pages/${this.id()}/draft`, this.draft);
      this.page.set(page);
      if (thenSubmit) page = await this.api.post<PageDetail>(`/api/v1/admin/content/pages/${this.id()}/submit`);
      return { page, done: thenSubmit ? 'content.submitted' : 'content.saved' };
    });
  }

  protected step(action: 'approve' | 'withdraw'): Promise<void> {
    return this.run(async () => ({
      page: await this.api.post<PageDetail>(`/api/v1/admin/content/pages/${this.id()}/${action}`),
      done: action === 'approve' ? 'content.published' : 'content.withdrawn',
    }));
  }

  protected reject(): Promise<void> {
    return this.run(async () => {
      const page = await this.api.post<PageDetail>(`/api/v1/admin/content/pages/${this.id()}/reject`, { note: this.note.trim() });
      this.note = '';
      return { page, done: 'content.rejected' };
    });
  }

  protected restore(version: PageVersion): Promise<void> {
    return this.run(async () => ({
      page: await this.api.post<PageDetail>(`/api/v1/admin/content/pages/${this.id()}/versions/${version.number}/restore`),
      done: 'content.restored',
    }));
  }

  private async run(action: () => Promise<{ page: PageDetail; done: string }>): Promise<void> {
    this.busy.set(true);
    this.failed.set(false);
    this.message.set('');
    try {
      const { page, done } = await action();
      this.page.set(page);
      this.message.set(this.i18n.t(done));
    } catch (error) {
      this.failed.set(true);
      this.message.set(errorText(this.i18n, error));
    } finally {
      this.busy.set(false);
    }
  }
}

function pick(v: PageContent): PageContent {
  return { titleAr: v.titleAr, titleEn: v.titleEn, descriptionAr: v.descriptionAr, descriptionEn: v.descriptionEn, bodyAr: v.bodyAr, bodyEn: v.bodyEn };
}
