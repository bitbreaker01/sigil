// REAL implementation of SigilApi over @microsoft/power-apps.
//
// ⚠️ INTEGRATION SEAM: calls to Custom APIs are made through the TYPED CLIENTS that
// `power-apps add-dataverse-api sanic_sigil_capi_*` generates (generated/ folder,
// doc 05 §2 — do NOT edit by hand). Since this environment has no CLI, generation happens
// in the team's environment. Each method here is a one-liner that delegates to its generated
// client; below is the exact pattern with getClient().executeAsync and the hook point is
// marked. Until generated/ exists, the app runs with MockSigilApi (api/index.ts) — the rest
// of the code doesn't change.

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
import type {
  SigilApi,
  TransactionView,
  ParticipantView,
  EventView,
  ZoneView,
  UserSummary,
} from './SigilApi';

// Schema names (doc 12) — the only schema strings in the frontend, centralized here.
const CAPI = {
  create: 'sanic_sigil_capi_CreateTransaction',
  update: 'sanic_sigil_capi_UpdateDraft',
  del: 'sanic_sigil_capi_DeleteDraft',
  send: 'sanic_sigil_capi_SendTransaction',
  submit: 'sanic_sigil_capi_SubmitSignature',
  reject: 'sanic_sigil_capi_RejectTransaction',
  cancel: 'sanic_sigil_capi_CancelTransaction',
  retry: 'sanic_sigil_capi_RetrySealing',
  getDoc: 'sanic_sigil_capi_GetDocumentContent',
  validateFm: 'sanic_sigil_capi_ValidateMasterSignature',
  getFm: 'sanic_sigil_capi_GetMasterSignature',
  verify: 'sanic_sigil_capi_VerifyDocument',
} as const;

export class PowerAppsSigilApi implements SigilApi {
  // Identity is NOT taken from here: the app shell resolves getContext() (async) ONCE at
  // startup and provides it via PowerProvider (doc 05 §9 — never authoritative). This method
  // exists for the seam contract; in production the UI uses the React context.
  currentUser() {
    return {};
  }

  // People picker: a systemuser datasource search (pac code add-data-source systemuser).
  async searchUsers(_query: string): Promise<UserSummary[]> {
    throw new Error(PENDING_DATASOURCE);
  }

  async validateMasterSignature(imageBase64: string): Promise<ValidateMasterSignatureOutput> {
    return this.execute(CAPI.validateFm, { ImageBase64: imageBase64 });
  }
  async getMasterSignature(): Promise<GetMasterSignatureOutput> {
    return this.execute(CAPI.getFm, {});
  }
  async createTransaction(input: CreateTransactionInput): Promise<string> {
    return (await this.execute<{ TransactionId: string }>(CAPI.create, input)).TransactionId;
  }
  async updateDraft(input: UpdateDraftInput): Promise<void> {
    await this.executeBound(CAPI.update, input.Target, input);
  }
  async deleteDraft(txId: string): Promise<void> {
    await this.executeBound(CAPI.del, txId, {});
  }
  async sendTransaction(txId: string): Promise<void> {
    await this.executeBound(CAPI.send, txId, {});
  }
  async submitSignature(txId: string): Promise<boolean> {
    return (await this.executeBound<{ IsLastSigner: boolean }>(CAPI.submit, txId, {})).IsLastSigner;
  }
  async rejectTransaction(input: RejectTransactionInput): Promise<void> {
    await this.executeBound(CAPI.reject, input.Target, { Reason: input.Reason });
  }
  async cancelTransaction(input: CancelTransactionInput): Promise<void> {
    await this.executeBound(CAPI.cancel, input.Target, { Reason: input.Reason });
  }
  async retrySealing(txId: string): Promise<void> {
    await this.executeBound(CAPI.retry, txId, {});
  }
  async getDocumentContent(input: GetDocumentContentInput): Promise<string> {
    return (await this.executeBound<{ PdfBase64: string }>(CAPI.getDoc, input.Target, {
      DocumentType: input.DocumentType,
    })).PdfBase64;
  }
  async verifyDocument(input: VerifyDocumentInput): Promise<VerifyDocumentOutput> {
    return this.execute(CAPI.verify, input);
  }

  // ── Table reads: go through the tabular clients generated with
  //    `pac code add-data-source` (doc 05 §1). The pattern is client.retrieveMultipleRecordsAsync.
  //    Implemented once those datasources exist; until then, MockSigilApi covers dev. ──
  async myPending(): Promise<{ tx: TransactionView; participant: ParticipantView }[]> {
    throw new Error(PENDING_DATASOURCE);
  }
  async myRequests(): Promise<TransactionView[]> {
    throw new Error(PENDING_DATASOURCE);
  }
  async myParticipations(): Promise<TransactionView[]> {
    throw new Error(PENDING_DATASOURCE);
  }
  async getTransaction(_txId: string): Promise<TransactionView | undefined> {
    throw new Error(PENDING_DATASOURCE);
  }
  async participantsOf(_txId: string): Promise<ParticipantView[]> {
    throw new Error(PENDING_DATASOURCE);
  }
  async zonesOf(_txId: string): Promise<ZoneView[]> {
    throw new Error(PENDING_DATASOURCE);
  }
  async eventsOf(_txId: string): Promise<EventView[]> {
    throw new Error(PENDING_DATASOURCE);
  }

  // ── invocation plumbing ──
  // Unbound: direct parameters. The generated client builds the dataverseRequest envelope;
  // here we show the SDK's standard hook point.
  private async execute<TOut>(_api: string, _input: unknown): Promise<TOut> {
    throw new Error(PENDING_GENERATED);
    // With generated/: return generated[_api](this.client, _input);
  }
  // Bound: the first parameter is the Target (EntityReference to the transaction).
  private async executeBound<TOut>(_api: string, _targetId: string, _input: unknown): Promise<TOut> {
    throw new Error(PENDING_GENERATED);
  }
}

const PENDING_GENERATED =
  'Typed client not generated. Run in the environment: `power-apps add-dataverse-api <capi>` (doc 05 §2). ' +
  'In the meantime the app uses MockSigilApi.';
const PENDING_DATASOURCE =
  'Tabular datasource not generated. Run `pac code add-data-source <table>` (doc 05 §1).';
