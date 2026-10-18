import { Component, computed, inject, input, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, Invoice, Loader, MoneyPipe, TranslatePipe, WhenPipe } from 'shared';
import { LoadError, Skeleton } from './ui';

@Component({
  selector: 'portal-invoices',
  imports: [RouterLink, TranslatePipe, MoneyPipe, WhenPipe, LoadError, Skeleton],
  template: `
    <h1>{{ 'invoices.title' | t }}</h1>
    @if (invoices.loading()) {
      <portal-skeleton />
    } @else if (invoices.error()) {
      <portal-load-error [error]="invoices.error()" (retry)="invoices.load()" />
    } @else if (invoices.value()?.length === 0) {
      <p class="card">{{ 'invoices.empty' | t }}</p>
    } @else {
      <div class="card table-wrap">
        <table>
          <thead>
            <tr>
              <th scope="col">{{ 'invoices.number' | t }}</th>
              <th scope="col">{{ 'invoices.date' | t }}</th>
              <th scope="col">{{ 'invoices.description' | t }}</th>
              <th scope="col">{{ 'invoices.amount' | t }}</th>
              <th scope="col"><span class="visually-hidden">{{ 'invoices.open' | t }}</span></th>
            </tr>
          </thead>
          <tbody>
            @for (invoice of invoices.value(); track invoice.id) {
              <tr>
                <td class="mono">{{ invoice.number }}</td>
                <td>{{ invoice.issuedAt | when }}</td>
                <td>{{ invoice.description }}</td>
                <td>{{ invoice.amount | money: invoice.currency }}</td>
                <td><a class="btn secondary" [routerLink]="['/invoices', invoice.id]" [attr.aria-label]="('invoices.open' | t) + ' ' + invoice.number">{{ 'invoices.open' | t }}</a></td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class InvoicesPage implements OnInit {
  private readonly api = inject(Api);
  protected readonly invoices = new Loader(() => this.api.get<Invoice[]>('/api/v1/invoices'));

  ngOnInit(): void {
    void this.invoices.load();
  }
}

/** A printable invoice; the browser's print dialog saves it as PDF. */
@Component({
  selector: 'portal-invoice',
  imports: [RouterLink, TranslatePipe, MoneyPipe, WhenPipe, LoadError, Skeleton],
  template: `
    <p class="no-print"><a routerLink="/invoices">← {{ 'invoices.title' | t }}</a></p>
    @if (invoices.loading()) {
      <portal-skeleton />
    } @else if (invoices.error()) {
      <portal-load-error [error]="invoices.error()" (retry)="invoices.load()" />
    } @else if (invoice(); as value) {
      <article class="card stack">
        <div class="page-head">
          <div>
            <p class="eyebrow">Monitor Agent</p>
            <h1>{{ 'invoice.title' | t: { number: value.number } }}</h1>
          </div>
          <span class="badge ok">{{ 'invoice.paid' | t }}</span>
        </div>
        <dl class="dl">
          <dt>{{ 'invoice.billedTo' | t }}</dt>
          <dd>{{ value.companyName }}</dd>
          <dt>{{ 'invoice.issued' | t }}</dt>
          <dd>{{ value.issuedAt | when }}</dd>
        </dl>
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th scope="col">{{ 'invoices.description' | t }}</th>
                <th scope="col">{{ 'invoices.amount' | t }}</th>
              </tr>
            </thead>
            <tbody>
              <tr>
                <td>{{ value.description }}</td>
                <td>{{ value.amount | money: value.currency }}</td>
              </tr>
              <tr>
                <th scope="row">{{ 'checkout.total' | t }}</th>
                <td><strong>{{ value.amount | money: value.currency }}</strong></td>
              </tr>
            </tbody>
          </table>
        </div>
        <button class="btn no-print" type="button" (click)="print()">{{ 'invoice.print' | t }}</button>
      </article>
    } @else {
      <p class="card">{{ 'error.generic' | t }}</p>
    }
  `,
})
export class InvoicePage implements OnInit {
  readonly id = input.required<string>();
  private readonly api = inject(Api);
  protected readonly invoices = new Loader(() => this.api.get<Invoice[]>('/api/v1/invoices'));
  protected readonly invoice = computed(() => this.invoices.value()?.find((i) => i.id === this.id()) ?? null);

  ngOnInit(): void {
    void this.invoices.load();
  }

  protected print(): void {
    window.print();
  }
}
