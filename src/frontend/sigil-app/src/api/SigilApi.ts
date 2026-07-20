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

// A signer on a document (Documents screen filter "other participants"). Just enough to filter by.
export interface DocumentParticipantRef {
  userId: string;
  name: string;
}

// A transaction enriched for the Documents screen (Phase 2): the base view PLUS the extra data the
// dedicated screen filters on — real creation timestamp, every signer, and which version of the
// caller's own Master Signature was used to sign it. The dashboard uses the plain TransactionView
// and never pays for these extra reads.
export interface DocumentRow extends TransactionView {
  createdOn?: string; // Dataverse system `createdon` (distinct from sentOn)
  participants: DocumentParticipantRef[]; // every signer on the doc
  mySignatureVersion?: number; // version of MY Master Signature used to sign this doc, if I signed it
}

// Server-side search query (Phase 3): the filters/sort/page the SearchDocuments Custom API applies
// so the client never loads the whole set. `sort` is a DocumentSort string (see documentsModel).
export interface DocumentQuery {
  text?: string;
  creatorId?: string;
  participantIds?: string[]; // AND — the doc must include ALL of these signers
  status?: number;
  signatureVersion?: number;
  sort?: string;
  pageSize?: number;
}

// One page of results. `nextCookie` is an opaque continuation ('' = last page).
export interface DocumentPage {
  rows: DocumentRow[];
  total: number; // full filtered count (server-side)
  nextCookie: string;
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

// A pickable signer (people picker). Real impl: Dataverse systemuser search; mock: fake set.
export interface UserSummary {
  id: string;
  name: string;
  email?: string;
}

// A document signed with a given Master Signature version (doc 03 §4.5).
export interface SignedDocumentRef {
  id: string;
  name: string;
  status: number; // transaction state (choice value)
}

// One version of the user's Master Signature (immutable history, doc 03 §4.5).
export interface MasterSignatureVersion {
  version: number;
  imageBase64: string;
  validatedOn: string; // ISO UTC
  isActive: boolean;
  documents: SignedDocumentRef[]; // documents signed with THIS version
}

// One page of dashboard transactions (Requests / Participations). `nextCookie` chains pages
// ('' = last page). Recent-first, server-side paged so the dashboard never loads everything.
export interface TransactionPage {
  rows: TransactionView[];
  nextCookie: string;
}

// A doc awaiting the caller's signature (Pending tab) — the tx plus the caller's participant row.
export interface PendingItem {
  tx: TransactionView;
  participant: ParticipantView;
}

export interface PendingPage {
  rows: PendingItem[];
  nextCookie: string;
}

export interface SigilApi {
  // Identity (getContext) — never authoritative (doc 05 §9).
  currentUser(): { id?: string; name?: string; upn?: string };
  // The caller's Dataverse systemuserid, resolved asynchronously (real mode maps the Entra objectId
  // from getContext() → systemuser). currentUser() is sync and can't carry it in real mode, so use
  // this to match the caller against participant.userId. UI hint only — the backend enforces (§9).
  getCurrentUserId(): Promise<string | undefined>;

  // People picker (create wizard): search selectable signers.
  searchUsers(query: string): Promise<UserSummary[]>;

  // Master Signature — validate is a PREVIEW (no persist); save COMMITS a new active version.
  // Splitting them lets the UI confirm before the irreversible replacement (RF-02).
  validateMasterSignature(imageBase64: string): Promise<ValidateMasterSignatureOutput>;
  saveMasterSignature(imageBase64: string): Promise<ValidateMasterSignatureOutput>;
  getMasterSignature(): Promise<GetMasterSignatureOutput>;
  // Immutable version history (doc 03 §4.5 — each upload is a new version). Backed by the
  // sanic_sigil_capi_GetMasterSignatureHistory Custom API (returns HistoryJson); mock serves it now.
  getMasterSignatureHistory(): Promise<MasterSignatureVersion[]>;

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
  // Paged (recent-first) Pending for the dashboard's infinite scroll.
  myPendingPage(cookie?: string): Promise<PendingPage>;
  // Paged (recent-first) variants for the dashboard's infinite-scroll lists (§5.1 — don't load all).
  myRequestsPage(cookie?: string): Promise<TransactionPage>;
  // `status` (optional) filters server-side (e.g. completed-only). Pages by participation, so a page
  // may return fewer than the page size after the status filter — infinite scroll keeps loading.
  myParticipationsPage(cookie?: string, status?: number): Promise<TransactionPage>;
  // Documents screen (Phase 3): server-side paged search — the backend filters/sorts/pages so the
  // client loads one page at a time. `cookie` chains pages (undefined = first page).
  searchDocuments(query: DocumentQuery, cookie?: string): Promise<DocumentPage>;
  getTransaction(txId: string): Promise<TransactionView | undefined>;
  participantsOf(txId: string): Promise<ParticipantView[]>;
  zonesOf(txId: string): Promise<ZoneView[]>;
  eventsOf(txId: string): Promise<EventView[]>;
}
