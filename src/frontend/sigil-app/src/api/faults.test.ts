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
