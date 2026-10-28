export type UserRole = 'Customer' | 'Admin' | 'ComplianceOfficer' | 'Auditor' | 'SystemAdmin';

export type KycTier = 'Tier0' | 'Tier1' | 'Tier2' | 'Tier3';

export type TransactionStatus = 'Pending' | 'Completed' | 'Failed' | 'Reversed';

export type TransactionType = 'Deposit' | 'Withdrawal' | 'Transfer' | 'Fee' | 'Reversal';

export interface User {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  phoneNumber?: string;
  role: UserRole;
  kycTier: KycTier;
  isActive: boolean;
  emailConfirmed: boolean;
  twoFactorEnabled: boolean;
  createdAtUtc: string;
}

export interface Wallet {
  id: string;
  userId: string;
  balance: number;
  currency: string;
  updatedAtUtc: string;
  isFrozen: boolean;
  freezeReason?: string | null;
}

export interface Transaction {
  id: string;
  reference: string;
  type: string;
  status: string;
  amount: number;
  currency: string;
  senderWalletId?: string | null;
  receiverWalletId?: string | null;
  sender?: string | null;
  recipient?: string | null;
  description?: string | null;
  idempotencyKey?: string | null;
  createdAtUtc: string;
  createdAt?: string;
  completedAtUtc?: string | null;
  failureReason?: string | null;
  exchangeRate: number;
  targetAmount?: number | null;
  targetCurrency?: string | null;
  feeAmount: number;
  fee?: number;
  grossAmount?: number | null;
  netAmount?: number | null;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface AuthTokens {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
}

export interface AuthResponse {
  user: User;
  tokens: AuthTokens;
}

export interface FeeQuote {
  amount: number;
  feeAmount: number;
  grossAmount: number;
  netAmount: number;
  currency: string;
  percentage: number;
  fixedFee: number;
  capAmount?: number | null;
}

export interface TransferRequest {
  receiverEmail: string;
  amount: number;
  description?: string;
  twoFactorCode?: string;
}
