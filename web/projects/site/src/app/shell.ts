import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { I18n, isLang, TranslatePipe } from 'shared';

/** Header, footer and the language of everything under /ar or /en. */
@Component({
  selector: 'site-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe],
  template: `
    <a class="skip-link" href="#main">{{ 'skip' | t }}</a>
    <header class="site-header">
      <div class="container bar">
        <a class="logo" [routerLink]="['/', lang()]">
          <svg aria-hidden="true" width="26" height="26" viewBox="0 0 24 24"><path d="M2 12h4l3-7 4 14 3-7h6" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"/></svg>
          {{ 'brand' | t }}
        </a>
        <nav [attr.aria-label]="'nav.menu' | t">
          <a [routerLink]="['/', lang()]" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: true }" ariaCurrentWhenActive="page">{{ 'nav.home' | t }}</a>
          <a [routerLink]="['/', lang(), 'pricing']" routerLinkActive="active" ariaCurrentWhenActive="page">{{ 'nav.pricing' | t }}</a>
          <a [routerLink]="['/', lang(), 'download']" routerLinkActive="active" ariaCurrentWhenActive="page">{{ 'nav.download' | t }}</a>
          <a [routerLink]="['/', lang(), 'demo']" routerLinkActive="active" ariaCurrentWhenActive="page">{{ 'nav.demo' | t }}</a>
        </nav>
        <div class="actions">
          <a class="lang" [routerLink]="otherLangPath()"><span class="visually-hidden">{{ 'lang.switchLabel' | t }}: </span><span [attr.lang]="i18n.other()">{{ 'lang.switch' | t }}</span></a>
          <a class="btn secondary" href="/portal/sign-in">{{ 'nav.signIn' | t }}</a>
          <a class="btn" [routerLink]="['/', lang(), 'pricing']">{{ 'nav.getStarted' | t }}</a>
        </div>
      </div>
    </header>
    <main id="main" tabindex="-1">
      <router-outlet />
    </main>
    <footer class="site-footer">
      <div class="container bar">
        <div>
          <strong>{{ 'brand' | t }}</strong>
          <p class="muted">{{ 'footer.tagline' | t }}</p>
        </div>
        <nav aria-label="Legal">
          <a [routerLink]="['/', lang(), 'terms']">{{ 'footer.terms' | t }}</a>
          <a [routerLink]="['/', lang(), 'privacy']">{{ 'footer.privacy' | t }}</a>
          <a [routerLink]="['/', lang(), 'refunds']">{{ 'footer.refunds' | t }}</a>
        </nav>
        <p class="muted">{{ 'footer.rights' | t: { year: year } }}</p>
      </div>
    </footer>
  `,
})
export class Shell {
  protected readonly i18n = inject(I18n);
  private readonly router = inject(Router);
  protected readonly year = new Date().getFullYear();
  protected readonly lang = this.i18n.lang;

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );

  /** The same page in the other language. */
  protected readonly otherLangPath = computed(() => {
    const path = this.url().split(/[?#]/)[0].split('/').filter(Boolean);
    path[0] = this.i18n.other();
    return '/' + path.join('/');
  });

  constructor() {
    const lang = inject(ActivatedRoute).snapshot.paramMap.get('lang');
    this.i18n.use(isLang(lang) ? lang : 'ar');
    inject(ActivatedRoute).paramMap.subscribe((params) => {
      const next = params.get('lang');
      if (isLang(next)) this.i18n.use(next);
    });
  }
}
