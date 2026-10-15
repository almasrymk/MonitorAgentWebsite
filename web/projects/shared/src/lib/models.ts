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
