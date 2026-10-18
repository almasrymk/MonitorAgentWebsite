import { Component, computed, inject, input, output } from '@angular/core';
import { I18n, TranslatePipe } from './i18n';
import { errorText } from './session';

/** The failed state of a page section, with a retry button. */
@Component({
  selector: 'ma-load-error',
  imports: [TranslatePipe],
  template: `
    <div class="alert" role="alert">
      {{ message() }}
      <button class="btn secondary" type="button" (click)="retry.emit()">{{ 'common.retry' | t }}</button>
    </div>
  `,
})
export class LoadError {
  private readonly i18n = inject(I18n);
  readonly error = input<unknown>();
  readonly retry = output();
  protected readonly message = computed(() => errorText(this.i18n, this.error()));
}

/** Placeholder rows while a section loads. */
@Component({
  selector: 'ma-skeleton',
  imports: [TranslatePipe],
  template: `
    <div class="card" aria-busy="true">
      <span class="visually-hidden" role="status">{{ 'common.loading' | t }}</span>
      @for (row of rows; track row) {
        <div class="skeleton" style="height: 1.25rem; margin-bottom: 0.75rem"></div>
      }
    </div>
  `,
})
export class Skeleton {
  protected readonly rows = [1, 2, 3, 4];
}

/** License and order states as a colored badge with text (never color alone). */
@Component({
  selector: 'ma-status',
  imports: [TranslatePipe],
  template: `<span class="badge" [class.ok]="tone() === 'ok'" [class.warn]="tone() === 'warn'" [class.bad]="tone() === 'bad'">{{ prefix() + value() | t }}</span>`,
})
export class StatusBadge {
  readonly value = input.required<string>();
  readonly prefix = input('status.');
  protected readonly tone = computed(() => {
    const value = this.value();
    if (['Active', 'Fulfilled', 'Succeeded', 'Paid', 'Answered', 'Planned', 'Done', 'Published'].includes(value)) return 'ok';
    if (['PendingPayment', 'Pending', 'Suspended', 'Open', 'New', 'Draft', 'InReview'].includes(value)) return 'warn';
    if (['Expired', 'Revoked', 'Cancelled', 'Failed'].includes(value)) return 'bad';
    return '';
  });
}
