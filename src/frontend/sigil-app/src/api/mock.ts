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
} from './SigilApi';

// A small fake directory for the people picker in dev (real impl searches Dataverse systemuser).
const FAKE_USERS: UserSummary[] = [
  { id: 'mock-user-0000-0000-000000000002', name: 'Ana Creator', email: 'ana@sigil.local' },
  { id: 'mock-user-0000-0000-000000000003', name: 'Bruno Signer', email: 'bruno@sigil.local' },
  { id: 'mock-user-0000-0000-000000000004', name: 'Carla Reviewer', email: 'carla@sigil.local' },
  { id: 'mock-user-0000-0000-000000000005', name: 'Diego Legal', email: 'diego@sigil.local' },
  { id: 'mock-user-0000-0000-000000000006', name: 'Elena Finance', email: 'elena@sigil.local' },
];

// A minimal single-page PDF (Letter) so the Sign viewer can render a real document in dev.
const SAMPLE_PDF =
  'JVBERi0xLjQKMSAwIG9iago8PC9UeXBlL0NhdGFsb2cvUGFnZXMgMiAwIFI+PgplbmRvYmoKMiAwIG9iago8PC9UeXBlL1BhZ2VzL0tpZHNbMyAwIFJdL0NvdW50IDE+PgplbmRvYmoKMyAwIG9iago8PC9UeXBlL1BhZ2UvUGFyZW50IDIgMCBSL01lZGlhQm94WzAgMCA2MTIgNzkyXS9SZXNvdXJjZXM8PC9Gb250PDwvRjEgNSAwIFI+Pj4+L0NvbnRlbnRzIDQgMCBSPj4KZW5kb2JqCjQgMCBvYmoKPDwvTGVuZ3RoIDU4Pj4Kc3RyZWFtCkJUIC9GMSAyNCBUZiA3MiA3MDAgVGQgKFNpZ2lsIC0gZG9jdW1lbnRvIGRlIHBydWViYSkgVGogRVQKZW5kc3RyZWFtCmVuZG9iago1IDAgb2JqCjw8L1R5cGUvRm9udC9TdWJ0eXBlL1R5cGUxL0Jhc2VGb250L0hlbHZldGljYT4+CmVuZG9iagp4cmVmCjAgNgowMDAwMDAwMDAwIDY1NTM1IGYgCjAwMDAwMDAwMDkgMDAwMDAgbiAKMDAwMDAwMDA1NCAwMDAwMCBuIAowMDAwMDAwMTA1IDAwMDAwIG4gCjAwMDAwMDAyMTcgMDAwMDAgbiAKMDAwMDAwMDMyMyAwMDAwMCBuIAp0cmFpbGVyCjw8L1NpemUgNi9Sb290IDEgMCBSPj4Kc3RhcnR4cmVmCjM4NgolJUVPRg==';

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

  async searchUsers(query: string): Promise<UserSummary[]> {
    await this.delay();
    const q = query.trim().toLowerCase();
    if (!q) return FAKE_USERS.slice(0, 5);
    return FAKE_USERS.filter((u) => u.name.toLowerCase().includes(q) || (u.email ?? '').toLowerCase().includes(q));
  }

  async validateMasterSignature(imageBase64: string): Promise<ValidateMasterSignatureOutput> {
    await this.delay();
    // the mock accepts any "large" PNG; rejects very small ones as "low quality"
    if (imageBase64.length < 200) {
      return {
        IsValid: false,
        FailureReasons: 'La imagen está borrosa (nitidez baja). Subí una versión más nítida.',
        MetricsJson: '{"alphaRatio":0.9,"rmsContrast":0.1,"laplacianVariance":10}',
      };
    }
    // Echo the uploaded image back as the "normalized" preview (the real backend normalizes to
    // 600×200; the mock just shows what you uploaded so the preview isn't a stretched placeholder).
    this.masterSignature = imageBase64;
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
    return this.docs.get(input.Target) ?? SAMPLE_PDF;
  }

  async verifyDocument(input: VerifyDocumentInput): Promise<VerifyDocumentOutput> {
    await this.delay();
    const meta: VerifyMetadata = {
      found: true,
      ledgerNumber: 'SIGIL-2026-000042',
      sealedOnUtc: new Date().toISOString(),
      finalHashHex: '0'.repeat(64),
      tsaStatus: 'sealed',
      historyIntact: true,
      isIntact: input.Sha256Hash ? input.Sha256Hash === '0'.repeat(64) : null,
      signerSummary: JSON.stringify({
        signers: [{ name: 'Test User', email: 'test@sigil.local', signedOnUtc: new Date().toISOString() }],
        routing: 'parallel',
        completedOnUtc: new Date().toISOString(),
        tsa: { status: 'sealed', tokenGenTimeUtc: new Date().toISOString() },
      }),
    };
    const output: VerifyDocumentOutput = {
      Found: true,
      MetadataJson: JSON.stringify(meta),
      TsaTokenBase64: 'dG9rZW4=',
    };
    if (input.Sha256Hash !== undefined) output.IsIntact = input.Sha256Hash === '0'.repeat(64);
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
