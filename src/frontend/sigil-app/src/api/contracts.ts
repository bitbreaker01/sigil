// Typed contracts for the 16 Custom APIs (EXACT mirror of doc 04 §3.1 and of what the
// backend actually returns). These types are the TS MIRROR of the clients that
// `power-apps add-dataverse-api` generates in `generated/` — when the CLI runs, its types
// must match these (a broken build = broken-contract alarm, doc 05 §10).

// ── JSON contracts (doc 04 §4) ──
export interface ParticipantInput {
  userId: string;
  order?: number; // sequential only
}

export interface ZoneInput {
  userId: string;
  page: number;
  x: number; // % of the visible area, origin top-left (doc 04 §6.1)
  y: number;
  w: number;
  h: number;
}

// ── Inputs/outputs per API ──
export interface CreateTransactionInput {
  Name: string;
  Message?: string;
  RoutingType: 'sequential' | 'parallel';
  ExpirationDays?: number;
  PdfBase64: string;
  ParticipantsJson: string; // JSON.stringify(ParticipantInput[])
  ZonesJson?: string; // JSON.stringify(ZoneInput[])
}
export interface CreateTransactionOutput {
  TransactionId: string;
}

export interface UpdateDraftInput {
  Target: string; // transactionId
  Name?: string;
  Message?: string;
  RoutingType?: 'sequential' | 'parallel';
  ExpirationDays?: number;
  PdfBase64?: string;
  ParticipantsJson?: string;
  ZonesJson?: string;
}

export interface GetDocumentContentInput {
  Target: string;
  DocumentType: 'content' | 'final';
}
export interface GetDocumentContentOutput {
  PdfBase64: string;
}

export interface SubmitSignatureOutput {
  IsLastSigner: boolean;
}

export interface RejectTransactionInput {
  Target: string;
  Reason: string;
}
export interface CancelTransactionInput {
  Target: string;
  Reason?: string;
}

export interface ValidateMasterSignatureOutput {
  IsValid: boolean;
  FailureReasons?: string; // one reason per line
  MetricsJson: string;
  NormalizedImageBase64?: string;
}
export interface GetMasterSignatureOutput {
  ImageBase64?: string;
  ValidatedOn?: string; // ISO UTC
}

// Two verification modes (doc 04 §3.1):
//  - by TransactionId (arrived via QR / Detail): compare the file hash to THAT sealed record;
//    without Sha256Hash ⇒ certificate only.
//  - by Sha256Hash alone (drop any sealed PDF): ledger lookup by hash — the backend finds the
//    sealed record whose finalhash matches (like Adobe/DocuSign). Found ⇒ authentic & intact.
// At least one field must be present.
export interface VerifyDocumentInput {
  TransactionId?: string;
  Sha256Hash?: string; // 64 hex
}
export interface VerifyDocumentOutput {
  Found: boolean;
  IsIntact?: boolean;
  MetadataJson: string;
  TsaTokenBase64?: string;
}

// The metadata that VerifyDocument serializes (doc 04 §3.1 VerifyDocumentPlugin).
export interface VerifyMetadata {
  found: boolean;
  ledgerNumber?: string;
  sealedOnUtc?: string;
  finalHashHex?: string;
  contentHashHex?: string;
  tsaStatus?: 'sealed' | 'pending' | 'none';
  historyIntact?: boolean;
  isIntact?: boolean | null;
  signerSummary?: string;
  verifiedOnUtc?: string;
}

// signersummary from the ledger (doc 04 §4) — shown in the certificate.
export interface SignerSummary {
  signers: { name: string; email: string; signedOnUtc: string | null }[];
  routing: 'sequential' | 'parallel';
  completedOnUtc: string;
  tsa: { status: 'sealed' | 'pending' | 'none'; tokenGenTimeUtc?: string | null };
}
