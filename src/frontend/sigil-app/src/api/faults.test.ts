import { describe, it, expect } from 'vitest';
import { dataverseFaultMessage } from './faults';

describe('dataverseFaultMessage', () => {
  it('extracts the nested Dataverse Web API fault message (plugin fault shape)', () => {
    const fault = {
      error: {
        code: '0x80040265',
        message: 'La imagen supera el tamaño máximo de carga de 1500 KB.',
      },
    };
    expect(dataverseFaultMessage(fault)).toBe(
      'La imagen supera el tamaño máximo de carga de 1500 KB.',
    );
  });

  it('falls back to a flat message when there is no nested error', () => {
    expect(dataverseFaultMessage({ message: 'Something went wrong' })).toBe('Something went wrong');
  });

  it('parses a JSON-encoded fault held as a string inside .message (real SDK shape)', () => {
    // The SDK wraps the whole OData fault as a JSON string; unicode is escaped in that string.
    const raw =
      '{"error":{"code":"0x80040265","message":"La imagen supera el tama\\u00f1o m\\u00e1ximo de carga de 1500 KB.","@Microsoft.PowerApps.CDS.HelpLink":"http://x"}}';
    expect(dataverseFaultMessage({ message: raw })).toBe(
      'La imagen supera el tamaño máximo de carga de 1500 KB.',
    );
  });

  it('parses a JSON fault passed directly as a string', () => {
    const raw = '{"error":{"code":"0x1","message":"boom"}}';
    expect(dataverseFaultMessage(raw)).toBe('boom');
  });

  it('extracts from an Error whose message is a JSON-encoded fault', () => {
    const err = new Error('{"error":{"code":"0x1","message":"too big"}}');
    expect(dataverseFaultMessage(err)).toBe('too big');
  });

  it('prefers the nested message over a flat one', () => {
    expect(dataverseFaultMessage({ message: 'outer', error: { message: 'inner' } })).toBe('inner');
  });

  it('serializes an unknown shape rather than returning nothing', () => {
    expect(dataverseFaultMessage({ weird: true })).toBe(
      'Dataverse operation failed: {"weird":true}',
    );
    expect(dataverseFaultMessage(null)).toBe('Dataverse operation failed: null');
    expect(dataverseFaultMessage(undefined)).toBe('Dataverse operation failed: null');
  });
});
