// The data SEAM (doc 05 §2): screens depend ONLY on this interface, never on the Power Apps
// SDK directly. Two implementations:
//   - PowerAppsSigilApi (api/powerApps.ts): the real one, over @microsoft/power-apps + the
//     typed clients that `power-apps add-dataverse-api` generates (generated/ folder).
//   - MockSigilApi (api/mock.ts): in-memory, for `npm run dev` and Vitest WITHOUT an environment.
// Switching from one to the other is a single line (api/index.ts) — the rest of the app is unaware.

import type {
  CreateTransactionInput,
  UpdateDraftInput,
  GetDocumentContentInput,
  RejectTransactionInput,
  CancelTransactionInput,
  ValidateMasterSignatureOutput,
  GetMasterSignatureOutput,
  VerifyDocumentInput,
  VerifyDocumentOutput,
} from './contracts';

// Transaction view that the screens consume (projection of the tables + logical labels).
export interface TransactionView {
  id: string;
  name: string;
  state: number; // choice value — the UI maps it by logical name (domain/states)
  routing: 'sequential' | 'parallel';
  creatorId: string;
  creatorName?: string;
  message?: string;
  sentOn?: string;
  expiresOn?: string;
  completedOn?: string;
}

export interface ParticipantView {
  id: string;
  userId: string;
  name?: string;
  email?: string;
  order?: number;
  state: number;
  signedOn?: string;
  turnActivatedOn?: string;
}

export interface EventView {
  id: string;
  type: number;
  actorName?: string;
  occurredOn: string;
  details?: string;
}

export interface ZoneView {
  id: string;
  participantId: string;
  page: number;
  x: number;
  y: number;
  w: number;
  h: number;
}

export interface SigilApi {
  // Identity (getContext) — never authoritative (doc 05 §9).
  currentUser(): { id?: string; name?: string; upn?: string };

  // Master Signature
  validateMasterSignature(imageBase64: string): Promise<ValidateMasterSignatureOutput>;
  getMasterSignature(): Promise<GetMasterSignatureOutput>;

  // Lifecycle (Custom APIs)
  createTransaction(input: CreateTransactionInput): Promise<string>; // → transactionId
  updateDraft(input: UpdateDraftInput): Promise<void>;
  deleteDraft(txId: string): Promise<void>;
  sendTransaction(txId: string): Promise<void>;
  submitSignature(txId: string): Promise<boolean>; // → IsLastSigner
  rejectTransaction(input: RejectTransactionInput): Promise<void>;
  cancelTransaction(input: CancelTransactionInput): Promise<void>;
  retrySealing(txId: string): Promise<void>;

  // Binaries (base64 — outside the Query cache, doc 05 §5.2)
  getDocumentContent(input: GetDocumentContentInput): Promise<string>; // → PdfBase64

  // Verification
  verifyDocument(input: VerifyDocumentInput): Promise<VerifyDocumentOutput>;

  // Table reads (projections for the screens)
  myPending(): Promise<{ tx: TransactionView; participant: ParticipantView }[]>;
  myRequests(): Promise<TransactionView[]>;
  myParticipations(): Promise<TransactionView[]>;
  getTransaction(txId: string): Promise<TransactionView | undefined>;
  participantsOf(txId: string): Promise<ParticipantView[]>;
  zonesOf(txId: string): Promise<ZoneView[]>;
  eventsOf(txId: string): Promise<EventView[]>;
}
