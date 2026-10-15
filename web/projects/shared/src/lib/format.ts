import { inject, Pipe, PipeTransform } from '@angular/core';
import { I18n, Lang } from './i18n';

const locales: Record<Lang, string> = { ar: 'ar-EG', en: 'en-US' };

export function formatMoney(amount: number, currency: string, lang: Lang): string {
  return new Intl.NumberFormat(locales[lang], { style: 'currency', currency, maximumFractionDigits: 2 }).format(amount);
}

/** Dates from the API are UTC; they are shown in the viewer's time zone. */
export function formatDate(iso: string | null | undefined, lang: Lang, withTime = false): string {
  if (!iso) return '—';
  return new Intl.DateTimeFormat(locales[lang], withTime ? { dateStyle: 'medium', timeStyle: 'short' } : { dateStyle: 'medium' }).format(
    new Date(iso),
  );
}

export function formatBytes(bytes: number, lang: Lang): string {
  const units = ['B', 'KB', 'MB', 'GB'];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  return `${new Intl.NumberFormat(locales[lang], { maximumFractionDigits: 1 }).format(value)} ${units[unit]}`;
}

@Pipe({ name: 'money', pure: false })
export class MoneyPipe implements PipeTransform {
  private readonly i18n = inject(I18n);
  transform(amount: number, currency: string): string {
    return formatMoney(amount, currency, this.i18n.lang());
  }
}

@Pipe({ name: 'when', pure: false })
export class WhenPipe implements PipeTransform {
  private readonly i18n = inject(I18n);
  transform(iso: string | null | undefined, withTime = false): string {
    return formatDate(iso, this.i18n.lang(), withTime);
  }
}

@Pipe({ name: 'bytes', pure: false })
export class BytesPipe implements PipeTransform {
  private readonly i18n = inject(I18n);
  transform(bytes: number): string {
    return formatBytes(bytes, this.i18n.lang());
  }
}

/** Compares "major.minor.patch" versions numerically: negative when a is older, positive when newer. */
export function compareVersions(a: string, b: string): number {
  const pa = a.split('.').map(Number);
  const pb = b.split('.').map(Number);
  for (let i = 0; i < 3; i++) {
    const diff = (pa[i] ?? 0) - (pb[i] ?? 0);
    if (diff !== 0) return diff;
  }
  return 0;
}
