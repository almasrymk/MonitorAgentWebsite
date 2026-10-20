import { Component, inject, input, ChangeDetectionStrategy } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { BRAND_MARK } from './brand';
import { I18n, TranslatePipe } from './i18n';
import { saveLanguage, Session } from './session';

export interface NavItem {
  path: string;
  label: string;
}

@Component({
  selector: 'ma-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <a class="skip-link" href="#main">{{ 'skip' | t }}</a>
    <div class="app">
      <aside class="sidebar">
        <a class="logo" routerLink="/">
          <img class="logo-mark" [src]="mark" width="28" height="28" alt="" />
          {{ 'brand' | t }}
        </a>
        <nav [attr.aria-label]="'nav.label' | t">
          @for (item of nav(); track item.path) {
            <a
              [routerLink]="item.path"
              routerLinkActive="active"
              [routerLinkActiveOptions]="{ exact: item.path === '/' }"
              ariaCurrentWhenActive="page"
              >{{ item.label | t }}</a
            >
          }
        </nav>
        <div class="foot">
          <a href="/">{{ 'nav.website' | t }}</a>
        </div>
      </aside>
      <div>
        <header class="topbar">
          <button class="link-button" type="button" (click)="switchLanguage()">
            <span class="visually-hidden">{{ 'lang.switchLabel' | t }}: </span
            ><span [attr.lang]="i18n.other()">{{ 'lang.switch' | t }}</span>
          </button>
          <div class="who">
            <strong>{{ session.me()?.fullName }}</strong>
            <div class="muted">{{ session.me()?.email }}</div>
          </div>
          <button class="btn secondary" type="button" (click)="signOut()">
            {{ 'nav.signOut' | t }}
          </button>
        </header>
        <main id="main" class="content" tabindex="-1">
          <router-outlet />
        </main>
      </div>
    </div>
  `,
})
/** The signed-in frame of the portal and admin app. The menu comes from the route's data (`nav`). */
export class AppShell {
  protected readonly mark = BRAND_MARK;
  readonly nav = input<NavItem[]>([]);
  protected readonly i18n = inject(I18n);
  protected readonly session = inject(Session);
  private readonly router = inject(Router);

  protected switchLanguage(): void {
    saveLanguage(this.i18n, this.i18n.other());
  }

  protected async signOut(): Promise<void> {
    await this.session.signOut();
    await this.router.navigateByUrl('/sign-in');
  }
}
