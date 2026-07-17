// Read-only inline document viewer. Fetches the PDF DIRECTLY through the seam and keeps it in
// screen-local state (freed on unmount) — never in the Query cache (doc 05 §5.2). Used to review a
// transaction's document from the detail screen (RF-24) without leaving the app.

import { useEffect, useRef, useState } from 'react';
import { Spinner, MessageBar, MessageBarBody } from '@fluentui/react-components';
import { useT } from '../i18n/useT';
import { sigilApi } from '../api';
import type { GetDocumentContentInput } from '../api/contracts';
import { usePdfDocument } from './usePdfDocument';
import { PdfViewer } from './PdfViewer';

export function DocumentView(props: { txId: string; documentType: GetDocumentContentInput['DocumentType'] }): JSX.Element {
  const { t } = useT();
  const [state, setState] = useState<{ phase: 'loading' | 'error' | 'ready'; base64?: string }>({ phase: 'loading' });
  const alive = useRef(true);

  useEffect(() => {
    alive.current = true;
    setState({ phase: 'loading' });
    sigilApi.getDocumentContent({ Target: props.txId, DocumentType: props.documentType })
      .then((base64) => { if (alive.current) setState({ phase: 'ready', base64 }); })
      .catch(() => { if (alive.current) setState({ phase: 'error' }); });
    return () => { alive.current = false; };
  }, [props.txId, props.documentType]);

  const pdf = usePdfDocument(state.base64);

  if (state.phase === 'loading' || pdf.phase === 'loading' || pdf.phase === 'idle') return <Spinner label={t('common.loading')} />;
  if (state.phase === 'error' || pdf.phase === 'error') return <MessageBar intent="error"><MessageBarBody>{t('common.genericError')}</MessageBarBody></MessageBar>;
  return <PdfViewer doc={pdf.doc} pageCount={pdf.pageCount} />;
}
