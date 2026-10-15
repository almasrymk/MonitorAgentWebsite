/** Shapes returned by the website API (api/v1). Enums arrive as strings. */

export interface CatalogPlan {
  id: string;
  productCode: string;
  productName: string;
  code: string;
  name: string;
  price: number;
  currency: string;
  durationDays: number | null;
  maxDevices: number | null;
  features: string[];
}

export interface ReleasePackage {
  platform: string;
  url: string;
  sha256: string;
  size: number;
}

export type ReleaseStatus = 'Draft' | 'Published' | 'Withdrawn';

export interface Release {
  id: string;
  version: string;
  channel: string;
  notes: string | null;
  status: ReleaseStatus;
  createdAt: string;
  publishedAt: string | null;
  packages: ReleasePackage[];
}

export interface Me {
  id: string;
  email: string;
  fullName: string;
  emailConfirmed: boolean;
  mfaEnabled: boolean;
  roles: string[];
}

export type MemberRole = 'Owner' | 'Billing' | 'Technical' | 'Viewer';

export interface Member {
  userId: string;
  email: string;
  fullName: string;
  role: MemberRole;
  joinedAt: string;
}

export interface Invitation {
  id: string;
  email: string;
  role: MemberRole;
  expiresAt: string;
}

export interface Company {
  id: string;
  name: string;
  myRole: MemberRole;
  members: Member[];
  invitations: Invitation[];
}

export interface LicenseSummary {
  id: string;
  licenseNumber: string;
  productCode: string;
  planCode: string;
  productKeyPrefix: string;
  status: string;
  issuedAt: string;
  expiresAt: string | null;
  maxDevices: number | null;
  activeDevices: number;
}

export interface Device {
  id: string;
  deviceId: string;
  deviceName: string | null;
  operatingSystem: string | null;
  appVersion: string | null;
  status: string;
  activatedAt: string;
  lastSeenAt: string | null;
  online: boolean;
}

export interface LicenseDetails {
  license: LicenseSummary;
  devices: Device[];
}

export type OrderStatus = 'PendingPayment' | 'Paid' | 'Fulfilled' | 'Cancelled';
export type PaymentStatus = 'Pending' | 'Succeeded' | 'Failed';

export interface Payment {
  id: string;
  provider: string;
  amount: number;
  currency: string;
  status: PaymentStatus;
  createdAt: string;
  failureReason: string | null;
}

export interface Order {
  id: string;
  number: string;
  productName: string;
  planName: string;
  total: number;
  currency: string;
  status: OrderStatus;
  placedAt: string;
  paidAt: string | null;
  licenseNumber: string | null;
  payments: Payment[];
  /** Filled in staff lists only. */
  companyId?: string | null;
  companyName?: string | null;
}

export interface Invoice {
  id: string;
  number: string;
  orderId: string;
  companyName: string;
  description: string;
  amount: number;
  currency: string;
  issuedAt: string;
}

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export type TicketCategory = 'Technical' | 'Licensing' | 'Billing' | 'Other';
export type TicketStatus = 'Open' | 'Answered' | 'Closed';

export interface TicketSummary {
  id: string;
  number: string;
  subject: string;
  category: TicketCategory;
  status: TicketStatus;
  openedAt: string;
  lastActivityAt: string;
  companyName: string | null;
}

export interface TicketMessage {
  id: string;
  authorName: string;
  fromStaff: boolean;
  body: string;
  postedAt: string;
}

export interface Ticket {
  id: string;
  number: string;
  subject: string;
  category: TicketCategory;
  status: TicketStatus;
  openedAt: string;
  companyName: string | null;
  messages: TicketMessage[];
}

export type SuggestionStatus = 'New' | 'Planned' | 'Done' | 'Declined';

export interface Suggestion {
  id: string;
  title: string;
  body: string;
  status: SuggestionStatus;
  staffNote: string | null;
  createdAt: string;
  companyName: string | null;
}

export type VersionStatus = 'Draft' | 'InReview' | 'Published' | 'Superseded';

/** What a page says in both languages, as editors write it. */
export interface PageContent {
  titleAr: string;
  titleEn: string;
  descriptionAr: string;
  descriptionEn: string;
  bodyAr: string;
  bodyEn: string;
}

export interface PublicPage extends PageContent {
  slug: string;
  version: number;
  publishedAt: string;
}

export interface PageSummary {
  id: string;
  slug: string;
  titleAr: string;
  titleEn: string;
  publishedVersion: number | null;
  openStatus: VersionStatus | null;
  updatedAt: string;
}

export interface PageVersion extends PageContent {
  number: number;
  status: VersionStatus;
  editedBy: string;
  editedAt: string;
  reviewedBy: string | null;
  reviewedAt: string | null;
  reviewNote: string | null;
  publishedAt: string | null;
}

export interface PageDetail {
  id: string;
  slug: string;
  publishedVersion: number | null;
  versions: PageVersion[];
}

export interface CompanyListItem {
  id: string;
  name: string;
  ownerName: string | null;
  ownerEmail: string | null;
  members: number;
  createdAt: string;
}

export interface CompanyDetails {
  id: string;
  name: string;
  createdAt: string;
  members: { userId: string; fullName: string; email: string; role: MemberRole; joinedAt: string }[];
  pendingInvitations: { email: string; role: MemberRole; expiresAt: string }[];
}

export interface CurrencyTotal {
  currency: string;
  payments: number;
  refunds: number;
  net: number;
  paymentCount: number;
  refundCount: number;
}

export interface RevenueReport {
  from: string;
  to: string;
  totals: CurrencyTotal[];
  byMonth: { month: string; currency: string; net: number; paymentCount: number }[];
  byPlan: { productName: string; planName: string; currency: string; net: number; paymentCount: number }[];
  byProvider: { provider: string; currency: string; net: number; paymentCount: number }[];
  truncated: boolean;
}
