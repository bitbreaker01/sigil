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
} from './SigilApi';

// A transparent 1x1 PNG in base64 (for the mock normalized preview).
const PNG_1X1 =
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==';

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
    this.masterSignature = PNG_1X1;
    return {
      IsValid: true,
      MetricsJson: '{"alphaRatio":0.4,"rmsContrast":0.9,"laplacianVariance":2000}',
      NormalizedImageBase64: PNG_1X1,
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

    const parsed = JSON.parse(input.ParticipantsJson) as { userId: string; order?: number }[];
    this.participants.set(id, parsed.map((p, i): ParticipantView => {
      const pv: ParticipantView = { id: this.newId(), userId: p.userId, state: 159460000, name: `Signer ${i + 1}` };
      if (p.order !== undefined) pv.order = p.order;
      return pv;
    }));
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

  async getDocumentContent(_input: GetDocumentContentInput): Promise<string> {
    await this.delay();
    return PNG_1X1; // a placeholder — the real viewer receives a base64 PDF
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

  async myPending() {
    await this.delay();
    return [...this.txs.values()]
      .filter((tx) => tx.state === 159460001 || tx.state === 159460002)
      .flatMap((tx) => {
        const me = (this.participants.get(tx.id) ?? []).find(
          (p) => p.userId === this.seed.userId && p.state === 159460001,
        );
        return me ? [{ tx, participant: me }] : [];
      });
  }

  async myRequests() {
    await this.delay();
    return [...this.txs.values()].filter((tx) => tx.creatorId === this.seed.userId);
  }

  async myParticipations() {
    await this.delay();
    return [...this.txs.values()].filter((tx) =>
      (this.participants.get(tx.id) ?? []).some((p) => p.userId === this.seed.userId),
    );
  }

  async getTransaction(txId: string) {
    await this.delay();
    return this.txs.get(txId);
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

  private seedExamples(): void {
    const other = 'mock-user-0000-0000-000000000002';
    const txId = this.newId();
    this.txs.set(txId, {
      id: txId,
      name: 'Services Agreement 2026',
      state: 159460001,
      routing: 'parallel',
      creatorId: other,
      creatorName: 'Ana Creator',
      sentOn: new Date(Date.now() - 2 * 86400_000).toISOString(),
      expiresOn: new Date(Date.now() + 5 * 86400_000).toISOString(),
    });
    this.participants.set(txId, [
      { id: this.newId(), userId: this.seed.userId, name: this.seed.userName, state: 159460001, turnActivatedOn: new Date().toISOString() },
    ]);
    this.events.set(txId, [this.event(159460000, 'Request created'), this.event(159460001, 'Sent for signature')]);
  }
}
