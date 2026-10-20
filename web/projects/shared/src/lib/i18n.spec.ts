import { Component, ChangeDetectionStrategy } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { DOCUMENT } from '@angular/common';
import { DICTIONARY, I18n, isLang, TranslatePipe } from './i18n';

@Component({
  imports: [TranslatePipe],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `{{ 'hello' | t: { name: 'Mona' } }}`,
})
class Host {}

describe('I18n', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [
        {
          provide: DICTIONARY,
          useValue: { en: { hello: 'Hello {name}', only: 'x' }, ar: { hello: 'أهلاً {name}' } },
        },
      ],
    }),
  );

  it('starts in Arabic, right to left', () => {
    const i18n = TestBed.inject(I18n);
    expect(i18n.lang()).toBe('ar');
    expect(i18n.dir()).toBe('rtl');
    expect(i18n.other()).toBe('en');
  });

  it('switches the page language and direction', () => {
    const i18n = TestBed.inject(I18n);
    i18n.use('en');
    const html = TestBed.inject(DOCUMENT).documentElement;
    expect(html.lang).toBe('en');
    expect(html.dir).toBe('ltr');
    expect(i18n.dir()).toBe('ltr');
    i18n.use('ar');
    expect(html.dir).toBe('rtl');
  });

  it('fills placeholders and falls back to the key', () => {
    const i18n = TestBed.inject(I18n);
    i18n.use('en');
    expect(i18n.t('hello', { name: 'Mona' })).toBe('Hello Mona');
    expect(i18n.t('missing.key')).toBe('missing.key');
    i18n.use('ar');
    expect(i18n.t('only')).toBe('only');
  });

  it('the pipe follows the current language', () => {
    const i18n = TestBed.inject(I18n);
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toBe('أهلاً Mona');
    i18n.use('en');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toBe('Hello Mona');
  });

  it('knows its two languages', () => {
    expect(isLang('ar')).toBe(true);
    expect(isLang('en')).toBe(true);
    expect(isLang('fr')).toBe(false);
    expect(isLang(null)).toBe(false);
  });
});
