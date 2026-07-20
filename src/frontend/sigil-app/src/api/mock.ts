// MOCK implementation of SigilApi (doc 11 §2 — declared limit: it does not replace the smokes
// against Dev). In-memory data that mimics the real backend's behavior just enough to develop
// and test the UI without an environment. It validates NO security (that's the backend's job) —
// it only produces responses with the correct shape.

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
  VerifyMetadata,
} from './contracts';
import type {
  SigilApi,
  TransactionView,
  ParticipantView,
  EventView,
  ZoneView,
  UserSummary,
  MasterSignatureVersion,
  DocumentRow,
  DocumentQuery,
  DocumentPage,
  TransactionPage,
  PendingPage,
} from './SigilApi';
import { base64ToBytes, sha256Hex } from './binaries';

// Mirrors the backend SearchDocuments sort (missing dates last) — mock-only, in-memory.
function sortMockDocs(rows: DocumentRow[], sort: string): DocumentRow[] {
  const byDate = (pick: (r: DocumentRow) => string | undefined, asc: boolean) => {
    const withD = rows.filter((r) => pick(r));
    const without = rows.filter((r) => !pick(r));
    withD.sort((a, b) => (asc ? 1 : -1) * (pick(a)! < pick(b)! ? -1 : pick(a)! > pick(b)! ? 1 : 0));
    return [...withD, ...without];
  };
  switch (sort) {
    case 'nameAsc': return [...rows].sort((a, b) => a.name.localeCompare(b.name));
    case 'nameDesc': return [...rows].sort((a, b) => b.name.localeCompare(a.name));
    case 'sentAsc': return byDate((r) => r.sentOn, true);
    case 'sentDesc': return byDate((r) => r.sentOn, false);
    case 'completedAsc': return byDate((r) => r.completedOn, true);
    case 'completedDesc': return byDate((r) => r.completedOn, false);
    case 'createdAsc': return byDate((r) => r.createdOn, true);
    default: return byDate((r) => r.createdOn, false); // createdDesc
  }
}

// A small fake directory for the people picker in dev (real impl searches Dataverse systemuser).
const FAKE_USERS: UserSummary[] = [
  { id: 'mock-user-0000-0000-000000000002', name: 'Ana Creator', email: 'ana@sigil.local' },
  { id: 'mock-user-0000-0000-000000000003', name: 'Bruno Signer', email: 'bruno@sigil.local' },
  { id: 'mock-user-0000-0000-000000000004', name: 'Carla Reviewer', email: 'carla@sigil.local' },
  { id: 'mock-user-0000-0000-000000000005', name: 'Diego Legal', email: 'diego@sigil.local' },
  { id: 'mock-user-0000-0000-000000000006', name: 'Elena Finance', email: 'elena@sigil.local' },
];

// A minimal single-page PDF (Letter) so the Sign viewer can render a real document in dev.
// This is the ORIGINAL (content) — the document before signatures.
const SAMPLE_PDF =
  'JVBERi0xLjQKMSAwIG9iago8PC9UeXBlL0NhdGFsb2cvUGFnZXMgMiAwIFI+PgplbmRvYmoKMiAwIG9iago8PC9UeXBlL1BhZ2VzL0tpZHNbMyAwIFJdL0NvdW50IDE+PgplbmRvYmoKMyAwIG9iago8PC9UeXBlL1BhZ2UvUGFyZW50IDIgMCBSL01lZGlhQm94WzAgMCA2MTIgNzkyXS9SZXNvdXJjZXM8PC9Gb250PDwvRjEgNSAwIFI+Pj4+L0NvbnRlbnRzIDQgMCBSPj4KZW5kb2JqCjQgMCBvYmoKPDwvTGVuZ3RoIDU4Pj4Kc3RyZWFtCkJUIC9GMSAyNCBUZiA3MiA3MDAgVGQgKFNpZ2lsIC0gZG9jdW1lbnRvIGRlIHBydWViYSkgVGogRVQKZW5kc3RyZWFtCmVuZG9iago1IDAgb2JqCjw8L1R5cGUvRm9udC9TdWJ0eXBlL1R5cGUxL0Jhc2VGb250L0hlbHZldGljYT4+CmVuZG9iagp4cmVmCjAgNgowMDAwMDAwMDAwIDY1NTM1IGYgCjAwMDAwMDAwMDkgMDAwMDAgbiAKMDAwMDAwMDA1NCAwMDAwMCBuIAowMDAwMDAwMTA1IDAwMDAwIG4gCjAwMDAwMDAyMTcgMDAwMDAgbiAKMDAwMDAwMDMyMyAwMDAwMCBuIAp0cmFpbGVyCjw8L1NpemUgNi9Sb290IDEgMCBSPj4Kc3RhcnR4cmVmCjM4NgolJUVPRg==';

// The FINAL (sealed) version — visibly different text so the "signed" vs "original" toggle shows a
// real difference in dev. The real backend COMPOSES the signatures onto the content and seals it;
// the mock can't render signatures, so it serves this distinct placeholder. Verify targets THIS
// (the sealed document you download and check), so its SHA-256 is the mock's "finalhash".
const SAMPLE_FINAL_PDF =
  'JVBERi0xLjQKMSAwIG9iago8PC9UeXBlL0NhdGFsb2cvUGFnZXMgMiAwIFI+PgplbmRvYmoKMiAwIG9iago8PC9UeXBlL1BhZ2VzL0tpZHNbMyAwIFJdL0NvdW50IDE+PgplbmRvYmoKMyAwIG9iago8PC9UeXBlL1BhZ2UvUGFyZW50IDIgMCBSL01lZGlhQm94WzAgMCA2MTIgNzkyXS9SZXNvdXJjZXM8PC9Gb250PDwvRjEgNSAwIFI+Pj4+L0NvbnRlbnRzIDQgMCBSPj4KZW5kb2JqCjQgMCBvYmoKPDwvTGVuZ3RoIDIwMD4+CnN0cmVhbQpCVCAvRjEgMjAgVGYgNzIgNzIwIFRkIChTaWdpbCAtIERPQ1VNRU5UTyBGSVJNQURPKSBUaiAwIC0yOCBUZCAoRmlybWFkbyBwb3I6IFRlc3QgVXNlciAodGVzdEBzaWdpbC5sb2NhbCkpIFRqIDAgLTI4IFRkIChTZWxsYWRvOiBTSUdJTC0yMDI2LTAwMDA0MikgVGogMCAtMjggVGQgKE1hcmNhIGRlIHRpZW1wbzogc2VsbGFkbyBjb24gVFNBKSBUaiBFVAplbmRzdHJlYW0KZW5kb2JqCjUgMCBvYmoKPDwvVHlwZS9Gb250L1N1YnR5cGUvVHlwZTEvQmFzZUZvbnQvSGVsdmV0aWNhPj4KZW5kb2JqCnhyZWYKMCA2CjAwMDAwMDAwMDAgNjU1MzUgZiAKMDAwMDAwMDAwOSAwMDAwMCBuIAowMDAwMDAwMDU0IDAwMDAwIG4gCjAwMDAwMDAxMDUgMDAwMDAgbiAKMDAwMDAwMDIxNyAwMDAwMCBuIAowMDAwMDAwNDY2IDAwMDAwIG4gCnRyYWlsZXIKPDwvU2l6ZSA2L1Jvb3QgMSAwIFI+PgpzdGFydHhyZWYKNTI5CiUlRU9G';

interface Seed {
  userId: string;
  userName: string;
  userUpn: string;
}

export class MockSigilApi implements SigilApi {
  private readonly txs = new Map<string, TransactionView>();
  private readonly participants = new Map<string, ParticipantView[]>();
  private readonly zones = new Map<string, ZoneView[]>();
  private readonly events = new Map<string, EventView[]>();
  private readonly docs = new Map<string, string>(); // txId → PdfBase64 (kept out of the app's Query cache)
  private masterSignature: string | undefined;
  private readonly signatureVersions: MasterSignatureVersion[] = []; // immutable history (doc 03 §4.5)
  private seq = 1;

  constructor(private readonly seed: Seed = {
    userId: 'mock-user-0000-0000-000000000001',
    userName: 'Test User',
    userUpn: 'test@sigil.local',
  }) {
    this.seedExamples();
  }

  currentUser() {
    return { id: this.seed.userId, name: this.seed.userName, upn: this.seed.userUpn };
  }

  async getCurrentUserId(): Promise<string> {
    return this.seed.userId;
  }

  async searchUsers(query: string): Promise<UserSummary[]> {
    await this.delay();
    const q = query.trim().toLowerCase();
    if (!q) return FAKE_USERS.slice(0, 5);
    return FAKE_USERS.filter((u) => u.name.toLowerCase().includes(q) || (u.email ?? '').toLowerCase().includes(q));
  }

  // Preview only (RF-02): validate WITHOUT persisting — the UI confirms before replacing.
  async validateMasterSignature(imageBase64: string): Promise<ValidateMasterSignatureOutput> {
    await this.delay();
    return this.validateSignature(imageBase64);
  }

  // Commit: validate again and, if valid, create the new immutable active version (doc 03 §4.5).
  async saveMasterSignature(imageBase64: string): Promise<ValidateMasterSignatureOutput> {
    await this.delay();
    const r = this.validateSignature(imageBase64);
    if (!r.IsValid) return r; // invalid → never persist
    this.signatureVersions.forEach((v) => (v.isActive = false));
    this.signatureVersions.push({
      version: this.signatureVersions.length + 1,
      imageBase64,
      validatedOn: new Date().toISOString(),
      isActive: true,
      documents: [], // the real backend fills this from participant.masterSignatureId
    });
    this.masterSignature = imageBase64;
    return r;
  }

  // the mock accepts any "large" PNG; rejects very small ones as "low quality". Echoes the image
  // back as the "normalized" preview (the real backend normalizes to 600×200).
  private validateSignature(imageBase64: string): ValidateMasterSignatureOutput {
    if (imageBase64.length < 200) {
      return {
        IsValid: false,
        FailureReasons: 'La imagen está borrosa (nitidez baja). Subí una versión más nítida.',
        MetricsJson: '{"alphaRatio":0.9,"rmsContrast":0.1,"laplacianVariance":10}',
      };
    }
    return {
      IsValid: true,
      MetricsJson: '{"alphaRatio":0.4,"rmsContrast":0.9,"laplacianVariance":2000}',
      NormalizedImageBase64: imageBase64,
    };
  }

  async getMasterSignature(): Promise<GetMasterSignatureOutput> {
    await this.delay();
    return this.masterSignature
      ? { ImageBase64: this.masterSignature, ValidatedOn: new Date().toISOString() }
      : {};
  }

  async getMasterSignatureHistory(): Promise<MasterSignatureVersion[]> {
    await this.delay();
    return [...this.signatureVersions].reverse().map((v) => ({ ...v })); // newest first, copies
  }

  async createTransaction(input: CreateTransactionInput): Promise<string> {
    await this.delay();
    const id = this.newId();
    const tx: TransactionView = {
      id,
      name: input.Name,
      state: 159460000, // Draft
      routing: input.RoutingType,
      creatorId: this.seed.userId,
      creatorName: this.seed.userName,
    };
    if (input.Message !== undefined) tx.message = input.Message;
    this.txs.set(id, tx);
    this.docs.set(id, input.PdfBase64);

    const parsed = JSON.parse(input.ParticipantsJson) as { userId: string; order?: number }[];
    const parts = parsed.map((p, i): ParticipantView => {
      const pv: ParticipantView = { id: this.newId(), userId: p.userId, state: 159460000, name: `Signer ${i + 1}` };
      if (p.order !== undefined) pv.order = p.order;
      return pv;
    });
    this.participants.set(id, parts);

    // Zones arrive by userId (ZonesJson); the view links to the participant record (participantId).
    if (input.ZonesJson) {
      const zs = JSON.parse(input.ZonesJson) as { userId: string; page: number; x: number; y: number; w: number; h: number }[];
      this.zones.set(id, zs.flatMap((z) => {
        const part = parts.find((p) => p.userId === z.userId);
        return part ? [{ id: this.newId(), participantId: part.id, page: z.page, x: z.x, y: z.y, w: z.w, h: z.h }] : [];
      }));
    }
    this.events.set(id, [this.event(159460000, 'Request created')]);
    return id;
  }

  async updateDraft(_input: UpdateDraftInput): Promise<void> {
    await this.delay();
  }

  async deleteDraft(txId: string): Promise<void> {
    await this.delay();
    this.txs.delete(txId);
  }

  async sendTransaction(txId: string): Promise<void> {
    await this.delay();
    const tx = this.txs.get(txId);
    if (tx) {
      tx.state = 159460001; // Pending signature
      tx.sentOn = new Date().toISOString();
      tx.expiresOn = new Date(Date.now() + 7 * 86400_000).toISOString();
      const parts = this.participants.get(txId) ?? [];
      parts.forEach((p) => {
        if (tx.routing === 'parallel' || p.order === 1) {
          p.state = 159460001;
          p.turnActivatedOn = new Date().toISOString();
        }
      });
      this.events.get(txId)?.push(this.event(159460001, 'Sent for signature'));
    }
  }

  async submitSignature(txId: string): Promise<boolean> {
    await this.delay();
    const parts = this.participants.get(txId) ?? [];
    const me = parts.find((p) => p.userId === this.seed.userId);
    if (me) {
      me.state = 159460002; // Signed
      me.signedOn = new Date().toISOString();
    }
    const last = parts.every((p) => p.state === 159460002);
    const tx = this.txs.get(txId);
    if (tx) tx.state = last ? 159460003 : 159460002;
    this.events.get(txId)?.push(this.event(159460002, 'Signature registered'));
    return last;
  }

  async rejectTransaction(input: RejectTransactionInput): Promise<void> {
    await this.delay();
    const tx = this.txs.get(input.Target);
    if (tx) tx.state = 159460005;
    this.events.get(input.Target)?.push(this.event(159460003, `Rejected: ${input.Reason}`));
  }

  async cancelTransaction(input: CancelTransactionInput): Promise<void> {
    await this.delay();
    const tx = this.txs.get(input.Target);
    if (tx) tx.state = 159460008;
    this.events.get(input.Target)?.push(this.event(159460011, 'Cancelled by creator'));
  }

  async retrySealing(txId: string): Promise<void> {
    await this.delay();
    const tx = this.txs.get(txId);
    if (tx) tx.state = 159460003;
  }

  async getDocumentContent(input: GetDocumentContentInput): Promise<string> {
    await this.delay();
    // 'final' → the signed/sealed version; 'content' → the original uploaded document.
    if (input.DocumentType === 'final') return SAMPLE_FINAL_PDF;
    return this.docs.get(input.Target) ?? SAMPLE_PDF;
  }

  async verifyDocument(input: VerifyDocumentInput): Promise<VerifyDocumentOutput> {
    await this.delay();

    // Mode A — ledger lookup by hash (no txId): the user dropped a sealed PDF and we find the
    // matching sealed record by its SHA-256, like Adobe/DocuSign (no QR needed). The real backend
    // queries sanic_sigil_finalhash. Found by exact hash ⇒ authentic & intact; else not found.
    if (!input.TransactionId) {
      const target = input.Sha256Hash?.toUpperCase();
      const hit = target ? await this.findSealedByHash(target) : undefined;
      if (!hit) return { Found: false, MetadataJson: JSON.stringify({ found: false } satisfies VerifyMetadata) };
      return this.verifyOutput(hit.hash, true, true);
    }

    // Mode B — transaction-scoped verify (QR / detail): compare the uploaded file's hash to THIS
    // tx's sealed hash — the SHA-256 of the FINAL (sealed) document (re-uploading it ⇒ GREEN, any
    // other file ⇒ RED). Without a hash ⇒ certificate only (no verdict).
    const realHash = await this.sealedHash();
    const intact = input.Sha256Hash ? input.Sha256Hash.toUpperCase() === realHash : null;
    return this.verifyOutput(realHash, intact, input.Sha256Hash !== undefined);
  }

  /** SHA-256 (64-hex uppercase) of the FINAL/sealed document — the one you download and verify. */
  private async sealedHash(): Promise<string> {
    return sha256Hex((await base64ToBytes(SAMPLE_FINAL_PDF)).buffer);
  }

  /** Ledger lookup: a COMPLETED tx whose sealed document hashes to `target`. */
  private async findSealedByHash(target: string): Promise<{ txId: string; hash: string } | undefined> {
    const hash = await this.sealedHash(); // in the mock every sealed tx shares the same final
    if (hash !== target) return undefined;
    for (const [id, tx] of this.txs) if (tx.state === 159460004) return { txId: id, hash };
    return undefined;
  }

  /** Builds the VerifyDocument response. `includeVerdict` adds IsIntact (a file was compared). */
  private verifyOutput(finalHash: string, intact: boolean | null, includeVerdict: boolean): VerifyDocumentOutput {
    const now = new Date().toISOString();
    const meta: VerifyMetadata = {
      found: true,
      ledgerNumber: 'SIGIL-2026-000042',
      sealedOnUtc: now,
      finalHashHex: finalHash,
      tsaStatus: 'sealed',
      historyIntact: true,
      isIntact: intact,
      signerSummary: JSON.stringify({
        signers: [{ name: 'Test User', email: 'test@sigil.local', signedOnUtc: now }],
        routing: 'parallel',
        completedOnUtc: now,
        tsa: { status: 'sealed', tokenGenTimeUtc: now },
      }),
    };
    const output: VerifyDocumentOutput = { Found: true, MetadataJson: JSON.stringify(meta), TsaTokenBase64: 'dG9rZW4=' };
    if (includeVerdict) output.IsIntact = intact === true;
    return output;
  }

  // Reads return fresh COPIES (like a real backend returning new JSON each call) so callers —
  // e.g. TanStack Query's structural sharing — correctly detect changes after a mutation.
  async myPending() {
    await this.delay();
    return [...this.txs.values()]
      .filter((tx) => tx.state === 159460001 || tx.state === 159460002)
      .flatMap((tx) => {
        const me = (this.participants.get(tx.id) ?? []).find(
          (p) => p.userId === this.seed.userId && p.state === 159460001,
        );
        return me ? [{ tx: { ...tx }, participant: { ...me } }] : [];
      });
  }

  async myRequests() {
    await this.delay();
    return [...this.txs.values()].filter((tx) => tx.creatorId === this.seed.userId).map((tx) => ({ ...tx }));
  }

  async myParticipations() {
    await this.delay();
    return [...this.txs.values()]
      .filter((tx) => (this.participants.get(tx.id) ?? []).some((p) => p.userId === this.seed.userId))
      .map((tx) => ({ ...tx }));
  }

  async myPendingPage(cookie?: string): Promise<PendingPage> {
    await this.delay();
    const all = [...this.txs.values()]
      .filter((tx) => tx.state === 159460001 || tx.state === 159460002)
      .flatMap((tx) => {
        const me = (this.participants.get(tx.id) ?? []).find((p) => p.userId === this.seed.userId && p.state === 159460001);
        return me ? [{ tx: { ...tx }, participant: { ...me } }] : [];
      });
    const size = 25;
    const offset = cookie ? Number.parseInt(cookie, 10) || 0 : 0;
    return { rows: all.slice(offset, offset + size), nextCookie: offset + size < all.length ? String(offset + size) : '' };
  }

  async myRequestsPage(cookie?: string): Promise<TransactionPage> {
    await this.delay();
    return this.pageOf([...this.txs.values()].filter((tx) => tx.creatorId === this.seed.userId), cookie);
  }

  async myParticipationsPage(cookie?: string, status?: number): Promise<TransactionPage> {
    await this.delay();
    const mine = [...this.txs.values()]
      .filter((tx) => (this.participants.get(tx.id) ?? []).some((p) => p.userId === this.seed.userId))
      .filter((tx) => status == null || tx.state === status);
    return this.pageOf(mine, cookie);
  }

  private pageOf(all: TransactionView[], cookie?: string): TransactionPage {
    const size = 25;
    const offset = cookie ? Number.parseInt(cookie, 10) || 0 : 0;
    const rows = all.slice(offset, offset + size).map((tx) => ({ ...tx }));
    return { rows, nextCookie: offset + size < all.length ? String(offset + size) : '' };
  }

  async myDocuments(): Promise<DocumentRow[]> {
    await this.delay();
    const me = this.seed.userId;
    return [...this.txs.values()]
      .filter((tx) => tx.creatorId === me || (this.participants.get(tx.id) ?? []).some((p) => p.userId === me))
      .map((tx) => {
        const parts = this.participants.get(tx.id) ?? [];
        const mine = parts.find((p) => p.userId === me);
        const row: DocumentRow = {
          ...tx,
          participants: parts.map((p) => ({ userId: p.userId, name: p.name ?? p.userId })),
        };
        // No dedicated createdOn in the mock store — fall back to sentOn so the created sort/filter demos.
        if (tx.sentOn) row.createdOn = tx.sentOn;
        // The mock has no participant.masterSignatureId; approximate: if I signed, it was v1.
        if (mine && mine.state === 159460002 && this.signatureVersions.length) row.mySignatureVersion = 1;
        return row;
      });
  }

  async searchDocuments(query: DocumentQuery, cookie?: string): Promise<DocumentPage> {
    const all = await this.myDocuments(); // full enriched set (dev only, small) — then filter/sort/page
    let rows = all;
    const text = query.text?.trim().toLowerCase();
    if (text) rows = rows.filter((r) => r.name.toLowerCase().includes(text));
    if (query.creatorId) rows = rows.filter((r) => r.creatorId === query.creatorId);
    if (query.status != null) rows = rows.filter((r) => r.state === query.status);
    if (query.participantIds?.length) {
      rows = rows.filter((r) => query.participantIds!.every((id) => r.participants.some((p) => p.userId === id)));
    }
    if (query.signatureVersion != null) rows = rows.filter((r) => r.mySignatureVersion === query.signatureVersion);
    rows = sortMockDocs(rows, query.sort ?? 'createdDesc');

    const total = rows.length;
    const pageSize = query.pageSize ?? 25;
    const offset = cookie ? Number.parseInt(cookie, 10) || 0 : 0;
    const page = rows.slice(offset, offset + pageSize);
    const nextCookie = offset + pageSize < total ? String(offset + pageSize) : '';
    return { rows: page, total, nextCookie };
  }

  async getTransaction(txId: string) {
    await this.delay();
    const tx = this.txs.get(txId);
    return tx ? { ...tx } : undefined;
  }

  async participantsOf(txId: string) {
    await this.delay();
    return this.participants.get(txId) ?? [];
  }

  async zonesOf(txId: string) {
    await this.delay();
    return this.zones.get(txId) ?? [];
  }

  async eventsOf(txId: string) {
    await this.delay();
    return this.events.get(txId) ?? [];
  }

  // ── internal ──
  private newId(): string {
    return `mock-${(this.seq++).toString().padStart(12, '0')}`;
  }

  private event(type: number, details: string): EventView {
    return { id: this.newId(), type, actorName: 'System', occurredOn: new Date().toISOString(), details };
  }

  private delay(): Promise<void> {
    return new Promise((r) => setTimeout(r, 120));
  }

  private seedTx(
    tx: Omit<TransactionView, 'id'>,
    parts: Omit<ParticipantView, 'id'>[],
    zones: { userId: string; page: number; x: number; y: number; w: number; h: number }[] = [],
  ): void {
    const id = this.newId();
    this.txs.set(id, { ...tx, id });
    const participants = parts.map((p) => ({ ...p, id: this.newId() }));
    this.participants.set(id, participants);
    if (zones.length) {
      this.zones.set(id, zones.flatMap((z) => {
        const part = participants.find((p) => p.userId === z.userId);
        return part ? [{ id: this.newId(), participantId: part.id, page: z.page, x: z.x, y: z.y, w: z.w, h: z.h }] : [];
      }));
    }
    this.events.set(id, [this.event(159460000, 'Request created')]);
  }

  // A varied fixture so the dashboard's three tabs demo every case (pending/sealing/error/done).
  private seedExamples(): void {
    const me = this.seed.userId, meName = this.seed.userName;
    const ana = 'mock-user-0000-0000-000000000002';
    const now = Date.now();
    const iso = (ms: number) => new Date(ms).toISOString();

    // Pending by my signature (I'm an active-turn signer) — one comfortable, one urgent (<24h).
    this.seedTx(
      { name: 'Services Agreement 2026', state: 159460001, routing: 'parallel', creatorId: ana, creatorName: 'Ana Creator', sentOn: iso(now - 2 * 86400_000), expiresOn: iso(now + 5 * 86400_000) },
      [
        { userId: me, name: meName, state: 159460001, turnActivatedOn: iso(now) },
        { userId: ana, name: 'Ana Creator', state: 159460002, signedOn: iso(now - 86400_000) },
      ],
      [{ userId: me, page: 1, x: 12, y: 78, w: 30, h: 10 }, { userId: ana, page: 1, x: 55, y: 78, w: 30, h: 10 }],
    );
    this.seedTx(
      { name: 'NDA — Project Falcon', state: 159460001, routing: 'sequential', creatorId: ana, creatorName: 'Ana Creator', sentOn: iso(now - 6 * 86400_000), expiresOn: iso(now + 12 * 3600_000) },
      [{ userId: me, name: meName, order: 1, state: 159460001, turnActivatedOn: iso(now) }],
      [{ userId: me, page: 1, x: 30, y: 60, w: 30, h: 10 }],
    );

    // My requests (created by me) — Sealing, Sealing Error, Completed.
    this.seedTx(
      { name: 'Vendor Contract Q3', state: 159460003, routing: 'parallel', creatorId: me, creatorName: meName, sentOn: iso(now - 3600_000) },
      [{ userId: ana, name: 'Ana Creator', state: 159460002, signedOn: iso(now - 600_000) }],
    );
    this.seedTx(
      { name: 'Board Resolution 12', state: 159460007, routing: 'parallel', creatorId: me, creatorName: meName, sentOn: iso(now - 7200_000) },
      [{ userId: ana, name: 'Ana Creator', state: 159460002, signedOn: iso(now - 3600_000) }],
    );
    this.seedTx(
      { name: 'Employment Offer — R. Diaz', state: 159460004, routing: 'parallel', creatorId: me, creatorName: meName, sentOn: iso(now - 10 * 86400_000), completedOn: iso(now - 8 * 86400_000) },
      [
        { userId: ana, name: 'Ana Creator', state: 159460002, signedOn: iso(now - 8 * 86400_000) },
        { userId: me, name: meName, state: 159460002, signedOn: iso(now - 9 * 86400_000) },
      ],
    );
  }
}
