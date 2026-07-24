// Signature editor: before saving, the user frames their signature — crop (locked to the 3:1
// signature ratio so what they crop is exactly what appears, no letterbox), zoom, rotate, and flip
// H/V. A live document preview updates as they adjust. All client-side (canvas); only on "Continue"
// do we hand the edited PNG back for validation. Touch-friendly (react-easy-crop) for phone use.

import { useCallback, useEffect, useState } from 'react';
import Cropper, { type Area, type Point } from 'react-easy-crop';
import {
  makeStyles, tokens, Field, Slider, Button, Text, Spinner,
} from '@fluentui/react-components';
import {
  ArrowRotateClockwiseRegular, FlipHorizontalRegular, FlipVerticalRegular, ArrowResetRegular,
} from '@fluentui/react-icons';
import { useT } from '../../i18n/useT';
import { SignatureMockup } from './SignatureMockup';
import { getEditedPng } from './cropImage';

const SIGNATURE_ASPECT = 3 / 1; // the normalized signature box (600×200)
// Allow zooming BELOW 1 so a near-square signature (little margin) can be shrunk to fit the wide
// 3:1 box, centered, with transparent padding around it (restrictPosition is off). Without this,
// minZoom=1 forces the image to cover the box and you can only capture a middle strip.
const MIN_ZOOM = 0.2;

const useStyles = makeStyles({
  root: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  // react-easy-crop fills its (positioned) container; a checkerboard hint shows transparency.
  stage: {
    position: 'relative', width: '100%', height: '320px',
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusMedium, overflow: 'hidden',
  },
  controls: { display: 'flex', flexWrap: 'wrap', gap: tokens.spacingHorizontalL, alignItems: 'flex-end' },
  slider: { minWidth: '200px', flexGrow: 1 },
  flips: { display: 'flex', gap: tokens.spacingHorizontalS, alignItems: 'center', flexWrap: 'wrap' },
  previewLabel: { color: tokens.colorNeutralForeground2 },
  actions: { display: 'flex', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
});

export function SignatureEditor(props: {
  source: string; // data-URL of the raw upload
  onApply: (base64: string) => void; // hand the edited PNG (raw base64) for validation
  onCancel: () => void;
}): JSX.Element {
  const s = useStyles();
  const { t } = useT();
  const [crop, setCrop] = useState<Point>({ x: 0, y: 0 });
  const [zoom, setZoom] = useState(1);
  const [rotation, setRotation] = useState(0);
  const [flipH, setFlipH] = useState(false);
  const [flipV, setFlipV] = useState(false);
  const [area, setArea] = useState<Area | null>(null);
  const [preview, setPreview] = useState<string>('');

  const onCropComplete = useCallback((_area: Area, areaPixels: Area) => setArea(areaPixels), []);

  // Debounced live preview: regenerate the edited PNG whenever the framing changes.
  useEffect(() => {
    if (!area) return;
    let cancelled = false;
    const id = setTimeout(() => {
      void getEditedPng(props.source, area, rotation, flipH, flipV).then((b64) => {
        if (!cancelled) setPreview(b64);
      });
    }, 200);
    return () => { cancelled = true; clearTimeout(id); };
  }, [area, rotation, flipH, flipV, props.source]);

  const reset = () => { setZoom(1); setRotation(0); setFlipH(false); setFlipV(false); setCrop({ x: 0, y: 0 }); };

  const apply = () => {
    if (!area) return;
    void getEditedPng(props.source, area, rotation, flipH, flipV).then(props.onApply);
  };

  return (
    <div className={s.root}>
      <Text weight="semibold">{t('onboarding.editTitle')}</Text>
      <Text size={200} style={{ color: tokens.colorNeutralForeground3 }}>{t('onboarding.editIntro')}</Text>

      <div className={s.stage}>
        <Cropper
          image={props.source}
          crop={crop}
          zoom={zoom}
          rotation={rotation}
          aspect={SIGNATURE_ASPECT}
          minZoom={MIN_ZOOM}
          maxZoom={5}
          restrictPosition={false}
          objectFit="contain"
          onCropChange={setCrop}
          onZoomChange={setZoom}
          onRotationChange={setRotation}
          onCropComplete={onCropComplete}
        />
      </div>

      <div className={s.controls}>
        <Field label={t('onboarding.editZoom')} className={s.slider}>
          <Slider min={MIN_ZOOM} max={5} step={0.05} value={zoom} onChange={(_e, d) => setZoom(d.value)} />
        </Field>
        <Field label={t('onboarding.editRotate')} className={s.slider}>
          <Slider min={-180} max={180} step={1} value={rotation} onChange={(_e, d) => setRotation(d.value)} />
        </Field>
      </div>

      <div className={s.flips}>
        <Button icon={<FlipHorizontalRegular />} appearance={flipH ? 'primary' : 'secondary'} onClick={() => setFlipH((f) => !f)}>
          {t('onboarding.editFlipH')}
        </Button>
        <Button icon={<FlipVerticalRegular />} appearance={flipV ? 'primary' : 'secondary'} onClick={() => setFlipV((f) => !f)}>
          {t('onboarding.editFlipV')}
        </Button>
        <Button icon={<ArrowRotateClockwiseRegular />} appearance="secondary" onClick={() => setRotation((r) => (r + 90) % 360)}>
          {t('onboarding.editRotate90')}
        </Button>
        <Button icon={<ArrowResetRegular />} appearance="subtle" onClick={reset}>{t('onboarding.editReset')}</Button>
      </div>

      <Text weight="semibold" className={s.previewLabel}>{t('onboarding.editPreview')}</Text>
      {preview ? <SignatureMockup signature={preview} /> : <Spinner size="tiny" label={t('common.loading')} />}

      <div className={s.actions}>
        <Button appearance="primary" disabled={!area} onClick={apply}>{t('onboarding.editApply')}</Button>
        <Button appearance="subtle" onClick={props.onCancel}>{t('common.cancel')}</Button>
      </div>
    </div>
  );
}
