import { Component, inject, input, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api, ApiError, Company, errorText, I18n, Loader, Member, MemberRole, TranslatePipe, WhenPipe } from 'shared';
import { LoadError, Skeleton } from './ui';

const roles: MemberRole[] = ['Owner', 'Billing', 'Technical', 'Viewer'];

@Component({
  selector: 'portal-company',
  imports: [FormsModule, RouterLink, TranslatePipe, WhenPipe, LoadError, Skeleton],
  template: `
    <h1>{{ 'company.title' | t }}</h1>
    @if (company.loading()) {
      <portal-skeleton />
    } @else if (noCompany()) {
      <p class="card">{{ 'overview.noCompanyBody' | t }} <a routerLink="/">{{ 'nav.overview' | t }}</a></p>
    } @else if (company.error()) {
      <portal-load-error [error]="company.error()" (retry)="company.load()" />
    } @else if (company.value(); as value) {
      <div class="stack">
        @if (message()) {
          <p class="alert" [class.ok]="!failed()" [attr.role]="failed() ? 'alert' : 'status'">{{ message() }}</p>
        }
        <section class="card">
          @if (value.myRole === 'Owner') {
            <form class="row" (ngSubmit)="rename()">
              <div style="flex: 1; min-width: 220px">
                <label for="company-name">{{ 'company.name' | t }}</label>
                <input id="company-name" name="name" required maxlength="150" [(ngModel)]="name" />
              </div>
              <button class="btn secondary" type="submit" [disabled]="busy()" style="align-self: flex-end">{{ 'company.rename' | t }}</button>
            </form>
          } @else {
            <h2>{{ value.name }}</h2>
          }
          <p class="muted">{{ 'company.roleHelp' | t }}</p>
        </section>

        <section class="card table-wrap" aria-labelledby="members-title">
          <h2 id="members-title">{{ 'company.members' | t }}</h2>
          <table>
            <thead>
              <tr>
                <th scope="col">{{ 'common.fullName' | t }}</th>
                <th scope="col">{{ 'common.email' | t }}</th>
                <th scope="col">{{ 'company.role' | t }}</th>
                <th scope="col">{{ 'company.joined' | t }}</th>
                @if (value.myRole === 'Owner') {
                  <th scope="col"><span class="visually-hidden">{{ 'company.remove' | t }}</span></th>
                }
              </tr>
            </thead>
            <tbody>
              @for (member of value.members; track member.userId) {
                <tr>
                  <td>{{ member.fullName }}</td>
                  <td>{{ member.email }}</td>
                  <td>
                    @if (value.myRole === 'Owner') {
                      <select [attr.aria-label]="'company.changeRole' | t: { name: member.fullName }" (change)="changeRole(member, $any($event.target).value)" [disabled]="busy()">
                        @for (role of roles; track role) {
                          <option [value]="role" [selected]="role === member.role">{{ 'common.role.' + role | t }}</option>
                        }
                      </select>
                    } @else {
                      {{ 'common.role.' + member.role | t }}
                    }
                  </td>
                  <td>{{ member.joinedAt | when }}</td>
                  @if (value.myRole === 'Owner') {
                    <td><button class="btn secondary" type="button" (click)="remove(member)" [disabled]="busy()">{{ 'company.remove' | t }}</button></td>
                  }
                </tr>
              }
            </tbody>
          </table>
        </section>

        @if (value.myRole === 'Owner') {
          <section class="card" aria-labelledby="invite-title">
            <h2 id="invite-title">{{ 'company.invite' | t }}</h2>
            <form class="row" (ngSubmit)="invite()">
              <div style="flex: 2; min-width: 220px">
                <label for="invite-email">{{ 'common.email' | t }}</label>
                <input id="invite-email" name="email" type="email" required [(ngModel)]="inviteEmail" />
              </div>
              <div style="flex: 1; min-width: 160px">
                <label for="invite-role">{{ 'company.role' | t }}</label>
                <select id="invite-role" name="role" [(ngModel)]="inviteRole">
                  @for (role of roles; track role) {
                    <option [value]="role">{{ 'common.role.' + role | t }}</option>
                  }
                </select>
              </div>
              <button class="btn" type="submit" [disabled]="busy()" style="align-self: flex-end">{{ 'company.inviteSend' | t }}</button>
            </form>
            @if (value.invitations.length) {
              <h3>{{ 'company.invitations' | t }}</h3>
              <div class="table-wrap">
                <table>
                  <tbody>
                    @for (invitation of value.invitations; track invitation.id) {
                      <tr>
                        <td>{{ invitation.email }}</td>
                        <td>{{ 'common.role.' + invitation.role | t }}</td>
                        <td>{{ 'company.expires' | t }} {{ invitation.expiresAt | when }}</td>
                        <td><button class="btn secondary" type="button" (click)="revoke(invitation.id)" [disabled]="busy()">{{ 'company.revoke' | t }}</button></td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          </section>
        }
      </div>
    }
  `,
})
export class CompanyPage implements OnInit {
  private readonly api = inject(Api);
  private readonly i18n = inject(I18n);
  protected readonly roles = roles;
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly failed = signal(false);
  protected readonly noCompany = signal(false);
  protected name = '';
  protected inviteEmail = '';
  protected inviteRole: MemberRole = 'Technical';

  protected readonly company = new Loader(async () => {
    try {
      const company = await this.api.get<Company>('/api/v1/company');
      this.name = company.name;
      return company;
    } catch (error) {
      if (error instanceof ApiError && error.code === 'NO_COMPANY') this.noCompany.set(true);
      throw error;
    }
  });

  ngOnInit(): void {
    void this.company.load();
  }

  protected rename(): Promise<void> {
    return this.act(async () => {
      this.company.set(await this.api.put<Company>('/api/v1/company', { name: this.name }));
    });
  }

  protected invite(): Promise<void> {
    return this.act(async () => {
      await this.api.post('/api/v1/company/invitations', { email: this.inviteEmail, role: this.inviteRole });
      this.message.set(this.i18n.t('company.invited', { email: this.inviteEmail }));
      this.inviteEmail = '';
      await this.company.load(true);
    });
  }

  protected revoke(invitationId: string): Promise<void> {
    return this.act(async () => {
      await this.api.delete(`/api/v1/company/invitations/${invitationId}`);
      await this.company.load(true);
    });
  }

  protected changeRole(member: Member, role: MemberRole): Promise<void> {
    return this.act(async () => {
      await this.api.put(`/api/v1/company/members/${member.userId}/role`, { role });
      await this.company.load(true);
    });
  }

  protected remove(member: Member): Promise<void> {
    if (!confirm(this.i18n.t('company.removeConfirm', { name: member.fullName }))) return Promise.resolve();
    return this.act(async () => {
      await this.api.delete(`/api/v1/company/members/${member.userId}`);
      await this.company.load(true);
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
      await this.company.load(true);
    } finally {
      this.busy.set(false);
    }
  }
}

/** The link from an invitation email. The guard has already made sure someone is signed in. */
@Component({
  selector: 'portal-accept-invitation',
  imports: [RouterLink, TranslatePipe],
  template: `
    <div class="card">
      <h1>{{ 'invite.title' | t }}</h1>
      @if (error()) {
        <p class="alert" role="alert">{{ error() }}</p>
        <p class="muted">{{ 'invite.signInFirst' | t }}</p>
      } @else if (joined()) {
        <p class="alert ok" role="status">{{ 'invite.accepted' | t: { company: joined() } }}</p>
        <a class="btn" routerLink="/">{{ 'nav.overview' | t }}</a>
      } @else {
        <p role="status">{{ 'invite.accepting' | t }}</p>
      }
    </div>
  `,
})
export class AcceptInvitationPage implements OnInit {
  readonly token = input<string>();
  private readonly api = inject(Api);
  private readonly i18n = inject(I18n);
  protected readonly joined = signal('');
  protected readonly error = signal('');

  async ngOnInit(): Promise<void> {
    try {
      const company = await this.api.post<Company>('/api/v1/company/invitations/accept', { token: this.token() });
      this.joined.set(company.name);
    } catch (error) {
      this.error.set(errorText(this.i18n, error));
    }
  }
}
